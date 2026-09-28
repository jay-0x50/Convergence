using System;
using System.Collections.Generic;
using System.Linq;
using Convergence.Duel;
using UnityEngine;

namespace Convergence.Prototype
{
    public sealed partial class DuelPrototype
    {
        private enum GameScreen { Title, Home, DeckBuilder, MatchSetup, Solo, Shop, Settings, Duel }
        private const string ProfileSaveKey = "Convergence.Prototype.Profile.v1";
        private GameScreen screen = GameScreen.Title, settingsReturn = GameScreen.Home;
        private PlayerProgress profile;
        private PrototypeSettings settingsDraft;
        private TutorialSession tutorial;
        private bool tutorialRewardHandled, useLoanerDeck;
        private string tutorialRewardNotice = "", shopSearch = "";
        private Vector2 shopScroll;
        private int firstTurnChoice, matchMode;
        private GUIStyle heroTitle, centeredTitle, centeredText;
        private bool TutorialComplete => tutorial != null && tutorial.IsComplete;
        private readonly string[] lessonTitles = { "기본 듀얼", "특수 소환과 전개", "필드 · 마법 · 함정" };
        private readonly string[] lessonDescriptions =
        {
            "일반 소환부터 유닛 전투, 직접 공격까지.\n턴과 페이즈를 익히고 첫 승리를 경험하세요.",
            "일반 소환 후에도 전개는 계속됩니다.\n다른 작품의 동료를 특수 소환하고 토큰을 펼치세요.",
            "필드 카드의 지속 강화, 마법 카드의 효과,\n상대 공격에 대응하는 함정과 체인을 연습하세요."
        };
        private readonly string[] lessonRewards =
        {
            "첫 완료: 혼합 시작 덱 28장 + 150 코인",
            "첫 완료: 특수 소환·부활·토큰 지원 카드 + 200 코인",
            "첫 완료: 필드·마법·함정 지원 카드 + 250 코인"
        };

        private void EnsureMenuStyles()
        {
            if (heroTitle != null) return;
            heroTitle = new GUIStyle(title) { fontSize = 64, alignment = TextAnchor.MiddleCenter };
            centeredTitle = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter };
            centeredText = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
        }

        private void DrawTitleScreen()
        {
            EnsureMenuStyles();
            // The title receives the input event before any destination menu controls can use it.
            if (Event.current.type == EventType.KeyDown || Event.current.type == EventType.MouseDown)
            {
                screen = GameScreen.Home;
                Event.current.Use();
                return;
            }
            for (int i = 0; i < 8; i++)
                Border(new Rect(460 + i * 20, 180 + i * 20, 520 - i * 40, 520 - i * 40), new Color(0.08f, 0.16f + i * 0.007f, 0.22f), 1);
            Text(new Rect(140, 286, 1160, 94), "CONVERGENCE", heroTitle);
            Text(new Rect(360, 396, 720, 50), "서로 다른 세계, 하나의 듀얼", centeredTitle, accent);
            float pulse = 0.6f + Mathf.Sin(Time.unscaledTime * 2f) * 0.3f;
            Text(new Rect(370, 608, 700, 42), "아무 키나 눌러주세요", centeredText, new Color(1, 1, 1, pulse));
            Text(new Rect(370, 658, 700, 31), "마우스 클릭으로도 시작할 수 있습니다", centeredText, muted);
            Text(new Rect(410, 814, 620, 30), "아스트랄 택틱스  /  나만의 덱으로 시작하는 이야기", centeredText, muted);
        }

