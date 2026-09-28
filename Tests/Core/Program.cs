using Convergence.Duel;
using System.Text.Json;

internal static class Program
{
    private static readonly IReadOnlyList<CardDefinition> Catalog = new[]
    {
        Unit("blade", "Series A", 7, 10),
        Unit("shield", "Series B", 3, 12),
        Unit("glass", "Series C", 8, 5),
        Unit("sturdy", "Series D", 2, 20),
        Spell("damage", EffectKind.DamageUnit, TargetKind.EnemyUnit, 6),
        Spell("destroy", EffectKind.DestroyUnit, TargetKind.AnyUnit),
        Spell("boost", EffectKind.BoostUnit, TargetKind.FriendlyUnit, 4),
        Spell("draw", EffectKind.Draw, amount: 2),
        Spell("special", EffectKind.SpecialSummonFromHand, TargetKind.HandCharacter),
        Spell("revive", EffectKind.Revive, TargetKind.GraveyardCharacter),
        Spell("token", EffectKind.CreateToken, amount: 1),
        Spell("heal", EffectKind.HealPlayer, amount: 10),
        Trap("negate", EffectKind.Negate),
        Trap("attack-negate", EffectKind.Negate, trigger: InterceptTrigger.AttackOnly),
        Trap("damage-trap", EffectKind.DamageUnit, TargetKind.EnemyUnit, 6),
        Trap("destroy-trap", EffectKind.DestroyUnit, TargetKind.EnemyUnit),
        Trap("sacrifice-trap", EffectKind.DestroyUnit, TargetKind.FriendlyUnit),
        Trap("token-trap", EffectKind.CreateToken, amount: 1),
        Trap("draw-trap", EffectKind.Draw, amount: 100),
        new CardDefinition("token-unit", "Token", "Shared", "", "", CardKind.Token, 2, 3)
    };

