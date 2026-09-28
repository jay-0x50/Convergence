using System;
using System.Collections.Generic;
using System.Linq;
using Convergence.Duel;
using UnityEngine;

namespace Convergence.Prototype
{
    /// <summary>A disposable prototype view. All game decisions go through the pure C# engine.</summary>
    public sealed partial class DuelPrototype : MonoBehaviour
    {
        [SerializeField] private DuelRules rules = new DuelRules();
        [SerializeField, Range(0.1f, 2f)] private float botActionDelay = 0.65f;
        private const float Width = 1440, Height = 900;
        private const string DeckSaveKey = "Convergence.Prototype.Deck.v1";
        private readonly Color background = new Color(0.035f, 0.055f, 0.09f);
        private readonly Color panel = new Color(0.067f, 0.094f, 0.14f);
        private readonly Color muted = new Color(0.56f, 0.64f, 0.74f);
        private readonly Color accent = new Color(0.35f, 0.89f, 0.8f);
        private readonly Color gold = new Color(1f, 0.78f, 0.42f);
        private IReadOnlyList<CardDefinition> catalog;
        private List<string> deck;
        private DuelEngine duel;
        private GUIStyle label, title, small, button, input, cardName, handName;
        private Texture2D buttonTexture, hoverTexture;
        private Font font;
        private Vector2 catalogScroll, deckScroll, handScroll, logScroll, targetScroll;
        private string search = "", seedText = "42", notice = "카드를 자유롭게 섞고, 나만의 전개를 시험하세요.";
        private int selectedId = -1, pendingSource = -1, viewer;
        private bool pendingAttack, vsAi = true, handoff, showHelp, smokeTesting;
        private float nextBotAction;
        private int smokeRepaints;

        [Serializable] private sealed class SavedDeck { public List<string> cards = new List<string>(); }

        private void Awake()
        {
            smokeTesting = Array.IndexOf(Environment.GetCommandLineArgs(), "-prototypeSmokeOutput") >= 0;
            Application.targetFrameRate = 60;
            catalog = SampleContent.CreateCatalog();
            LoadProfile();
            seedText = Environment.TickCount.ToString();
            ApplySettings(profile.Settings);
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("Duel Camera", typeof(Camera));
                cameraObject.tag = "MainCamera";
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
                camera.transform.position = new Vector3(0, 0, -10);
            }
        }

        private void Update()
        {
            if (screen != GameScreen.Duel || duel == null || duel.IsGameOver || handoff || showHelp || smokeTesting || TutorialComplete) return;
            if (tutorial != null)
            {
                if (tutorial.IsOpponentStep && Time.unscaledTime >= nextBotAction)
                    Apply(tutorial.StepOpponent());
                return;
            }
            if (vsAi && DecisionPlayer() == 1 && Time.unscaledTime >= nextBotAction)
            {
                var result = SimpleDuelBot.Step(duel, 1);
                if (!result.Success) notice = result.Message;
                nextBotAction = Time.unscaledTime + botActionDelay;
            }
            SyncHandoff();
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Opt-in render check for the standalone development build; never runs during normal play.
        private System.Collections.IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int smokeIndex = Array.IndexOf(args, "-prototypeSmokeOutput");
            if (smokeIndex < 0 || smokeIndex + 1 >= args.Length) yield break;
            smokeTesting = true;
            string output = System.IO.Path.GetFullPath(args[smokeIndex + 1]);
            System.IO.Directory.CreateDirectory(output);
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "smoke-result.txt"), "RUNNING: screen capture has not been validated yet.");
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            deck = SampleContent.CreateStarterDeck();
            ProgressionService.ImportLegacyDeck(profile, deck, catalog, rules);
            seedText = "42";
            yield return SmokeCapture(output, "title");
            screen = GameScreen.Home;
            yield return SmokeCapture(output, "home");
            screen = GameScreen.Solo;
            yield return SmokeCapture(output, "solo");
            OpenMatchSetup(false);
            yield return SmokeCapture(output, "match-setup");
            OpenSettings();
            yield return SmokeCapture(output, "settings");
            screen = GameScreen.Shop;
            yield return SmokeCapture(output, "shop");
            screen = GameScreen.DeckBuilder;
            yield return SmokeCapture(output, "deck-builder");
            StartDuel(true);
            for (int step = 0; step < 250 && !duel.IsGameOver; step++)
            {
                if (duel.TurnNumber >= 3 && duel.Chain.Count > 0 && duel.Players.Sum(p => p.Field.Count) >= 2) break;
                var result = SimpleDuelBot.Step(duel, DecisionPlayer());
                if (!result.Success) throw new InvalidOperationException("Smoke action failed: " + result.Message);
            }
            notice = "체인 응답 차례입니다. 세트한 인터셉트를 발동하거나 패스하세요.";
            selectedId = duel.Players[0].Field.FirstOrDefault()?.InstanceId ?? -1;
            yield return SmokeCapture(output, "duel-board");
            for (int step = 0; step < 3000 && !duel.IsGameOver; step++)
            {
                var result = SimpleDuelBot.Step(duel, DecisionPlayer());
                if (!result.Success) throw new InvalidOperationException("Smoke action failed: " + result.Message);
            }
            if (!duel.IsGameOver) throw new InvalidOperationException("Smoke match did not finish.");
            yield return SmokeCapture(output, "duel-result");
            for (int lesson = 0; lesson < 3; lesson++)
            {
                StartTutorial(lesson);
                for (int step = 0; step < 100 && !tutorial.IsComplete; step++)
                {
                    if (lesson == 2 && !tutorial.IsOpponentStep && tutorial.ExpectedSourceId.HasValue &&
                        duel.FindCard(tutorial.ExpectedSourceId.Value).Definition.Kind == CardKind.Intercept &&
                        tutorial.ExpectedAction == TutorialAction.Activate)
                    {
                        selectedId = tutorial.ExpectedSourceId.Value;
                        yield return SmokeCapture(output, "tutorial-response");
                    }
                    if (tutorial.IsOpponentStep) Apply(tutorial.StepOpponent());
                    else Act(tutorial.ExpectedAction, tutorial.ExpectedSourceId, tutorial.ExpectedTargetId);
                }
                if (!tutorial.IsComplete) throw new InvalidOperationException("Smoke tutorial did not finish.");
                yield return SmokeCapture(output, "tutorial-" + (lesson + 1) + "-complete");
            }
            for (int i = 0; i < 5; i++) yield return new WaitForEndOfFrame();
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "smoke-result.txt"),
                "PASS: all menus, deck builder, battle chain, result, and 3 tutorials rendered. Full match completed; rewards processed in memory only.");
            Debug.Log("CONVERGENCE RENDER SMOKE PASSED");
            Application.Quit(0);
        }

        private System.Collections.IEnumerator SmokeCapture(string output, string name)
        {
            int previousRepaints = smokeRepaints;
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
            var capture = ScreenCapture.CaptureScreenshotAsTexture();
            if (capture == null || smokeRepaints == previousRepaints || !capture.GetPixels32().Any(pixel => pixel.r > 32 || pixel.g > 32 || pixel.b > 32))
            {
                Debug.LogError("Smoke capture failed or blank: " + name + ", repaints=" + smokeRepaints + ", size=" + Screen.width + "x" + Screen.height);
                Application.Quit(2);
                throw new InvalidOperationException("Rendered UI could not be captured: " + name);
            }
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, name + ".png"), capture.EncodeToPNG());
            Destroy(capture);
        }
