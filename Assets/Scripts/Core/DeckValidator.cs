using System;
using System.Collections.Generic;

namespace Convergence.Duel
{
    /// <summary>Deck legality is independent of every card's series, archetype and element.</summary>
    public static class DeckValidator
    {
        public static IReadOnlyList<string> Validate(IEnumerable<string> deck,
            IReadOnlyList<CardDefinition> catalog, DuelRules rules)
        {
            var errors = new List<string>();
            if (rules == null)
            {
                errors.Add("듀얼 규칙이 없습니다.");
                return errors.AsReadOnly();
            }
            if (rules.MinDeckSize < 1 || rules.MaxDeckSize < rules.MinDeckSize)
                errors.Add("덱 최소/최대 매수 설정이 올바르지 않습니다.");
            if (rules.MaxCopies < 1) errors.Add("동일 카드 제한은 1장 이상이어야 합니다.");
            if (rules.StartingHand < 0 || rules.StartingLife < 1 || rules.FieldSlots < 1 ||
                rules.BackrowSlots < 1 || rules.NormalSummonsPerTurn < 0 ||
                (rules.FirstPlayer != 0 && rules.FirstPlayer != 1))
                errors.Add("시작 패, 라이프, 슬롯 또는 턴 규칙 설정이 올바르지 않습니다.");

            var definitions = new Dictionary<string, CardDefinition>(StringComparer.Ordinal);
            if (catalog == null)
                errors.Add("카드 목록이 없습니다.");
            else
            {
                foreach (var definition in catalog)
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
                    {
                        errors.Add("카드 목록에 ID가 없는 카드가 있습니다.");
                        continue;
                    }
                    if (definitions.ContainsKey(definition.Id))
                        errors.Add("카드 목록에 중복 ID가 있습니다: " + definition.Id);
                    else
                        definitions.Add(definition.Id, definition);
                }
            }
            if (deck == null)
            {
                errors.Add("덱이 없습니다.");
                return errors.AsReadOnly();
            }

            int count = 0;
            bool hasCharacter = false;
            var copies = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in deck)
            {
                count++;
                CardDefinition definition;
                if (id == null || !definitions.TryGetValue(id, out definition))
                {
                    errors.Add("존재하지 않는 카드입니다: " + (id ?? "(빈 ID)"));
                    continue;
                }
                if (definition.Kind == CardKind.Token)
                    errors.Add("소환수는 덱에 넣을 수 없습니다: " + definition.Name);
                if (definition.Kind == CardKind.Character) hasCharacter = true;
                int previous;
                copies.TryGetValue(id, out previous);
                copies[id] = previous + 1;
                if (previous + 1 == rules.MaxCopies + 1)
                    errors.Add(definition.Name + ": 동일 카드는 " + rules.MaxCopies + "장까지 넣을 수 있습니다.");
            }
            if (count < rules.MinDeckSize || count > rules.MaxDeckSize)
                errors.Add("덱은 " + rules.MinDeckSize + "~" + rules.MaxDeckSize + "장이어야 합니다. 현재 " + count + "장입니다.");
            if (!hasCharacter)
                errors.Add("메인 캐릭터 추첨을 위해 캐릭터가 최소 1장 필요합니다.");
            return errors.AsReadOnly();
        }
    }
}
