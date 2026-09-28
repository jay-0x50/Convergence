using System;
using System.Collections.Generic;

namespace Convergence.Duel
{
    /// <summary>
    /// A deterministic, Unity-independent duel. Commands start a chain; set intercepts respond.
    /// Two consecutive priority passes resolve the complete chain in reverse order.
    /// </summary>
    public sealed class DuelEngine
    {
        private readonly Dictionary<string, CardDefinition> definitions;
        private readonly Dictionary<int, CardInstance> instances = new Dictionary<int, CardInstance>();
        private readonly List<ChainLink> chain = new List<ChainLink>();
        private readonly List<string> log = new List<string>();
        private int nextInstanceId = 1;
        private int nextChainId = 1;
        private int consecutivePasses;

        public PlayerState[] Players { get; }
        public DuelRules Rules { get; }
        public int ActivePlayer { get; private set; }
        public int PriorityPlayer { get; private set; } = -1;
        public DuelPhase Phase { get; private set; }
        public int TurnNumber { get; private set; }
        public bool IsGameOver { get; private set; }
        public int? Winner { get; private set; }
        public string ResultReason { get; private set; } = "";
        public IReadOnlyList<ChainLink> Chain => chain.AsReadOnly();
        public IReadOnlyList<string> Log => log.AsReadOnly();

        public DuelEngine(IReadOnlyList<CardDefinition> catalog, DuelRules rules,
            IEnumerable<string> deck0, IEnumerable<string> deck1, int seed = 1)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (deck0 == null) throw new ArgumentNullException(nameof(deck0));
            if (deck1 == null) throw new ArgumentNullException(nameof(deck1));
            var decks = new[] { new List<string>(deck0), new List<string>(deck1) };
            for (int player = 0; player < decks.Length; player++)
            {
                var errors = DeckValidator.Validate(decks[player], catalog, rules);
                if (errors.Count > 0)
                    throw new ArgumentException("플레이어 " + (player + 1) + " 덱: " + string.Join(" / ", errors));
            }
            Rules = rules;
            definitions = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
            foreach (var definition in catalog) definitions.Add(definition.Id, definition);
            Players = new[]
            {
                new PlayerState { Id = 0, Name = "플레이어 1", Life = Rules.StartingLife },
                new PlayerState { Id = 1, Name = "플레이어 2", Life = Rules.StartingLife }
            };
            ActivePlayer = Rules.FirstPlayer;
            TurnNumber = 1;
            Phase = DuelPhase.Draw;
            var random = new Random(seed);
            for (int player = 0; player < Players.Length; player++)
            {
                var characterIds = new List<string>();
                foreach (var id in decks[player])
                {
                    var card = CreateInstance(definitions[id], player, CardZone.Deck);
                    Players[player].DeckCards.Add(card);
                    if (card.Definition.Kind == CardKind.Character) characterIds.Add(id);
                }
                // Picking a main character is presentation metadata, not an extra draw.
                Players[player].MainCharacterId = characterIds[random.Next(characterIds.Count)];
                if (Rules.ShuffleDeck) Shuffle(Players[player].DeckCards, random);
                Write(Players[player].Name + "의 메인 캐릭터: " + definitions[Players[player].MainCharacterId].Name);
            }
            for (int player = 0; player < Players.Length && !IsGameOver; player++)
                DrawCards(player, Rules.StartingHand);
            if (!IsGameOver) Write("듀얼 시작! " + Players[ActivePlayer].Name + "의 드로우 페이즈입니다.");
        }

        public CardInstance FindCard(int id)
        {
            CardInstance result;
            return instances.TryGetValue(id, out result) ? result : null;
        }

        public ActionResult Summon(int player, int cardId)
        {
            var error = RequireMainAction(player);
            if (error != null) return error;
            var card = FindCard(cardId);
            if (!OwnedIn(card, player, CardZone.Hand) || card.Definition.Kind != CardKind.Character)
                return Fail("자신의 패에 있는 캐릭터만 일반 소환할 수 있습니다.");
            if (Players[player].NormalSummonsUsed >= Rules.NormalSummonsPerTurn)
                return Fail("이번 턴의 일반 소환 횟수를 모두 사용했습니다.");
            if (Players[player].Field.Count >= Rules.FieldSlots) return Fail("캐릭터 존이 가득 찼습니다.");
            EnterField(card);
            Players[player].NormalSummonsUsed++;
            return Succeed(card.Definition.Name + " 일반 소환!");
        }

