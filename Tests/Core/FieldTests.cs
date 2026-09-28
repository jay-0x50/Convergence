using Convergence.Duel;

internal static class FieldTests
{
    private static readonly IReadOnlyList<CardDefinition> Catalog = new[]
    {
        new CardDefinition("unit", "Unit", "A", "", "", CardKind.Character, 1000, 2000),
        new CardDefinition("other", "Other", "B", "", "", CardKind.Character, 1200, 1500),
        new CardDefinition("field", "Field", "A", "", "", CardKind.Domain, effect: EffectKind.FieldAura, amount: 300),
        new CardDefinition("small-field", "Small field", "B", "", "", CardKind.Domain, effect: EffectKind.FieldAura, amount: 100),
        new CardDefinition("boost", "Boost", "", "", "", CardKind.Command,
            effect: EffectKind.BoostUnit, amount: 500, target: TargetKind.FriendlyUnit),
        new CardDefinition("special", "Special", "", "", "", CardKind.Command,
            effect: EffectKind.SpecialSummonFromHand, target: TargetKind.HandCharacter),
        new CardDefinition("destroy", "Destroy", "", "", "", CardKind.Command,
            effect: EffectKind.DestroyUnit, target: TargetKind.AnyUnit),
        new CardDefinition("revive", "Revive", "", "", "", CardKind.Command,
            effect: EffectKind.Revive, target: TargetKind.GraveyardCharacter),
        new CardDefinition("token", "Token spell", "", "", "", CardKind.Command,
            effect: EffectKind.CreateToken, amount: 1),
        new CardDefinition("negate", "Negate", "", "", "", CardKind.Intercept, effect: EffectKind.Negate)
    };

    public static IEnumerable<(string Name, Action Run)> Cases => new (string, Action)[]
    {
        ("field aura applies to existing units, later summons and tokens", AuraAndNewSummons),
        ("field replacement recomputes aura without stacking or erasing boosts", Replacement),
        ("negated field preserves previous domain and cleans chain sources", NegatedReplacement),
        ("counter-negation permits field replacement", CounterNegation),
        ("leaving the field removes aura and revival restores it once", LeaveAndRevive),
        ("domain has a separate slot and follows main-phase activation rules", SeparateSlotAndActivation),
        ("bot activates a domain and keeps a duplicate in hand", BotDomain),
        ("unshuffled setup preserves deck order and second-player initiative", UnshuffledSecondPlayer),
        ("duel rules copy preserves all values without sharing rule mutations", RuleCopy)
    };

    private static DuelEngine Game(string[] first, string[] second = null, Action<DuelRules> configure = null)
    {
        var rules = new DuelRules
        {
            MinDeckSize = 1, MaxDeckSize = 100, MaxCopies = 100,
            StartingHand = 12, NormalSummonsPerTurn = 2, ShuffleDeck = false
        };
        configure?.Invoke(rules);
        return new DuelEngine(Catalog, rules, Deck(first), Deck(second ?? new[] { "other" }));
    }

    private static List<string> Deck(string[] required)
    {
        var deck = required.ToList();
        while (deck.Count < 22) deck.Add("unit");
        return deck;
    }

    private static CardInstance Hand(DuelEngine game, int player, string id) =>
        game.Players[player].Hand.First(c => c.Definition.Id == id);

    private static CardInstance Summon(DuelEngine game, int player, string id = "unit")
    {
        var card = Hand(game, player, id);
        Ok(game.Summon(player, card.InstanceId));
        return card;
    }

    private static CardInstance Cast(DuelEngine game, int player, string id, int? target = null)
    {
        var card = Hand(game, player, id);
        Ok(game.Activate(player, card.InstanceId, target));
        Resolve(game);
        return card;
    }

    private static void Resolve(DuelEngine game)
    {
        Ok(game.PassPriority(game.PriorityPlayer));
        Ok(game.PassPriority(game.PriorityPlayer));
        Check(game.Chain.Count == 0 && game.PriorityPlayer == -1, "Chain did not finish cleanly.");
    }

    private static void MainPhase(DuelEngine game) => Ok(game.AdvancePhase(game.ActivePlayer));

    private static void NextTurn(DuelEngine game)
    {
        int turn = game.TurnNumber;
        for (int guard = 0; guard < 6 && game.TurnNumber == turn; guard++)
            Ok(game.AdvancePhase(game.ActivePlayer));
        Check(game.TurnNumber == turn + 1, "Turn did not advance.");
        MainPhase(game);
    }