    private static int Main()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("mixed-series deck is accepted", MixedSeriesDeck),
            ("deck size, copies, unknown cards and tokens are rejected", InvalidDecks),
            ("main character stays in deck and shuffle is deterministic", SetupAndShuffle),
            ("normal summon limit and special summon bypass", NormalAndSpecialSummons),
            ("wrong turn, phase and card ownership fail without mutation", InvalidActions),
            ("first player cannot attack on turn one", FirstTurnBattleRestriction),
            ("combat damage is simultaneous and health persists", CombatDamage),
            ("direct attack requires empty opposing field", DirectAttack),
            ("attack can be made only once per turn", AttackLimit),
            ("response priority alternates and needs two consecutive passes", PriorityAndPasses),
            ("pending chain blocks other actions and phase changes", ChainBlocksActions),
            ("intercepts cannot activate during their set turn", SetTurnRestriction),
            ("negation resolves last in first out", Negation),
            ("a negated negate preserves the original effect", CounterNegation),
            ("attack-only intercept rejects an effect-only chain", AttackOnlyRestriction),
            ("sample attack response must follow the attack directly", SampleAttackResponse),
            ("effect targets respect ownership and zone without consuming cards", InvalidEffectTargets),
            ("removed targets fizzle without retargeting", StaleTarget),
            ("attack rechecks blockers and targets after responses", AttackRevalidation),
            ("deck-out during a response stops and cleans up the chain", DeckOutDuringChain),
            ("life victory cleans up resolved response sources", LifeVictoryCleanup),
            ("revival restores health and tokens leave the graveyard", RevivalAndTokens),
            ("empty draw pile loses the duel", DeckOut),
            ("full field rejects summons without consuming resources", FieldCapacity),
            ("sample decks validate and seeded randomized play preserves invariants", RandomizedPlay),
            ("baseline bot completes deterministic matches", BaselineBotPlay),
            ("three guided tutorials require real resolved actions", TutorialTests.RunAll),
            ("profile rewards, ownership, shop and persistence", ProgressTests.RunAll)
        }.Concat(FieldTests.Cases).ToArray();
        var failures = new List<string>();
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS  " + test.Name); }
            catch (Exception error)
            {
                failures.Add(test.Name + ": " + error.Message);
                Console.WriteLine("FAIL  " + test.Name + "\n      " + error);
            }
        }
        Console.WriteLine($"\n{cases.Length - failures.Count}/{cases.Length} checks passed.");
        return failures.Count == 0 ? 0 : 1;
    }

    private static CardDefinition Unit(string id, string series, int attack, int health) =>
        new CardDefinition(id, id, series, "", "", CardKind.Character, attack, health);

    private static CardDefinition Spell(string id, EffectKind effect, TargetKind target = TargetKind.None, int amount = 0) =>
        new CardDefinition(id, id, "Shared", "", "", CardKind.Command, effect: effect, amount: amount, target: target);

    private static CardDefinition Trap(string id, EffectKind effect, TargetKind target = TargetKind.None,
        int amount = 0, InterceptTrigger trigger = InterceptTrigger.AnyChain) =>
        new CardDefinition(id, id, "Shared", "", "", CardKind.Intercept, effect: effect,
            amount: amount, target: target, trigger: trigger);

    private static DuelRules FixtureRules() => new DuelRules
    {
        MinDeckSize = 1, MaxDeckSize = 100, MaxCopies = 100, StartingHand = 60,
        StartingLife = 100, FirstPlayerCannotAttackFirstTurn = false
    };

    private static string[] FixtureDeck(IEnumerable<string> required, string filler = "sturdy")
    {
        var deck = required.Distinct().SelectMany(id => Enumerable.Repeat(id, 5)).ToList();
        Check(deck.Count <= 64, "Fixture has too many required card types.");
        deck.AddRange(Enumerable.Repeat(filler, 64 - deck.Count));
        return deck.ToArray();
    }

    private static DuelEngine Game(string[] first = null, string[] second = null, Action<DuelRules> configure = null)
    {
        var rules = FixtureRules();
        configure?.Invoke(rules);
        return new DuelEngine(Catalog, rules, FixtureDeck(first ?? new[] { "blade" }),
            FixtureDeck(second ?? new[] { "shield" }), 173);
    }

    private static CardInstance Hand(DuelEngine game, int player, string id) =>
        game.Players[player].Hand.First(c => c.Definition.Id == id);

    private static void MainPhase(DuelEngine game)
    {
        Check(game.Phase == DuelPhase.Draw, "Expected draw phase.");
        Ok(game.AdvancePhase(game.ActivePlayer));
        Check(game.Phase == DuelPhase.Main1, "Draw should advance to first main phase.");
    }

    private static void Resolve(DuelEngine game)
    {
        Check(game.Chain.Count > 0, "Expected pending chain.");
        Ok(game.PassPriority(game.PriorityPlayer));
        Ok(game.PassPriority(game.PriorityPlayer));
        Check(game.Chain.Count == 0, "Two passes should resolve the chain.");
    }

    private static void NextTurn(DuelEngine game)
    {
        var startingTurn = game.TurnNumber;
        for (var guard = 0; guard < 6 && game.TurnNumber == startingTurn; guard++)
            Ok(game.AdvancePhase(game.ActivePlayer));
        Check(game.TurnNumber == startingTurn + 1, "Turn failed to advance.");
        MainPhase(game);
    }

    private static void Battle(DuelEngine game)
    {
        Check(game.Phase == DuelPhase.Main1, "Expected first main phase before battle.");
        Ok(game.AdvancePhase(game.ActivePlayer));
        Check(game.Phase == DuelPhase.Battle, "Expected battle phase.");
    }

    private static CardInstance Summon(DuelEngine game, int player, string id)
    {
        var card = Hand(game, player, id);
        Ok(game.Summon(player, card.InstanceId));
        return card;
    }

    private static CardInstance Set(DuelEngine game, int player, string id)
    {
        var card = Hand(game, player, id);
        Ok(game.SetIntercept(player, card.InstanceId));
        return card;
    }

    private static CardInstance Cast(DuelEngine game, int player, string id, int? target = null)
    {
        var card = Hand(game, player, id);
        Ok(game.Activate(player, card.InstanceId, target));
        return card;
    }

    private static void MixedSeriesDeck()
    {
        var rules = new DuelRules { MinDeckSize = 4, MaxDeckSize = 8, MaxCopies = 3 };
        Check(DeckValidator.Validate(new[] { "blade", "shield", "glass", "sturdy" }, Catalog, rules).Count == 0,
            "Series metadata must never restrict deck membership.");
    }

    private static void InvalidDecks()
    {
        var rules = new DuelRules { MinDeckSize = 2, MaxDeckSize = 4, MaxCopies = 2 };
        foreach (var deck in new[]
        {
            new[] { "blade" },
            new[] { "blade", "blade", "shield", "shield", "glass" },
            new[] { "blade", "blade", "blade" },
            new[] { "blade", "missing-id" },
            new[] { "blade", "token-unit" }
        })
            Check(DeckValidator.Validate(deck, Catalog, rules).Count > 0, "Invalid deck was accepted: " + string.Join(",", deck));
    }

    private static void SetupAndShuffle()
    {
        var deck = FixtureDeck(new[] { "blade", "shield", "glass" });
        var first = new DuelEngine(Catalog, FixtureRules(), deck, deck, 27);
        var same = new DuelEngine(Catalog, FixtureRules(), deck, deck, 27);
        var other = new DuelEngine(Catalog, FixtureRules(), deck, deck, 91);
        Check(Snapshot(first) == Snapshot(same), "Same seed must reproduce initial state.");
        Check(Snapshot(first) != Snapshot(other), "Different seeds should change this varied deck's initial state.");
        foreach (var player in first.Players)
        {
            Check(player.Deck.Count + player.Hand.Count == deck.Length, "Main selection removed a card.");
            Check(!string.IsNullOrEmpty(player.MainCharacterId), "Main character should be selected.");
            Check(player.Deck.Concat(player.Hand).Any(c => c.Definition.Id == player.MainCharacterId), "Selected main is missing.");
            foreach (var id in deck.Distinct())
                Check(player.Deck.Concat(player.Hand).Count(c => c.Definition.Id == id) == deck.Count(c => c == id), "Card count changed during setup.");
        }
        Invariants(first);
    }

    private static void NormalAndSpecialSummons()
    {
        var game = Game(new[] { "blade", "shield", "special" });
        MainPhase(game);
        Summon(game, 0, "blade");
        var target = Hand(game, 0, "shield");
        RejectUnchanged(game, () => game.Summon(0, target.InstanceId));
        Cast(game, 0, "special", target.InstanceId);
        Resolve(game);
        Check(target.Zone == CardZone.Field && game.Players[0].Field.Count == 2, "Special summon did not reach the field.");
        Check(game.Players[0].NormalSummonsUsed == 1, "Special summon consumed normal summon count.");
        NextTurn(game);
        NextTurn(game);
        Summon(game, 0, "blade");
        Check(game.Players[0].NormalSummonsUsed == 1, "Normal summon allowance did not reset.");
    }

    private static void InvalidActions()
    {
        var game = Game(new[] { "blade", "damage" }, new[] { "shield" });
        RejectUnchanged(game, () => game.Summon(0, Hand(game, 0, "blade").InstanceId));
        RejectUnchanged(game, () => game.AdvancePhase(1));
        MainPhase(game);
        RejectUnchanged(game, () => game.Summon(1, Hand(game, 1, "shield").InstanceId));
        RejectUnchanged(game, () => game.Summon(0, Hand(game, 1, "shield").InstanceId));
        RejectUnchanged(game, () => game.Summon(0, int.MaxValue));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "damage").InstanceId));
        RejectUnchanged(game, () => game.PassPriority(0));
        var unit = Summon(game, 0, "blade");
        RejectUnchanged(game, () => game.Attack(0, unit.InstanceId));
        Battle(game);
        RejectUnchanged(game, () => game.Summon(0, Hand(game, 0, "blade").InstanceId));
    }

    private static void FirstTurnBattleRestriction()
    {
        var game = Game(configure: rules => rules.FirstPlayerCannotAttackFirstTurn = true);
        MainPhase(game);
        var unit = Summon(game, 0, "blade");
        Battle(game);
        RejectUnchanged(game, () => game.Attack(0, unit.InstanceId));
        NextTurn(game);
        var enemy = Summon(game, 1, "shield");
        Battle(game);
        Ok(game.Attack(1, enemy.InstanceId, unit.InstanceId));
        Resolve(game);
    }

    private static void CombatDamage()
    {
        var game = Game(new[] { "blade" }, new[] { "shield", "glass" });
        MainPhase(game);
        var defender = Summon(game, 0, "blade");
        NextTurn(game);
        var attacker = Summon(game, 1, "shield");
        Battle(game);
        Ok(game.Attack(1, attacker.InstanceId, defender.InstanceId));
        Check(attacker.Health == 12 && defender.Health == 10, "Attack must wait for chain resolution.");
        Resolve(game);
        Check(attacker.Health == 5 && defender.Health == 7, "Combat must apply damage in both directions.");
        NextTurn(game);
        Check(defender.Health == 7 && attacker.Health == 5, "Damage healed at turn boundary.");
        Battle(game);
        Ok(game.Attack(0, defender.InstanceId, attacker.InstanceId));
        Resolve(game);
        Check(attacker.Zone == CardZone.Graveyard && defender.Health == 4, "Lethal defender must still deal counterdamage.");

        var lethal = Game(new[] { "glass" }, new[] { "glass" });
        MainPhase(lethal);
        var a = Summon(lethal, 0, "glass");
        NextTurn(lethal);
        var b = Summon(lethal, 1, "glass");
        Battle(lethal);
        Ok(lethal.Attack(1, b.InstanceId, a.InstanceId));
        Resolve(lethal);
        Check(a.Zone == CardZone.Graveyard && b.Zone == CardZone.Graveyard, "Mutual lethal damage must destroy both units.");
    }

    private static void DirectAttack()
    {
        var game = Game(new[] { "blade", "destroy" }, new[] { "shield" });
        MainPhase(game);
        var attacker = Summon(game, 0, "blade");
        NextTurn(game);
        var defender = Summon(game, 1, "shield");
        NextTurn(game);
        Battle(game);
        RejectUnchanged(game, () => game.Attack(0, attacker.InstanceId));
        Ok(game.AdvancePhase(0));
        Cast(game, 0, "destroy", defender.InstanceId);
        Resolve(game);
        NextTurn(game);
        NextTurn(game);
        Battle(game);
        var life = game.Players[1].Life;
        Ok(game.Attack(0, attacker.InstanceId));
        Resolve(game);
        Check(game.Players[1].Life == life - attacker.Attack, "Direct attack did not reduce opponent life.");
    }

    private static void AttackLimit()
    {
        var game = Game();
        MainPhase(game);
        var attacker = Summon(game, 0, "blade");
        Battle(game);
        Ok(game.Attack(0, attacker.InstanceId));
        Resolve(game);
        RejectUnchanged(game, () => game.Attack(0, attacker.InstanceId));
        NextTurn(game);
        NextTurn(game);
        Battle(game);
        Ok(game.Attack(0, attacker.InstanceId));
        Resolve(game);
    }

    private static void PriorityAndPasses()
    {
        var game = Game(new[] { "heal", "negate" }, new[] { "heal", "negate" },
            rules => rules.CanActivateInterceptOnSetTurn = true);
        MainPhase(game);
        var counter0 = Set(game, 0, "negate");
        NextTurn(game);
        var counter1 = Set(game, 1, "negate");
        Cast(game, 1, "heal");
        Check(game.PriorityPlayer == 0, "Opponent should receive priority after activation.");
        RejectUnchanged(game, () => game.PassPriority(1));
        Ok(game.PassPriority(0));
        Check(game.Chain.Count == 1 && game.PriorityPlayer == 1, "First pass resolved too early.");
        Ok(game.Activate(1, counter1.InstanceId));
        Check(game.PriorityPlayer == 0 && game.Chain.Count == 2, "Response did not transfer priority.");
        Ok(game.Activate(0, counter0.InstanceId));
        Check(game.PriorityPlayer == 1 && game.Chain.Count == 3, "Second response did not transfer priority.");
        Ok(game.PassPriority(1));
        Check(game.Chain.Count == 3, "Adding a response must reset the consecutive pass count.");
        Ok(game.PassPriority(0));
        Check(game.Chain.Count == 0 && game.PriorityPlayer == -1, "Resolved chain retained priority.");
    }

    private static void ChainBlocksActions()
    {
        var game = Game(new[] { "blade", "heal", "negate" });
        MainPhase(game);
        Cast(game, 0, "heal");
        RejectUnchanged(game, () => game.AdvancePhase(0));
        RejectUnchanged(game, () => game.Summon(0, Hand(game, 0, "blade").InstanceId));
        RejectUnchanged(game, () => game.SetIntercept(0, Hand(game, 0, "negate").InstanceId));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "heal").InstanceId));
        Resolve(game);
        Summon(game, 0, "blade");
    }

    private static void SetTurnRestriction()
    {
        var game = Game(new[] { "heal", "negate" }, new[] { "heal" });
        MainPhase(game);
        var counter = Set(game, 0, "negate");
        Cast(game, 0, "heal");
        Ok(game.PassPriority(1));
        RejectUnchanged(game, () => game.Activate(0, counter.InstanceId));
        Ok(game.PassPriority(0));
        NextTurn(game);
        Cast(game, 1, "heal");
        Ok(game.Activate(0, counter.InstanceId));
        Resolve(game);
        Check(counter.Zone == CardZone.Graveyard, "Activated intercept should move to the graveyard.");
    }

    private static void Negation()
    {
        var game = Game(new[] { "blade", "negate" }, new[] { "damage" });
        MainPhase(game);
        var unit = Summon(game, 0, "blade");
        var counter = Set(game, 0, "negate");
        NextTurn(game);
        var damage = Cast(game, 1, "damage", unit.InstanceId);
        Ok(game.Activate(0, counter.InstanceId));
        Resolve(game);
        Check(unit.Health == unit.Definition.MaxHealth, "Negated damage still resolved.");
        Check(damage.Zone == CardZone.Graveyard && counter.Zone == CardZone.Graveyard, "Resolved sources should leave the chain.");
    }

    private static void CounterNegation()
    {
        var game = Game(new[] { "blade", "negate" }, new[] { "damage", "negate" });
        MainPhase(game);
        var unit = Summon(game, 0, "blade");
        var counter0 = Set(game, 0, "negate");
        NextTurn(game);
        var counter1 = Set(game, 1, "negate");
        NextTurn(game);
        NextTurn(game);
        Cast(game, 1, "damage", unit.InstanceId);
        Ok(game.Activate(0, counter0.InstanceId));
        Ok(game.Activate(1, counter1.InstanceId));
        Resolve(game);
        Check(unit.Health == 4, "Negating the negate should allow original damage to resolve.");
    }

    private static void AttackOnlyRestriction()
    {
        var game = Game(new[] { "blade", "attack-negate" }, new[] { "shield", "heal" });
        MainPhase(game);
        var defender = Summon(game, 0, "blade");
        var counter = Set(game, 0, "attack-negate");
        NextTurn(game);
        var attacker = Summon(game, 1, "shield");
        Cast(game, 1, "heal");
        RejectUnchanged(game, () => game.Activate(0, counter.InstanceId));
        Resolve(game);
        Battle(game);
        Ok(game.Attack(1, attacker.InstanceId, defender.InstanceId));
        Ok(game.Activate(0, counter.InstanceId));
        Resolve(game);
        Check(attacker.Health == 12 && defender.Health == 10, "Negated attack dealt damage.");
        RejectUnchanged(game, () => game.Attack(1, attacker.InstanceId, defender.InstanceId));
    }

    private static void StaleTarget()
    {
        var game = Game(new[] { "blade", "destroy-trap" }, new[] { "glass", "boost" });
        MainPhase(game);
        var destroy = Set(game, 0, "destroy-trap");
        NextTurn(game);
        var target = Summon(game, 1, "glass");
        var boost = Cast(game, 1, "boost", target.InstanceId);
        Ok(game.Activate(0, destroy.InstanceId, target.InstanceId));
        Resolve(game);
        Check(target.Zone == CardZone.Graveyard && target.BonusAttack == 0, "Removed target received a later effect.");
        Check(boost.Zone == CardZone.Graveyard, "Fizzled command did not finish resolving.");
    }

    private static void SampleAttackResponse()
    {
        var game = new DuelEngine(SampleContent.CreateCatalog(), FixtureRules(),
            FixtureDeck(new[] { "amber", "hold_the_line", "disrupt" }, "march7"),
            FixtureDeck(new[] { "kaeya" }, "march7"), 173);
        MainPhase(game);
        var defender = Summon(game, 0, "amber");
        var hold = Set(game, 0, "hold_the_line");
        var disrupt = Set(game, 0, "disrupt");
        NextTurn(game);
        var attacker = Summon(game, 1, "kaeya");
        Battle(game);
        Ok(game.Attack(1, attacker.InstanceId, defender.InstanceId));
        RejectUnchanged(game, () => game.Activate(0, hold.InstanceId, attacker.InstanceId));
        Ok(game.Activate(0, disrupt.InstanceId));
        Ok(game.PassPriority(1));
        RejectUnchanged(game, () => game.Activate(0, hold.InstanceId));
        Ok(game.PassPriority(0));
        Check(hold.Zone == CardZone.Backrow && disrupt.Zone == CardZone.Graveyard,
            "Invalid attack-only response was consumed or valid response stayed in the chain.");
        NextTurn(game);
        NextTurn(game);
        Battle(game);
        Ok(game.Attack(1, attacker.InstanceId, defender.InstanceId));
        Ok(game.Activate(0, hold.InstanceId));
        Resolve(game);
        Check(defender.Health == defender.Definition.MaxHealth && attacker.Health == attacker.Definition.MaxHealth,
            "Production hold_the_line failed to negate a direct attack response.");
    }

    private static void InvalidEffectTargets()
    {
        var game = Game(new[] { "blade", "damage", "boost", "special", "revive", "heal" }, new[] { "shield" });
        MainPhase(game);
        var own = Summon(game, 0, "blade");
        NextTurn(game);
        var enemy = Summon(game, 1, "shield");
        NextTurn(game);
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "damage").InstanceId, own.InstanceId));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "boost").InstanceId, enemy.InstanceId));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "special").InstanceId, Hand(game, 1, "shield").InstanceId));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "revive").InstanceId, own.InstanceId));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "heal").InstanceId, own.InstanceId));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "damage").InstanceId, int.MaxValue));
        Battle(game);
        RejectUnchanged(game, () => game.Attack(0, own.InstanceId, own.InstanceId));
        RejectUnchanged(game, () => game.Attack(0, own.InstanceId, Hand(game, 1, "shield").InstanceId));
    }

    private static void RevivalAndTokens()
    {
        var game = Game(new[] { "glass", "destroy", "revive", "token" });
        MainPhase(game);
        var unit = Summon(game, 0, "glass");
        Cast(game, 0, "destroy", unit.InstanceId);
        Resolve(game);
        Check(unit.Zone == CardZone.Graveyard, "Unit failed to enter graveyard.");
        Cast(game, 0, "revive", unit.InstanceId);
        Resolve(game);
        Check(unit.Zone == CardZone.Field && unit.Health == unit.Definition.MaxHealth, "Revival did not restore the unit.");
        Cast(game, 0, "token");
        Resolve(game);
        var token = game.Players[0].Field.Single(c => c.Definition.Kind == CardKind.Token);
        Cast(game, 0, "destroy", token.InstanceId);
        Resolve(game);
        Check(token.Zone == CardZone.Banished && !game.Players[0].Graveyard.Contains(token), "Destroyed token entered graveyard.");
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "revive").InstanceId, token.InstanceId));
        Invariants(game);
    }

    private static void AttackRevalidation()
    {
        var blocked = Game(new[] { "token-trap" }, new[] { "shield" });
        MainPhase(blocked);
        var tokenTrap = Set(blocked, 0, "token-trap");
        NextTurn(blocked);
        var attacker = Summon(blocked, 1, "shield");
        Battle(blocked);
        Ok(blocked.Attack(1, attacker.InstanceId));
        Ok(blocked.Activate(0, tokenTrap.InstanceId));
        Resolve(blocked);
        Check(blocked.Players[0].Life == 100, "Direct attack ignored the blocker summoned in response.");
        Check(blocked.Players[0].Field.Count == 1, "Response failed to create its blocker.");

        var removed = Game(new[] { "blade", "sacrifice-trap" }, new[] { "shield" });
        MainPhase(removed);
        var defender = Summon(removed, 0, "blade");
        var sacrifice = Set(removed, 0, "sacrifice-trap");
        NextTurn(removed);
        var otherAttacker = Summon(removed, 1, "shield");
        Battle(removed);
        Ok(removed.Attack(1, otherAttacker.InstanceId, defender.InstanceId));
        Ok(removed.Activate(0, sacrifice.InstanceId, defender.InstanceId));
        Resolve(removed);
        Check(removed.Players[0].Life == 100 && otherAttacker.Health == 12,
            "Attack against a removed target redirected to the player or dealt counterdamage.");
    }

    private static void DeckOutDuringChain()
    {
        var game = Game(new[] { "blade", "draw-trap" }, new[] { "damage" });
        MainPhase(game);
        var unit = Summon(game, 0, "blade");
        var draw = Set(game, 0, "draw-trap");
        NextTurn(game);
        var damage = Cast(game, 1, "damage", unit.InstanceId);
        Ok(game.Activate(0, draw.InstanceId));
        Resolve(game);
        Check(game.IsGameOver && game.Winner == 1, "Response deck-out did not finish the duel.");
        Check(unit.Health == 10, "Lower chain effect resolved after the duel ended.");
        Check(draw.Zone == CardZone.Graveyard && damage.Zone == CardZone.Graveyard,
            "Game-ending resolution left consumed cards in the chain.");
        Invariants(game);
    }

    private static void LifeVictoryCleanup()
    {
        var game = Game(new[] { "blade" }, new[] { "damage-trap" }, rules => rules.StartingLife = 5);
        MainPhase(game);
        var attacker = Summon(game, 0, "blade");
        NextTurn(game);
        var response = Set(game, 1, "damage-trap");
        NextTurn(game);
        Battle(game);
        Ok(game.Attack(0, attacker.InstanceId));
        Ok(game.Activate(1, response.InstanceId, attacker.InstanceId));
        Resolve(game);
        Check(game.IsGameOver && game.Winner == 0 && game.Players[1].Life == 0,
            "Lethal direct attack did not declare the correct winner.");
        Check(attacker.Health == 4 && response.Zone == CardZone.Graveyard,
            "Winning attack skipped the response or left its source in the chain.");
        Invariants(game);
    }

    private static void DeckOut()
    {
        var rules = new DuelRules { MinDeckSize = 1, MaxDeckSize = 3, StartingHand = 1 };
        var game = new DuelEngine(Catalog, rules, new[] { "blade" }, new[] { "shield" }, 1);
        MainPhase(game);
        while (game.ActivePlayer == 0) Ok(game.AdvancePhase(0));
        Ok(game.AdvancePhase(1));
        Check(game.IsGameOver && game.Winner == 0, "Player drawing an empty deck must lose.");
        RejectUnchanged(game, () => game.AdvancePhase(1));
        RejectUnchanged(game, () => game.Summon(1, Hand(game, 1, "shield").InstanceId));
    }

    private static void FieldCapacity()
    {
        var game = Game(new[] { "blade", "special" }, configure: rules =>
        {
            rules.FieldSlots = 1;
            rules.NormalSummonsPerTurn = 2;
        });
        MainPhase(game);
        Summon(game, 0, "blade");
        RejectUnchanged(game, () => game.Summon(0, Hand(game, 0, "blade").InstanceId));
        RejectUnchanged(game, () => game.Activate(0, Hand(game, 0, "special").InstanceId, Hand(game, 0, "blade").InstanceId));
        Check(game.Players[0].NormalSummonsUsed == 1, "Failed summon consumed summon allowance.");
    }

    private static void RandomizedPlay()
    {
        var catalog = SampleContent.CreateCatalog();
        var starter = SampleContent.CreateStarterDeck();
        var opponent = SampleContent.CreateOpponentDeck();
        Check(DeckValidator.Validate(starter, catalog, new DuelRules()).Count == 0, "Starter deck is invalid.");
        Check(DeckValidator.Validate(opponent, catalog, new DuelRules()).Count == 0, "Opponent deck is invalid.");
        var completed = 0;
        for (var seed = 0; seed < 32; seed++)
        {
            var game = new DuelEngine(catalog, new DuelRules(), starter, opponent, seed);
            var random = new Random(seed + 81);
            for (var step = 0; step < 1600 && !game.IsGameOver; step++)
            {
                var before = Snapshot(game);
                var action = RandomAction(game, random);
                if (!action.Success) Check(Snapshot(game) == before, $"Rejected action changed state: seed {seed}, step {step}, {action.Message}");
                Invariants(game);
            }
            Check(game.IsGameOver, $"Randomized match {seed} failed to finish within its step budget.");
            completed++;
        }
        Console.WriteLine($"      {completed} seeded matches completed with state checks after every action.");
    }

    private static ActionResult RandomAction(DuelEngine game, Random random)
    {
        var player = game.Chain.Count > 0 ? game.PriorityPlayer : game.ActivePlayer;
        if (game.Chain.Count > 0)
        {
            var traps = game.Players[player].Backrow;
            if (traps.Count > 0 && random.Next(3) == 0)
            {
                var trap = traps[random.Next(traps.Count)];
                return game.Activate(player, trap.InstanceId, RandomTarget(game, player, trap, random));
            }
            return game.PassPriority(player);
        }
        if (game.Phase == DuelPhase.Battle && random.Next(3) != 0 && game.Players[player].Field.Count > 0)
        {
            var field = game.Players[player].Field;
            var enemy = game.Players[1 - player].Field;
            return game.Attack(player, field[random.Next(field.Count)].InstanceId,
                enemy.Count == 0 ? null : enemy[random.Next(enemy.Count)].InstanceId);
        }
        if ((game.Phase == DuelPhase.Main1 || game.Phase == DuelPhase.Main2) && random.Next(5) != 0)
        {
            var hand = game.Players[player].Hand;
            if (hand.Count == 0) return game.AdvancePhase(player);
            var card = hand[random.Next(hand.Count)];
            if (card.Definition.Kind == CardKind.Character) return game.Summon(player, card.InstanceId);
            if (card.Definition.Kind == CardKind.Intercept) return game.SetIntercept(player, card.InstanceId);
            return game.Activate(player, card.InstanceId, RandomTarget(game, player, card, random));
        }
        return game.AdvancePhase(player);
    }

    private static void BaselineBotPlay()
    {
        var catalog = SampleContent.CreateCatalog();
        for (var seed = 0; seed < 8; seed++)
        {
            var game = new DuelEngine(catalog, new DuelRules(), SampleContent.CreateStarterDeck(),
                SampleContent.CreateOpponentDeck(), seed);
            for (var step = 0; step < 1600 && !game.IsGameOver; step++)
            {
                var player = game.Chain.Count > 0 ? game.PriorityPlayer : game.ActivePlayer;
                Ok(SimpleDuelBot.Step(game, player));
                Invariants(game);
            }
            Check(game.IsGameOver, $"Baseline bot match {seed} did not finish.");
        }
    }

    private static int? RandomTarget(DuelEngine game, int player, CardInstance card, Random random)
    {
        IEnumerable<CardInstance> candidates = card.Definition.Target switch
        {
            TargetKind.FriendlyUnit => game.Players[player].Field,
            TargetKind.EnemyUnit => game.Players[1 - player].Field,
            TargetKind.AnyUnit => game.Players.SelectMany(p => p.Field),
            TargetKind.HandCharacter => game.Players[player].Hand.Where(c => c.Definition.Kind == CardKind.Character),
            TargetKind.GraveyardCharacter => game.Players[player].Graveyard.Where(c => c.Definition.Kind == CardKind.Character),
            _ => Array.Empty<CardInstance>()
        };
        var choices = candidates.ToArray();
        return choices.Length == 0 ? null : choices[random.Next(choices.Length)].InstanceId;
    }

    private static void Invariants(DuelEngine game)
    {
        Check(game.ActivePlayer == 0 || game.ActivePlayer == 1, "Invalid active player.");
        Check(game.Chain.Count == 0 ? game.PriorityPlayer == -1 : game.PriorityPlayer == 0 || game.PriorityPlayer == 1, "Priority and chain disagree.");
        var ids = new HashSet<int>();
        foreach (var player in game.Players)
        {
            Check(player.Field.Count <= game.Rules.FieldSlots, "Field exceeds capacity.");
            Check(player.Backrow.Count <= game.Rules.BackrowSlots, "Backrow exceeds capacity.");
            Check(player.NormalSummonsUsed >= 0 && player.NormalSummonsUsed <= game.Rules.NormalSummonsPerTurn, "Invalid normal summon count.");
            Check(player.Field.All(card => card.Health > 0 && card.IsUnit), "Dead or non-unit card on field.");
            Check(player.Graveyard.All(card => card.Definition.Kind != CardKind.Token), "Token in graveyard.");
            foreach (var zone in new[]
            {
                (CardZone.Deck, player.Deck), (CardZone.Hand, player.Hand),
                (CardZone.Field, player.Field), (CardZone.Backrow, player.Backrow),
                (CardZone.Graveyard, player.Graveyard),
                (CardZone.Domain, (IReadOnlyList<CardInstance>)(player.DomainCard == null ? Array.Empty<CardInstance>() : new[] { player.DomainCard }))
            })
            foreach (var card in zone.Item2)
            {
                Check(ids.Add(card.InstanceId), "Card occurs in multiple zones.");
                Check(card.Owner == player.Id && card.Zone == zone.Item1, "Card owner or zone disagrees with containing list.");
                Check(ReferenceEquals(game.FindCard(card.InstanceId), card), "Card lookup returned a different instance.");
            }
        }
        foreach (var link in game.Chain.Where(c => c.Kind == ChainLinkKind.Effect))
        {
            Check(ids.Add(link.Source.InstanceId), "Effect source still appears in another zone.");
            Check(link.Source.Zone == CardZone.Chain, "Effect source is not in the chain zone.");
        }
        if (game.IsGameOver) Check(game.Winner.HasValue, "Finished game has no result.");
    }

    private static string Snapshot(DuelEngine game) => JsonSerializer.Serialize(new
    {
        game.ActivePlayer, game.PriorityPlayer, game.Phase, game.TurnNumber,
        game.IsGameOver, game.Winner, game.ResultReason,
        Log = game.Log,
        Players = game.Players.Select(p => new
        {
            p.Id, p.Life, p.NormalSummonsUsed, p.MainCharacterId,
            Deck = p.Deck.Select(CardState), Hand = p.Hand.Select(CardState),
            Field = p.Field.Select(CardState), Backrow = p.Backrow.Select(CardState),
            Graveyard = p.Graveyard.Select(CardState),
            Domain = p.DomainCard == null ? null : CardState(p.DomainCard)
        }),
        Chain = game.Chain.Select(c => new { c.Id, c.Kind, c.Controller, Source = CardState(c.Source), c.TargetId, c.NegatesLinkId, c.Negated })
    });

    private static object CardState(CardInstance card) => new
    {
        card.InstanceId, Definition = card.Definition.Id, card.Owner, card.Zone,
        card.Health, card.BonusAttack, card.AuraAttack, card.AttacksMade, card.SetOnTurn
    };

    private static void RejectUnchanged(DuelEngine game, Func<ActionResult> action)
    {
        var before = Snapshot(game);
        var result = action();
        Check(!result.Success, "Expected action to be rejected.");
        Check(Snapshot(game) == before, "Rejected action changed state: " + result.Message);
        Invariants(game);
    }

    private static void Ok(ActionResult result) => Check(result.Success, "Action failed: " + result.Message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
