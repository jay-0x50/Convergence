using System;
using System.Collections.Generic;

namespace Convergence.Duel
{
    // Public fields keep this format compatible with Unity JsonUtility and a plain C# host.
    [Serializable]
    public sealed class PlayerProgress
    {
        public int Version = 1;
        public int Coins;
        public List<OwnedCard> Collection = new List<OwnedCard>();
        public List<int> CompletedLessons = new List<int>();
        public List<string> SavedDeck = new List<string>();
        public PrototypeSettings Settings = new PrototypeSettings();
    }

    [Serializable]
    public sealed class OwnedCard
    {
        public string Id;
        public int Count;
    }

    [Serializable]
    public sealed class PrototypeSettings
    {
        public float MasterVolume = 0.8f;
        public bool Fullscreen = false;
        public float BotDelay = 0.65f;
        public int TargetFrameRate = 60;
    }

    /// <summary>Local tutorial rewards and fixed-price card purchases. No series restrictions.</summary>
    public static class ProgressionService
    {
        public const int CardPrice = 100;
        public const int TutorialLessonCount = 3;

        public static PlayerProgress CreateNew() { return new PlayerProgress(); }

        /// <summary>Repairs a loaded profile in place; returns a new profile when the input is null.</summary>
        public static PlayerProgress Normalize(PlayerProgress profile, IReadOnlyList<CardDefinition> catalog, DuelRules rules)
        {
            if (profile == null) profile = CreateNew();
            profile.Version = 1;
            profile.Coins = Math.Max(0, profile.Coins);
            int copyLimit = rules == null ? 3 : Math.Max(0, rules.MaxCopies);
            var normalized = new List<OwnedCard>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (profile.Collection != null)
            {
                foreach (var owned in profile.Collection)
                {
                    if (owned == null || owned.Count <= 0) continue;
                    var definition = FindDefinition(catalog, owned.Id);
                    if (definition == null || definition.Kind == CardKind.Token) continue;
                    int previous;
                    counts.TryGetValue(owned.Id, out previous);
                    counts[owned.Id] = (int)Math.Min(copyLimit, (long)previous + owned.Count);
                }
            }
            foreach (var entry in counts)
                if (entry.Value > 0) normalized.Add(new OwnedCard { Id = entry.Key, Count = entry.Value });
            normalized.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            profile.Collection = normalized;

            int completedCount = CompletedPrefix(profile);
            profile.CompletedLessons = new List<int>();
            for (int lesson = 0; lesson < completedCount; lesson++) profile.CompletedLessons.Add(lesson);

            if (profile.Settings == null) profile.Settings = new PrototypeSettings();
            profile.Settings.MasterVolume = ClampFinite(profile.Settings.MasterVolume, 0f, 1f, 0.8f);
            profile.Settings.BotDelay = ClampFinite(profile.Settings.BotDelay, 0.1f, 2f, 0.65f);
            profile.Settings.TargetFrameRate = Math.Max(30, Math.Min(240, profile.Settings.TargetFrameRate));

            if (profile.SavedDeck == null) profile.SavedDeck = new List<string>();
            if (ValidateOwnedDeck(profile, profile.SavedDeck, catalog, rules).Count > 0)
            {
                var starter = SampleContent.CreateStarterDeck();
                profile.SavedDeck = ValidateOwnedDeck(profile, starter, catalog, rules).Count == 0
                    ? starter : new List<string>();
            }
            return profile;
        }

        public static int OwnedCount(PlayerProgress profile, string id)
        {
            if (profile == null || profile.Collection == null || string.IsNullOrEmpty(id)) return 0;
            long count = 0;
            foreach (var owned in profile.Collection)
                if (owned != null && string.Equals(owned.Id, id, StringComparison.Ordinal) && owned.Count > 0)
                    count = Math.Min(int.MaxValue, count + owned.Count);
            return (int)count;
        }