    private static void AuraAndNewSummons()
    {
        var game = Game(new[] { "unit", "field", "other", "special", "token" });
        MainPhase(game);
        var existing = Summon(game, 0);
        var field = Hand(game, 0, "field");
        Ok(game.Activate(0, field.InstanceId));
        Check(existing.Attack == 1000 && game.Players[0].DomainCard == null,
            "A pending field applied its aura before chain resolution.");
        Resolve(game);
        Check(existing.Attack == 1300 && existing.AuraAttack == 300, "Existing unit missed aura.");
        var future = Hand(game, 0, "other");
        Cast(game, 0, "special", future.InstanceId);
        Cast(game, 0, "token");
        Check(future.Attack == 1500 && future.AuraAttack == 300, "Later special summon missed aura.");
        Check(game.Players[0].Field.Single(c => c.Definition.Kind == CardKind.Token).AuraAttack == 300,
            "Token missed aura.");
        Check(field.Zone == CardZone.Domain && game.Players[0].DomainCard == field &&
            !game.Players[0].Graveyard.Contains(field), "Persistent source was consumed by chain cleanup.");
        NextTurn(game);
        var enemy = Summon(game, 1, "other");
        Check(enemy.Attack == 1200 && enemy.AuraAttack == 0, "Domain affected the opposing player.");
    }

    private static void Replacement()
    {
        var game = Game(new[] { "unit", "field", "field", "small-field", "boost" });
        MainPhase(game);
        var unit = Summon(game, 0);
        var first = Cast(game, 0, "field");
        Cast(game, 0, "boost", unit.InstanceId);
        Check(unit.Attack == 1800, "Boost and aura should add independently.");
        var second = Cast(game, 0, "small-field");
        Check(first.Zone == CardZone.Graveyard && game.Players[0].DomainCard == second,
            "Replaced field did not leave its slot.");
        Check(unit.Attack == 1600 && unit.BonusAttack == 500 && unit.AuraAttack == 100,
            "Replacement stacked aura or erased the separate boost.");
        Cast(game, 0, "field");
        Check(second.Zone == CardZone.Graveyard && unit.Attack == 1800,
            "Repeated replacement permanently grew the unit attack.");
    }

    private static void NegatedReplacement()
    {
        var game = Game(new[] { "unit", "field", "small-field" }, new[] { "other", "negate" },
            rules => rules.FirstPlayer = 1);
        MainPhase(game);
        var trap = Hand(game, 1, "negate");
        Ok(game.SetIntercept(1, trap.InstanceId));
        NextTurn(game);
        var unit = Summon(game, 0);
        var first = Cast(game, 0, "field");
        var replacement = Hand(game, 0, "small-field");
        Ok(game.Activate(0, replacement.InstanceId));
        Check(game.Players[0].DomainCard == first && unit.Attack == 1300,
            "Announcing a replacement removed the active field too early.");
        Ok(game.Activate(1, trap.InstanceId));
        Resolve(game);
        Check(game.Players[0].DomainCard == first && first.Zone == CardZone.Domain && unit.Attack == 1300,
            "Negated replacement changed the old field or aura.");
        Check(replacement.Zone == CardZone.Graveyard && trap.Zone == CardZone.Graveyard,
            "Negated field or response was left in chain limbo.");
    }

    private static void CounterNegation()
    {
        var game = Game(new[] { "unit", "field", "small-field", "negate" }, new[] { "other", "negate" },
            rules => { rules.FirstPlayer = 1; rules.CanActivateInterceptOnSetTurn = true; });
        MainPhase(game);
        var enemyTrap = Hand(game, 1, "negate");
        Ok(game.SetIntercept(1, enemyTrap.InstanceId));
        NextTurn(game);
        var unit = Summon(game, 0);
        var original = Cast(game, 0, "field");
        var selfTrap = Hand(game, 0, "negate");
        Ok(game.SetIntercept(0, selfTrap.InstanceId));
        var replacement = Hand(game, 0, "small-field");
        Ok(game.Activate(0, replacement.InstanceId));
        Ok(game.Activate(1, enemyTrap.InstanceId));
        Ok(game.Activate(0, selfTrap.InstanceId));
        Resolve(game);
        Check(original.Zone == CardZone.Graveyard && game.Players[0].DomainCard == replacement && unit.Attack == 1100,
            "Counter-negation failed to resolve the surviving field replacement.");
    }

    private static void LeaveAndRevive()
    {
        var game = Game(new[] { "unit", "field", "boost", "destroy", "revive" });
        MainPhase(game);
        var unit = Summon(game, 0);
        Cast(game, 0, "field");
        Cast(game, 0, "boost", unit.InstanceId);
        Cast(game, 0, "destroy", unit.InstanceId);
        Check(unit.Zone == CardZone.Graveyard && unit.AuraAttack == 0 && unit.BonusAttack == 0 && unit.Attack == 1000,
            "A unit carried a field aura or boost into its graveyard.");
        Cast(game, 0, "revive", unit.InstanceId);
        Check(unit.Attack == 1300 && unit.AuraAttack == 300 && unit.Health == 2000,
            "Revival did not restore the current aura exactly once.");
    }