        public ActionResult SetIntercept(int player, int cardId)
        {
            var error = RequireMainAction(player);
            if (error != null) return error;
            var card = FindCard(cardId);
            if (!OwnedIn(card, player, CardZone.Hand) || card.Definition.Kind != CardKind.Intercept)
                return Fail("자신의 패에 있는 인터셉트만 세트할 수 있습니다.");
            if (Players[player].Backrow.Count >= Rules.BackrowSlots) return Fail("인터셉트 존이 가득 찼습니다.");
            Move(card, CardZone.Backrow);
            card.SetOnTurn = TurnNumber;
            // Keep the name concealed in the shared duel log until activation.
            return Succeed(Players[player].Name + "이 인터셉트를 세트했습니다.");
        }

        public ActionResult Activate(int player, int cardId, int? targetId = null)
        {
            var error = RequirePlayer(player);
            if (error != null) return error;
            var card = FindCard(cardId);
            if (card == null || card.Owner != player) return Fail("자신의 카드를 선택해 주세요.");
            if (card.Definition.Kind == CardKind.Command || card.Definition.Kind == CardKind.Domain)
            {
                error = RequireMainAction(player);
                if (error != null) return error;
                if (card.Zone != CardZone.Hand) return Fail("커맨드와 필드 카드는 패에서 발동합니다.");
            }
            else if (card.Definition.Kind == CardKind.Intercept)
            {
                if (card.Zone != CardZone.Backrow) return Fail("인터셉트는 세트한 뒤 발동합니다.");
                if (chain.Count == 0) return Fail("인터셉트는 공격 또는 카드 발동에 대응합니다.");
                if (PriorityPlayer != player) return Fail("상대의 체인 응답을 기다려 주세요.");
                if (!Rules.CanActivateInterceptOnSetTurn && card.SetOnTurn >= TurnNumber)
                    return Fail("세트한 턴에는 인터셉트를 발동할 수 없습니다.");
                if (card.Definition.Trigger == InterceptTrigger.AttackOnly && chain[chain.Count - 1].Kind != ChainLinkKind.Attack)
                    return Fail("공격 선언에 직접 대응하는 인터셉트입니다.");
            }
            else return Fail("커맨드, 필드 카드 또는 인터셉트만 발동할 수 있습니다.");

            if (card.Definition.Effect == EffectKind.Negate && chain.Count == 0)
                return Fail("무효로 할 이전 체인 링크가 없습니다.");
            error = ValidateEffectTarget(card.Definition, player, targetId);
            if (error != null) return error;
            if (IsSummonEffect(card.Definition.Effect) && Players[player].Field.Count >= Rules.FieldSlots)
                return Fail("캐릭터 존이 가득 찼습니다.");

            var link = new ChainLink
            {
                Id = nextChainId++, Kind = ChainLinkKind.Effect, Controller = player,
                Source = card, TargetId = targetId,
                NegatesLinkId = card.Definition.Effect == EffectKind.Negate ? (int?)chain[chain.Count - 1].Id : null,
                Label = card.Definition.Name
            };
            Move(card, CardZone.Chain);
            AddChainLink(link);
            return Succeed("체인 " + chain.Count + ": " + card.Definition.Name + " 발동!");
        }

        public ActionResult Attack(int player, int attackerId, int? targetId = null)
        {
            var error = RequirePlayer(player);
            if (error != null) return error;
            if (ActivePlayer != player || Phase != DuelPhase.Battle)
                return Fail("자신의 배틀 페이즈에 공격할 수 있습니다.");
            if (chain.Count != 0) return Fail("현재 체인을 먼저 해결해 주세요.");
            if (Rules.FirstPlayerCannotAttackFirstTurn && TurnNumber == 1)
                return Fail("선공 첫 턴에는 공격할 수 없습니다.");
            var attacker = FindCard(attackerId);
            if (!OwnedIn(attacker, player, CardZone.Field) || !attacker.IsUnit)
                return Fail("필드의 자신의 캐릭터 또는 소환수를 선택해 주세요.");
            if (attacker.AttacksMade > 0) return Fail("이 유닛은 이번 턴에 이미 공격했습니다.");
            if (targetId.HasValue)
            {
                var target = FindCard(targetId.Value);
                if (!OwnedIn(target, 1 - player, CardZone.Field) || !target.IsUnit)
                    return Fail("상대 필드의 유닛을 공격 대상으로 선택해 주세요.");
            }
            else if (Players[1 - player].Field.Count != 0)
                return Fail("상대 필드에 유닛이 있어 직접 공격할 수 없습니다.");
            attacker.AttacksMade++;
            AddChainLink(new ChainLink
            {
                Id = nextChainId++, Kind = ChainLinkKind.Attack, Controller = player,
                Source = attacker, TargetId = targetId, Label = attacker.Definition.Name + "의 공격"
            });
            return Succeed(attacker.Definition.Name + (targetId.HasValue ? " 공격 선언!" : " 직접 공격 선언!"));
        }