        private void DrawHomeScreen()
        {
            Text(new Rect(1040, 34, 360, 27), "보유 카드 " + profile.Collection.Sum(c => c.Count) + "장   /   " + profile.Coins + " 코인", label, gold);
            Panel(new Rect(24, 111, 1392, 223));
            Text(new Rect(52, 143, 780, 49), "나만의 덱으로, 다음 듀얼을.", title);
            Text(new Rect(52, 211, 858, 67), profile.CompletedLessons.Count == 0
                ? "솔로 튜토리얼에서 첫 덱을 얻으세요.\n스타레일과 원신, 어떤 작품의 카드든 자유롭게 함께 편성할 수 있습니다."
                : "익힌 전개를 나만의 조합으로 바꿔보세요.\n보유한 카드로 덱을 만들고 AI 또는 같은 기기의 친구와 대전할 수 있습니다.", label, muted);
            Text(new Rect(1073, 157, 295, 34), "튜토리얼  " + profile.CompletedLessons.Count + " / 3", title, accent);
            Text(new Rect(1074, 215, 289, 55), "현재 덱  " + deck.Count + "장\n작품 제한 없음", label, muted);

            if (MenuTile(new Rect(24, 358, 448, 190), "01  게임 시작", "AI 또는 로컬 2인 대전\n선공·후공을 고르고 대전을 시작합니다.", accent)) OpenMatchSetup(false);
            if (MenuTile(new Rect(496, 358, 448, 190), "02  덱 만들기", "보유한 카드를 자유롭게 조합\n20–40장으로 나만의 덱을 완성하세요.", gold)) { screen = GameScreen.DeckBuilder; notice = "보유 카드 안에서 작품 구분 없이 자유롭게 편성하세요."; }
            if (MenuTile(new Rect(968, 358, 448, 190), "03  솔로 모드", "세 가지 튜토리얼과 AI 연습\n대여 덱으로 배우고 시작 카드를 획득하세요.", new Color(0.57f, 0.72f, 1f))) screen = GameScreen.Solo;
            if (MenuTile(new Rect(24, 572, 448, 190), "04  상점", "튜토리얼 보상 코인으로 카드 구매\n원하는 카드를 골라 컬렉션을 늘리세요.", gold)) screen = GameScreen.Shop;
            if (MenuTile(new Rect(496, 572, 448, 190), "05  설정", "화면 · 음량 · 진행 속도\n편한 환경으로 맞추고 기기에 저장합니다.", muted)) OpenSettings();
            if (MenuTile(new Rect(968, 572, 448, 190), "06  타이틀로", "진행 상황은 이 기기에 보관됩니다.\n처음 화면으로 돌아갑니다.", muted)) screen = GameScreen.Title;
            Text(new Rect(36, 805, 1370, 54), notice, label, muted);
        }

        private bool MenuTile(Rect rect, string heading, string description, Color color)
        {
            Panel(rect); Fill(new Rect(rect.x, rect.y, 4, rect.height), color);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            Text(new Rect(rect.x + 26, rect.y + 26, rect.width - 50, 38), heading, title, color);
            Text(new Rect(rect.x + 26, rect.y + 94, rect.width - 52, 67), description, label, muted);
            return clicked;
        }

        private bool DrawMenuBack(string heading)
        {
            Text(new Rect(32, 118, 1000, 41), heading, title, accent);
            if (!Button(new Rect(1225, 29, 189, 36), "메인 화면으로")) return false;
            GoHome();
            return true;
        }

