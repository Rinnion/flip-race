using System;
using System.Globalization;
using UnityEngine;
using FlipRace.Core;

namespace FlipRace.Presentation
{
    /// <summary>Native C# presentation of the original split-screen Canvas game.</summary>
    public sealed partial class FlipRaceGame : MonoBehaviour
    {
        public GameState Game { get; private set; }
        public bool IsPaused { get { return paused; } }
        public Rect FullscreenRect { get { return fullscreenRect; } }
        public Rect TrackMenuButtonRect { get { return trackButton; } }
        public bool CharacterSelectionVisible
        { get { return Game != null && Game.Phase == "ready" && !paused && !showRules && !showTracks && !Portrait; } }
        // CSS pixels from the canvas top-left, matching the rendered 180-degree upper lane.
        public Rect CharacterScreenRect(int playerIndex, int heroIndex)
        {
            if (playerIndex < 0 || playerIndex >= Game.Players.Length || heroIndex < 0 || heroIndex >= CharacterRoster.Count) throw new ArgumentOutOfRangeException();
            if (BotEnabled) return playerIndex==0?BotCharacterRect(heroIndex):default(Rect);
            if (SoloEnabled) return SoloCharacterRect(heroIndex);
            if (PartyEnabled) return PartyScreenRect(playerIndex, PartyCharacterRect(playerIndex, heroIndex));
            if (SharedEnabled) return SharedScreenRect(playerIndex, SharedCharacterRect(playerIndex, heroIndex));
            Rect lane = lanes[playerIndex];
            return LaneScreenRect(playerIndex, CharacterRect(heroIndex, lane.width, lane.height));
        }
        public Rect ReadyScreenRect(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= Game.Players.Length) throw new ArgumentOutOfRangeException();
            if (BotEnabled) return playerIndex==0?(Game.Phase=="finished"?BotRetryRect:BotStartRect):default(Rect);
            if (SoloEnabled) return Game.Phase=="finished" ? SoloRetryRect : SoloStartRect;
            if (PartyEnabled) return PartyScreenRect(playerIndex, PartyReadyRect(playerIndex));
            if (SharedEnabled) return SharedScreenRect(playerIndex, SharedReadyRect(playerIndex));
            Rect lane = lanes[playerIndex];
            return LaneScreenRect(playerIndex, ReadyButtonRect(lane.width, lane.height));
        }
        Rect LaneScreenRect(int playerIndex, Rect local)
        {
            Rect lane = lanes[playerIndex];
            return playerIndex == 0
                ? new Rect(lane.xMax - local.xMax, lane.yMax - local.yMax, local.width, local.height)
                : new Rect(lane.x + local.x, lane.y + local.y, local.width, local.height);
        }
        readonly Color[] accents = { Hex(0x7e6baf), Hex(0xb56839), Hex(0x428778), Hex(0xa45d83) };
        readonly Color[] pales = { Hex(0xd9d2f2), Hex(0xffc397), Hex(0xc8e6d9), Hex(0xedd0e1) };
        readonly Color ink = Hex(0x353b31), paper = Hex(0xf5f3ea), muted = Hex(0x79806d);
        Font font, semibold;
        GUIStyle label;
        Texture2D circle, veil;
        CorridorArtwork artwork;
        AudioSource audioSource;
        AudioClip tapClip, hitClip, startClip, finishClip;
        bool paused, showRules, showTracks, soundEnabled, wasPausedBeforeRules, hadPortrait;
        bool selectedNegative, trackNegativeChoice, legacyControls;
        bool suppressFrame, gameplayInputBlocked, compact;
        int inputBlockFrame, round = 1;
        string selectedTrackId;
        string buildVersion;
        bool Trial { get { return Game.Mode == "trial" || PartyEnabled || SoloEnabled || BotEnabled; } }
        bool Controls { get { return Trial && !legacyControls; } }
        string pauseReason = "Катикаавры ждут на своих местах.";
        double lastFrame;
        float uiScale, width, height, animationTime, winnerReactionAt = -10;
        Rect shell, session, arena, divider, footer, helpRect, headerHelp, pauseRect, soundRect,
            fullscreenRect, rulesPanel, rulesClose, rulesDone, resumeRect, trackButton, trackPanel, trackClose, negativeChoiceRect,
            racePracticeChoiceRect;
        readonly Rect[] lanes = new Rect[2];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<FlipRaceGame>() == null)
                new GameObject("FlipRace").AddComponent<FlipRaceGame>();
        }

        void Awake()
        {
            gameObject.name = "FlipRace";
            buildVersion = "v" + Application.version;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Input.multiTouchEnabled = true;
            Input.simulateMouseWithTouches = false;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            InitializeFlightMenu();
#if FLIP_V4_TRIAL
            Game = RaceSimulation.CreateTrial(InitialFlightTrack(), flightSelection.Settings);
            legacyControls = !activeCustomFlight && activeFlightPreset >= 4 && activeFlightPreset <= 9;
#else
            Game = RaceSimulation.CreateGame();
#endif
            selectedTrackId = Game.Track.Id;
            Game.Players[0].CharacterIndex = 0;
            Game.Players[1].CharacterIndex = 1;
#if FLIP_SCREEN_STAGE
            Game = RaceSimulation.CreateTrial(TracksMixed.Get(), flightSelection.Settings);
            selectedTrackId = Game.Track.Id;
            Game.Players[0].CharacterIndex = Game.Players[1].CharacterIndex = 0;
            sharedField = true;
#endif
            font = Resources.Load<Font>("Inter-Regular");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            semibold = Resources.Load<Font>("Inter-SemiBold");
            if (semibold == null) semibold = font;
            circle = MakeCircle();
            veil = MakeVeil();
            artwork = new CorridorArtwork();
            MascotArtwork.GetFrame(0, 0, 0, false, false, false);
            MascotArtwork.GetFrame(1, 1, 0, false, false, false);
            for (int slot = 0; slot < 2; slot++) for (int hero = 0; hero < CharacterRoster.Count; hero++) MascotArtwork.Create(hero, slot);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            soundEnabled = PlayerPrefs.GetInt("flip-race-sound", 0) != 0;
            tapClip = Tone(680, .085f); hitClip = Tone(150, .18f);
            startClip = Tone(880, .18f); finishClip = Tone(1040, .3f);
            lastFrame = Time.realtimeSinceStartupAsDouble;
            Measure();
        }

        void Measure()
        {
            MigrateRetiredSharedHeight();
            uiScale = Mathf.Max(1, FlipRace.Web.FlipDiagnostics.FlipPixelRatio());
            width = Screen.width / uiScale; height = Screen.height / uiScale;
            compact = width > height && height <= 600;
            float margin = compact ? 8 : width <= 950 ? 24 : 44;
            float appWidth = compact ? width : Mathf.Min(width, 1320), left = (width - appWidth) / 2 + margin;
            float sw = appWidth - margin * 2;
            float sy = compact ? 0 : width >= 1450 ? 163 : (width <= 950 ? 15 : 25) + 105;
            float sh = compact ? 27 : 42, dh = compact ? 24 : 32;
            float lh = compact ? (height - 2 - 2 - sh - dh) / 2
                : width >= 1450 ? Mathf.Clamp(height * .29f, 220, 320)
                : width <= 950 ? Mathf.Clamp(height * .29f, 180, 260) : Mathf.Clamp(height * .28f, 190, 286);
            if (!compact) lh = Mathf.Min(lh, Mathf.Max(92, (height - sy - sh - dh - 90) / 2));
            shell = new Rect(left, sy, sw, 2 + sh + lh * 2 + dh);
            session = new Rect(left + 1, sy + 1, sw - 2, sh);
            lanes[0] = new Rect(left + 1, session.yMax, sw - 2, lh);
            divider = new Rect(left + 1, lanes[0].yMax, sw - 2, dh);
            lanes[1] = new Rect(left + 1, divider.yMax, sw - 2, lh);
            arena = new Rect(left + 1, lanes[0].y, sw - 2, lh * 2 + dh);
            footer = new Rect(left, shell.yMax, sw, compact ? 2 : 80);
            PlaceSession(sh, dh, lh);
            float tool = compact ? 31 : 34, ty = session.y + (sh - 25) / 2;
            fullscreenRect = new Rect(session.xMax - tool - 12, ty, tool, 25);
            pauseRect = new Rect(fullscreenRect.x - tool, ty, tool, 25);
            soundRect = new Rect(pauseRect.x - (compact ? 31 : 91), ty, compact ? 31 : 91, 25);
            helpRect = new Rect(soundRect.x - 31, ty, 31, 25);
            float trackLeft = session.x + 12 + (compact ? 120 : 0);
            trackButton = new Rect(trackLeft, session.y, helpRect.x - trackLeft - 8, sh);
            headerHelp = new Rect(DesktopBrandRight - 206, sy - 78, 100, 32);
            float pw = Mathf.Min(width - 32, 410), ph = Mathf.Min(height - 24, 282);
            rulesPanel = new Rect((width - pw) / 2, (height - ph) / 2, pw, ph);
            rulesClose = new Rect(rulesPanel.xMax - 42, rulesPanel.y + 10, 32, 32);
            rulesDone = new Rect(rulesPanel.x + 24, rulesPanel.yMax - 49, 142, 31);
            resumeRect = new Rect(arena.center.x - 92, SessionCentered && !SharedEnabled ? divider.yMax + 42 : arena.center.y + 39, 184, 33);
            float th = Mathf.Min(height - 24, Trial ? 232 : 264);
            trackPanel = new Rect((width - Mathf.Min(width - 32, 350)) / 2, (height - th) / 2, Mathf.Min(width - 32, 350), th);
            trackClose = new Rect(trackPanel.xMax - 42, trackPanel.y + 10, 32, 32);
            negativeChoiceRect = new Rect(trackPanel.x + 22, trackPanel.y + (Trial ? 175 : 167), trackPanel.width - 44, 30);
            racePracticeChoiceRect = new Rect(trackPanel.x + 22, trackPanel.y + 205, trackPanel.width - 44, 30);
        }

        Rect SafeUiArea()
        {
            Rect safe=Screen.safeArea;
            if(safe.width<=0||safe.height<=0)return new Rect(0,0,width,height);
            return new Rect(safe.x/uiScale,(Screen.height-safe.yMax)/uiScale,safe.width/uiScale,safe.height/uiScale);
        }
        bool Active { get { return Game.Phase == "racing" || Game.Phase == "countdown"; } }
        bool Portrait { get { return height > width && width <= 700; } }

        void Update()
        {
            Measure();
            double now = Time.realtimeSinceStartupAsDouble, delta = Math.Max(0, now - lastFrame);
            lastFrame = now;
            if (gameplayInputBlocked && Time.frameCount > inputBlockFrame
                && Input.touchCount == 0 && !Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0))
                gameplayInputBlocked = false;
            if (Portrait && !hadPortrait && Active) Pause("Телефон повернулся. Верни его горизонтально и продолжи.");
            hadPortrait = Portrait;
            if (delta > .3 && Active) Pause("Игра остановилась на мгновение. Продолжим вместе!");
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (showRules || showTracks) CloseRules();
                else if (paused && !Portrait) Resume();
                else Pause(SoloEnabled ? "Гонка приостановлена." : "Катикаавры ждут на своих местах.");
            }
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                    Pointer(new Vector2(touch.position.x / uiScale, (Screen.height - touch.position.y) / uiScale));
            }
            if (Input.touchCount == 0 && Input.GetMouseButtonDown(0))
                Pointer(new Vector2(Input.mousePosition.x / uiScale, (Screen.height - Input.mousePosition.y) / uiScale));
            BeginBotPerformanceFrame(delta);
            bool botPerformanceFrameCompleted=false;
            try
            {
            if (!paused && !showRules && !showTracks && !Portrait)
            {
                if (Input.GetKeyDown(KeyCode.A)) Act(0);
                if (!BotEnabled && Game.Players.Length > 1 && Input.GetKeyDown(KeyCode.L)) Act(1);
                if (PartyEnabled && Game.Players.Length > 2 && Input.GetKeyDown(KeyCode.S)) Act(2);
                if (PartyEnabled && Game.Players.Length > 3 && Input.GetKeyDown(KeyCode.K)) Act(3);
                animationTime += Mathf.Min((float)delta, .1f);
                if (!suppressFrame)
                {
                    string oldPhase = Game.Phase;
                    object winner = Game.Winner;
                    int countdown = (int)Math.Ceiling(Game.CountdownRemaining);
                    int hits = TotalHits();
                    int boosts = TotalRewards();
                    int slows = TotalSlows();
                    if(BotEnabled) AdvanceMeasuredBot(delta); else RaceSimulation.Advance(Game, delta);
                    if (oldPhase == "countdown" && Game.Phase == "racing") Beep(startClip);
                    else if (Game.Phase == "countdown" && countdown != (int)Math.Ceiling(Game.CountdownRemaining)) Beep(tapClip);
                    if (hits != TotalHits()) Beep(hitClip);
                    if (boosts != TotalRewards()) Beep(startClip);
                    if (slows != TotalSlows()) Beep(hitClip);
                    if (winner == null && Game.Winner != null) { Beep(finishClip); winnerReactionAt = animationTime; }
                }
            }
            botPerformanceFrameCompleted=true;
            }
            finally { EndBotPerformanceFrame(!botPerformanceFrameCompleted); }
            suppressFrame = false;
        }

        // Drawing and hit-testing share CSS-pixel rectangles, including all touch controls.
        void Pointer(Vector2 p)
        {
            if (gameplayInputBlocked || Portrait) return;
            if (showTracks)
            {
                if (Trial) { FlightPointer(p); return; }
                if (trackClose.Contains(p)) { CloseRules(); return; }
                if (racePracticeChoiceRect.Contains(p))
                {
                    selectedNegative = trackNegativeChoice;
                    NewGame(Game.Mode == "practice" ? "race" : "practice"); return;
                }
                if (negativeChoiceRect.Contains(p)) { trackNegativeChoice = !trackNegativeChoice; Beep(tapClip); return; }
                for (int i = 0; i < TracksV3.All.Length; i++) if (TrackChoiceRect(i).Contains(p))
                { selectedTrackId = TracksV3.All[i].Id; selectedNegative = trackNegativeChoice; NewGame("race"); return; }
                return;
            }
            if (showRules) { if (rulesClose.Contains(p) || rulesDone.Contains(p)) CloseRules(); return; }
            if (paused) { if (resumeRect.Contains(p) || pauseRect.Contains(p)) Resume(); return; }
            if (pauseRect.Contains(p)) { Pause(SoloEnabled ? "Гонка приостановлена." : "Катикаавры ждут на своих местах."); return; }
            if ((compact ? helpRect : headerHelp).Contains(p)) { OpenRules(); return; }
            if (soundRect.Contains(p)) { ToggleSound(); return; }
            if (trackButton.Contains(p) && !Active) { OpenTracks(); return; }
            if (session.Contains(p)) return;
            if (PointerPaw(p)) return;
            if (BotEnabled) { BotPointer(p); return; }
            if (SoloEnabled) { SoloPointer(p); return; }
            if (PartyEnabled) { PartyPointer(p); return; }
            if (SharedEnabled) { SharedPointer(p); return; }
            for (int slot = 0; slot < 2; slot++) if (lanes[slot].Contains(p))
            {
                Vector2 local = new Vector2(p.x - lanes[slot].x, p.y - lanes[slot].y);
                if (slot == 0) local = new Vector2(lanes[slot].width - local.x, lanes[slot].height - local.y);
                if (Game.Phase == "ready")
                {
                    for (int hero = 0; hero < CharacterRoster.Count; hero++)
                        if (CharacterRect(hero, lanes[slot].width, lanes[slot].height).Contains(local))
                        { SelectCharacter(slot, hero); return; }
                    if (ReadyButtonRect(lanes[slot].width, lanes[slot].height).Contains(local)) Act(slot);
                    return; // A gap or margin is not a readiness action.
                }
                LocalRacePress(slot, p); return;
            }
        }
        void ToggleSound()
        {
            soundEnabled = !soundEnabled;
            PlayerPrefs.SetInt("flip-race-sound", soundEnabled ? 1 : 0);
            Beep(tapClip);
        }
        void NewGame(string mode)
        {
            int first = Game.Players[0].CharacterIndex, second = Game.Players.Length > 1 ? Game.Players[1].CharacterIndex : 1;
            Game = RaceSimulation.CreateGame(mode, selectedTrackId, selectedNegative);
            Game.Players[0].CharacterIndex = first; Game.Players[1].CharacterIndex = second;
            round = 1; paused = false; showRules = false; showTracks = false; suppressFrame = true;
            lastFrame = Time.realtimeSinceStartupAsDouble;
            BlockGameplayInput();
        }
        void SelectCharacter(int slot, int hero)
        {
            RaceSimulation.SetCharacter(Game, slot, hero);
            MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false);
            lastFrame = Time.realtimeSinceStartupAsDouble;
            // The selection tap was consumed above; leave the other player's
            // simultaneous touch available in this same input frame.
            Beep(tapClip);
        }
        string PlayerName(int slot) { return CharacterRoster.Name(Game.Players[slot].CharacterIndex); }
        string TrialMotionName() { return activeCustomFlight ? "Свои настройки" : FlightSettingsStore.PresetName(activeFlightPreset); }
        string TrialMotionHint() { return FlightActionHint(); }
        void OpenTracks()
        {
            if (Active || paused) return;
            if (Trial) BeginFlightMenu();
            wasPausedBeforeRules = paused; trackNegativeChoice = selectedNegative;
            showTracks = true; paused = true;
        }
        Rect TrackChoiceRect(int index) { return new Rect(trackPanel.x + 22, trackPanel.y + 49 + index * 37, trackPanel.width - 44, 34); }
        Rect CharacterRect(int hero, float w, float h)
        {
            // Six fast targets fit the132px lane at568x320. Readiness owns its
            // fixed bottom strip; names and previews are painted inside cards.
            float gap = 4, ch = compact ? 44 : 56;
            float cw = Mathf.Min((w - 16 - gap * 2) / 3, 176);
            float listBottom = ReadyButtonRect(w, h).y - 4;
            float y = Mathf.Max(4, (listBottom - (ch * 2 + gap)) / 2);
            float total = cw * 3 + gap * 2;
            return new Rect((w - total) / 2 + hero % 3 * (cw + gap), y + hero / 3 * (ch + gap), cw, ch);
        }
        Rect ReadyButtonRect(float w, float h)
        {
            float rw = Mathf.Min(w - 16, compact ? 190 : 220), rh = compact ? 32 : 38;
            return new Rect((w - rw) / 2, h - rh, rw, rh);
        }
        void Act(int index)
        {
            if (paused || showRules || showTracks || Portrait || gameplayInputBlocked || (BotEnabled && index!=0)) return;
            if (BotEnabled && Game.Phase=="finished") { ResetBotRace(); round++; }
            string phase = Game.Phase;
            if (Trial && (phase == "ready" || phase == "finished") && RaceTracks.NormalizeId(Game.Track.Id) != Game.Track.Id)
            {
                Game.Track = RaceTracks.Get(Game.Track.Id);
                selectedTrackId = Game.Track.Id;
            }
            if (RaceSimulation.Press(Game, index)) Beep(tapClip);
            if (phase == "finished" && Game.Phase == "countdown") round++;
            if ((phase == "ready" || phase == "finished") && Game.Phase == "countdown") suppressFrame = true;
        }
        public void BrowserPause(string reason)
        {
            if (Active) Pause(reason == "portrait" ? "Телефон повернулся. Верни его горизонтально и продолжи."
                : string.IsNullOrEmpty(reason) ? "Вкладка была скрыта. Продолжим вместе!" : reason);
            lastFrame = Time.realtimeSinceStartupAsDouble;
        }
        void OnApplicationFocus(bool focus) { if (!focus && Active) Pause("Окно потеряло фокус. Продолжим вместе!"); }
        void OnApplicationPause(bool value) { if (value && Active) Pause("Игра была свёрнута. Продолжим вместе!"); }
        void Pause(string reason) { paused = true; pauseReason = reason; suppressFrame = true; }
        void Resume() { if (Portrait) return; paused = false; lastFrame = Time.realtimeSinceStartupAsDouble; suppressFrame = true; BlockGameplayInput(); }
        void OpenRules() { wasPausedBeforeRules = paused; showRules = true; paused = true; }
        void CloseRules() { showRules = false; showTracks = false; paused = wasPausedBeforeRules; lastFrame = Time.realtimeSinceStartupAsDouble; suppressFrame = true; BlockGameplayInput(); }
        void BlockGameplayInput() { gameplayInputBlocked = true; inputBlockFrame = Time.frameCount; }

        void OnGUI()
        {
            if (Game == null) return;
            Measure();
            if (label == null) label = new GUIStyle(GUI.skin.label) { font = font, richText = false,
                wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0) };
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1));
            Fill(new Rect(0, 0, width, height), paper);
            if (!compact) DrawHeader();
            if (!compact) Round(new Rect(shell.x, shell.y + 5, shell.width, shell.height), 18, new Color(.36f, .4f, .29f, .07f));
            Round(shell, compact ? 11 : 18, Hex(0xd8ddcb));
            Round(new Rect(shell.x + 1, shell.y + 1, shell.width - 2, shell.height - 2), compact ? 10 : 17, Hex(0xfbfbf3));
            if (BotEnabled) DrawBotField();
            else if (SoloEnabled) DrawSoloField();
            else if (PartyEnabled) DrawPartyField();
            else if (SharedEnabled) DrawSharedField();
            else { DrawLane(0); DrawLane(1); DrawDivider(); }
            DrawPaws();
            DrawFooter();
            DrawSession();
            if (Portrait) DrawPause();
            else if (showTracks) DrawTracks();
            else if (showRules) DrawRules();
            else if (paused || Portrait) DrawPause();
            if (paused && !showTracks && !showRules && !Portrait) DrawSession();
            DrawBuildVersion();
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawBuildVersion()
        {
            if (compact || Portrait)
            {
                bool overlay = showTracks || showRules;
                float x = Portrait || overlay ? 24 : session.x + 12, y = Portrait ? 18 : overlay ? 3 : session.y + 3;
                Round(new Rect(x - 4, y - 1, 120, 18), 4, paper);
                Text(new Rect(x, y, 35, 16), "флип!", 11, ink, true);
                Text(new Rect(x + 40, y, 76, 16), buildVersion, 10, muted, false, TextAnchor.MiddleLeft);
            }
            else
            {
                Round(new Rect(DesktopBrandLeft + 184, shell.y - 54, 82, 18), 4, paper);
                Text(new Rect(DesktopBrandLeft + 188, shell.y - 53, 74, 16), buildVersion, 10, muted, false, TextAnchor.MiddleLeft);
            }
        }

        void DrawHeader()
        {
            float y = shell.y - 105;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = saved * Matrix4x4.TRS(new Vector3(DesktopBrandLeft + 27, y + 39, 0), Quaternion.Euler(0, 0, -8), Vector3.one);
            Round(new Rect(-27, -29, 54, 58), 14, pales[0]);
            Text(new Rect(-25, -29, 50, 58), "↕", 36, accents[0], true, TextAnchor.MiddleCenter);
            GUI.matrix = saved;
            Text(new Rect(DesktopBrandLeft + 69, y + 4, 200, 48), "флип", 39, ink, true);
            Text(new Rect(DesktopBrandLeft + 169, y + 4, 35, 48), "!", 39, accents[0], true);
            Text(new Rect(DesktopBrandLeft + 72, y + 53, 240, 15), "ГОНКА КАТИКААВРОВ", 8, muted);
            if (shell.width > 900 && !PartyEnabled && !SoloEnabled && !BotEnabled)
            {
                Text(new Rect(DesktopBrandLeft + 265, y + 18, 380, 25), "Два катикаавра. Один экран.", 12, muted);
                Text(new Rect(DesktopBrandLeft + 265, y + 43, 400, 22), "Кто перевернёт гонку?", 12, ink);
            }
            Text(headerHelp, "?   Как играть", 11, ink, false, TextAnchor.MiddleCenter);
            StrokeRound(new Rect(DesktopBrandRight - 84, headerHelp.y + 2, 84, 28), 5, Hex(0xd8ddcb), paper);
            Text(new Rect(DesktopBrandRight - 84, headerHelp.y + 2, 84, 28), (BotEnabled ? "С БОТОМ" : SoloEnabled ? "ОДИН" : PartyEnabled ? "КОМПАНИЯ" : "ДЛЯ ДВОИХ"), 8, muted, false, TextAnchor.MiddleCenter);
        }
        void DrawSession()
        {
            Fill(session, Hex(0xfbfbf3));
            float pad = compact ? 132 : 20;
            Ellipse(new Rect(session.x + pad, session.center.y - 2.5f, 5, 5), Hex(0x9ca986));
            string title = (BotEnabled ? "С ботом · "+BotLevelNames[(int)Game.BotDifficulty]+" · "+(Game.PartyCamera==PartyCamera.Constant?"Скролл":"За последним")+" · "+Game.Track.Name : SoloEnabled ? "Один · " + (Game.PartyCamera == PartyCamera.Constant ? "Скролл" : "За героем") + " · " + Game.Track.Name : PartyEnabled ? "Компания " + Game.Players.Length + " · " + (Game.PartyCamera == PartyCamera.Constant ? "Скролл" : "Камера последнего") + " · " + Game.Track.Name : Trial ? TrialMotionName() + " · " + Game.Track.Name : Game.Mode == "practice" ? "Проба серии" : Game.Track.Name)
                + (Game.Track.NegativeEnabled ? "  ·  ◇−" : "")
                + (Active ? "   /   " + round.ToString("00") : "   ↗");
            Text(new Rect(session.x + pad + 14, session.y, helpRect.x - session.x - pad - 22, session.height), title,
                compact ? 8 : 10, muted, false, TextAnchor.MiddleLeft);
            if (compact) Text(helpRect, "?", 17, muted, false, TextAnchor.MiddleCenter);
            DrawSoundIcon(soundRect.x + (compact ? 9 : 3), soundRect.center.y - 7);
            if (!compact) Text(new Rect(soundRect.x + 20, soundRect.y, soundRect.width - 20, soundRect.height),
                soundEnabled ? "Звук вкл." : "Звук выкл.", 10, muted, false, TextAnchor.MiddleLeft);
            Text(pauseRect, "Ⅱ", compact ? 17 : 19, muted, false, TextAnchor.MiddleCenter);
#if !UNITY_WEBGL || UNITY_EDITOR
            Text(fullscreenRect, "⛶", 18, muted, false, TextAnchor.MiddleCenter);
#endif
        }

        void DrawSoundIcon(float x, float y)
        {
            Color c = soundEnabled ? Hex(0x758465) : Hex(0x9a9e8b);
            Fill(new Rect(x + 4, y + 2, 2, 11), c);
            Fill(new Rect(x + 11, y, 2, 11), c);
            Ellipse(new Rect(x, y + 10, 6, 4), c);
            Ellipse(new Rect(x + 7, y + 8, 6, 4), c);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.TRS(new Vector3(x + 4, y + 2, 0), Quaternion.Euler(0, 0, -15), Vector3.one);
            Fill(new Rect(0, 0, 9, 2), c);
            GUI.matrix = previous;
        }

        void DrawLane(int index)
        {
            Rect r = lanes[index]; float w = r.width, h = r.height;
            Matrix4x4 original = GUI.matrix;
            // Keep transforms in canvas coordinates. IMGUI clip offsets are not
            // local matrix origins and would rotate the upper lane off screen.
            GUI.matrix = original * (index == 0
                ? Matrix4x4.TRS(new Vector3(r.xMax, r.yMax, 0), Quaternion.Euler(0, 0, 180), Vector3.one)
                : Matrix4x4.Translate(new Vector3(r.x, r.y, 0)));
            PlayerState p = Game.Players[index];
            CorridorArtwork.Geometry g = artwork.Draw(w, h, Game, index, animationTime);
            if (SplitGhostEnabled && Game.Phase == "racing") DrawOpponentGhost(index, w, h, g);
            Matrix4x4 lane = GUI.matrix;
            GUI.matrix = lane * Matrix4x4.TRS(new Vector3(g.X, g.Y, 0), Quaternion.Euler(0, 0, g.Rotation),
                new Vector3(g.MirrorX ? -1 : 1, 1, 1));
            Rect b = MascotArtwork.UnitBounds;
            GUI.color = Color.white;
            MascotReaction reactionPose = MascotReaction.None;
            float reactionAge = 0;
            if (p.Recovering > 0) reactionPose = MascotReaction.Hit;
            else if (p.FinishTime.HasValue && Game.Winner is int && (int)Game.Winner == index && animationTime - winnerReactionAt < 1.2f)
            { reactionPose = MascotReaction.Win; reactionAge = animationTime - winnerReactionAt; }
            else if (p.LastFinishBoost != null && Game.Elapsed - p.LastFinishBoost.At < .65)
            { reactionPose = MascotReaction.Success; reactionAge = (float)(Game.Elapsed - p.LastFinishBoost.At); }
            else if (p.LastSuccess != null && Game.Elapsed - p.LastSuccess.At < .65)
            { reactionPose = MascotReaction.Success; reactionAge = (float)(Game.Elapsed - p.LastSuccess.At); }
            Texture2D frame = MascotArtwork.GetMotionFrame(p.CharacterIndex, index, animationTime, g.MotionTime, g.FlipProgress,
                g.Running, p.Flipping, g.Recovering, reactionPose, reactionAge);
            GUI.DrawTexture(new Rect(b.x * g.Radius, b.y * g.Radius, b.width * g.Radius, b.height * g.Radius), frame);
            GUI.matrix = lane;
            artwork.DrawForeground(w, h, Game, index, animationTime, g);
            if (Game.Phase == "racing" && !p.FinishTime.HasValue)
            {
                bool recentSlow = p.LastSlow != null && Game.Elapsed - p.LastSlow.At < 1.1
                    && (p.LastSuccess == null || p.LastSlow.At >= p.LastSuccess.At)
                    && (p.LastInstant == null || p.LastSlow.At > p.LastInstant.At);
                bool recentInstant = p.LastInstant != null && Game.Elapsed - p.LastInstant.At < 1.1
                    && (p.LastSlow == null || p.LastInstant.At >= p.LastSlow.At)
                    && (p.LastSuccess == null || p.LastInstant.At >= p.LastSuccess.At);
                bool landed = false;
                if (recentInstant) foreach (var zone in Game.Track.RiskZones)
                    if (zone.Id == p.LastInstant.Id && zone.IsLanding) { landed = true; break; }
                bool recentFinish = p.LastFinishBoost != null && Game.Elapsed - p.LastFinishBoost.At < 1.1;
                bool recentCoin = p.LastCoin != null && Game.Elapsed - p.LastCoin.At < .8;
                string reaction = recentCoin ? "Монетка +1" : recentFinish ? "Рывок ×2"
                    : recentInstant ? (landed ? "Точная посадка! ×1,5" : "Молния! Скорость ×1,5") : recentSlow ? CharacterRoster.SlowText(p.CharacterIndex) + "  +0,3 с"
                    : p.LastSuccess != null && Game.Elapsed - p.LastSuccess.At < 1.15
                    ? CharacterRoster.SuccessText(p.CharacterIndex) + "  −0,3 с"
                    : p.LastHit != null && Game.Elapsed - p.LastHit.At < .8 ? CharacterRoster.HitText(p.CharacterIndex) : "";
                if (reaction.Length > 0)
                {
                    float rw = Mathf.Min(145, TextWidth(reaction, 9, true) + 14);
                    Rect rr = new Rect(Mathf.Clamp(g.X + (index == 0 ? -rw - 19 : 19), 5, w - rw - 5),
                        Mathf.Clamp(g.Y - 34, 31, h - 48), rw, 21);
                    Round(rr, 7, recentInstant || recentFinish ? Hex(0xdaf0e7) : recentSlow ? Hex(0xf3e0ed) : Hex(0xfff5d7));
                    Text(rr, reaction, 9, recentInstant || recentFinish ? Hex(0x397b70) : recentSlow ? Hex(0x925478) : Hex(0x947139), true, TextAnchor.MiddleCenter);
                }
            }
            if (Game.Phase != "racing" || p.FinishTime.HasValue) DrawOverlay(index, w, h);
            if (Game.Phase != "ready") DrawLaneHeading(index, w, h);
            string hint = Game.Phase == "ready" ? "" : Hint(p);
            if (hint.Length > 0)
            {
                int size = compact ? 9 : 10;
                float tw = Mathf.Min(w * .8f, TextWidth(hint, size) + 18), hh = compact ? 19 : 22;
                Rect hr = new Rect((w - tw) / 2, h - hh - (compact ? 5 : 10), tw, hh);
                Round(hr, 8, new Color(.984f, .988f, .945f, .91f));
                Text(hr, hint, size, Hex(0x657153), false, TextAnchor.MiddleCenter);
            }
            if (g.ShowPracticeMarker && Game.Phase == "racing")
            {
                string tip = g.AtPracticeMarker ? "СЕЙЧАС!" : Game.Track.RiskZones.Length > 0 ? "Кольцо: жми здесь ↕" : "Нажми у метки ↕";
                Rect tag = new Rect(Mathf.Clamp(g.PracticeMarkerX - 55, 6, w - 116), h * .45f, 110, 22);
                Round(tag, 11, g.AtPracticeMarker ? pales[index] : Hex(0xf7f7e9));
                Text(tag, tip, 9, accents[index], true, TextAnchor.MiddleCenter);
            }
            GUI.matrix = original;
        }

        void DrawLaneHeading(int index, float w, float h)
        {
            PlayerState p = Game.Players[index];
            float x = compact ? 12 : 18, y = compact ? 7 : 13, ph = compact ? 20 : 26;
            int size = compact ? 9 : 11;
            float pw = TextWidth(PlayerName(index), size, true) + (compact ? 29 : 62);
            StrokeRound(new Rect(x, y, pw, ph), ph / 2, Hex(0xe0e2d4), new Color(1, .992f, .945f, .91f));
            Ellipse(new Rect(x + 8, y + ph / 2 - 3, 6, 6), accents[index]);
            Text(new Rect(x + 20, y, pw - 20, ph), PlayerName(index), size, ink, true, TextAnchor.MiddleLeft);
            if (!compact)
            {
                Round(new Rect(x + pw - 27, y + 5, 18, 16), 4, Hex(0xeceee4));
                Text(new Rect(x + pw - 27, y + 5, 18, 16), index == 0 ? "A" : "L", 9, muted, false, TextAnchor.MiddleCenter);
            }
            string status = p.FinishTime.HasValue ? "На финише ✓" : Game.Phase != "racing" ? "Твоя половина экрана" : p.Recovering > 0 ? "Кувырок · +0,5 с"
                : p.FinishBoost ? "К ленте · ×2"
                : p.SlowRemaining > 0 ? "Замедление · +0,3 с"
                : p.BoostRemaining > 0 ? "Рывок · −0,3 сек"
                : p.Flipping ? "В полёте" : p.Support == SupportKind.Stone ? "На камне" : "Готов к перевороту";
            if (p.PendingEffects.Count > 0 && p.Recovering <= 0) status += " · +" + p.PendingEffects.Count;
            if (Trial && Game.Phase == "racing" && !p.FinishTime.HasValue)
            {
                string gravity = Game.Flight.Law == MotionLaw.Linear ? (p.VerticalSpeed < 0 ? "↑ полёт" : p.VerticalSpeed > 0 ? "↓ полёт" : "На опоре")
                    : p.Gravity < 0 ? "↑ тяга" : "↓ тяга";
                string charge = Game.Flight.AirRepeats == AirRepeatLimit.Unlimited ? " · повтор ∞"
                    : Game.Flight.AirRepeats == AirRepeatLimit.One ? p.AirFlipAvailable ? " · повтор 1" : " · повтор 0" : " · повтор 0";
                status = gravity + charge + (p.Recovering > 0 ? " · удар" : p.FinishBoost ? " · ×2" : p.SlowRemaining > 0 ? " · ×0,5"
                    : p.BoostRemaining > 0 ? " · ×1,5" : "");
                if (p.Support == SupportKind.Stone) status += " · камень";
            }
            int ss = compact ? 8 : 10;
            float stw = TextWidth(status, ss) + (compact ? 12 : 16);
            Rect sr = new Rect(w - x - stw, y, stw, ph);
            Round(sr, 5, p.Recovering > 0 ? Hex(0xfff0db) : new Color(.98f, .984f, .945f, .79f));
            Text(sr, status, ss, p.Recovering > 0 ? Hex(0xa64d2d) : Hex(0x6a755a), false, TextAnchor.MiddleCenter);
            if ((Game.Phase == "racing" || Game.Phase == "finished") && Game.Track.RiskZones.Length > 0)
                Text(new Rect(w / 2 - 65, y, 130, ph), "+ " + Rewards(p) + " / " + Game.Track.PositiveRingCount
                    + (Game.Track.NegativeEnabled ? "    ◇− " + p.SlowsEarned : "") + CoinScore(p),
                    compact ? 7 : 8, Hex(0x8c7b4e), true, TextAnchor.MiddleCenter);
        }
        void DrawOverlay(int index, float w, float h)
        {
            GUI.color = Color.white; GUI.DrawTexture(new Rect(0, 0, w, h), veil);
            PlayerState p = Game.Players[index];
            float center = h / 2 + (compact ? 8 : 6);
            if (Game.Phase == "countdown" && SharedEnabled && SessionCentered) center = SharedCountdownCenter(index);
            if (Game.Phase == "ready") { DrawCharacterSelection(index, w, h); return; }
            if (Game.Phase == "countdown")
            {
                bool narrowBand = SharedEnabled && SessionCentered && SharedCountdownBandHeight(index) < 80;
                float numberHeight = narrowBand ? 60 : compact ? 80 : 110;
                Text(new Rect(0, center - numberHeight / 2, w, numberHeight),
                    Math.Ceiling(Game.CountdownRemaining).ToString(), narrowBand ? 48 : compact ? 58 : 80, accents[index], true, TextAnchor.MiddleCenter);
                if (!narrowBand) Text(new Rect(0, center + (compact ? 34 : 48), w, 14), "ПРИГОТОВЬСЯ К ПЕРЕВОРОТУ", compact ? 7 : 8, muted, false, TextAnchor.MiddleCenter);
                return;
            }
            bool results = Game.Phase == "finished", crossedFinish = p.FinishTime.HasValue;
            bool tie = Game.Winner is string, won = Game.Winner is int && (int)Game.Winner == index;
            string eyebrow = results ? (Game.Mode == "practice" ? "ТРЕНИРОВКА ЗАВЕРШЕНА" : tie ? "ФИНИШ ВМЕСТЕ"
                : "ИГРОК 0" + ((int)Game.Winner + 1) + " — " + PlayerName((int)Game.Winner).ToUpperInvariant()) : crossedFinish ? "ПЕРВЫЙ НА ФИНИШЕ"
                : Game.Mode == "practice" ? "ПРОБУЕМ БЕЗ СПЕШКИ" : "ИГРОК 0" + (index + 1);
            string title = results ? (Game.Mode == "practice" ? "Отличная разминка!" : tie ? "Вот это ничья!"
                : won ? CharacterRoster.WinText(p.CharacterIndex) : "Реванш всё решит.") : crossedFinish ? "Победа!"
                : p.Ready ? "Всё, я готов!" : Game.Mode == "practice" ? "Поймай момент." : "Переверни гонку.";
            string finishTime = crossedFinish ? Number(p.FinishTime.Value, 2) + " сек" : "Не успел к финишу";
            string subtitle = results ? finishTime + " · +" + Rewards(p) + "/" + Game.Track.PositiveRingCount
                + (Game.Track.NegativeEnabled ? " · −" + p.SlowsEarned : "") + " · ошибок: " + p.Collisions : crossedFinish ? finishTime + " · ждём соперника"
                : p.Ready ? "Ждём второго игрока — и начинаем." : Game.Mode == "practice" ? "Короткая трасса с подсказками для двоих." : "Коснись своей половины — и ты на потолке.";
            if (results && p.FinishBoostsEarned > 0) subtitle += " · Рывок ×2";
            if (results && Trial && activeCustomFlight)
                subtitle = finishTime + " · " + LastFlightText(p);
            if (results) subtitle += CoinScore(p);
            bool ready = p.Ready;
            string action = results ? (ready ? "✓ Ждём соперника" : Game.Mode == "practice" ? "Ещё попытка? Нажми ↗" : "Реванш? Нажми ↗")
                : ready ? "✓ Готов к старту" : "Нажми, когда готов   ↗";
            bool narrowResult = SharedEnabled && sharedAdjacent && results && w < 400;
            float extra = narrowResult ? 27 : 0;
            float top = center - (compact ? (Trial && results ? 116 : 95) : (Trial && results ? 154 : 136)) / 2f;
            top -= extra / 2;
            Text(new Rect(0, top, w, 12), eyebrow, compact ? 7 : 8, muted, false, TextAnchor.MiddleCenter);
            int titleSize = compact ? 24 : Mathf.RoundToInt(Mathf.Clamp(width * .026f, 24, 34));
            if (narrowResult) while (titleSize > 18 && TextWidth(title, titleSize, true) > w - 16) titleSize--;
            Text(new Rect(0, top + (compact ? 13 : 18), w, compact ? 29 : 43), title,
                titleSize, ink, true, TextAnchor.MiddleCenter);
            label.wordWrap = narrowResult;
            Text(new Rect(narrowResult ? 6 : 0, top + (compact ? 44 : 65), w - (narrowResult ? 12 : 0), (compact ? 14 : 17) + (narrowResult ? 14 : 0)), subtitle, compact ? 9 : 11, muted, false, TextAnchor.MiddleCenter);
            label.wordWrap = false;
            if (Trial && results)
            {
                string detail = FlightSettingsStore.Summary(Game.Flight);
                if (activeCustomFlight && !TracksV4.IsOpen(Game.Track.Id))
                    detail += " · +" + Rewards(p) + "/" + Game.Track.PositiveRingCount
                        + (Game.Track.NegativeEnabled ? " · −" + p.SlowsEarned : "")
                        + " · ошибок " + p.Collisions + (p.FinishBoostsEarned > 0 ? " · Рывок ×2" : "");
                int detailSize = compact ? 8 : 9;
                if (!narrowResult) while (detailSize > 7 && TextWidth(detail, detailSize) > w - 12) detailSize--;
                label.wordWrap = narrowResult;
                Text(new Rect(6, top + (compact ? 61 : 86) + (narrowResult ? 14 : 0), w - 12, (compact ? 13 : 17) + (narrowResult ? 13 : 0)), detail, detailSize, muted, false, TextAnchor.MiddleCenter);
                label.wordWrap = false;
            }
            if (crossedFinish && !results) return;
            int size = compact ? 9 : 11;
            float bw = TextWidth(action, size, true) + (compact ? 26 : 36), bh = compact ? 28 : 38;
            Rect br = new Rect((w - bw) / 2, top + (compact ? (Trial && results ? 79 : 65) : (Trial && results ? 112 : 94)) + extra, bw, bh);
            Round(new Rect(br.x, br.y + 3, br.width, br.height), compact ? 6 : 9, new Color(.3f, .35f, .24f, .08f));
            Round(br, compact ? 6 : 9, ready ? Hex(0xdbe7ca) : pales[index]);
            Text(br, action, size, ready ? Hex(0x536547) : accents[index], true, TextAnchor.MiddleCenter);
        }

        string CoinScore(PlayerState p)
        {
            int total = 0;
            foreach (var zone in Game.Track.RiskZones) if (zone.IsCoin) total++;
            return total == 0 ? "" : " · монеты " + p.CoinsEarned + "/" + total;
        }
        void DrawCharacterSelection(int slot, float w, float h)
        {
            PlayerState p = Game.Players[slot];
            for (int hero = 0; hero < CharacterRoster.Count; hero++)
            {
                Rect card = CharacterRect(hero, w, h);
                bool chosen = p.CharacterIndex == hero;
                StrokeRound(card, 8, chosen ? accents[slot] : Hex(0xd9dece),
                    chosen ? new Color(pales[slot].r, pales[slot].g, pales[slot].b, .62f) : Hex(0xf5f6ec));
                float size = Mathf.Min(card.height - 4, card.width * .35f);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(card.x + 3, card.center.y - size / 2, size, size), MascotArtwork.Create(hero, slot));
                Text(new Rect(card.x + size + 4, card.y, card.width - size - 12, card.height),
                    CharacterRoster.Name(hero), compact ? 10 : 12, chosen ? accents[slot] : ink, chosen, TextAnchor.MiddleCenter);
                if (chosen) Text(new Rect(card.xMax - 16, card.y + 2, 13, 13), "✓", 10, accents[slot], true, TextAnchor.MiddleCenter);
            }
            string action = p.Ready ? "✓ Готов · ждём второго" : "Готов →";
            Rect ready = ReadyButtonRect(w, h);
            Round(ready, 7, p.Ready ? Hex(0xdbe7ca) : pales[slot]);
            Text(ready, action, compact ? 11 : 12, p.Ready ? Hex(0x536547) : accents[slot], true, TextAnchor.MiddleCenter);
        }

        void DrawTracks()
        {
            if (Trial) { DrawTrialChoices(); return; }
            Fill(new Rect(0, 0, width, height), new Color(.16f, .2f, .14f, .44f));
            Round(new Rect(trackPanel.x, trackPanel.y + 5, trackPanel.width, trackPanel.height), 14, new Color(.1f, .14f, .08f, .12f));
            Round(trackPanel, 14, Hex(0xfbfbf1));
            Text(new Rect(trackPanel.x + 22, trackPanel.y + 17, trackPanel.width - 65, 24), "Ритм гонки", 18, ink, true);
            Text(trackClose, "×", 24, muted, false, TextAnchor.MiddleCenter);
            for (int i = 0; i < TracksV3.All.Length; i++)
            {
                Track track = TracksV3.All[i]; Rect r = TrackChoiceRect(i);
                bool selected = track.Id == selectedTrackId;
                if (selected) Round(r, 6, Hex(0xe9efdd));
                Ellipse(new Rect(r.x + 10, r.center.y - 3, 6, 6), selected ? Hex(0x96a773) : Hex(0xd8ddcb));
                Text(new Rect(r.x + 26, r.y, r.width - 48, r.height), track.Name, 11, selected ? ink : muted, selected, TextAnchor.MiddleLeft);
                Text(new Rect(r.xMax - 25, r.y, 20, r.height), selected ? "✓" : "↗", 12, muted, false, TextAnchor.MiddleCenter);
            }
            Fill(new Rect(negativeChoiceRect.x, negativeChoiceRect.y - 5, negativeChoiceRect.width, 1), Hex(0xe1e5d6));
            DrawNegativeSymbol(negativeChoiceRect.x + 13, negativeChoiceRect.center.y, 7, Hex(0x925478));
            Text(new Rect(negativeChoiceRect.x + 29, negativeChoiceRect.y, negativeChoiceRect.width - 90, 30),
                "Минус-кольца", 10, ink, false, TextAnchor.MiddleLeft);
            Round(new Rect(negativeChoiceRect.xMax - 52, negativeChoiceRect.y + 3, 51, 24), 12,
                trackNegativeChoice ? Hex(0xf1e0eb) : Hex(0xeef0e4));
            Text(new Rect(negativeChoiceRect.xMax - 52, negativeChoiceRect.y, 51, 30),
                trackNegativeChoice ? "ВКЛ" : "ВЫКЛ", 9, trackNegativeChoice ? Hex(0x925478) : muted, true, TextAnchor.MiddleCenter);
            StrokeRound(racePracticeChoiceRect, 6, Hex(0xd8ddcb), Hex(0xf1f3e8));
            Text(racePracticeChoiceRect, Game.Mode == "practice" ? "Перейти к гонке  ↗" : "Попробовать тренировку  ↗",
                10, ink, false, TextAnchor.MiddleCenter);
            if (trackPanel.height >= 260) Text(new Rect(trackPanel.x + 22, trackPanel.yMax - 25, trackPanel.width - 44, 16),
                Math.Round(TracksV3.All[0].Length / RaceConfig.Speed) + " секунд · " + TracksV3.All[0].PositiveRingCount
                    + " полезных колец", 9, muted, false, TextAnchor.MiddleCenter);
        }

        void DrawTrialChoices() { DrawFlightMenu(); }

        void DrawNegativeSymbol(float x, float y, float radius, Color color)
        {
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = saved * Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.Euler(0, 0, 45), Vector3.one);
            float side = radius * 1.4f;
            Fill(new Rect(-side / 2, -side / 2, side, side), color);
            Fill(new Rect(-side / 2 + 1.7f, -side / 2 + 1.7f, side - 3.4f, side - 3.4f), Hex(0xf8edf4));
            GUI.matrix = saved;
            Fill(new Rect(x - 3.5f, y - 1, 7, 2), color);
        }

        void DrawDivider()
        {
            Fill(divider, Hex(0x354534));
            float left = divider.x + (compact ? 16 : 23), right = divider.xMax - (compact ? 128 : 150), cy = divider.center.y;
            Ellipse(new Rect(left, cy - 3, 6, 6), Hex(0xc3cead)); left += 17;
            for (float x = left; x < right; x += 9) Fill(new Rect(x, cy - 1, 3, 2), Hex(0x617056));
            for (int i = 0; i < 2; i++)
            {
                float progress = Mathf.Clamp01((float)(Game.Players[i].Distance / Game.Track.Length));
                Round(new Rect(left + (right - left - 13) * progress, cy - 7 + i * 9, 13, 6), 3, pales[i]);
            }
            string clock = Game.Phase == "racing" ? Number(Game.Elapsed, 1) + " сек" : Game.Phase == "finished"
                ? (Game.Winner is string ? "НИЧЬЯ!" : "Игрок 0" + ((int)Game.Winner + 1) + " ★")
                : Math.Round(Game.Track.Length / RaceConfig.Speed) + " сек · до финиша";
            Text(new Rect(divider.xMax - (compact ? 127 : 146), divider.y, compact ? 115 : 123, divider.height),
                clock, compact ? 8 : 9, Hex(0xd2d9c5), false, TextAnchor.MiddleRight);
            Fill(new Rect(right + 6, cy - 5, 1.5f, 11), Hex(0xc3cead));
            Fill(new Rect(right + 7, cy - 5, 8, 5), Hex(0xc3cead));
        }
        void DrawFooter()
        {
            if (!compact)
            {
                Ellipse(new Rect(footer.x + 2, footer.y + 23, 42, 42), Hex(0xe7eadc));
                Text(new Rect(footer.x + 2, footer.y + 23, 42, 42), "↕", 24, Hex(0x879477), false, TextAnchor.MiddleCenter);
                Text(new Rect(footer.x + 57, footer.y + 25, 340, 20), Trial && Game.Flight.FirstAction == FirstAction.Jump
                    ? "Первое касание — обычный прыжок" : Trial && Game.Flight.Law == MotionLaw.Linear
                    ? "Первое касание — ровный перелёт" : "Одно касание — переворот", 10, ink, true);
                Text(new Rect(footer.x + 57, footer.y + 45, 340, 20), Trial ? TrialMotionHint() : "Сядьте напротив. Каждому — своя половина.", 9, muted);
                if (shell.width > 1000)
                {
                    Text(new Rect(footer.x + 470, footer.y + 33, 340, 20), Controls && Game.Track.Id == TracksControl.UnlimitedId
                        ? "Меняй высоту перед каждым проходом"
                        : Controls ? "Посадка — ×1,5  ·  Две стрелки — рывок ×2"
                        : Trial ? "Молния — сразу  ·  Ромб − — замедление"
                        : "+кольцо: −0,3 с  ·  ◇−: +0,3 с  ·  удар: +0,5 с", 10, muted);
                }
                if (footer.yMax + 15 < height)
                {
                    Fill(new Rect(footer.x, footer.yMax - 2, footer.width, 1), Hex(0xe2e4d8));
                    Text(new Rect(0, footer.yMax + 5, width, 20), (BotEnabled ? "С БОТОМ · A — действие · Esc — пауза" : SoloEnabled ? "ОДИН · A — действие · Esc — пауза" : PartyEnabled ? "КОМПАНИЯ · A / L / S / K — игроки 1 / 2 / 3 / 4 · Esc — пауза" : "МАЛЕНЬКИЙ ПЕРЕВОРОТ. БОЛЬШАЯ ГОНКА.       A / L — на клавиатуре · Esc — пауза"), 8, Hex(0xa0a590), false, TextAnchor.MiddleCenter);
                }
            }
        }
        void DrawPause()
        {
            Rect cover = Portrait ? new Rect(0, 0, width, height) : arena;
            Fill(cover, new Color(.965f, .969f, .929f, .96f));
            float cx = cover.center.x, cy = cover.center.y;
            float titleY = Portrait ? cy - 30 : PauseTitleY;
            float iconSize = Portrait ? 40 : PauseIconSize;
            if (iconSize > 0)
            {
                Rect icon = new Rect(cx - iconSize / 2, titleY - iconSize - (SessionCentered && SharedEnabled ? 8 : 14), iconSize, iconSize);
                Ellipse(icon, pales[0]);
                Text(icon, Portrait ? "↻" : "Ⅱ", 22, accents[0], true, TextAnchor.MiddleCenter);
            }
            Text(new Rect(cover.x + 15, titleY, cover.width - 30, 30), Portrait ? "Поверни телефон" : "Маленькая передышка",
                23, ink, true, TextAnchor.MiddleCenter);
            Text(new Rect(cover.x + 20, Portrait ? cy + 5 : PauseSubtitleY, cover.width - 40, 28), Portrait ? (SoloEnabled ? "Положи телефон горизонтально." : "Положите телефон горизонтально.\nИгроки садятся друг напротив друга.") : pauseReason,
                10, muted, false, TextAnchor.MiddleCenter);
            if (!Portrait)
            {
                Round(resumeRect, 9, Hex(0xdbe7ca));
                Text(resumeRect, "Продолжить гонку →", 11, Hex(0x536547), true, TextAnchor.MiddleCenter);
            }
        }
        void DrawRules()
        {
            Fill(new Rect(0, 0, width, height), new Color(.16f, .2f, .14f, .44f));
            Rect r = rulesPanel;
            Round(new Rect(r.x, r.y + 5, r.width, r.height), 14, new Color(.1f, .14f, .08f, .14f));
            Round(r, 14, Hex(0xfbfbf1));
            Text(new Rect(r.x + 24, r.y + 18, r.width - 70, 12), "КАК ИГРАТЬ", 8, muted);
            Text(new Rect(r.x + 24, r.y + 35, r.width - 48, 28), "Пол. Потолок. Финиш!", 22, ink, true);
            Text(rulesClose, "×", 24, muted, false, TextAnchor.MiddleCenter);
            string[] lines = {
                "Своя половина — переворот. Клавиши A / L. В полёте дождись посадки.",
                "Круг +: −0,3 с после манёвра. Удар о его камень после касания отменяет ожидающую награду.",
                "Ромб −: +0,3 с, переворот работает. Ромбы можно отключить в меню трасс.",
                "Удар: +0,5 с; нетронутые кольца доступны. Первый на финише побеждает. Реванш — вдвоём."
            };
            if (Trial) lines = new[] {
                Game.Flight.FirstAction == FirstAction.Jump
                    ? "Тап с опоры — обычный прыжок. Своя половина экрана или клавиши A / L."
                    : "Тап с опоры — перелёт к другой стене. Своя половина экрана или клавиши A / L.",
                FlightRule(),
                "Метка на грани: точная посадка даёт ×1,5. По камню можно бежать; с него можно прыгать.",
                "Узкая метка у ленты: рывок −0,1 с. Настройки общие, реванш их сохраняет. Выбор — в меню."
            };
            if (BotEnabled) lines = new[] { "Выбери героя и нажми «Начать». A, поле или лапка — твоё действие.", "Бот готов автоматически. Уровень выбран до старта; награды личные.", Game.PartyCamera==PartyCamera.Constant?"Карта движется сама. Полностью за левым краем — выбыл.":"Камера за последним. Выбывания за экран нет.", "Оба бегут до своего исхода. Повтор — новый план; результаты не сохраняются." };
            else if (SoloEnabled) lines = new[] {
                "Выбери героя и нажми «Начать». Тап по полю/лапке или A — действие.",
                Game.PartyCamera == PartyCamera.Constant ? "Карта движется сама. Полностью за левым краем — выбыл; справа выбывания нет." : "Камера следует за героем. Выбывания за экран нет.",
                "Награды личные. Финиш или выбывание сразу завершает попытку.",
                "«Ещё раз» — сначала. Прогресс и результаты не сохраняются."
            };
            else if (PartyEnabled) lines = new[] {
                "Каждому своя четверть и номер. Тап или A / L / S / K — действие игроков 1 / 2 / 3 / 4.",
                Game.PartyCamera == PartyCamera.Constant ? "Карта движется сама. Полностью ушёл за левый край — выбыл. Справа выбывания нет." : "Камера следует за последним бегущим. Выбывания за экран нет.",
                "Бонусы и монеты личные. Выбывшие и финишировавшие больше не управляются.",
                "Первый финиш побеждает. Остальные продолжают до своего исхода. Ещё раз — когда готовы все." };
            float start = r.y + 77, row = (r.height - 134) / 4;
            for (int i = 0; i < lines.Length; i++)
            {
                Ellipse(new Rect(r.x + 24, start + i * row + 2, 18, 18), Hex(0xe8ecd9));
                Text(new Rect(r.x + 24, start + i * row + 2, 18, 18), (i + 1).ToString(), 9, Hex(0x6a7856), true, TextAnchor.MiddleCenter);
                label.wordWrap = true;
                Text(new Rect(r.x + 54, start + i * row, r.width - 78, row - 3), lines[i], 10, ink);
                label.wordWrap = false;
            }
            Round(rulesDone, 9, Hex(0xdbe7ca));
            Text(rulesDone, "Всё понятно →", 11, Hex(0x536547), true, TextAnchor.MiddleCenter);
        }

        string Hint(PlayerState p)
        {
            if (Game.Phase != "racing" || p.FinishTime.HasValue) return "";
            if (Trial && activeCustomFlight && p.LastFlight != null) return LastFlightText(p);
            if (p.LastHit != null && Game.Elapsed - p.LastHit.At < 1.7)
            {
                foreach (RiskZone zone in Game.Track.RiskZones)
                    if (!zone.IsNegative && !zone.FreeReward && zone.ObstacleId == p.LastHit.Id
                        && p.FailedRiskIds.Contains(zone.Id))
                        return "Касание было до удара — награда отменена.";
                if (Controls) return Game.Track.Id == TracksControl.UnlimitedId
                    ? "Кувырок! Подстрой высоту к следующему проходу."
                    : "Кувырок! Точная посадка ещё может принести ускорение.";
                return Game.Track.RiskZones.Length > 0 ? "Нетронутые кольца доступны и после удара."
                    : "Кувырок! Следующий камень можно обойти.";
            }
            if (Game.Winner is int) return "Соперник на финише — беги до конца!";
            if (Controls && p.Support == SupportKind.Stone) return "Точная посадка! С камня можно прыгнуть снова.";
            foreach (var obstacle in Game.Track.Obstacles)
            {
                if (p.HitObstacleIds.Contains(obstacle.Id)
                    || ObstacleGeometry.Right(obstacle) + RaceConfig.PlayerRadius <= p.Distance
                    || ObstacleGeometry.Left(obstacle) - p.Distance >= 650) continue;
                if (CorridorArtwork.TryLowThreshold(obstacle, out bool floorThreshold))
                    return floorThreshold ? "Низкий порог: перелёт или возврат на пол."
                        : "Верхний порог: перелёт или возврат к потолку.";
                if (CorridorArtwork.TryOffsetEntry(obstacle, out bool fromFloor))
                    return p.Distance > ObstacleGeometry.Left(obstacle) + obstacle.Width
                        ? "Дальняя часть впереди — закончи перелёт."
                        : fromFloor ? "Вход с пола к потолку: между двумя частями."
                            : "Вход с потолка к полу: между двумя частями.";
            }
            if (Controls && !TracksV4.IsOpen(Game.Track.Id))
                foreach (var zone in Game.Track.RiskZones)
                {
                    if (zone.IsLanding && zone.X - p.Distance > 0 && zone.X - p.Distance < 350)
                        return "Приземлись точно в метку на грани камня.";
                    if (zone.IsFinish && zone.X - p.Distance > 0 && zone.X - p.Distance < 650)
                        return "Стрелки у ленты: пролети через них для рывка ×2.";
                }
            if (Trial)
                return TracksV4.IsOpen(Game.Track.Id) || Game.Elapsed < 3.5 ? TrialMotionHint() : "";
            if (Game.Mode != "practice")
                return Game.Elapsed < 3.5 ? "Коснись своей половины — переворот." : "";
            if (p.LastSlow != null && Game.Elapsed - p.LastSlow.At < 1.1) return "Ромб замедляет. Переворот работает.";
            if (p.LastSuccess != null && Game.Elapsed - p.LastSuccess.At < 1.4) return "Чистый манёвр — кольцо твоё!";
            foreach (RiskZone zone in Game.Track.RiskZones)
                if (zone.X - p.Distance > -25 && zone.X - p.Distance < 470 && !p.FailedRiskIds.Contains(zone.Id)
                    && !p.SuccessfulRiskIds.Contains(zone.Id) && !p.NegativeRiskIds.Contains(zone.Id))
                    return zone.IsNegative ? "Обойди ромб по другой стороне."
                        : zone.AfterObstacle ? "Камень позади? Вернись за кольцом."
                        : "Рано — безопасно. Чуть позже — кольцо.";
            if (Game.Mode == "practice")
            {
                if (p.Flipping) return "Дождись посадки перед следующим касанием.";
                for (int i = 0; i < Game.Track.Obstacles.Length; i++)
                {
                    Obstacle o = Game.Track.Obstacles[i];
                    if (ObstacleGeometry.Right(o) + RaceConfig.PlayerRadius <= p.Distance || p.HitObstacleIds.Contains(o.Id)) continue;
                    if (o.Kind == "gate") return "Ворота: нажми у метки, чтобы оказаться в центре окна.";
                    bool safe = p.Y == (o.Kind == "floor" ? 0 : 1);
                    if (safe && i + 1 < Game.Track.Obstacles.Length && Game.Track.Obstacles[i + 1].Kind == o.Kind)
                        return "Два камня подряд: оставайся на свободной стороне.";
                    if (safe) return o.Kind == "floor" ? "Оставайся наверху, пока камень не будет позади." : "Оставайся внизу, пока выступ не будет позади.";
                    return o.Kind == "floor" ? "Камень на полу: касание отправит тебя на потолок." : "Выступ сверху: касание вернёт тебя на пол.";
                }
                return "Финиш рядом. Ты уже умеешь переворачиваться!";
            }
            if (Game.Elapsed < 3.5) return "Нажми в любом месте своей половины. Обойди первый камень!";
            foreach (Obstacle o in Game.Track.Obstacles)
                if (o.Kind == "gate" && ObstacleGeometry.Left(o) - p.Distance > 0 && ObstacleGeometry.Left(o) - p.Distance < 470)
                    return "Ворота: окажись в середине окна во время перелёта.";
            return "";
        }
        void Text(Rect r, string text, int size, Color color, bool bold = false, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            label.font = bold ? semibold : font; label.fontSize = size; label.fontStyle = FontStyle.Normal;
            label.alignment = alignment; label.normal.textColor = color; GUI.Label(r, text, label);
        }
        float TextWidth(string text, int size, bool bold = false)
        {
            label.font = bold ? semibold : font; label.fontSize = size; label.fontStyle = FontStyle.Normal;
            return label.CalcSize(new GUIContent(text)).x;
        }
        static int Rewards(PlayerState p) { return p.BoostsEarned + p.FinishBoostsEarned; }
        static string Number(double value, int decimals) { return value.ToString("F" + decimals, CultureInfo.InvariantCulture).Replace('.', ','); }
        static Color Hex(int rgb) { return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f); }
        static void Fill(Rect r, Color color) { GUI.color = color; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }
        void Ellipse(Rect r, Color color) { GUI.color = color; GUI.DrawTexture(r, circle); GUI.color = Color.white; }
        void StrokeRound(Rect r, float radius, Color border, Color bg)
        {
            Round(r, radius, bg);
            GUI.color = Color.white;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, border, 1, radius);
        }
        void Round(Rect r, float radius, Color color)
        {
            radius = Mathf.Min(radius, Mathf.Min(r.width, r.height) * .5f);
            // One rounded draw keeps translucent fills even at corners and joins.
            GUI.color = Color.white;
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, radius);
        }
        static Texture2D MakeCircle()
        {
            Texture2D t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            Color[] colors = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(32, 32));
                colors[y * 64 + x] = new Color(1, 1, 1, Mathf.Clamp01(32 - d));
            }
            t.SetPixels(colors); t.Apply(false, true); return t;
        }
        static Texture2D MakeVeil()
        {
            Texture2D t = new Texture2D(128, 1, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            Color[] colors = new Color[128];
            for (int x = 0; x < 128; x++)
            {
                float n = x / 127f, alpha = Mathf.Min(1, Mathf.Min(n / .35f, (1 - n) / .35f)) * .79f;
                colors[x] = new Color(.953f, .961f, .914f, alpha);
            }
            t.SetPixels(colors); t.Apply(false, true); return t;
        }
        AudioClip Tone(float frequency, float duration)
        {
            int rate = 22050, count = (int)(duration * rate); float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float p = (float)i / count;
                data[i] = Mathf.Sin(2 * Mathf.PI * frequency * i / rate * (1 - p * .15f)) * .12f * Mathf.Sin(Mathf.PI * p) * (1 - p);
            }
            AudioClip c = AudioClip.Create("tone", count, 1, rate, false); c.SetData(data, 0); return c;
        }
        void Beep(AudioClip clip) { if (soundEnabled && clip != null) audioSource.PlayOneShot(clip); }
    }
}
