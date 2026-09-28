using System;
using System.Collections.Generic;

namespace Convergence.Duel
{
    public enum TutorialAction { AdvancePhase, Summon, SetIntercept, Activate, Attack, PassPriority }

    /// <summary>
    /// A guided duel using normal engine actions and ordered loaner decks.
    /// The UI must send player actions through Perform and only autoplay StepOpponent.
    /// Completion is a resolved board outcome; profile rewards belong to the caller.
    /// </summary>
    public sealed class TutorialSession
    {
        private sealed class Step
        {
            public int Player;
            public TutorialAction Action;
            public int? SourceId;
            public int? TargetId;
            public string Instruction;
            public Func<bool> Verify;
        }

        private readonly List<Step> steps = new List<Step>();
        private int stepIndex;
        private bool complete;
        private string failedGoal = "";
        private Func<bool> completionGoal;

        public DuelEngine Engine { get; }
        public int LessonId { get; }
        public string Title { get; }
        public int StepNumber => Math.Min(stepIndex + 1, steps.Count);
        public int StepCount => steps.Count;
        public bool IsComplete => complete;
        public bool IsOpponentStep => !complete && stepIndex < steps.Count && steps[stepIndex].Player == 1;
        public int? ExpectedSourceId => Current == null ? null : Current.SourceId;
        public int? ExpectedTargetId => Current == null ? null : Current.TargetId;
        public TutorialAction ExpectedAction => Current == null ? TutorialAction.AdvancePhase : Current.Action;
        public string Instruction => complete ? "실습 완료! 배운 행동을 자유 듀얼에서 사용해 보세요."
            : failedGoal.Length > 0 ? failedGoal : Current.Instruction;
        private Step Current => stepIndex < steps.Count ? steps[stepIndex] : null;

        public TutorialSession(int lessonId)
        {
            if (lessonId < 0 || lessonId > 2) throw new ArgumentOutOfRangeException(nameof(lessonId));
            LessonId = lessonId;
            var rules = new DuelRules
            {
                MinDeckSize = 1, MaxDeckSize = 40, MaxCopies = 3, StartingHand = 5,
                ShuffleDeck = false, FirstPlayer = lessonId == 0 ? 1 : 0,
                StartingLife = lessonId == 0 ? 1700 : 8000
            };
            var opponentDeck = Deck("lisa", "dan_heng", "kaeya", "amber", "noelle");
            List<string> playerDeck;
            if (lessonId == 0)
            {
                Title = "1. 소환과 전투";
                playerDeck = Deck("trailblazer", "amber", "noelle", "shared_notes", "rally");
            }
            else if (lessonId == 1)
            {
                Title = "2. 작품을 넘는 전개";
                playerDeck = Deck("trailblazer", "noelle", "crossworld_relay", "memory_echo", "shared_notes");
            }
            else
            {
                Title = "3. 필드 효과와 대응";
                playerDeck = Deck("convergence_field", "noelle", "rally", "hold_the_line", "shared_notes");
            }
            Engine = new DuelEngine(SampleContent.CreateCatalog(), rules, playerDeck, opponentDeck, seed: 100 + lessonId);
            if (lessonId == 0) BuildCombatLesson();
            else if (lessonId == 1) BuildDeploymentLesson();
            else BuildResponseLesson();
        }

        public ActionResult Perform(TutorialAction action, int? sourceId = null, int? targetId = null)
        {
            if (complete) return ActionResult.Fail("이미 완료한 실습입니다.");
            if (failedGoal.Length > 0 || Current == null) return ActionResult.Fail("실습을 다시 시작해 주세요.");
            if (Current.Player != 0) return ActionResult.Fail("실습 상대의 행동을 기다려 주세요.");
            // Reject before calling the engine, including unrelated selected cards or targets.
            if (action != Current.Action || sourceId != Current.SourceId || targetId != Current.TargetId)
                return ActionResult.Fail("안내된 행동과 강조된 카드를 선택해 주세요. " + Current.Instruction);
            return ExecuteCurrent();
        }