        public ActionResult PassPriority(int player)
        {
            var error = RequirePlayer(player);
            if (error != null) return error;
            if (chain.Count == 0) return Fail("응답할 체인이 없습니다.");
            if (PriorityPlayer != player) return Fail("상대의 체인 응답을 기다려 주세요.");
            consecutivePasses++;
            Write(Players[player].Name + " 체인 응답 패스.");
            if (consecutivePasses >= 2)
            {
                ResolveChain();
                return ActionResult.Ok(IsGameOver ? ResultReason : "체인을 해결했습니다.");
            }
            PriorityPlayer = 1 - player;
            return ActionResult.Ok("상대에게 체인 응답권을 넘겼습니다.");
        }

        public ActionResult AdvancePhase(int player)
        {
            var error = RequirePlayer(player);
            if (error != null) return error;
            if (ActivePlayer != player) return Fail("자신의 턴에만 페이즈를 진행할 수 있습니다.");
            if (chain.Count != 0) return Fail("현재 체인을 먼저 해결해 주세요.");
            switch (Phase)
            {
                case DuelPhase.Draw:
                    if (TurnNumber == 1 && Rules.FirstPlayerSkipsFirstDraw)
                        Write("선공 첫 턴 드로우를 생략합니다.");
                    else DrawCards(player, 1);
                    // Advancing the phase is the only place a turn draw occurs.
                    if (!IsGameOver) Phase = DuelPhase.Main1;
                    break;
                case DuelPhase.Main1: Phase = DuelPhase.Battle; break;
                case DuelPhase.Battle: Phase = DuelPhase.Main2; break;
                case DuelPhase.Main2: Phase = DuelPhase.End; break;
                case DuelPhase.End:
                    ActivePlayer = 1 - ActivePlayer;
                    TurnNumber++;
                    Phase = DuelPhase.Draw;
                    Players[ActivePlayer].NormalSummonsUsed = 0;
                    foreach (var unit in Players[ActivePlayer].FieldCards) unit.AttacksMade = 0;
                    break;
            }
            if (IsGameOver) return ActionResult.Ok(ResultReason);
            return Succeed("턴 " + TurnNumber + " · " + Players[ActivePlayer].Name + " · " + PhaseName(Phase));
        }

        private void AddChainLink(ChainLink link)
        {
            chain.Add(link);
            consecutivePasses = 0;
            PriorityPlayer = 1 - link.Controller;
        }

        private void ResolveChain()
        {
            Write("체인을 역순으로 해결합니다.");
            for (int index = chain.Count - 1; index >= 0 && !IsGameOver; index--)
            {
                var link = chain[index];
                if (link.Negated)
                {
                    Write(link.Label + "은(는) 무효화되었습니다.");
                    continue;
                }
                if (link.Kind == ChainLinkKind.Attack) ResolveAttack(link);
                else ResolveEffect(link);
            }
            // Consumed sources leave the chain even when an earlier effect ends the duel.
            foreach (var link in chain)
                if (link.Kind == ChainLinkKind.Effect && link.Source.Zone == CardZone.Chain)
                    Move(link.Source, CardZone.Graveyard);
            chain.Clear();
            consecutivePasses = 0;
            PriorityPlayer = -1;
        }

