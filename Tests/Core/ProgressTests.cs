using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Convergence.Duel;

public static class ProgressTests
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };

    public static void RunAll()
    {
        NewProfileAndRewards();
        PurchaseAndOwnership();
        Normalization();
        LegacyImport();
        PersistenceRoundTrip();
        Console.WriteLine("      Progression: sequential rewards, shop, ownership, repair, legacy import and persistence passed.");
    }

    private static void NewProfileAndRewards()
    {
        var catalog = SampleContent.CreateCatalog();
        var rules = new DuelRules();
        var profile = ProgressionService.CreateNew();
        Check(profile.Coins == 0 && profile.Collection.Count == 0 && profile.SavedDeck.Count == 0, "New players must start without cards, coins or a deck.");
        Check(ProgressionService.IsLessonUnlocked(profile, 0), "First lesson should be unlocked.");
        Check(!ProgressionService.IsLessonUnlocked(profile, 1) && !ProgressionService.IsLessonUnlocked(profile, 2), "Later lessons unlocked too early.");
        RejectUnchanged(profile, () => ProgressionService.ClaimTutorialReward(profile, -1, catalog, rules));
        RejectUnchanged(profile, () => ProgressionService.ClaimTutorialReward(profile, 1, catalog, rules));
        RejectUnchanged(profile, () => ProgressionService.ClaimTutorialReward(profile, 3, catalog, rules));

        profile.Collection.Add(new OwnedCard { Id = "march7", Count = 3 });
        profile.Collection.Add(new OwnedCard { Id = "ambush", Count = 3 });
        Ok(ProgressionService.ClaimTutorialReward(profile, 0, catalog, rules));
        Check(profile.Coins == 150 && profile.SavedDeck.Count == 28, "Lesson one reward or starter deck is incorrect.");
        Check(ProgressionService.OwnedCount(profile, "march7") == 3 && ProgressionService.OwnedCount(profile, "ambush") == 3, "Starter reward must preserve cards already owned.");
        Check(ProgressionService.ValidateOwnedDeck(profile, profile.SavedDeck, catalog, rules).Count == 0, "Reward deck contains cards the player does not own.");
        Check(ProgressionService.IsLessonUnlocked(profile, 1) && !ProgressionService.IsLessonUnlocked(profile, 2), "Lesson unlock sequence is wrong.");
        RejectUnchanged(profile, () => ProgressionService.ClaimTutorialReward(profile, 0, catalog, rules));
        var savedBefore = profile.SavedDeck.ToArray();
        Ok(ProgressionService.ClaimTutorialReward(profile, 1, catalog, rules));
        Check(profile.Coins == 350 && ProgressionService.IsLessonUnlocked(profile, 2), "Lesson two did not award coins or unlock lesson three.");
        Check(ProgressionService.OwnedCount(profile, "crossworld_relay") == 3, "Relay reward must respect the copy limit.");
        Check(ProgressionService.OwnedCount(profile, "second_chance") == 3 && ProgressionService.OwnedCount(profile, "memory_echo") == 3, "Lesson two extra cards are missing.");
        Ok(ProgressionService.ClaimTutorialReward(profile, 2, catalog, rules));
        Check(profile.Coins == 600 && profile.CompletedLessons.SequenceEqual(new[] { 0, 1, 2 }), "Three lesson rewards should total 600 coins.");
        Check(ProgressionService.OwnedCount(profile, "convergence_field") == 2, "Lesson three field card reward is missing.");
        Check(profile.Collection.All(c => c.Count <= rules.MaxCopies), "A tutorial reward exceeded the ownership cap.");
        Check(profile.SavedDeck.SequenceEqual(savedBefore), "Later tutorial rewards overwrote the player's deck.");
        RejectUnchanged(profile, () => ProgressionService.ClaimTutorialReward(profile, 2, catalog, rules));

        var rich = ProgressionService.CreateNew();
        rich.Coins = int.MaxValue - 10;
        Ok(ProgressionService.ClaimTutorialReward(rich, 0, catalog, rules));
        Check(rich.Coins == int.MaxValue, "Coin rewards overflowed.");
        var missingCatalog = catalog.Where(c => c.Id != "march7").ToArray();
        var invalidReward = ProgressionService.CreateNew();
        RejectUnchanged(invalidReward, () => ProgressionService.ClaimTutorialReward(invalidReward, 0, missingCatalog, rules));
    }

    private static void PurchaseAndOwnership()
    {
        var catalog = SampleContent.CreateCatalog();
        var rules = new DuelRules();
        var profile = ProgressionService.CreateNew();
        RejectUnchanged(profile, () => ProgressionService.BuyCard(profile, "amber", catalog, rules));
        profile.Coins = 1000;
        RejectUnchanged(profile, () => ProgressionService.BuyCard(profile, "missing", catalog, rules));
        RejectUnchanged(profile, () => ProgressionService.BuyCard(profile, null, catalog, rules));
        RejectUnchanged(profile, () => ProgressionService.BuyCard(profile, "token_memory", catalog, rules));
        for (int i = 0; i < rules.MaxCopies; i++) Ok(ProgressionService.BuyCard(profile, "amber", catalog, rules));
        Check(profile.Coins == 700 && ProgressionService.OwnedCount(profile, "amber") == 3, "Card purchases charged the wrong amount or lost copies.");
        RejectUnchanged(profile, () => ProgressionService.BuyCard(profile, "amber", catalog, rules));
        Check(ProgressionService.ValidateOwnedDeck(profile, SampleContent.CreateStarterDeck(), catalog, rules).Count > 0, "Unowned cards were allowed in a deck.");
        Ok(ProgressionService.ClaimTutorialReward(profile, 0, catalog, rules));
        Check(ProgressionService.ValidateOwnedDeck(profile, profile.SavedDeck, catalog, rules).Count == 0, "Mixed-series starter deck was rejected after owning its cards.");
        var extraCopy = new List<string>(profile.SavedDeck) { "march7" };
        Check(ProgressionService.ValidateOwnedDeck(profile, extraCopy, catalog, rules).Any(e => e.Contains("보유")), "Deck accepted more copies than owned.");
    }

    private static void Normalization()
    {
        var catalog = SampleContent.CreateCatalog();
        var rules = new DuelRules();
        var profile = new PlayerProgress
        {
            Version = -99, Coins = -100,
            Collection = new List<OwnedCard>
            {
                null, new OwnedCard { Id = "march7", Count = 2 }, new OwnedCard { Id = "march7", Count = int.MaxValue },
                new OwnedCard { Id = "amber", Count = -3 }, new OwnedCard { Id = "unknown", Count = 3 },
                new OwnedCard { Id = "token_memory", Count = 3 }, new OwnedCard { Id = null, Count = 1 }
            },
            CompletedLessons = new List<int> { -1, 2, 2, 9 },
            SavedDeck = new List<string> { "missing" },
            Settings = new PrototypeSettings { MasterVolume = float.NaN, BotDelay = float.PositiveInfinity, TargetFrameRate = -100 }
        };
        Check(ReferenceEquals(profile, ProgressionService.Normalize(profile, catalog, rules)), "Normalize should repair an existing object in place.");
        Check(profile.Version == 1 && profile.Coins == 0, "Version or negative coins were not repaired.");
        Check(profile.Collection.Count == 1 && ProgressionService.OwnedCount(profile, "march7") == 3, "Invalid ownership or duplicate counts survived normalization.");
        Check(profile.CompletedLessons.Count == 0 && profile.SavedDeck.Count == 0, "A broken lesson sequence or unowned deck survived normalization.");
        Check(profile.Settings.MasterVolume == 0.8f && profile.Settings.BotDelay == 0.65f && profile.Settings.TargetFrameRate == 30, "Invalid settings were not repaired.");
        profile.CompletedLessons = new List<int> { 2, 0, 1, 1, 99 };
        profile.Settings.MasterVolume = 7f; profile.Settings.BotDelay = -10f; profile.Settings.TargetFrameRate = 999;
        ProgressionService.Normalize(profile, catalog, rules);
        Check(profile.CompletedLessons.SequenceEqual(new[] { 0, 1, 2 }), "Completed lessons were not sorted and deduplicated.");
        Check(profile.Settings.MasterVolume == 1 && profile.Settings.BotDelay == 0.1f && profile.Settings.TargetFrameRate == 240, "Settings were not clamped.");

        var nulls = new PlayerProgress { Collection = null, CompletedLessons = null, SavedDeck = null, Settings = null };
        ProgressionService.Normalize(nulls, catalog, rules);
        Check(nulls.Collection != null && nulls.CompletedLessons != null && nulls.SavedDeck != null && nulls.Settings != null, "Null saved fields were not restored.");
        Check(ProgressionService.Normalize(null, catalog, rules) != null, "A null profile should become a fresh profile.");

        var ownsStarter = ProgressionService.CreateNew();
        Ok(ProgressionService.ClaimTutorialReward(ownsStarter, 0, catalog, rules));
        ownsStarter.SavedDeck = new List<string> { "missing" };
        ProgressionService.Normalize(ownsStarter, catalog, rules);
        Check(ownsStarter.SavedDeck.SequenceEqual(SampleContent.CreateStarterDeck()), "Corrupt saved deck should fall back to an owned legal starter.");
    }

    private static void LegacyImport()
    {
        var catalog = SampleContent.CreateCatalog();
        var rules = new DuelRules();
        var profile = ProgressionService.CreateNew();
        RejectUnchanged(profile, () => ProgressionService.ImportLegacyDeck(profile, new[] { "missing" }, catalog, rules));
        var legacyDeck = SampleContent.CreateOpponentDeck();
        Ok(ProgressionService.ImportLegacyDeck(profile, legacyDeck, catalog, rules));
        Check(profile.Coins == 0 && profile.CompletedLessons.Count == 0, "Legacy migration must not grant coins or fake lesson completion.");
        Check(profile.SavedDeck.SequenceEqual(legacyDeck), "Legacy deck was not preserved.");
        Check(ProgressionService.ValidateOwnedDeck(profile, legacyDeck, catalog, rules).Count == 0, "Legacy cards were not granted.");
        var counts = profile.Collection.ToDictionary(c => c.Id, c => c.Count);
        Ok(ProgressionService.ImportLegacyDeck(profile, legacyDeck, catalog, rules));
        Check(profile.Collection.All(c => counts[c.Id] == c.Count), "Importing again duplicated owned cards.");
        Ok(ProgressionService.ClaimTutorialReward(profile, 0, catalog, rules));
        Check(profile.SavedDeck.SequenceEqual(legacyDeck), "Starter reward overwrote an imported deck.");
    }

    private static void PersistenceRoundTrip()
    {
        var catalog = SampleContent.CreateCatalog();
        var rules = new DuelRules();
        var profile = ProgressionService.CreateNew();
        Ok(ProgressionService.ClaimTutorialReward(profile, 0, catalog, rules));
        profile.Settings.MasterVolume = 0.35f; profile.Settings.Fullscreen = true;
        profile.Settings.BotDelay = 1.2f; profile.Settings.TargetFrameRate = 120;
        var json = Snapshot(profile);
        var restored = JsonSerializer.Deserialize<PlayerProgress>(json, JsonOptions);
        Check(Snapshot(restored) == json, "Field-based profile failed its JSON round trip.");
        ProgressionService.Normalize(restored, catalog, rules);
        Check(restored.Coins == 150 && restored.CompletedLessons.SequenceEqual(new[] { 0 }), "Rewards changed after reloading.");
        Check(restored.Settings.Fullscreen && restored.Settings.MasterVolume == 0.35f && restored.Settings.TargetFrameRate == 120, "Settings changed after reloading.");
        RejectUnchanged(restored, () => ProgressionService.ClaimTutorialReward(restored, 0, catalog, rules));
    }

    private static string Snapshot(PlayerProgress profile) { return JsonSerializer.Serialize(profile, JsonOptions); }
    private static void RejectUnchanged(PlayerProgress profile, Func<ActionResult> action)
    {
        var before = Snapshot(profile);
        var result = action();
        Check(!result.Success, "Expected rejected progression operation.");
        Check(Snapshot(profile) == before, "Rejected progression operation mutated the profile: " + result.Message);
    }
    private static void Ok(ActionResult result) { Check(result.Success, "Progression operation failed: " + result.Message); }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
