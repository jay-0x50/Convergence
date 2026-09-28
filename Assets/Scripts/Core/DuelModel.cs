using System;
using System.Collections.Generic;

namespace Convergence.Duel
{
    public enum CardKind { Character, Command, Intercept, Token, Domain }
    public enum CardZone { Deck, Hand, Field, Backrow, Graveyard, Chain, Banished, Domain }
    public enum DuelPhase { Draw, Main1, Battle, Main2, End }
    public enum EffectKind { None, Draw, DamageUnit, BoostUnit, SpecialSummonFromHand, Revive, CreateToken, HealPlayer, Negate, DestroyUnit, FieldAura }
    public enum TargetKind { None, FriendlyUnit, EnemyUnit, AnyUnit, HandCharacter, GraveyardCharacter }
    public enum InterceptTrigger { AnyChain, AttackOnly }
    public enum ChainLinkKind { Effect, Attack }

    // Series/archetype/element describe cards. They never restrict deck membership.
    public sealed class CardDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public string Series { get; }
        public string Archetype { get; }
        public string Element { get; }
        public string Description { get; }
        public CardKind Kind { get; }
        public int Attack { get; }
        public int MaxHealth { get; }
        public EffectKind Effect { get; }
        public int Amount { get; }
        public TargetKind Target { get; }
        public InterceptTrigger Trigger { get; }

        public CardDefinition(string id, string name, string series, string archetype, string element,
            CardKind kind, int attack = 0, int maxHealth = 0, EffectKind effect = EffectKind.None,
            int amount = 0, TargetKind target = TargetKind.None, string description = "",
            InterceptTrigger trigger = InterceptTrigger.AnyChain)
        {
            Id = id; Name = name; Series = series; Archetype = archetype; Element = element;
            Kind = kind; Attack = attack; MaxHealth = maxHealth; Effect = effect;
            Amount = amount; Target = target; Description = description; Trigger = trigger;
        }
    }

    [Serializable]
    public sealed class DuelRules
    {
        public int MinDeckSize = 20;
        public int MaxDeckSize = 40;
        public int MaxCopies = 3;
        public int StartingHand = 5;
        public int StartingLife = 8000;
        public int FieldSlots = 5;
        public int BackrowSlots = 5;
        public int NormalSummonsPerTurn = 1;
        public bool FirstPlayerSkipsFirstDraw = true;
        public bool FirstPlayerCannotAttackFirstTurn = true;
        public bool CanActivateInterceptOnSetTurn = false;
        public bool Counterattack = true;
        public int FirstPlayer = 0;
        public bool ShuffleDeck = true;

        public DuelRules Copy()
        {
            // Rule fields are value types; callers can change a match setup independently.
            return (DuelRules)MemberwiseClone();
        }
    }

    public sealed class CardInstance
    {
        public int InstanceId { get; internal set; }
        public CardDefinition Definition { get; internal set; }
        public int Owner { get; internal set; }
        public CardZone Zone { get; internal set; }
        public int Health { get; internal set; }
        public int BonusAttack { get; internal set; }
        public int AuraAttack { get; internal set; }
        public int AttacksMade { get; internal set; }
        public int SetOnTurn { get; internal set; } = -1;
        public int Attack => Math.Max(0, Definition.Attack + BonusAttack + AuraAttack);
        public bool IsUnit => Definition.Kind == CardKind.Character || Definition.Kind == CardKind.Token;
    }

    public sealed class PlayerState
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public int Life { get; internal set; }
        public int NormalSummonsUsed { get; internal set; }
        public string MainCharacterId { get; internal set; }
        public CardInstance DomainCard { get; internal set; }
        internal readonly List<CardInstance> DeckCards = new List<CardInstance>();
        internal readonly List<CardInstance> HandCards = new List<CardInstance>();
        internal readonly List<CardInstance> FieldCards = new List<CardInstance>();
        internal readonly List<CardInstance> BackrowCards = new List<CardInstance>();
        internal readonly List<CardInstance> GraveyardCards = new List<CardInstance>();
        public IReadOnlyList<CardInstance> Deck => DeckCards.AsReadOnly();
        public IReadOnlyList<CardInstance> Hand => HandCards.AsReadOnly();
        public IReadOnlyList<CardInstance> Field => FieldCards.AsReadOnly();
        public IReadOnlyList<CardInstance> Backrow => BackrowCards.AsReadOnly();
        public IReadOnlyList<CardInstance> Graveyard => GraveyardCards.AsReadOnly();
    }

    public sealed class ChainLink
    {
        public int Id { get; internal set; }
        public ChainLinkKind Kind { get; internal set; }
        public int Controller { get; internal set; }
        public CardInstance Source { get; internal set; }
        public int? TargetId { get; internal set; }
        public int? NegatesLinkId { get; internal set; }
        public bool Negated { get; internal set; }
        public string Label { get; internal set; }
    }

    public sealed class ActionResult
    {
        public bool Success { get; }
        public string Message { get; }
        private ActionResult(bool success, string message) { Success = success; Message = message; }
        public static ActionResult Ok(string message = "") => new ActionResult(true, message);
        public static ActionResult Fail(string message) => new ActionResult(false, message);
    }
}