        private void ResolveEffect(ChainLink link)
        {
            var definition = link.Source.Definition;
            if (ValidateEffectTarget(definition, link.Controller, link.TargetId) != null)
            {
                Write(link.Label + ": 대상이 유효하지 않아 효과가 적용되지 않습니다.");
                return;
            }
            var target = link.TargetId.HasValue ? FindCard(link.TargetId.Value) : null;
            int amount = Math.Max(0, definition.Amount);
            if (definition.Kind == CardKind.Domain)
            {
                var previous = Players[link.Controller].DomainCard;
                if (previous != null)
                {
                    Move(previous, CardZone.Graveyard);
                    Write(previous.Definition.Name + " 필드 카드 교체로 묘지로 이동.");
                }
                Move(link.Source, CardZone.Domain);
                Write(definition.Name + " 필드 카드 적용.");
            }
            switch (definition.Effect)
            {
                case EffectKind.None:
                    Write(link.Label + " 해결.");
                    break;
                case EffectKind.Draw:
                    DrawCards(link.Controller, amount);
                    break;
                case EffectKind.DamageUnit:
                    target.Health = Math.Max(0, target.Health - amount);
                    Write(target.Definition.Name + "에게 " + amount + " 피해.");
                    RemoveDefeated(target);
                    break;
                case EffectKind.BoostUnit:
                    target.BonusAttack += definition.Amount;
                    Write(target.Definition.Name + " 공격력 " + (definition.Amount >= 0 ? "+" : "") + definition.Amount + ".");
                    break;
                case EffectKind.SpecialSummonFromHand:
                case EffectKind.Revive:
                    if (Players[link.Controller].Field.Count >= Rules.FieldSlots)
                    {
                        Write(link.Label + ": 캐릭터 존이 가득 차 소환하지 못했습니다.");
                        break;
                    }
                    EnterField(target);
                    Write(target.Definition.Name + " 특수 소환!");
                    break;
                case EffectKind.CreateToken:
                    CardDefinition token;
                    if (!definitions.TryGetValue("token_memory", out token) || token.Kind != CardKind.Token)
                        token = new CardDefinition("token_memory", "기억 정령", "공통", "기억", "무속성", CardKind.Token, 800, 800);
                    int created = 0;
                    while (created < amount && Players[link.Controller].Field.Count < Rules.FieldSlots)
                    {
                        var instance = CreateInstance(token, link.Controller, CardZone.Banished);
                        EnterField(instance);
                        created++;
                    }
                    Write("기억 정령 " + created + "장 소환.");
                    break;
                case EffectKind.HealPlayer:
                    Players[link.Controller].Life = (int)Math.Min(int.MaxValue, (long)Players[link.Controller].Life + amount);
                    Write(Players[link.Controller].Name + " 라이프 " + amount + " 회복.");
                    break;
                case EffectKind.Negate:
                    foreach (var candidate in chain)
                        if (candidate.Id == link.NegatesLinkId)
                        {
                            candidate.Negated = true;
                            Write(link.Label + ": " + candidate.Label + " 무효!");
                            break;
                        }
                    break;
                case EffectKind.DestroyUnit:
                    target.Health = 0;
                    Write(target.Definition.Name + " 파괴.");
                    RemoveDefeated(target);
                    break;
                case EffectKind.FieldAura:
                    Write(Players[link.Controller].Name + "의 필드 유닛 공격력 +" + amount + ".");
                    break;
            }
        }

        private void ResolveAttack(ChainLink link)
        {
            var attacker = link.Source;
            if (!OwnedIn(attacker, link.Controller, CardZone.Field) || attacker.Health <= 0)
            {
                Write(link.Label + ": 공격 유닛이 없어 공격이 취소됩니다.");
                return;
            }
            if (!link.TargetId.HasValue)
            {
                if (Players[1 - link.Controller].Field.Count != 0)
                {
                    Write(link.Label + ": 상대 필드에 유닛이 생겨 직접 공격이 취소됩니다.");
                    return;
                }
                var enemy = Players[1 - link.Controller];
                enemy.Life = Math.Max(0, enemy.Life - attacker.Attack);
                Write(attacker.Definition.Name + " 직접 공격! " + attacker.Attack + " 라이프 피해.");
                CheckLifeDefeat();
                return;
            }
            var defender = FindCard(link.TargetId.Value);
            if (!OwnedIn(defender, 1 - link.Controller, CardZone.Field) || !defender.IsUnit || defender.Health <= 0)
            {
                Write(link.Label + ": 공격 대상이 없어 공격이 취소됩니다.");
                return;
            }
            int attackDamage = attacker.Attack;
            int counterDamage = Rules.Counterattack ? defender.Attack : 0;
            defender.Health = Math.Max(0, defender.Health - attackDamage);
            attacker.Health = Math.Max(0, attacker.Health - counterDamage);
            Write(attacker.Definition.Name + " → " + defender.Definition.Name + ": " + attackDamage + " 피해" +
                (Rules.Counterattack ? ", 반격 " + counterDamage + " 피해." : "."));
            // Damage is simultaneous; destroying the defender does not prevent its counterattack.
            RemoveDefeated(defender);
            RemoveDefeated(attacker);
        }