        public static IReadOnlyList<string> ValidateOwnedDeck(PlayerProgress profile, IEnumerable<string> deck,
            IReadOnlyList<CardDefinition> catalog, DuelRules rules)
        {
            var cards = deck == null ? null : new List<string>(deck);
            var errors = new List<string>(DeckValidator.Validate(cards, catalog, rules));
            if (cards == null) return errors.AsReadOnly();
            var required = CountCards(cards);
            foreach (var entry in required)
            {
                int owned = OwnedCount(profile, entry.Key);
                if (entry.Value <= owned) continue;
                var definition = FindDefinition(catalog, entry.Key);
                errors.Add((definition == null ? entry.Key : definition.Name) + ": 보유 " + owned +
                    "장, 덱에는 " + entry.Value + "장이 필요합니다.");
            }
            return errors.AsReadOnly();
        }

        public static bool IsLessonUnlocked(PlayerProgress profile, int lessonId)
        {
            return lessonId >= 0 && lessonId < TutorialLessonCount && lessonId <= CompletedPrefix(profile);
        }

        /// <summary>The caller must verify TutorialSession.IsComplete before claiming this reward.</summary>
        public static ActionResult ClaimTutorialReward(PlayerProgress profile, int lessonId,
            IReadOnlyList<CardDefinition> catalog, DuelRules rules)
        {
            if (profile == null) return ActionResult.Fail("플레이어 저장 정보가 없습니다.");
            if (!IsLessonUnlocked(profile, lessonId)) return ActionResult.Fail("앞 단계의 튜토리얼을 먼저 완료해 주세요.");
            if (profile.CompletedLessons != null && profile.CompletedLessons.Contains(lessonId))
                return ActionResult.Fail("이미 받은 튜토리얼 보상입니다. 재도전은 자유롭게 할 수 있습니다.");
            if (rules == null || rules.MaxCopies < 1) return ActionResult.Fail("카드 보유 제한 설정이 올바르지 않습니다.");

            var reward = RewardCards(lessonId);
            foreach (var entry in reward)
            {
                var definition = FindDefinition(catalog, entry.Key);
                if (definition == null || definition.Kind == CardKind.Token)
                    return ActionResult.Fail("보상 카드 정보를 찾을 수 없습니다: " + entry.Key);
            }
            if (lessonId == 0)
            {
                var errors = DeckValidator.Validate(SampleContent.CreateStarterDeck(), catalog, rules);
                if (errors.Count != 0) return ActionResult.Fail("기본 덱을 지급할 수 없습니다: " + errors[0]);
            }

            // Validate the whole grant before touching the profile, so a failed claim is atomic.
            foreach (var entry in reward)
            {
                int existing = OwnedCount(profile, entry.Key);
                int desired = lessonId == 0 ? Math.Max(existing, entry.Value)
                    : (int)Math.Min(int.MaxValue, (long)existing + entry.Value);
                SetOwnedCount(profile, entry.Key, Math.Min(rules.MaxCopies, desired));
            }
            profile.Coins = SaturatingCoins(profile.Coins, 150 + lessonId * 50);
            if (profile.CompletedLessons == null) profile.CompletedLessons = new List<int>();
            profile.CompletedLessons.Add(lessonId);
            if (lessonId == 0 && (profile.SavedDeck == null || profile.SavedDeck.Count == 0))
                profile.SavedDeck = SampleContent.CreateStarterDeck();
            return ActionResult.Ok("튜토리얼 " + (lessonId + 1) + " 완료! 카드 보상과 " + (150 + lessonId * 50) + "코인을 받았습니다.");
        }