        private void DrawMatchSetup()
        {
            if (DrawMenuBack("대전 준비")) return;
            Panel(new Rect(24, 183, 886, 603));
            Panel(new Rect(934, 183, 482, 603));
            Text(new Rect(53, 210, 780, 35), "대전 방식", title);
            if (Choice(new Rect(53, 266, 397, 66), "AI 대전", matchMode == 0)) matchMode = 0;
            if (Choice(new Rect(478, 266, 397, 66), "로컬 2인 대전", matchMode == 1)) matchMode = 1;
            Text(new Rect(53, 350, 814, 53), matchMode == 0 ? "AI와 자신의 덱을 시험합니다. 상대 턴에도 함정으로 대응할 수 있습니다."
                : "하나의 기기에서 번갈아 플레이합니다. 2번 플레이어는 기본 상대 덱을 사용합니다.", label, muted);
            Text(new Rect(53, 431, 800, 37), "선공 / 후공 선택", title);
            if (Choice(new Rect(53, 486, 259, 66), "선공", firstTurnChoice == 0)) firstTurnChoice = 0;
            if (Choice(new Rect(335, 486, 259, 66), "후공", firstTurnChoice == 1)) firstTurnChoice = 1;
            if (Choice(new Rect(617, 486, 259, 66), "무작위", firstTurnChoice == 2)) firstTurnChoice = 2;
            Text(new Rect(53, 573, 800, 64), "선공은 먼저 전개하며 첫 턴 드로우와 공격을 생략합니다.\n후공은 자신의 첫 턴부터 드로우하고 공격할 수 있습니다.", label, muted);
            Text(new Rect(53, 686, 356, 26), "셔플 시드 · 같은 패를 다시 연습할 때 사용", small, muted);
            seedText = GUI.TextField(new Rect(429, 675, 241, 42), seedText, input);
            if (Button(new Rect(692, 675, 184, 42), "새로운 시드")) seedText = Environment.TickCount.ToString();

            var matchDeck = useLoanerDeck ? SampleContent.CreateStarterDeck() : deck;
            var errors = useLoanerDeck ? DeckValidator.Validate(matchDeck, catalog, rules) : ProgressionService.ValidateOwnedDeck(profile, matchDeck, catalog, rules);
            Text(new Rect(961, 216, 420, 39), useLoanerDeck ? "연습용 대여 덱" : "내 덱", title, gold);
            Text(new Rect(961, 281, 416, 71), "카드 " + matchDeck.Count + "장  /  혼합 편성\n" + (useLoanerDeck ? "카드가 없어도 자유롭게 연습할 수 있습니다." : "덱 만들기에서 보유 카드의 구성을 바꿀 수 있습니다."), label, muted);
            Text(new Rect(961, 390, 416, 145), errors.Count == 0 ? "대전 준비가 끝났습니다.\n\n선공·후공 설정은 이번 대전에만 적용됩니다." : string.Join("\n", errors.Take(3)) + "\n솔로 튜토리얼 1을 마치면 시작 덱을 얻습니다.", label, errors.Count == 0 ? accent : gold);
            if (Button(new Rect(961, 575, 426, 53), "솔로 모드로")) { screen = GameScreen.Solo; return; }
            if (Button(new Rect(961, 650, 426, 53), "덱 구성 확인")) { screen = GameScreen.DeckBuilder; return; }
            Text(new Rect(32, 818, 800, 56), notice, small, gold);
            if (Button(new Rect(934, 813, 482, 57), "듀얼 시작", errors.Count == 0)) StartDuel(matchMode == 0);
        }

        private bool Choice(Rect rect, string caption, bool selected)
        {
            bool clicked = Button(rect, caption + (selected ? "  · 선택됨" : ""));
            if (selected) Border(rect, accent, 2);
            return clicked;
        }

        private void DrawSoloScreen()
        {
            if (DrawMenuBack("솔로 모드  /  듀얼 아카데미")) return;
            Text(new Rect(34, 175, 1320, 45), "대여 덱으로 배우는 세 가지 수업. 직접 행동을 완료하면 보상을 받습니다. 다시 도전해도 보상은 중복 지급되지 않습니다.", label, muted);
            for (int i = 0; i < 3; i++)
            {
                float x = 24 + i * 472;
                bool unlocked = ProgressionService.IsLessonUnlocked(profile, i);
                bool complete = profile.CompletedLessons.Contains(i);
                Panel(new Rect(x, 250, 448, 402));
                Text(new Rect(x + 28, 278, 388, 29), "LESSON 0" + (i + 1) + (complete ? "   ·   완료" : unlocked ? "   ·   도전 가능" : "   ·   잠김"), cardName, complete ? accent : muted);
                Text(new Rect(x + 28, 338, 393, 48), lessonTitles[i], title);
                Text(new Rect(x + 28, 412, 392, 95), lessonDescriptions[i], label, muted);
                Text(new Rect(x + 28, 520, 392, 57), complete ? "첫 완료 보상 수령 완료" : lessonRewards[i], small, gold);
                if (Button(new Rect(x + 28, 590, 392, 43), !unlocked ? "이전 수업을 먼저 완료하세요" : complete ? "다시 연습하기" : "튜토리얼 시작", unlocked)) { StartTutorial(i); return; }
            }
            Panel(new Rect(24, 679, 1392, 113));
            Text(new Rect(49, 699, 858, 30), "자유 연습  /  대여 덱으로 AI와 대전", cardName, accent);
            Text(new Rect(49, 742, 858, 32), "멀티플레이어 없이도 연습할 수 있습니다. 튜토리얼 진행도와 관계없이 사용할 수 있습니다.", label, muted);
            if (Button(new Rect(1080, 708, 309, 55), "AI 자유 연습")) { OpenMatchSetup(true); matchMode = 0; }
            Text(new Rect(32, 824, 1350, 47), notice, label, muted);
        }

