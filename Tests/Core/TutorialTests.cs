using System;
using System.Text.Json;
using Convergence.Duel;

public static class TutorialTests
{
    public static void RunAll()
    {
        RejectInvalidLessonIds();
        for (int lesson = 0; lesson < 3; lesson++) RunLesson(lesson);
        CannotCompleteWithoutResolvingLastChain();
    }

    private static void RunLesson(int lesson)
    {
        var session = new TutorialSession(lesson);
        var repeat = new TutorialSession(lesson);
        Check(Snapshot(session) == Snapshot(repeat), "Tutorial setup must be deterministic.");
        Check(!session.Engine.Rules.ShuffleDeck, "Loaner deck order must be preserved.");
        Check(session.Engine.Rules.FirstPlayerCannotAttackFirstTurn, "Tutorial must keep first-turn attack rules.");
        Check(!session.Engine.Rules.CanActivateInterceptOnSetTurn, "Tutorial must keep set-turn restriction.");
        Check(session.StepNumber == 1 && session.StepCount > 0, "Tutorial must begin on its first step.");
        int actions = 0;
        int playerActions = 0;
        while (!session.IsComplete && actions < 100)
        {
            string before = Snapshot(session);
            if (session.IsOpponentStep)
            {
                var reject = session.Perform(session.ExpectedAction, session.ExpectedSourceId, session.ExpectedTargetId);
                Check(!reject.Success && Snapshot(session) == before, "Player action cannot execute an opponent step.");
                Check(session.StepOpponent().Success, "Scripted opponent action must be legal.");
            }
            else
            {
                Check(!session.StepOpponent().Success && Snapshot(session) == before,
                    "Opponent automation cannot perform a player action.");
                TutorialAction wrongAction = session.ExpectedAction == TutorialAction.AdvancePhase
                    ? TutorialAction.PassPriority : TutorialAction.AdvancePhase;
                Check(!session.Perform(wrongAction, session.ExpectedSourceId, session.ExpectedTargetId).Success
                    && Snapshot(session) == before, "Wrong action must not change any state.");
                Check(!session.Perform(session.ExpectedAction, -100, session.ExpectedTargetId).Success
                    && Snapshot(session) == before, "Wrong source must not change any state.");
                Check(!session.Perform(session.ExpectedAction, session.ExpectedSourceId, -100).Success
                    && Snapshot(session) == before, "Wrong target must not change any state.");
                var result = session.Perform(session.ExpectedAction, session.ExpectedSourceId, session.ExpectedTargetId);
                Check(result.Success, "Expected action failed in lesson " + lesson + ": " + result.Message);
                playerActions++;
            }
            actions++;
            Check(session.IsComplete || session.StepNumber == actions + 1, "Exactly one step must advance per action.");
        }
        Check(session.IsComplete && actions == session.StepCount, "Lesson must complete all actual actions.");
        Check(playerActions >= 5, "A lesson must require player participation.");
        Check(session.Engine.Chain.Count == 0, "Lesson cannot finish while effects remain unresolved.");
        Check(session.StepNumber == session.StepCount, "Completed progress must remain within the step count.");
        string final = Snapshot(session);
        Check(!session.Perform(TutorialAction.AdvancePhase).Success && !session.StepOpponent().Success
            && Snapshot(session) == final, "Completed lesson must be stable and cannot complete twice.");

        if (lesson == 0)
        {
            Check(session.Engine.IsGameOver && session.Engine.Winner == 0 && session.Engine.Players[1].Life == 0,
                "Combat lesson must end by an actual life victory.");
            Check(session.Engine.Players[0].Field[0].Health == 200, "Counterattack damage must persist into direct attack.");
            Check(session.Engine.Players[1].Graveyard.Count > 0, "Combat lesson must defeat an opposing unit first.");
        }
        else if (lesson == 1)
        {
            Check(!session.Engine.IsGameOver && session.Engine.Players[0].Field.Count == 3,
                "Deployment lesson must actually deploy three units.");
            Check(session.Engine.Players[0].NormalSummonsUsed == 1, "Special summon must not consume another normal summon.");
            Check(session.Engine.Players[0].Field[0].Definition.Series != session.Engine.Players[0].Field[1].Definition.Series,
                "Deployment lesson must demonstrate cross-franchise special summon.");
            Check(session.Engine.Players[0].Field[2].Definition.Kind == CardKind.Token, "Deployment must create a real token.");
        }
        else
        {
            var unit = session.Engine.Players[0].Field[0];
            Check(session.Engine.Players[0].DomainCard != null && unit.Attack == 2000, "Domain and command buffs must both resolve.");
            Check(unit.Health == unit.Definition.MaxHealth && session.Engine.Players[0].Life == 8000,
                "Negated attack must leave player health and life untouched.");
            Check(session.Engine.Players[1].Field[0].AttacksMade == 1, "The opponent must actually declare its attack.");
            Check(session.Engine.Players[0].Backrow.Count == 0, "The trap must be consumed by a real activation.");
        }
    }

    private static void CannotCompleteWithoutResolvingLastChain()
    {
        for (int lesson = 0; lesson < 3; lesson++)
        {
            var session = new TutorialSession(lesson);
            while (session.StepNumber < session.StepCount)
            {
                var result = session.IsOpponentStep ? session.StepOpponent()
                    : session.Perform(session.ExpectedAction, session.ExpectedSourceId, session.ExpectedTargetId);
                Check(result.Success, result.Message);
            }
            Check(!session.IsComplete && !session.IsOpponentStep && session.ExpectedAction == TutorialAction.PassPriority,
                "All lessons must end with the player's own resolution pass.");
            Check(session.Engine.Chain.Count > 0, "Final resolution must be a real pending chain.");
            string before = Snapshot(session);
            Check(!session.Perform(TutorialAction.AdvancePhase).Success && Snapshot(session) == before,
                "A next-phase click cannot substitute for resolving the objective.");
            Check(session.Perform(TutorialAction.PassPriority).Success && session.IsComplete,
                "Completion follows actual final effect resolution.");
        }
    }

    private static void RejectInvalidLessonIds()
    {
        foreach (int id in new[] { -1, 3 })
        {
            bool rejected = false;
            try { new TutorialSession(id); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Unknown lessons must be rejected.");
        }
    }

    private static string Snapshot(TutorialSession session) => JsonSerializer.Serialize(new
    {
        session.LessonId, session.StepNumber, session.StepCount, session.IsComplete, session.IsOpponentStep,
        session.ExpectedAction, session.ExpectedSourceId, session.ExpectedTargetId, session.Instruction,
        session.Engine.ActivePlayer, session.Engine.PriorityPlayer, session.Engine.Phase, session.Engine.TurnNumber,
        session.Engine.IsGameOver, session.Engine.Winner, session.Engine.Players, session.Engine.Chain, session.Engine.Log
    });

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