        public ActionResult StepOpponent()
        {
            if (complete) return ActionResult.Fail("이미 완료한 실습입니다.");
            if (failedGoal.Length > 0 || Current == null) return ActionResult.Fail("실습을 다시 시작해 주세요.");
            if (Current.Player != 1) return ActionResult.Fail("직접 안내된 행동을 진행해 주세요.");
            return ExecuteCurrent();
        }

        private ActionResult ExecuteCurrent()
        {
            Step step = Current;
            ActionResult result;
            switch (step.Action)
            {
                case TutorialAction.AdvancePhase: result = Engine.AdvancePhase(step.Player); break;
                case TutorialAction.Summon: result = Engine.Summon(step.Player, step.SourceId.Value); break;
                case TutorialAction.SetIntercept: result = Engine.SetIntercept(step.Player, step.SourceId.Value); break;
                case TutorialAction.Activate: result = Engine.Activate(step.Player, step.SourceId.Value, step.TargetId); break;
                case TutorialAction.Attack: result = Engine.Attack(step.Player, step.SourceId.Value, step.TargetId); break;
                default: result = Engine.PassPriority(step.Player); break;
            }
            if (!result.Success) return result;
            if (step.Verify != null && !step.Verify())
            {
                failedGoal = "예상한 전투 결과와 달라졌습니다. 실습을 다시 시작해 주세요.";
                return ActionResult.Fail(failedGoal);
            }
            stepIndex++;
            if (stepIndex == steps.Count)
            {
                complete = completionGoal != null && completionGoal();
                if (!complete)
                {
                    failedGoal = "실습 목표가 해결되지 않았습니다. 실습을 다시 시작해 주세요.";
                    return ActionResult.Fail(failedGoal);
                }
                return ActionResult.Ok("실습 완료! " + result.Message);
            }
            return result;
        }

        private void BuildCombatLesson()
        {
            var hero = InitialCard(0, "trailblazer");
            var enemy = InitialCard(1, "lisa");
            Add(1, TutorialAction.AdvancePhase, "상대가 선공 첫 드로우를 생략합니다. 연습 라이프는 1700입니다.");
            Add(1, TutorialAction.Summon, "상대가 리사를 일반 소환합니다.", enemy);
            OpponentFinishTurn();
            Add(0, TutorialAction.AdvancePhase, "후공 턴입니다. 다음 단계 버튼으로 1장 드로우하고 메인으로 이동하세요.");
            Add(0, TutorialAction.Summon, "패의 개척자를 선택하고 일반 소환하세요. 턴마다 기본 1회 소환할 수 있습니다.", hero);
            Add(0, TutorialAction.AdvancePhase, "다음 단계 버튼으로 배틀에 들어가세요.");
            Add(0, TutorialAction.Attack, "개척자를 선택하고 상대 리사를 대상으로 공격하세요.", hero, enemy);
            Add(1, TutorialAction.PassPriority, "상대가 공격에 대응하지 않고 패스합니다.");
            Add(0, TutorialAction.PassPriority, "대응 패스를 누르면 전투가 해결됩니다. 서로 동시에 피해를 줍니다.",
                verify: () => enemy.Zone == CardZone.Graveyard && hero.Health == 200 && Engine.Chain.Count == 0);
            Add(0, TutorialAction.AdvancePhase, "개척자에게 체력 200이 남았습니다. 다음 단계로 메인 2에 이동하세요.");
            Add(0, TutorialAction.AdvancePhase, "다음 단계로 종료 단계에 이동하세요.");
            Add(0, TutorialAction.AdvancePhase, "다음 단계로 턴을 마치세요. 사용한 공격은 다음 내 턴에 회복됩니다.");
            Add(1, TutorialAction.AdvancePhase, "상대가 드로우합니다.");
            OpponentFinishTurn();
            Add(0, TutorialAction.AdvancePhase, "다음 내 턴입니다. 드로우하고 메인으로 이동하세요.");
            Add(0, TutorialAction.AdvancePhase, "배틀로 이동하세요. 체력은 저절로 회복되지 않습니다.");
            Add(0, TutorialAction.Attack, "상대 필드가 비었습니다. 개척자를 선택하고 직접 공격하세요.", hero);
            Add(1, TutorialAction.PassPriority, "상대가 직접 공격에 대응하지 않고 패스합니다.");
            Add(0, TutorialAction.PassPriority, "대응 패스를 눌러 직접 공격을 해결하세요. 상대 라이프 0이 승리 조건입니다.");
            completionGoal = () => Engine.IsGameOver && Engine.Winner == 0 && Engine.Players[1].Life == 0
                && hero.Health == 200 && enemy.Zone == CardZone.Graveyard && Engine.Chain.Count == 0;
        }