        private void DrawShopScreen()
        {
            if (DrawMenuBack("상점  /  카드 직접 구매")) return;
            Text(new Rect(985, 120, 410, 39), "보유 코인  " + profile.Coins, title, gold);
            Text(new Rect(32, 178, 1350, 41), "튜토리얼 완료 보상으로 얻은 코인을 사용합니다. 카드 1장 " + ProgressionService.CardPrice + " 코인, 최대 " + rules.MaxCopies + "장 보유.", label, muted);
            shopSearch = GUI.TextField(new Rect(32, 232, 1378, 39), shopSearch, input);
            if (shopSearch.Length == 0) Text(new Rect(46, 243, 1100, 24), "카드 이름 또는 작품으로 검색", small, muted);
            var cards = catalog.Where(c => c.Kind != CardKind.Token && (string.IsNullOrWhiteSpace(shopSearch) ||
                (c.Name + c.Series + KindName(c.Kind)).IndexOf(shopSearch, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            shopScroll = GUI.BeginScrollView(new Rect(24, 291, 1392, 489), shopScroll, new Rect(0, 0, 1366, cards.Length * 88));
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i]; float y = i * 88;
                Panel(new Rect(0, y, 1366, 79));
                Fill(new Rect(0, y, 4, 79), CardColor(card));
                Text(new Rect(22, y + 12, 389, 27), card.Name, cardName, CardColor(card));
                Text(new Rect(22, y + 44, 389, 27), card.Series + " · " + KindName(card.Kind), small, muted);
                Text(new Rect(422, y + 17, 656, 57), card.Description, label, muted);
                int owned = ProgressionService.OwnedCount(profile, card.Id);
                Text(new Rect(1111, y + 10, 232, 25), "보유 " + owned + " / " + rules.MaxCopies + "장", small, gold);
                if (Button(new Rect(1106, y + 38, 238, 32), owned >= rules.MaxCopies ? "보유 한도 도달" : ProgressionService.CardPrice + " 코인 · 1장 구매", owned < rules.MaxCopies && profile.Coins >= ProgressionService.CardPrice))
                {
                    var result = ProgressionService.BuyCard(profile, card.Id, catalog, rules);
                    notice = result.Message;
                    if (result.Success) SaveProfile();
                }
            }
            GUI.EndScrollView();
            Text(new Rect(32, 815, 1120, 64), notice, label, gold);
        }

        private void DrawSettingsScreen()
        {
            Text(new Rect(32, 118, 1000, 41), "설정", title, accent);
            Panel(new Rect(220, 190, 1000, 530));
            Text(new Rect(254, 223, 282, 34), "전체 음량", cardName);
            settingsDraft.MasterVolume = GUI.HorizontalSlider(new Rect(569, 235, 421, 25), settingsDraft.MasterVolume, 0f, 1f);
            Text(new Rect(1064, 226, 120, 28), Mathf.RoundToInt(settingsDraft.MasterVolume * 100) + "%", label, gold);
            Text(new Rect(254, 306, 282, 34), "화면 모드", cardName);
            if (Choice(new Rect(568, 296, 271, 43), "창 모드", !settingsDraft.Fullscreen)) settingsDraft.Fullscreen = false;
            if (Choice(new Rect(863, 296, 321, 43), "전체 화면", settingsDraft.Fullscreen)) settingsDraft.Fullscreen = true;
            Text(new Rect(254, 391, 292, 36), "AI 행동 간격", cardName);
            settingsDraft.BotDelay = GUI.HorizontalSlider(new Rect(569, 402, 421, 25), settingsDraft.BotDelay, 0.15f, 1.5f);
            Text(new Rect(1064, 392, 120, 28), settingsDraft.BotDelay.ToString("0.00") + "초", label, gold);
            Text(new Rect(254, 476, 282, 34), "최대 프레임", cardName);
            int[] rates = { 30, 60, 120 };
            for (int i = 0; i < rates.Length; i++)
                if (Choice(new Rect(568 + i * 208, 463, 192, 44), rates[i] + " FPS", settingsDraft.TargetFrameRate == rates[i])) settingsDraft.TargetFrameRate = rates[i];
            Text(new Rect(254, 558, 928, 92), "AI 행동 간격은 튜토리얼 상대의 진행 속도에도 적용됩니다.\n전체 화면 전환은 게임 실행 파일에서 적용됩니다. 음량은 재생되는 게임 소리 전체에 적용됩니다.", label, muted);
            if (Button(new Rect(254, 656, 251, 43), "기본값으로")) settingsDraft = new PrototypeSettings();
            if (Button(new Rect(624, 777, 271, 54), "취소")) screen = settingsReturn;
            if (Button(new Rect(922, 777, 298, 54), "저장 후 돌아가기"))
            {
                profile.Settings = settingsDraft;
                ApplySettings(profile.Settings);
                bool saved = SaveProfile(); screen = settingsReturn;
                if (saved) notice = "설정을 저장했습니다.";
            }
        }

        private void DrawTutorialObjective()
        {
            Text(new Rect(1086, 672, 310, 26), "수업 " + (tutorial.LessonId + 1) + "  ·  " + tutorial.StepNumber + " / " + tutorial.StepCount, cardName, gold);
            Text(new Rect(1086, 709, 308, 112), tutorial.Instruction, label);
            Text(new Rect(1086, 836, 308, 37), tutorial.IsOpponentStep ? "상대 행동을 살펴보세요." : "금색으로 표시된 카드나 버튼을 사용하세요.", small, accent);
        }

        private void DrawTutorialResult()
        {
            Fill(new Rect(227, 232, 985, 414), background);
            Border(new Rect(227, 232, 985, 414), accent, 2);
            Text(new Rect(263, 264, 905, 50), "수업 완료  /  " + lessonTitles[tutorial.LessonId], title, accent);
            Text(new Rect(263, 347, 905, 117), tutorialRewardNotice + "\n\n획득한 카드와 코인은 메인 메뉴의 덱 만들기와 상점에서 사용할 수 있습니다.", label);
            int lesson = tutorial.LessonId;
            if (Button(new Rect(263, 549, 267, 58), "다시 연습하기")) { StartTutorial(lesson); return; }
            if (Button(new Rect(552, 549, 286, 58), "솔로 모드로")) { LeaveDuel(GameScreen.Solo); return; }
            if (Button(new Rect(861, 549, 313, 58), lesson < 2 ? "다음 수업으로" : "내 덱 확인하기"))
            {
                if (lesson < 2) StartTutorial(lesson + 1);
                else LeaveDuel(GameScreen.DeckBuilder);
            }
        }

        private void OpenMatchSetup(bool loaner)
        {
            LeaveDuel(GameScreen.MatchSetup);
            useLoanerDeck = loaner;
        }

        private void OpenSettings()
        {
            settingsReturn = screen;
            var current = profile.Settings;
            settingsDraft = new PrototypeSettings { MasterVolume = current.MasterVolume, Fullscreen = current.Fullscreen, BotDelay = current.BotDelay, TargetFrameRate = current.TargetFrameRate };
            screen = GameScreen.Settings;
        }

        private void GoHome() => LeaveDuel(GameScreen.Home);
        private void LeaveDuel(GameScreen destination)
        {
            screen = destination; duel = null; tutorial = null;
            handoff = false; showHelp = false; ResetSelection();
        }

        private void StartTutorial(int lesson)
        {
            if (!ProgressionService.IsLessonUnlocked(profile, lesson)) { notice = "이전 수업을 먼저 완료해주세요."; return; }
            tutorial = new TutorialSession(lesson);
            duel = tutorial.Engine;
            tutorialRewardHandled = false; tutorialRewardNotice = "";
            vsAi = true; viewer = 0; handoff = false; showHelp = false; ResetSelection();
            screen = GameScreen.Duel; handScroll = Vector2.zero; logScroll = Vector2.zero;
            notice = "대여 덱 실습입니다. 오른쪽 아래 안내를 따라 진행하세요.";
            nextBotAction = Time.unscaledTime + botActionDelay;
        }

        private void Act(TutorialAction action, int? sourceId = null, int? targetId = null)
        {
            if (tutorial != null) { Apply(tutorial.Perform(action, sourceId, targetId)); return; }
            switch (action)
            {
                case TutorialAction.AdvancePhase: Apply(duel.AdvancePhase(viewer)); break;
                case TutorialAction.Summon: Apply(duel.Summon(viewer, sourceId ?? -1)); break;
                case TutorialAction.SetIntercept: Apply(duel.SetIntercept(viewer, sourceId ?? -1)); break;
                case TutorialAction.Activate: Apply(duel.Activate(viewer, sourceId ?? -1, targetId)); break;
                case TutorialAction.Attack: Apply(duel.Attack(viewer, sourceId ?? -1, targetId)); break;
                case TutorialAction.PassPriority: Apply(duel.PassPriority(viewer)); break;
            }
        }

        private bool IsTutorialHighlight(int id) => tutorial != null && !tutorial.IsComplete && !tutorial.IsOpponentStep &&
            (tutorial.ExpectedSourceId == id || tutorial.ExpectedTargetId == id);
        private bool TutorialExpects(TutorialAction action) => tutorial != null && !tutorial.IsComplete && !tutorial.IsOpponentStep && tutorial.ExpectedAction == action;

        private void CheckTutorialCompletion()
        {
            if (!TutorialComplete || tutorialRewardHandled) return;
            tutorialRewardHandled = true;
            bool alreadyComplete = profile.CompletedLessons.Contains(tutorial.LessonId);
            var result = ProgressionService.ClaimTutorialReward(profile, tutorial.LessonId, catalog, rules);
            tutorialRewardNotice = alreadyComplete ? "복습을 완료했습니다. 첫 완료 보상은 이미 수령했습니다." : result.Message;
            if (result.Success)
            {
                if (deck.Count == 0 && profile.SavedDeck.Count > 0) deck = new List<string>(profile.SavedDeck);
                if (!SaveProfile()) tutorialRewardNotice += "\n" + notice;
            }
        }

        private void ApplySettings(PrototypeSettings settings)
        {
            AudioListener.volume = settings.MasterVolume;
            botActionDelay = settings.BotDelay;
            Application.targetFrameRate = settings.TargetFrameRate;
            if (!Application.isEditor) Screen.fullScreenMode = settings.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }

        private void LoadStarterDeck()
        {
            var starter = SampleContent.CreateStarterDeck();
            if (ProgressionService.ValidateOwnedDeck(profile, starter, catalog, rules).Count > 0)
            { notice = "솔로 모드의 기본 듀얼 수업을 완료하면 이 시작 덱을 얻습니다."; return; }
            deck = starter; notice = "보유한 시작 덱 28장을 불러왔습니다.";
        }

        private void LoadProfile()
        {
            if (smokeTesting)
            {
                profile = ProgressionService.CreateNew();
                deck = new List<string>();
                return;
            }
            bool hasProfile = PlayerPrefs.HasKey(ProfileSaveKey);
            if (hasProfile)
            {
                try { profile = JsonUtility.FromJson<PlayerProgress>(PlayerPrefs.GetString(ProfileSaveKey)); }
                catch (ArgumentException) { notice = "저장 정보를 읽지 못해 새 진행 정보로 시작합니다."; }
            }
            profile = ProgressionService.Normalize(profile ?? ProgressionService.CreateNew(), catalog, rules);
            if (!hasProfile && PlayerPrefs.HasKey(DeckSaveKey))
            {
                try
                {
                    var previous = JsonUtility.FromJson<SavedDeck>(PlayerPrefs.GetString(DeckSaveKey));
                    if (previous?.cards != null && ProgressionService.ImportLegacyDeck(profile, previous.cards, catalog, rules).Success)
                        notice = "이전에 저장한 덱과 카드 보유량을 이어받았습니다.";
                }
                catch (ArgumentException) { notice = "이전 덱을 읽지 못했습니다. 솔로 모드에서 시작 덱을 얻을 수 있습니다."; }
            }
            deck = new List<string>(profile.SavedDeck);
            if (deck.Count == 0) notice = "솔로 모드의 첫 수업을 마치면 시작 덱을 얻습니다. 자유 연습도 바로 사용할 수 있습니다.";
            SaveProfile();
        }

        private bool SaveProfile()
        {
            if (smokeTesting) return true;
            try
            {
                PlayerPrefs.SetString(ProfileSaveKey, JsonUtility.ToJson(profile));
                PlayerPrefs.Save();
                return true;
            }
            catch (PlayerPrefsException error)
            {
                notice = "기기에 저장하지 못했습니다. 현재 진행은 유지되지만 종료 전에 저장 권한을 확인해주세요.";
                Debug.LogWarning("Profile save failed: " + error.Message);
                return false;
            }
        }
    }
}