        public static ActionResult BuyCard(PlayerProgress profile, string id,
            IReadOnlyList<CardDefinition> catalog, DuelRules rules)
        {
            if (profile == null) return ActionResult.Fail("플레이어 저장 정보가 없습니다.");
            var definition = FindDefinition(catalog, id);
            if (definition == null || definition.Kind == CardKind.Token)
                return ActionResult.Fail("구매할 수 없는 카드입니다.");
            if (rules == null || rules.MaxCopies < 1) return ActionResult.Fail("카드 보유 제한 설정이 올바르지 않습니다.");
            int owned = OwnedCount(profile, id);
            if (owned >= rules.MaxCopies) return ActionResult.Fail("이미 최대 " + rules.MaxCopies + "장을 보유하고 있습니다.");
            if (profile.Coins < CardPrice) return ActionResult.Fail("코인이 부족합니다. 카드 1장은 " + CardPrice + "코인입니다.");
            SetOwnedCount(profile, id, owned + 1);
            profile.Coins -= CardPrice;
            return ActionResult.Ok(definition.Name + " 1장을 구매했습니다.");
        }

        /// <summary>Preserves a legal deck from the original prototype without completing tutorials.</summary>
        public static ActionResult ImportLegacyDeck(PlayerProgress profile, IEnumerable<string> deck,
            IReadOnlyList<CardDefinition> catalog, DuelRules rules)
        {
            if (profile == null) return ActionResult.Fail("플레이어 저장 정보가 없습니다.");
            var cards = deck == null ? null : new List<string>(deck);
            var errors = DeckValidator.Validate(cards, catalog, rules);
            if (errors.Count > 0) return ActionResult.Fail("이전 덱을 불러올 수 없습니다: " + errors[0]);
            foreach (var entry in CountCards(cards))
                SetOwnedCount(profile, entry.Key, Math.Min(rules.MaxCopies, Math.Max(OwnedCount(profile, entry.Key), entry.Value)));
            profile.SavedDeck = cards;
            return ActionResult.Ok("이전 프로토타입의 덱과 해당 카드 보유 정보를 가져왔습니다.");
        }

        private static Dictionary<string, int> RewardCards(int lessonId)
        {
            if (lessonId == 0) return CountCards(SampleContent.CreateStarterDeck());
            if (lessonId == 1)
                return new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    { "crossworld_relay", 3 }, { "second_chance", 2 }, { "memory_echo", 2 }
                };
            return new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "convergence_field", 2 }, { "disrupt", 2 }, { "hold_the_line", 2 }, { "rally", 2 }
            };
        }

        private static int CompletedPrefix(PlayerProgress profile)
        {
            if (profile == null || profile.CompletedLessons == null) return 0;
            int completed = 0;
            while (completed < TutorialLessonCount && profile.CompletedLessons.Contains(completed)) completed++;
            return completed;
        }

        private static Dictionary<string, int> CountCards(IEnumerable<string> cards)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in cards)
            {
                if (string.IsNullOrEmpty(id)) continue;
                int count;
                counts.TryGetValue(id, out count);
                counts[id] = count + 1;
            }
            return counts;
        }

        private static CardDefinition FindDefinition(IReadOnlyList<CardDefinition> catalog, string id)
        {
            if (catalog == null || string.IsNullOrEmpty(id)) return null;
            foreach (var definition in catalog)
                if (definition != null && string.Equals(definition.Id, id, StringComparison.Ordinal)) return definition;
            return null;
        }

        private static void SetOwnedCount(PlayerProgress profile, string id, int count)
        {
            if (profile.Collection == null) profile.Collection = new List<OwnedCard>();
            OwnedCard stored = null;
            for (int index = 0; index < profile.Collection.Count; index++)
            {
                var owned = profile.Collection[index];
                if (owned == null || !string.Equals(owned.Id, id, StringComparison.Ordinal)) continue;
                if (stored == null) stored = owned;
                else { profile.Collection.RemoveAt(index); index--; }
            }
            if (stored == null) profile.Collection.Add(new OwnedCard { Id = id, Count = count });
            else stored.Count = count;
        }

        private static int SaturatingCoins(int existing, int amount)
        {
            return (int)Math.Min(int.MaxValue, (long)Math.Max(0, existing) + amount);
        }

        private static float ClampFinite(float value, float minimum, float maximum, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