    private static void SeparateSlotAndActivation()
    {
        var game = Game(new[] { "unit", "other", "field", "negate" }, configure: rules =>
        { rules.FieldSlots = 1; rules.BackrowSlots = 1; });
        var field = Hand(game, 0, "field");
        Check(!game.Activate(0, field.InstanceId).Success, "Domain activated in draw phase.");
        MainPhase(game);
        var unit = Summon(game, 0);
        Ok(game.SetIntercept(0, Hand(game, 0, "negate").InstanceId));
        Check(!game.Activate(1, field.InstanceId).Success, "Opponent activated our domain.");
        Check(!game.Activate(0, field.InstanceId, unit.InstanceId).Success && field.Zone == CardZone.Hand,
            "No-target domain accepted a target or consumed an invalid activation.");
        Cast(game, 0, "field");
        Check(game.Players[0].Field.Count == 1 && game.Players[0].Backrow.Count == 1 && game.Players[0].DomainCard == field,
            "Domain consumed a character or intercept slot.");
        Check(!game.Summon(0, Hand(game, 0, "other").InstanceId).Success,
            "A full character zone allowed an extra normal summon.");
        Check(!game.Activate(0, field.InstanceId).Success && field.Zone == CardZone.Domain,
            "An active domain was reactivated from its slot.");
    }

    private static void BotDomain()
    {
        var game = Game(new[] { "field", "field", "unit" }, configure: rules => rules.NormalSummonsPerTurn = 0);
        MainPhase(game);
        Ok(SimpleDuelBot.Step(game, 0));
        Check(game.Chain.Count == 1 && game.Chain[0].Source.Definition.Kind == CardKind.Domain,
            "Bot did not activate its domain.");
        Resolve(game);
        Ok(SimpleDuelBot.Step(game, 0));
        Check(game.Phase == DuelPhase.Battle && game.Players[0].Hand.Count(c => c.Definition.Id == "field") == 1,
            "Bot wasted an identical active field instead of advancing.");
    }

    private static void UnshuffledSecondPlayer()
    {
        var game = Game(new[] { "field", "other", "unit" }, new[] { "other", "unit", "field" },
            rules => rules.FirstPlayer = 1);
        Check(game.ActivePlayer == 1 && game.Players[0].Hand.Take(3).Select(c => c.Definition.Id)
            .SequenceEqual(new[] { "field", "other", "unit" }), "Supplied first-player hand order changed.");
        Check(game.Players[1].Hand.Take(3).Select(c => c.Definition.Id)
            .SequenceEqual(new[] { "other", "unit", "field" }), "Supplied second-player hand order changed.");
        foreach (var player in game.Players)
            Check(player.Hand.Count + player.Deck.Count == 22 &&
                player.Hand.Concat(player.Deck).Any(c => c.Definition.Id == player.MainCharacterId),
                "Main-character choice removed a card or altered starting card counts.");
        MainPhase(game);
        Check(game.Players[1].Hand.Count == 12, "Player index 1 did not skip the first turn draw.");
        var firstUnit = Summon(game, 1, "other");
        Ok(game.AdvancePhase(1));
        Check(!game.Attack(1, firstUnit.InstanceId).Success, "Player index 1 bypassed the first-turn attack restriction.");
        NextTurn(game);
        Check(game.ActivePlayer == 0 && game.Players[0].Hand.Count == 13,
            "Second turn did not give player index 0 its normal draw.");
        var secondUnit = Summon(game, 0, "unit");
        Ok(game.AdvancePhase(0));
        Ok(game.Attack(0, secondUnit.InstanceId, firstUnit.InstanceId));
    }

    private static void RuleCopy()
    {
        var original = new DuelRules
        {
            MinDeckSize = 7, MaxDeckSize = 55, MaxCopies = 4, StartingHand = 8, StartingLife = 6000,
            FieldSlots = 4, BackrowSlots = 3, NormalSummonsPerTurn = 2, FirstPlayer = 1,
            FirstPlayerSkipsFirstDraw = false, FirstPlayerCannotAttackFirstTurn = false,
            CanActivateInterceptOnSetTurn = true, Counterattack = false, ShuffleDeck = false
        };
        var copy = original.Copy();
        Check(!ReferenceEquals(original, copy), "Rule copy is the original reference.");
        foreach (var field in typeof(DuelRules).GetFields())
            Check(Equals(field.GetValue(original), field.GetValue(copy)), "Rule copy lost field " + field.Name);
        copy.FirstPlayer = 0;
        copy.StartingLife = 2000;
        copy.ShuffleDeck = true;
        Check(original.FirstPlayer == 1 && original.StartingLife == 6000 && !original.ShuffleDeck,
            "Match setup modified the shared rules.");
    }

    private static void Ok(ActionResult result) => Check(result.Success, result.Message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