#endif

        private void EnsureStyles()
        {
            if (label != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 18);
            buttonTexture = Solid(new Color(0.12f, 0.20f, 0.27f));
            hoverTexture = Solid(new Color(0.18f, 0.32f, 0.39f));
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true, richText = false, padding = new RectOffset(0, 0, 0, 0) };
            label.normal.textColor = Color.white;
            title = new GUIStyle(label) { fontSize = 28, fontStyle = FontStyle.Bold };
            small = new GUIStyle(label) { fontSize = 13 };
            cardName = new GUIStyle(label) { fontSize = 17, fontStyle = FontStyle.Bold };
            handName = new GUIStyle(cardName) { fontSize = 14 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, wordWrap = true, padding = new RectOffset(8, 8, 5, 5) };
            button.normal.background = buttonTexture; button.hover.background = hoverTexture;
            button.active.background = hoverTexture;
            button.normal.textColor = Color.white; button.hover.textColor = accent; button.active.textColor = accent;
            input = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 16, padding = new RectOffset(10, 10, 7, 7) };
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color); texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            if (buttonTexture != null) Destroy(buttonTexture);
            if (hoverTexture != null) Destroy(hoverTexture);
            if (font != null) Destroy(font);
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint) smokeRepaints++;
            EnsureStyles();
            float scale = Mathf.Min(Screen.width / Width, Screen.height / Height);
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - Width * scale) / 2, (Screen.height - Height * scale) / 2, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            Fill(new Rect(0, 0, Width, Height), background);
            Text(new Rect(28, 21, 700, 38), "CONVERGENCE  /  DUEL LAB", title);
            Text(new Rect(30, 62, 1000, 22), "아스트랄 택틱스 · 서로 다른 세계가 만나는 듀얼", small, muted);
            bool overlay = handoff || showHelp || (screen == GameScreen.Duel && duel != null && (duel.IsGameOver || TutorialComplete));
            GUI.enabled = !overlay;
            switch (screen)
            {
                case GameScreen.Title: DrawTitleScreen(); break;
                case GameScreen.Home: DrawHomeScreen(); break;
                case GameScreen.DeckBuilder: DrawDeckBuilder(); break;
                case GameScreen.MatchSetup: DrawMatchSetup(); break;
                case GameScreen.Solo: DrawSoloScreen(); break;
                case GameScreen.Shop: DrawShopScreen(); break;
                case GameScreen.Settings: DrawSettingsScreen(); break;
                case GameScreen.Duel: DrawDuel(); break;
            }
            GUI.enabled = true;
            if (screen == GameScreen.Duel && !showHelp)
            {
                if (TutorialComplete) DrawTutorialResult();
                else if (duel != null && duel.IsGameOver) DrawResult();
            }
            if (handoff) DrawHandoff();
            if (showHelp) DrawHelp();
            GUI.matrix = previous;
        }

        private void DrawDeckBuilder()
        {
            if (Button(new Rect(1248, 30, 164, 36), "플레이 가이드")) showHelp = true;
            Panel(new Rect(24, 104, 865, 692));
            Text(new Rect(44, 122, 430, 32), "카드 라이브러리", title);
            Text(new Rect(44, 162, 780, 24), "작품은 분류일 뿐입니다. 모든 캐릭터와 공용 카드를 함께 사용할 수 있습니다.", small, muted);
            search = GUI.TextField(new Rect(44, 202, 823, 36), search, input);
            if (search.Length == 0) Text(new Rect(57, 210, 780, 22), "이름 · 작품 · 종류로 검색", small, muted);
            var cards = catalog.Where(c => c.Kind != CardKind.Token && (string.IsNullOrWhiteSpace(search) ||
                (c.Name + c.Series + KindName(c.Kind)).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            catalogScroll = GUI.BeginScrollView(new Rect(44, 252, 823, 522), catalogScroll, new Rect(0, 0, 800, cards.Length * 92));
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i]; float y = i * 92;
                Fill(new Rect(0, y, 800, 84), new Color(0.09f, 0.13f, 0.19f));
                Fill(new Rect(0, y, 4, 84), CardColor(card));
                Text(new Rect(16, y + 9, 450, 25), card.Name, cardName);
                Text(new Rect(16, y + 36, 590, 20), card.Series + "  /  " + KindName(card.Kind) + (card.Kind == CardKind.Character ? "  /  ATK " + card.Attack + " · HP " + card.MaxHealth : ""), small, CardColor(card));
                Text(new Rect(16, y + 58, 649, 25), card.Description, small, muted);
                int count = deck.Count(id => id == card.Id);
                int owned = ProgressionService.OwnedCount(profile, card.Id);
                Text(new Rect(672, y + 9, 120, 20), "편성 " + count + " / 보유 " + owned, small, muted);
                if (Button(new Rect(680, y + 35, 104, 35), owned == 0 ? "미보유" : "+ 추가", count < Math.Min(rules.MaxCopies, owned) && deck.Count < rules.MaxDeckSize)) deck.Add(card.Id);
            }
            GUI.EndScrollView();

            Panel(new Rect(910, 104, 506, 692));
            Text(new Rect(932, 122, 300, 36), "MY DECK", title);
            Text(new Rect(1260, 131, 140, 26), deck.Count + " / " + rules.MaxDeckSize + "장", label, accent);
            int unitCount = deck.Count(id => Definition(id)?.Kind == CardKind.Character);
            Text(new Rect(932, 169, 450, 42), $"캐릭터 {unitCount}  ·  지원 {deck.Count - unitCount}\n{rules.MinDeckSize}–{rules.MaxDeckSize}장 / 같은 카드 최대 {rules.MaxCopies}장 / 작품 제한 없음", small, muted);
            var groups = deck.GroupBy(id => id).ToArray();
            deckScroll = GUI.BeginScrollView(new Rect(932, 226, 462, 386), deckScroll, new Rect(0, 0, 440, groups.Length * 39));
            for (int i = 0; i < groups.Length; i++)
            {
                float y = i * 39; var group = groups[i]; var card = Definition(group.Key);
                Text(new Rect(0, y + 6, 317, 26), card.Name, label, CardColor(card));
                Text(new Rect(321, y + 6, 40, 26), "×" + group.Count(), label);
                if (Button(new Rect(369, y, 55, 31), "−")) deck.Remove(group.Key);
            }
            GUI.EndScrollView();
            if (Button(new Rect(932, 631, 145, 36), "시작 덱 불러오기")) LoadStarterDeck();
            if (Button(new Rect(1088, 631, 145, 36), "덱 비우기")) deck.Clear();
            if (Button(new Rect(1244, 631, 148, 36), "덱 저장")) SaveDeck();
            var errors = ProgressionService.ValidateOwnedDeck(profile, deck, catalog, rules);
            Text(new Rect(932, 684, 455, 75), errors.Count == 0 ? "준비 완료  ·  자유롭게 섞은 덱으로 출전하세요.\n저장한 덱은 다음 실행에도 유지됩니다." : string.Join("\n", errors.Take(3)), small, errors.Count == 0 ? accent : gold);
            Text(new Rect(30, 819, 500, 57), notice, small, muted);
            if (Button(new Rect(797, 814, 294, 55), "메인 화면으로")) GoHome();
            if (Button(new Rect(1106, 814, 309, 55), "게임 시작 →", errors.Count == 0)) OpenMatchSetup(false);
        }

        private void DrawDuel()
        {
            if (Button(new Rect(962, 29, 139, 36), "플레이 가이드")) showHelp = true;
            if (Button(new Rect(1113, 29, 126, 36), "설정")) { OpenSettings(); return; }
            if (Button(new Rect(1251, 29, 163, 36), "메인으로 나가기")) { GoHome(); return; }
            var self = duel.Players[viewer]; var enemy = duel.Players[1 - viewer];
            DrawPlayer(new Rect(24, 101, 1024, 57), enemy, true);
            DrawBackrow(enemy, 171, false);
            DrawField(enemy, 232);
            DrawPhaseBar();
            DrawField(self, 474);
            DrawBackrow(self, 642, true);
            DrawPlayer(new Rect(24, 699, 1024, 47), self, false);
            DrawHand(self);
            DrawSidebar();
        }

        private void DrawPlayer(Rect rect, PlayerState player, bool enemy)
        {
            Panel(rect);
            string name = player.Id == 0 ? "PLAYER 01" : (vsAi ? "TRAINING AI" : "PLAYER 02");
            Text(new Rect(rect.x + 15, rect.y + 9, 185, 25), name, cardName, enemy ? new Color(1f, 0.56f, 0.52f) : accent);
            Text(new Rect(rect.x + 203, rect.y + 9, 209, 27), "LP  " + player.Life.ToString("N0"), cardName);
            Text(new Rect(rect.x + 405, rect.y + 11, 578, 26), "덱 " + player.Deck.Count + "  ·  패 " + player.Hand.Count + "  ·  묘지 " + player.Graveyard.Count + "  ·  메인 " + (Definition(player.MainCharacterId)?.Name ?? "—"), small, muted);
            if (enemy && pendingAttack && HumanCanAct() && player.Field.Count == 0)
            {
                if (Button(new Rect(rect.x + 824, rect.y + 6, 188, rect.height - 12), "직접 공격")) Act(TutorialAction.Attack, pendingSource);
            }
        }

        private void DrawField(PlayerState player, float y)
        {
            for (int i = 0; i < duel.Rules.FieldSlots; i++)
            {
                float cardWidth = Mathf.Min(191, (1024f - 16 * (duel.Rules.FieldSlots - 1)) / duel.Rules.FieldSlots);
                var rect = new Rect(24 + i * (cardWidth + 16), y, cardWidth, 154);
                if (i < player.Field.Count) DrawCard(rect, player.Field[i], true);
                else
                {
                    Fill(rect, new Color(0.053f, 0.078f, 0.116f));
                    Border(rect, new Color(0.13f, 0.18f, 0.25f), 1);
                    Text(new Rect(rect.x + 17, y + 58, rect.width - 25, 30), "CHARACTER  0" + (i + 1), small, new Color(0.22f, 0.29f, 0.38f));
                }
            }
        }

        private void DrawBackrow(PlayerState player, float y, bool reveal)
        {
            float domainWidth = 169;
            for (int i = 0; i < duel.Rules.BackrowSlots; i++)
            {
                float width = (1024f - domainWidth - 12 - 10 * (duel.Rules.BackrowSlots - 1)) / duel.Rules.BackrowSlots;
                var rect = new Rect(24 + i * (width + 10), y, width, 43);
                Fill(rect, new Color(0.09f, 0.086f, 0.15f));
                if (i < player.Backrow.Count)
                {
                    var card = player.Backrow[i];
                    if (Button(rect, reveal ? card.Definition.Name : "◆  SET", reveal)) ClickCard(card);
                    if (reveal && selectedId == card.InstanceId) Border(rect, accent, 2);
                    if (reveal && IsTutorialHighlight(card.InstanceId)) Border(rect, gold, 3);
                }
                else Text(new Rect(rect.x + 13, y + 12, rect.width - 20, 25), "INTERCEPT  0" + (i + 1), small, new Color(0.34f, 0.31f, 0.43f));
            }
            var domainRect = new Rect(1048 - domainWidth, y, domainWidth, 43);
            Fill(domainRect, new Color(0.08f, 0.20f, 0.21f));
            if (player.DomainCard != null)
            {
                if (Button(domainRect, "필드 · " + player.DomainCard.Definition.Name)) ClickCard(player.DomainCard);
                if (selectedId == player.DomainCard.InstanceId) Border(domainRect, accent, 2);
            }
            else Text(new Rect(domainRect.x + 15, y + 12, domainWidth - 22, 26), "FIELD  /  필드 카드", small, muted);
        }

        private void DrawHand(PlayerState player)
        {
            handScroll = GUI.BeginScrollView(new Rect(24, 755, 1026, 137), handScroll, new Rect(0, 0, Mathf.Max(1004, player.Hand.Count * 158), 116));
            for (int i = 0; i < player.Hand.Count; i++) DrawCard(new Rect(i * 158, 0, 146, 114), player.Hand[i], false);
            GUI.EndScrollView();
        }

        private void DrawCard(Rect rect, CardInstance card, bool field)
        {
            var definition = card.Definition;
            Color tint = CardColor(definition);
            Fill(rect, Color.Lerp(panel, tint, 0.12f));
            Fill(new Rect(rect.x, rect.y, rect.width, 4), tint);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) ClickCard(card);
            if (selectedId == card.InstanceId) Border(rect, accent, 2);
            if (IsTutorialHighlight(card.InstanceId)) Border(rect, gold, 3);
            bool main = definition.Id == duel.Players[card.Owner].MainCharacterId;
            Text(new Rect(rect.x + 12, rect.y + 10, rect.width - 22, field ? 28 : 38), definition.Name, field ? cardName : handName);
            Text(new Rect(rect.x + 12, rect.y + (field ? 41 : 51), rect.width - 20, 22), ShortSeries(definition.Series) + " / " + KindName(definition.Kind), small, tint);
            if (field)
            {
                Text(new Rect(rect.x + 12, rect.y + 71, rect.width - 20, 29), "ATK  " + card.Attack, cardName);
                Text(new Rect(rect.x + 12, rect.y + 104, rect.width - 20, 24), "HP  " + card.Health + " / " + definition.MaxHealth, small, card.Health < definition.MaxHealth ? gold : muted);
                Fill(new Rect(rect.x + 12, rect.y + 132, rect.width - 24, 4), new Color(0.14f, 0.19f, 0.25f));
                Fill(new Rect(rect.x + 12, rect.y + 132, (rect.width - 24) * Mathf.Clamp01((float)card.Health / definition.MaxHealth), 4), tint);
                if (card.AttacksMade > 0) Text(new Rect(rect.x + rect.width - 63, rect.y + 107, 58, 18), "공격 완료", small, muted);
            }
            else Text(new Rect(rect.x + 12, rect.y + 81, rect.width - 20, 27), card.IsUnit ? card.Attack + " / " + definition.MaxHealth : EffectName(definition.Effect), small, Color.white);
            if (main) Fill(new Rect(rect.x + rect.width - 11, rect.y + 4, 7, 7), gold);
        }

        private void DrawPhaseBar()
        {
            Panel(new Rect(24, 400, 1024, 58));
            Text(new Rect(40, 416, 160, 25), "TURN " + duel.TurnNumber + "  ·  P" + (duel.ActivePlayer + 1), cardName, accent);
            var phases = new[] { "드로우", "메인 1", "배틀", "메인 2", "엔드" };
            for (int i = 0; i < phases.Length; i++)
            {
                var rect = new Rect(204 + i * 126, 409, 114, 39);
                if ((int)duel.Phase == i) Fill(rect, new Color(0.14f, 0.3f, 0.32f));
                Text(new Rect(rect.x + 19, rect.y + 10, 95, 25), phases[i], label, (int)duel.Phase == i ? accent : muted);
            }
            var nextRect = new Rect(849, 410, 183, 37);
            if (Button(nextRect, duel.Chain.Count > 0 ? "체인 처리 중" : "다음 페이즈 →", HumanCanAct() && duel.Chain.Count == 0 && duel.ActivePlayer == viewer && !duel.IsGameOver)) Act(TutorialAction.AdvancePhase);
            if (TutorialExpects(TutorialAction.AdvancePhase)) Border(nextRect, gold, 2);
        }

        private void DrawSidebar()
        {
            Panel(new Rect(1068, 101, 348, 320));
            string status = duel.IsGameOver ? "DUEL FINISHED" : duel.Chain.Count > 0 ? "RESPONSE  ·  P" + (duel.PriorityPlayer + 1) : "ACTION  ·  P" + (duel.ActivePlayer + 1);
            Text(new Rect(1086, 116, 312, 26), status, cardName, accent);
            Text(new Rect(1086, 153, 308, 48), notice, small, gold);
            var selected = duel.FindCard(selectedId);
            if (selected != null && IsVisible(selected))
            {
                Text(new Rect(1086, 207, 306, 29), selected.Definition.Name, cardName);
                Text(new Rect(1086, 241, 306, 27), selected.Definition.Series + " · " + selected.Definition.Element, small, CardColor(selected.Definition));
                Text(new Rect(1086, 274, 306, 63), selected.Definition.Description, label);
                DrawSelectedActions(selected);
            }
            else Text(new Rect(1086, 212, 308, 118), "패에서 카드를 선택해 소환하거나 발동하세요.\n\n배틀에서는 아군 → 공격 선언 → 상대 순서로 선택합니다.", label, muted);
            if (pendingSource >= 0)
            {
                if (Button(new Rect(1086, 373, 308, 33), "대상 선택 취소")) { pendingSource = -1; pendingAttack = false; notice = "대상 선택을 취소했습니다."; }
            }

            Panel(new Rect(1068, 434, 348, 211));
            Text(new Rect(1086, 446, 310, 25), "CHAIN  /  " + duel.Chain.Count + " LINKS", cardName);
            if (duel.Chain.Count == 0) Text(new Rect(1086, 485, 302, 85), "공격과 커맨드 발동 후 응답 창이 열립니다.\n양쪽이 연속으로 패스하면 마지막 링크부터 처리합니다.", small, muted);
            else
            {
                for (int i = 0; i < Math.Min(duel.Chain.Count, 4); i++)
                {
                    var link = duel.Chain[duel.Chain.Count - 1 - i];
                    Text(new Rect(1086, 480 + i * 25, 310, 24), "#" + link.Id + "  P" + (link.Controller + 1) + "  " + link.Label, small, i == 0 ? accent : muted);
                }
                var passRect = new Rect(1086, 593, 310, 37);
                if (Button(passRect, "응답 패스 / 체인 진행", HumanCanAct() && duel.PriorityPlayer == viewer)) Act(TutorialAction.PassPriority);
                if (TutorialExpects(TutorialAction.PassPriority)) Border(passRect, gold, 2);
            }

            Panel(new Rect(1068, 658, 348, 230));
            var source = pendingSource >= 0 ? duel.FindCard(pendingSource) : null;
            if (!pendingAttack && source?.Definition.Target == TargetKind.GraveyardCharacter)
            {
                Text(new Rect(1086, 672, 310, 26), "부활할 묘지 캐릭터 선택", cardName, accent);
                var targets = duel.Players[viewer].Graveyard.Where(c => c.Definition.Kind == CardKind.Character).ToArray();
                targetScroll = GUI.BeginScrollView(new Rect(1086, 708, 310, 163), targetScroll, new Rect(0, 0, 284, targets.Length * 41));
                for (int i = 0; i < targets.Length; i++)
                    if (Button(new Rect(0, i * 41, 280, 35), targets[i].Definition.Name)) ClickCard(targets[i]);
                GUI.EndScrollView();
            }
            else if (tutorial != null)
            {
                DrawTutorialObjective();
            }
            else
            {
                Text(new Rect(1086, 672, 310, 26), "DUEL LOG", cardName);
                int count = Math.Min(duel.Log.Count, 50);
                logScroll = GUI.BeginScrollView(new Rect(1086, 708, 310, 163), logScroll, new Rect(0, 0, 282, count * 47));
                for (int i = 0; i < count; i++) Text(new Rect(0, i * 47, 280, 46), duel.Log[duel.Log.Count - 1 - i], small, i == 0 ? Color.white : muted);
                GUI.EndScrollView();
            }
        }

        private void DrawSelectedActions(CardInstance card)
        {
            if (pendingSource >= 0 || card.Owner != viewer || !HumanCanAct() || duel.IsGameOver) return;
            if (card.Zone == CardZone.Hand)
            {
                bool ownMain = duel.ActivePlayer == viewer && duel.Chain.Count == 0 &&
                    (duel.Phase == DuelPhase.Main1 || duel.Phase == DuelPhase.Main2);
                if (!ownMain)
                {
                    Text(new Rect(1086, 347, 308, 42), "자신의 메인 페이즈에서 사용할 수 있습니다.", small, muted);
                    return;
                }
                if (card.Definition.Kind == CardKind.Character)
                { if (Button(new Rect(1086, 343, 308, 39), "일반 소환", duel.Players[viewer].NormalSummonsUsed < duel.Rules.NormalSummonsPerTurn && duel.Players[viewer].Field.Count < duel.Rules.FieldSlots)) Act(TutorialAction.Summon, card.InstanceId); }
                else if (card.Definition.Kind == CardKind.Intercept)
                { if (Button(new Rect(1086, 343, 308, 39), "함정 / 인터셉트 세트")) Act(TutorialAction.SetIntercept, card.InstanceId); }
                else if (Button(new Rect(1086, 343, 308, 39), card.Definition.Kind == CardKind.Domain ? "필드 카드 발동" : "마법 / 커맨드 발동")) BeginEffect(card);
            }
            else if (card.Zone == CardZone.Backrow && duel.Chain.Count > 0 && duel.PriorityPlayer == viewer)
            { if (Button(new Rect(1086, 343, 308, 39), "인터셉트 발동")) BeginEffect(card); }
            else if (card.Zone == CardZone.Field && duel.Phase == DuelPhase.Battle && duel.Chain.Count == 0)
            {
                if (Button(new Rect(1086, 343, 308, 39), "공격 선언", card.AttacksMade == 0 && duel.ActivePlayer == viewer))
                {
                    pendingSource = card.InstanceId; pendingAttack = true;
                    notice = duel.Players[1 - viewer].Field.Count == 0 ? "상대 LP 옆의 직접 공격 버튼을 누르세요." : "공격할 상대 캐릭터를 선택하세요.";
                }
            }
        }

        private void BeginEffect(CardInstance card)
        {
            if (card.Definition.Target == TargetKind.None) { Act(TutorialAction.Activate, card.InstanceId); return; }
            pendingSource = card.InstanceId; pendingAttack = false;
            switch (card.Definition.Target)
            {
                case TargetKind.FriendlyUnit: notice = "대상으로 삼을 아군 필드 유닛을 선택하세요."; break;
                case TargetKind.EnemyUnit: notice = "대상으로 삼을 상대 필드 유닛을 선택하세요."; break;
                case TargetKind.HandCharacter: notice = "특수 소환할 패의 캐릭터를 선택하세요."; break;
                case TargetKind.GraveyardCharacter: notice = "오른쪽 아래 목록에서 부활할 캐릭터를 고르세요."; break;
                default: notice = "효과를 적용할 필드 유닛을 선택하세요."; break;
            }
        }

        private void ClickCard(CardInstance card)
        {
            if (pendingSource >= 0 && HumanCanAct() && !duel.IsGameOver)
            {
                Act(pendingAttack ? TutorialAction.Attack : TutorialAction.Activate, pendingSource, card.InstanceId);
                return;
            }
            selectedId = card.InstanceId;
        }

        private void Apply(ActionResult result)
        {
            notice = result.Success ? (string.IsNullOrEmpty(result.Message) ? "행동을 처리했습니다." : result.Message) : result.Message;
            if (result.Success)
            {
                ResetSelection();
                nextBotAction = Time.unscaledTime + botActionDelay;
                CheckTutorialCompletion();
                SyncHandoff();
            }
        }

        private bool HumanCanAct() => !handoff && !TutorialComplete && (tutorial == null || !tutorial.IsOpponentStep) && (!vsAi || DecisionPlayer() == 0);
        private int DecisionPlayer() => duel.Chain.Count > 0 ? duel.PriorityPlayer : duel.ActivePlayer;
        private bool IsVisible(CardInstance card) => card.Owner == viewer || card.Zone == CardZone.Field || card.Zone == CardZone.Graveyard || card.Zone == CardZone.Chain || card.Zone == CardZone.Domain;
        private void ResetSelection() { selectedId = -1; pendingSource = -1; pendingAttack = false; }
        private void SyncHandoff()
        {
            if (!vsAi && !duel.IsGameOver && DecisionPlayer() != viewer) { handoff = true; ResetSelection(); }
        }

        private void DrawHandoff()
        {
            Fill(new Rect(0, 0, Width, Height), background);
            Text(new Rect(433, 284, 650, 53), "P" + (DecisionPlayer() + 1) + " 플레이어에게 차례를 넘겨주세요", title);
            Text(new Rect(433, 359, 588, 70), "응답할 카드가 없어도 체인 응답 차례를 넘깁니다.\n준비가 되면 버튼을 눌러 자신의 패를 확인하세요.", label, muted);
            if (Button(new Rect(480, 462, 470, 57), "준비 완료 · 내 패 보기")) { viewer = DecisionPlayer(); handoff = false; notice = "P" + (viewer + 1) + "의 행동 차례입니다."; }
        }

        private void DrawResult()
        {
            Fill(new Rect(210, 293, 665, 269), new Color(0.05f, 0.095f, 0.13f));
            Border(new Rect(210, 293, 665, 269), accent, 2);
            Text(new Rect(245, 321, 590, 48), duel.Winner == -1 ? "DRAW" : "PLAYER " + (duel.Winner + 1) + "  WINS", title, accent);
            Text(new Rect(245, 383, 585, 61), duel.ResultReason, label);
            if (Button(new Rect(245, 468, 273, 53), tutorial != null ? "튜토리얼 다시 시작" : "대전 설정으로"))
            {
                if (tutorial != null) StartTutorial(tutorial.LessonId); else OpenMatchSetup(useLoanerDeck);
            }
            if (Button(new Rect(538, 468, 295, 53), "메인 화면으로")) GoHome();
        }

        private void DrawHelp()
        {
            Fill(new Rect(0, 0, Width, Height), new Color(0.018f, 0.028f, 0.05f, 0.98f));
            Panel(new Rect(245, 125, 950, 650));
            Text(new Rect(285, 157, 850, 44), "플레이 가이드", title, accent);
            Text(new Rect(285, 227, 850, 453),
                "01  덱 구성\n20–40장, 같은 카드 최대 3장. 작품 제한 없이 혼합합니다. 저장 후 대전을 시작하세요.\n\n" +
                "02  전개\n드로우 → 메인 1에서 패를 클릭해 일반 소환(턴당 1회), 커맨드 발동, 인터셉트 세트.\n차원을 잇는 합류·부활·토큰 효과로 일반 소환 외에 추가 전개할 수 있습니다.\n\n" +
                "03  체인\n공격이나 커맨드가 발동되면 상대부터 응답합니다. 이전 턴에 세트한 인터셉트로 개입하세요.\n양쪽이 연속 패스하면 역순 처리합니다. AI 대전에서도 내 응답은 직접 패스해야 합니다.\n\n" +
                "04  전투와 승리\n배틀에서 아군 선택 → 공격 선언 → 상대 선택. 상대 필드가 비면 직접 공격할 수 있습니다.\n캐릭터끼리는 서로 공격력만큼 피해를 주며 체력은 유지됩니다. 각 유닛은 턴당 한 번 공격.\nLP가 0이 되거나 덱이 빈 상태에서 드로우하면 패배합니다. 선공 첫 턴은 공격할 수 없습니다.\n\n" +
                "현재 수치는 임시 규칙입니다. 메인 캐릭터 표시는 금색 점이며 카드 능력을 바꾸지 않습니다.", label);
            if (Button(new Rect(858, 704, 293, 43), "가이드 닫기")) showHelp = false;
        }

        private void StartDuel(bool ai)
        {
            int seed;
            if (!int.TryParse(seedText, out seed)) { notice = "셔플 시드는 정수로 입력해주세요."; return; }
            try
            {
                var matchDeck = useLoanerDeck ? SampleContent.CreateStarterDeck() : deck;
                var errors = useLoanerDeck ? DeckValidator.Validate(matchDeck, catalog, rules) : ProgressionService.ValidateOwnedDeck(profile, matchDeck, catalog, rules);
                if (errors.Count > 0) { notice = errors[0]; return; }
                var matchRules = rules.Copy();
                matchRules.FirstPlayer = firstTurnChoice == 0 ? 0 : firstTurnChoice == 1 ? 1 : new System.Random(seed).Next(2);
                duel = new DuelEngine(catalog, matchRules, matchDeck, SampleContent.CreateOpponentDeck(), seed);
                tutorial = null; tutorialRewardHandled = false; screen = GameScreen.Duel;
                vsAi = ai; viewer = 0; handoff = false; showHelp = false; ResetSelection();
                handScroll = Vector2.zero; logScroll = Vector2.zero;
                notice = (matchRules.FirstPlayer == 0 ? "선공" : "후공") + "으로 시작합니다. 시작 패 " + matchRules.StartingHand + "장.";
                nextBotAction = Time.unscaledTime + botActionDelay;
                SyncHandoff();
            }
            catch (ArgumentException error) { notice = error.Message; duel = null; }
        }

        private void SaveDeck()
        {
            var errors = ProgressionService.ValidateOwnedDeck(profile, deck, catalog, rules);
            if (errors.Count > 0) { notice = errors[0]; return; }
            profile.SavedDeck = new List<string>(deck);
            if (SaveProfile()) notice = "덱을 이 기기에 저장했습니다.";
        }

        private CardDefinition Definition(string id) => catalog.FirstOrDefault(c => c.Id == id);
        private static string ShortSeries(string series) => series.Contains("스타레일") ? "스타레일" : series;
        private static string KindName(CardKind kind) => kind == CardKind.Character ? "캐릭터" : kind == CardKind.Command ? "마법·커맨드" : kind == CardKind.Intercept ? "함정·인터셉트" : kind == CardKind.Domain ? "필드" : "토큰";
        private static string EffectName(EffectKind effect)
        {
            switch (effect)
            {
                case EffectKind.Draw: return "드로우";
                case EffectKind.DamageUnit: return "대상 피해";
                case EffectKind.BoostUnit: return "공격력 강화";
                case EffectKind.SpecialSummonFromHand: return "특수 소환";
                case EffectKind.Revive: return "묘지에서 부활";
                case EffectKind.CreateToken: return "토큰 소환";
                case EffectKind.HealPlayer: return "LP 회복";
                case EffectKind.Negate: return "체인 무효";
                case EffectKind.DestroyUnit: return "대상 파괴";
                case EffectKind.FieldAura: return "필드 · 아군 공격력 강화";
                default: return "효과";
            }
        }
        private Color CardColor(CardDefinition definition)
        {
            if (definition.Kind == CardKind.Command) return accent;
            if (definition.Kind == CardKind.Domain) return new Color(0.42f, 0.92f, 0.64f);
            if (definition.Kind == CardKind.Intercept) return new Color(0.77f, 0.59f, 0.98f);
            if (definition.Kind == CardKind.Token) return new Color(0.72f, 0.78f, 0.89f);
            return definition.Series.Contains("스타레일") ? new Color(0.47f, 0.72f, 1f) : gold;
        }
        private void Panel(Rect rect) => Fill(rect, panel);
        private static void Fill(Rect rect, Color color)
        {
            var saved = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = saved;
        }
        private static void Border(Rect rect, Color color, int thickness)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            Fill(new Rect(rect.x, rect.y, thickness, rect.height), color);
            Fill(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
        private static void Text(Rect rect, string text, GUIStyle style, Color? color = null)
        {
            var saved = GUI.color; GUI.color = color ?? Color.white; GUI.Label(rect, text, style); GUI.color = saved;
        }
        private bool Button(Rect rect, string text, bool enabled = true)
        {
            bool saved = GUI.enabled; GUI.enabled = saved && enabled;
            bool clicked = GUI.Button(rect, text, button); GUI.enabled = saved;
            return clicked;
        }
    }
}
