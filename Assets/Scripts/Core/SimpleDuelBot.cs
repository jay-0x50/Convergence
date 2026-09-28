using System;
using System.Linq;

namespace Convergence.Duel
{
    // A transparent baseline opponent, deliberately limited to its own hidden information.
    public static class SimpleDuelBot
    {
        public static ActionResult Step(DuelEngine duel, int player)
        {
            if (duel.IsGameOver) return ActionResult.Fail("대전이 끝났습니다.");
            var self = duel.Players[player];
            if (duel.Chain.Count > 0)
            {
                if (duel.PriorityPlayer != player) return ActionResult.Fail("응답 차례가 아닙니다.");
                if (duel.Chain[duel.Chain.Count - 1].Controller != player)
                {
                    foreach (var trap in self.Backrow.ToArray())
                    {
                        int? target = ChooseTarget(duel, player, trap.Definition);
                        if (trap.Definition.Target != TargetKind.None && !target.HasValue) continue;
                        var response = duel.Activate(player, trap.InstanceId, target);
                        if (response.Success) return response;
                    }
                }
                return duel.PassPriority(player);
            }
            if (duel.ActivePlayer != player) return ActionResult.Fail("자신의 턴이 아닙니다.");
            if (duel.Phase == DuelPhase.Main1 || duel.Phase == DuelPhase.Main2)
            {
                foreach (var card in self.Hand.Where(c => c.Definition.Kind == CardKind.Character).OrderByDescending(c => c.Attack).ToArray())
                {
                    var summon = duel.Summon(player, card.InstanceId);
                    if (summon.Success) return summon;
                }
                foreach (var card in self.Hand.Where(c => c.Definition.Kind == CardKind.Command || c.Definition.Kind == CardKind.Domain).ToArray())
                {
                    if (!IsUseful(duel, player, card.Definition)) continue;
                    int? target = ChooseTarget(duel, player, card.Definition);
                    if (card.Definition.Target != TargetKind.None && !target.HasValue) continue;
                    var effect = duel.Activate(player, card.InstanceId, target);
                    if (effect.Success) return effect;
                }
                foreach (var card in self.Hand.Where(c => c.Definition.Kind == CardKind.Intercept).ToArray())
                {
                    var set = duel.SetIntercept(player, card.InstanceId);
                    if (set.Success) return set;
                }
                return duel.AdvancePhase(player);
            }
            if (duel.Phase == DuelPhase.Battle)
            {
                foreach (var attacker in self.Field.Where(c => c.AttacksMade == 0).OrderByDescending(c => c.Attack).ToArray())
                {
                    var defenders = duel.Players[1 - player].Field;
                    var target = defenders.OrderBy(c => c.Health <= attacker.Attack ? 0 : 1).ThenBy(c => c.Attack).FirstOrDefault();
                    var attack = duel.Attack(player, attacker.InstanceId, target?.InstanceId);
                    if (attack.Success) return attack;
                }
            }
            return duel.AdvancePhase(player);
        }

        public static int? ChooseTarget(DuelEngine duel, int player, CardDefinition card)
        {
            var self = duel.Players[player];
            var enemy = duel.Players[1 - player];
            switch (card.Target)
            {
                case TargetKind.FriendlyUnit: return self.Field.OrderByDescending(c => c.Attack).FirstOrDefault()?.InstanceId;
                case TargetKind.EnemyUnit: return enemy.Field.OrderByDescending(c => c.Attack).FirstOrDefault()?.InstanceId;
                case TargetKind.AnyUnit:
                    return (card.Effect == EffectKind.BoostUnit ? self.Field : enemy.Field).OrderByDescending(c => c.Attack).FirstOrDefault()?.InstanceId;
                case TargetKind.HandCharacter: return self.Hand.Where(c => c.Definition.Kind == CardKind.Character).OrderByDescending(c => c.Attack).FirstOrDefault()?.InstanceId;
                case TargetKind.GraveyardCharacter: return self.Graveyard.Where(c => c.Definition.Kind == CardKind.Character).OrderByDescending(c => c.Attack).FirstOrDefault()?.InstanceId;
                default: return null;
            }
        }

        private static bool IsUseful(DuelEngine duel, int player, CardDefinition card)
        {
            var self = duel.Players[player];
            if (card.Kind == CardKind.Domain)
                return self.DomainCard == null || self.DomainCard.Definition.Id != card.Id;
            if (card.Effect == EffectKind.Draw) return self.Deck.Count >= card.Amount;
            if (card.Effect == EffectKind.HealPlayer) return self.Life < duel.Rules.StartingLife;
            if (card.Effect == EffectKind.CreateToken || card.Effect == EffectKind.SpecialSummonFromHand || card.Effect == EffectKind.Revive)
                return self.Field.Count < duel.Rules.FieldSlots;
            return true;
        }
    }
}