        private ActionResult ValidateEffectTarget(CardDefinition definition, int player, int? targetId)
        {
            CardInstance target = targetId.HasValue ? FindCard(targetId.Value) : null;
            switch (definition.Target)
            {
                case TargetKind.None:
                    if (targetId.HasValue) return Fail("대상을 지정하지 않는 카드입니다.");
                    break;
                case TargetKind.FriendlyUnit:
                    if (!OwnedIn(target, player, CardZone.Field) || !target.IsUnit)
                        return Fail("자신의 필드 유닛을 대상으로 지정해 주세요.");
                    break;
                case TargetKind.EnemyUnit:
                    if (!OwnedIn(target, 1 - player, CardZone.Field) || !target.IsUnit)
                        return Fail("상대 필드 유닛을 대상으로 지정해 주세요.");
                    break;
                case TargetKind.AnyUnit:
                    if (target == null || target.Zone != CardZone.Field || !target.IsUnit)
                        return Fail("필드 유닛을 대상으로 지정해 주세요.");
                    break;
                case TargetKind.HandCharacter:
                    if (!OwnedIn(target, player, CardZone.Hand) || target.Definition.Kind != CardKind.Character)
                        return Fail("자신의 패에 있는 캐릭터를 대상으로 지정해 주세요.");
                    break;
                case TargetKind.GraveyardCharacter:
                    if (!OwnedIn(target, player, CardZone.Graveyard) || target.Definition.Kind != CardKind.Character)
                        return Fail("자신의 묘지에 있는 캐릭터를 대상으로 지정해 주세요.");
                    break;
                default: return Fail("지원하지 않는 대상 규칙입니다.");
            }
            // Protect custom card data from invalid effect/target combinations as well.
            if (definition.Effect == EffectKind.DamageUnit || definition.Effect == EffectKind.BoostUnit ||
                definition.Effect == EffectKind.DestroyUnit)
                if (target == null || !target.IsUnit || target.Zone != CardZone.Field)
                    return Fail("이 효과에는 필드 유닛 대상이 필요합니다.");
            if (definition.Effect == EffectKind.SpecialSummonFromHand)
                if (!OwnedIn(target, player, CardZone.Hand) || target.Definition.Kind != CardKind.Character)
                    return Fail("특수 소환할 패의 캐릭터가 필요합니다.");
            if (definition.Effect == EffectKind.Revive)
                if (!OwnedIn(target, player, CardZone.Graveyard) || target.Definition.Kind != CardKind.Character)
                    return Fail("부활시킬 묘지의 캐릭터가 필요합니다.");
            return null;
        }

        private ActionResult RequirePlayer(int player)
        {
            if (IsGameOver) return Fail("듀얼이 종료되었습니다.");
            if (player != 0 && player != 1) return Fail("존재하지 않는 플레이어입니다.");
            return null;
        }

        private ActionResult RequireMainAction(int player)
        {
            var error = RequirePlayer(player);
            if (error != null) return error;
            if (player != ActivePlayer || (Phase != DuelPhase.Main1 && Phase != DuelPhase.Main2))
                return Fail("자신의 메인 페이즈에 사용할 수 있습니다.");
            if (chain.Count != 0) return Fail("현재 체인을 먼저 해결해 주세요.");
            return null;
        }

        private static bool OwnedIn(CardInstance card, int owner, CardZone zone)
        {
            return card != null && card.Owner == owner && card.Zone == zone;
        }

        private static bool IsSummonEffect(EffectKind effect)
        {
            return effect == EffectKind.SpecialSummonFromHand || effect == EffectKind.Revive || effect == EffectKind.CreateToken;
        }

        private void DrawCards(int player, int count)
        {
            for (int index = 0; index < count && !IsGameOver; index++)
            {
                if (Players[player].DeckCards.Count == 0)
                {
                    Finish(1 - player, Players[player].Name + "이 드로우할 카드가 없어 패배했습니다.");
                    return;
                }
                Move(Players[player].DeckCards[0], CardZone.Hand);
                Write(Players[player].Name + " 카드 1장 드로우.");
            }
        }