        private void BuildDeploymentLesson()
        {
            var hero = InitialCard(0, "trailblazer");
            var guest = InitialCard(0, "noelle");
            var relay = InitialCard(0, "crossworld_relay");
            var memory = InitialCard(0, "memory_echo");
            Add(0, TutorialAction.AdvancePhase, "메인으로 이동하세요. 선공 첫 턴은 드로우와 공격을 생략합니다.");
            Add(0, TutorialAction.Summon, "패의 스타레일 캐릭터 개척자를 일반 소환하세요.", hero);
            Add(0, TutorialAction.Activate, "차원을 잇는 합류를 선택하고 패의 원신 캐릭터 노엘을 대상으로 발동하세요.", relay, guest);
            ResolvePlayerEffect("대응 패스를 눌러 노엘을 특수 소환하세요. 작품이 달라도 함께 전개할 수 있습니다.",
                () => guest.Zone == CardZone.Field && Engine.Players[0].NormalSummonsUsed == 1);
            Add(0, TutorialAction.Activate, "기억의 잔향을 발동하세요. 토큰은 카드 효과로 생성합니다.", memory);
            ResolvePlayerEffect("대응 패스를 눌러 토큰을 생성하세요. 일반 소환 1회로 유닛 3체를 전개했습니다.");
            completionGoal = () => !Engine.IsGameOver && Engine.Chain.Count == 0 && hero.Zone == CardZone.Field
                && guest.Zone == CardZone.Field && Engine.Players[0].Field.Count == 3
                && Engine.Players[0].NormalSummonsUsed == 1 && HasMemoryToken();
        }

