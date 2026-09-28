using System;
using System.IO;
using Convergence.Duel;
using Convergence.Prototype;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Convergence.Editor
{
    public static class PrototypeTools
    {
        private const string ScenePath = "Assets/Scenes/DuelPrototype.unity";

        [MenuItem("Convergence/Open Duel Prototype")]
        public static void OpenPrototype()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        public static void ValidateProject()
        {
            Configure();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var view = UnityEngine.Object.FindAnyObjectByType<DuelPrototype>();
            if (view == null) throw new InvalidOperationException("Prototype component missing from scene.");
            var serialized = new SerializedObject(view);
            if (serialized.FindProperty("rules").FindPropertyRelative("StartingLife").intValue != 8000)
                throw new InvalidOperationException("Default rules did not deserialize correctly.");
            var catalog = SampleContent.CreateCatalog();
            int completed = 0;
            for (int seed = 0; seed < 8; seed++)
            {
                var duel = new DuelEngine(catalog, new DuelRules(), SampleContent.CreateStarterDeck(), SampleContent.CreateOpponentDeck(), seed);
                for (int step = 0; step < 3000 && !duel.IsGameOver; step++)
                {
                    int player = duel.Chain.Count > 0 ? duel.PriorityPlayer : duel.ActivePlayer;
                    var result = SimpleDuelBot.Step(duel, player);
                    if (!result.Success) throw new InvalidOperationException("Bot got stuck: " + result.Message);
                }
                if (!duel.IsGameOver) throw new InvalidOperationException("Match failed to complete.");
                completed++;
            }
            var progress = ProgressionService.CreateNew();
            for (int lesson = 0; lesson < 3; lesson++)
            {
                var session = new TutorialSession(lesson);
                for (int step = 0; step < 100 && !session.IsComplete; step++)
                {
                    var result = session.IsOpponentStep ? session.StepOpponent() :
                        session.Perform(session.ExpectedAction, session.ExpectedSourceId, session.ExpectedTargetId);
                    if (!result.Success) throw new InvalidOperationException("Tutorial stalled: " + result.Message);
                }
                if (!session.IsComplete) throw new InvalidOperationException("Tutorial did not complete.");
                if (!ProgressionService.ClaimTutorialReward(progress, lesson, catalog, new DuelRules()).Success)
                    throw new InvalidOperationException("Tutorial reward failed.");
            }
            var restored = JsonUtility.FromJson<PlayerProgress>(JsonUtility.ToJson(progress));
            if (restored.CompletedLessons.Count != 3 || restored.Coins != 600 ||
                ProgressionService.ValidateOwnedDeck(restored, restored.SavedDeck, catalog, new DuelRules()).Count != 0)
                throw new InvalidOperationException("Unity profile serialization failed.");
            EditorSceneManager.SaveScene(scene);
            Debug.Log("CONVERGENCE VALIDATION PASSED: scene loaded, rules serialized, " + completed + " matches, 3 tutorials and profile JSON round-trip completed in Unity.");
        }

        [MenuItem("Convergence/Build Windows Prototype")]
        public static void BuildWindows()
        {
            ValidateProject();
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/Convergence.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
            Debug.Log("CONVERGENCE BUILD PASSED: " + report.summary.outputPath);
        }

        private static void Configure()
        {
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
            PlayerSettings.companyName = "ConvergencePrototype";
            PlayerSettings.productName = "Convergence Duel Lab";
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }
    }
}