        private CardInstance CreateInstance(CardDefinition definition, int owner, CardZone zone)
        {
            var card = new CardInstance
            {
                InstanceId = nextInstanceId++, Definition = definition, Owner = owner,
                Zone = zone, Health = Math.Max(1, definition.MaxHealth)
            };
            instances.Add(card.InstanceId, card);
            return card;
        }

        private void EnterField(CardInstance card)
        {
            Move(card, CardZone.Field);
            card.Health = Math.Max(1, card.Definition.MaxHealth);
            card.BonusAttack = 0;
            card.AttacksMade = 0;
        }

        private void RemoveDefeated(CardInstance card)
        {
            if (card.Zone != CardZone.Field || card.Health > 0) return;
            Move(card, card.Definition.Kind == CardKind.Token ? CardZone.Banished : CardZone.Graveyard);
            Write(card.Definition.Name + (card.Definition.Kind == CardKind.Token ? " 소환수 소멸." : " 묘지로 이동."));
        }

        private void Move(CardInstance card, CardZone destination)
        {
            var player = Players[card.Owner];
            bool updateAura = card.Zone == CardZone.Field || card.Zone == CardZone.Domain ||
                destination == CardZone.Field || destination == CardZone.Domain;
            player.DeckCards.Remove(card);
            player.HandCards.Remove(card);
            player.FieldCards.Remove(card);
            player.BackrowCards.Remove(card);
            player.GraveyardCards.Remove(card);
            if (player.DomainCard == card) player.DomainCard = null;
            if (card.Zone == CardZone.Field && destination != CardZone.Field)
            {
                card.BonusAttack = 0;
                card.AttacksMade = 0;
            }
            card.AuraAttack = 0;
            card.Zone = destination;
            switch (destination)
            {
                case CardZone.Deck: player.DeckCards.Add(card); break;
                case CardZone.Hand: player.HandCards.Add(card); break;
                case CardZone.Field: player.FieldCards.Add(card); break;
                case CardZone.Backrow: player.BackrowCards.Add(card); break;
                case CardZone.Graveyard: player.GraveyardCards.Add(card); break;
                case CardZone.Domain: player.DomainCard = card; break;
            }
            if (updateAura) RefreshFieldAura(player);
        }

        private static void RefreshFieldAura(PlayerState player)
        {
            int amount = player.DomainCard != null && player.DomainCard.Definition.Effect == EffectKind.FieldAura
                ? Math.Max(0, player.DomainCard.Definition.Amount) : 0;
            foreach (var unit in player.FieldCards) unit.AuraAttack = amount;
        }

        private void CheckLifeDefeat()
        {
            if (Players[0].Life <= 0 && Players[1].Life <= 0) Finish(-1, "양쪽 라이프가 0이 되어 무승부입니다.");
            else if (Players[0].Life <= 0) Finish(1, "플레이어 1의 라이프가 0이 되어 플레이어 2가 승리했습니다.");
            else if (Players[1].Life <= 0) Finish(0, "플레이어 2의 라이프가 0이 되어 플레이어 1이 승리했습니다.");
        }

        private void Finish(int winner, string reason)
        {
            if (IsGameOver) return;
            IsGameOver = true;
            Winner = winner;
            ResultReason = reason;
            Write(reason);
        }

        private static void Shuffle(List<CardInstance> cards, Random random)
        {
            for (int index = cards.Count - 1; index > 0; index--)
            {
                int selected = random.Next(index + 1);
                var temporary = cards[index];
                cards[index] = cards[selected];
                cards[selected] = temporary;
            }
        }

        private static string PhaseName(DuelPhase phase)
        {
            switch (phase)
            {
                case DuelPhase.Draw: return "드로우 페이즈";
                case DuelPhase.Main1: return "메인 페이즈 1";
                case DuelPhase.Battle: return "배틀 페이즈";
                case DuelPhase.Main2: return "메인 페이즈 2";
                default: return "엔드 페이즈";
            }
        }

        private ActionResult Succeed(string message) { Write(message); return ActionResult.Ok(message); }
        private static ActionResult Fail(string message) { return ActionResult.Fail(message); }
        private void Write(string message) { log.Add(message); }
    }
}