        private void BuildResponseLesson()
        {
            var domain = InitialCard(0, "convergence_field");
            var hero = InitialCard(0, "noelle");
            var rally = InitialCard(0, "rally");
            var trap = InitialCard(0, "hold_the_line");
            var enemy = InitialCard(1, "dan_heng");
            Add(0, TutorialAction.AdvancePhase, "메인으로 이동하여 필드 카드를 준비하세요.");
            Add(0, TutorialAction.Activate, "필드 카드 교차하는 세계를 선택하고 발동하세요.", domain);
            ResolvePlayerEffect("대응 패스를 누르세요. 필드 효과가 해결되면 내 유닛의 공격력이 300 증가합니다.",
                () => Engine.Players[0].DomainCard == domain);
            Add(0, TutorialAction.Summon, "노엘을 일반 소환하세요. 필드 효과로 공격력이 1200에서 1500이 됩니다.", hero,
                verify: () => hero.Attack == 1500);
            Add(0, TutorialAction.Activate, "함께하는 진격을 선택하고 필드의 노엘을 대상으로 발동하세요.", rally, hero);
            ResolvePlayerEffect("대응 패스를 눌러 강화하세요. 노엘의 공격력이 2000이 됩니다.", () => hero.Attack == 2000);
            Add(0, TutorialAction.SetIntercept, "패의 전선 사수를 세트하세요. 세트한 턴에는 발동할 수 없습니다.", trap);
            Add(0, TutorialAction.AdvancePhase, "배틀로 이동하세요. 선공 첫 턴이므로 공격하지 않습니다.");
            Add(0, TutorialAction.AdvancePhase, "메인 2로 이동하세요. 대응 카드는 뒷열에서 기다립니다.");
            Add(0, TutorialAction.AdvancePhase, "종료 단계로 이동하세요.");
            Add(0, TutorialAction.AdvancePhase, "턴을 마치세요. 다음 턴부터 세트한 대응 카드를 사용할 수 있습니다.");
            Add(1, TutorialAction.AdvancePhase, "상대가 드로우합니다.");
            Add(1, TutorialAction.Summon, "상대가 단항을 소환합니다.", enemy);
            Add(1, TutorialAction.AdvancePhase, "상대가 배틀에 들어갑니다.");
            Add(1, TutorialAction.Attack, "상대 단항이 노엘을 공격합니다. 아직 피해는 발생하지 않았습니다.", enemy, hero);
            Add(0, TutorialAction.Activate, "지금 뒷열의 전선 사수를 선택해 발동하세요. 공격 바로 다음에 대응합니다.", trap);
            Add(1, TutorialAction.PassPriority, "상대가 대응하지 않고 패스합니다.");
            Add(0, TutorialAction.PassPriority, "대응 패스를 누르세요. 마지막에 쌓은 전선 사수가 먼저 해결되어 공격을 무효로 합니다.");
            completionGoal = () => !Engine.IsGameOver && Engine.Chain.Count == 0
                && Engine.Players[0].DomainCard == domain && hero.Attack == 2000
                && hero.Health == hero.Definition.MaxHealth && enemy.Health == enemy.Definition.MaxHealth
                && Engine.Players[0].Life == 8000 && Engine.Players[1].Life == 8000
                && trap.Zone == CardZone.Graveyard && enemy.AttacksMade == 1;
        }

        private void OpponentFinishTurn()
        {
            Add(1, TutorialAction.AdvancePhase, "상대가 배틀 단계로 이동합니다.");
            Add(1, TutorialAction.AdvancePhase, "상대가 메인 2로 이동합니다.");
            Add(1, TutorialAction.AdvancePhase, "상대가 종료 단계로 이동합니다.");
            Add(1, TutorialAction.AdvancePhase, "상대가 턴을 마칩니다.");
        }

        private void ResolvePlayerEffect(string instruction, Func<bool> verify = null)
        {
            Add(1, TutorialAction.PassPriority, "상대가 효과에 대응하지 않고 패스합니다.");
            Add(0, TutorialAction.PassPriority, instruction, verify: verify);
        }

        private void Add(int player, TutorialAction action, string instruction, CardInstance source = null,
            CardInstance target = null, Func<bool> verify = null)
        {
            steps.Add(new Step
            {
                Player = player, Action = action, Instruction = instruction,
                SourceId = source == null ? (int?)null : source.InstanceId,
                TargetId = target == null ? (int?)null : target.InstanceId, Verify = verify
            });
        }

        private CardInstance InitialCard(int player, string id)
        {
            foreach (var card in Engine.Players[player].Hand)
                if (card.Definition.Id == id) return card;
            throw new InvalidOperationException("실습 시작 패에 필요한 카드가 없습니다: " + id);
        }

        private bool HasMemoryToken()
        {
            foreach (var card in Engine.Players[0].Field)
                if (card.Definition.Id == "token_memory" && card.Definition.Kind == CardKind.Token) return true;
            return false;
        }

        private static List<string> Deck(params string[] openingHand)
        {
            var deck = new List<string>(openingHand);
            // Ordered spare draws avoid deck-out; no scripted state injection is needed.
            deck.AddRange(new[] { "march7", "kaeya", "amber", "safe_haven", "focused_strike", "himeko", "second_chance" });
            return deck;
        }
    }
}
