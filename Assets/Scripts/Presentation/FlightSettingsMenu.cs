using System;
using UnityEngine;
using FlipRace.Core;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        FlightSelection flightSelection;
        int flightPage, activeFlightPreset;
        bool activeCustomFlight;
        bool pendingSharedField, pendingSharedAdjacent, pendingSharedFullHeight, pendingSplitGhost;
        string pendingFlightTrack;
        Rect flightPanel, flightBack, flightApply, flightCustom, flightEdit, flightReset, flightDone, flightCopy, flightChosen, flightFastFlip, flightOldTracks, flightTracks;
        Rect flightView;

        public bool FlightSettingsVisible { get { return showTracks && Trial && flightPage == 1 && !Portrait; } }
        public Rect FlightCopyRect { get { return flightCopy; } }
        public string FlightCopyText { get { return flightSelection == null ? "" : FlightSettingsStore.CopyText(flightSelection.Custom); } }
        public bool IsCustomFlight { get { return activeCustomFlight; } }
        public int FlightMenuPage { get { return showTracks && Trial ? flightPage : -1; } }
        public Rect ViewMenuButtonRect { get { MeasureFlightMenu(); return flightView; } }

        void InitializeFlightMenu()
        {
            MigrateRetiredSharedHeight();
            FlightDraft custom;
            string storedCustom = PlayerPrefs.GetString(FlightSettingsStore.Key, "");
            bool validCustom = FlightSettingsStore.TryDecode(storedCustom, out custom);
            flightSelection = new FlightSelection(custom);
            int storedChoice = PlayerPrefs.GetInt(FlightSettingsStore.ActiveChoiceKey, FlightSettingsStore.ChosenPresetIndex);
            int saved = FlightSettingsStore.ValidActiveChoice(storedChoice);
            activeCustomFlight = saved == -1; activeFlightPreset = saved;
            if (activeCustomFlight) flightSelection.SelectCustom(); else flightSelection.SelectPreset(activeFlightPreset);
            // Retire only preset references; never replace a valid custom's physical values.
            bool changed = false;
            if (storedChoice != saved) { PlayerPrefs.SetInt(FlightSettingsStore.ActiveChoiceKey, saved); changed = true; }
            if (validCustom && storedCustom != FlightSettingsStore.Encode(custom))
            { PlayerPrefs.SetString(FlightSettingsStore.Key, FlightSettingsStore.Encode(custom)); changed = true; }
            if (changed) PlayerPrefs.Save();
        }
        static Track InitialFlightTrack()
        { return RaceTracks.GetAt(0); }
        void SaveCustomFlight()
        { PlayerPrefs.SetString(FlightSettingsStore.Key, FlightSettingsStore.Encode(flightSelection.Custom)); PlayerPrefs.Save(); }
        void BeginFlightMenu()
        {
            if (flightSelection == null) InitializeFlightMenu();
            if (activeCustomFlight) flightSelection.SelectCustom(); else flightSelection.SelectPreset(activeFlightPreset);
            pendingFlightTrack = RaceTracks.NormalizeId(Game.Track.Id); flightPage = 0;
            pendingSolo = SoloEnabled; pendingParty = PartyEnabled; pendingPartyCount = PartyEnabled ? Game.Players.Length : 4;
            pendingPartyCamera = PartyEnabled || SoloEnabled ? Game.PartyCamera : PartyCamera.Constant;
            pendingSharedField = sharedField; pendingSharedAdjacent = sharedAdjacent; pendingSharedFullHeight = false; pendingSplitGhost = splitGhost;
        }
        void MeasureFlightMenu()
        {
            // The permanent version badge ends at y=22.
            float fw = Mathf.Min(width - 24, 620), fh = Mathf.Min(height - 40, 300);
            flightPanel = new Rect((width - fw) / 2, Mathf.Max(28, (height - fh) / 2), fw, fh);
            flightBack = new Rect(flightPanel.x + 12, flightPanel.y + 8, 30, 27);
            flightApply = new Rect(flightPanel.xMax - 177, flightPanel.yMax - 40, 161, 28);
            flightCopy = new Rect(flightPanel.x + 16, flightPanel.yMax - 40, 103, 28);
            flightReset = new Rect(flightCopy.xMax + 8, flightCopy.y, 150, 28);
            flightDone = new Rect(flightPanel.xMax - 126, flightCopy.y, 110, 28);
            float col = (flightPanel.width - 48) / 2;
            flightChosen = new Rect(flightPanel.x + 16, flightPanel.y + 60, col, 42);
            flightFastFlip = new Rect(flightPanel.x + 16, flightPanel.y + 111, col, 42);
            flightCustom = new Rect(flightPanel.x + 16, flightPanel.y + 172, col, 36);
            flightEdit = new Rect(flightPanel.x + 32 + col, flightPanel.y + 106, col, 24);
            flightOldTracks = new Rect(flightPanel.xMax - 126, flightPanel.y + 131, 110, 20);
            flightTracks = new Rect(flightPanel.x + 32 + col, flightPanel.y + 154, col, 64);
            flightView = new Rect(flightPanel.x + 16, flightPanel.y + 214, col, 24);
        }
        Rect FullTrackRect(int i)
        {
            float col = (flightPanel.width - 48) / 2;
            return new Rect(flightPanel.x + 16 + i % 2 * (col + 16), flightPanel.y + 77 + i / 2 * 49, col, 44);
        }
        Rect OldTrackRect(int i)
        {
            float col = (flightPanel.width - 48) / 2;
            return new Rect(flightPanel.x + 16 + i % 2 * (col + 16), flightPanel.y + 85 + i / 2 * 37, col, 34);
        }
        static readonly string[] OldTrackIds = { TracksV4.OpenId, TracksControl.ChallengeId, TracksControl.UnlimitedId,
            TracksTune.ChallengeId, TracksV4.ChallengeId, TracksFull.PracticeId, TracksRhythm.PracticeId };
        static readonly string[] OldTrackNames = { "Свободный полёт", "Точные посадки", "Проходы по высоте",
            "Кольца и ускорители", "Прежний участок", "Проба посадки и цепочки", "Проба ворот" };
        Rect FlightFieldRect(int column, int row)
        {
            float col = (flightPanel.width - 48) / 2, rh = (flightPanel.height - 98) / 4;
            return new Rect(flightPanel.x + 16 + column * (col + 16), flightPanel.y + 49 + row * rh, col, rh - 3);
        }
        Rect FlightCell(Rect field, int index, int count)
        { float cell = (field.width - (count - 1) * 3) / count; return new Rect(field.x + index * (cell + 3), field.y + 14, cell, field.height - 15); }
        Rect ViewFieldRect(int row)
        { return new Rect(flightPanel.x + 16, flightPanel.y + 49 + row * 55, flightPanel.width - 32, 49); }
        public Rect ViewOptionRect(int row, int option)
        { MeasureFlightMenu(); return row < 0 || row > 1 || option < 0 || option >= (row == 0 ? 3 : 2) ? default(Rect) : FlightCell(ViewFieldRect(row), option, row == 0 ? 3 : 2); }
        bool CanApplyFlight { get { return (!pendingSolo || RaceTracks.IsFull(pendingFlightTrack)) && (pendingFlightTrack != TracksControl.UnlimitedId || flightSelection.Settings.AirRepeats == AirRepeatLimit.Unlimited); } }

        void FlightPointer(Vector2 p)
        {
            MeasureFlightMenu();
            if (flightBack.Contains(p))
            { if (flightPage == 0) CloseRules(); else { flightPage = 0; BlockGameplayInput(); } return; }
            if (flightPage == 1)
            {
                if (flightCopy.Contains(p))
                {
#if !UNITY_WEBGL || UNITY_EDITOR
                    GUIUtility.systemCopyBuffer = FlightCopyText;
#endif
                    return;
                }
                if (flightReset.Contains(p)) { flightSelection.ResetCustom(); SaveCustomFlight(); return; }
                if (flightDone.Contains(p)) { flightPage = 0; BlockGameplayInput(); return; }
                HandleFlightFields(p); return;
            }
            if (flightApply.Contains(p)) { if (CanApplyFlight) ApplyFlightChoice(); return; }
            if (flightPage == 7) { SoloMenuPointer(p); return; }
            if (flightPage == 6) { PartyMenuPointer(p); return; }
            if (flightPage == 5)
            {
                if (PartyMenuButtonRect.Contains(p)) { pendingSolo = false; pendingParty = true; flightPage = 6; BlockGameplayInput(); return; }
                if (SoloMenuButtonRect.Contains(p)) { EnterSoloMenu(); return; }
                for (int option = 0; option < 3; option++)
                    if (ViewOptionRect(0, option).Contains(p))
                    { pendingSolo = pendingParty = false; pendingSharedField = option == 2; pendingSplitGhost = option == 1; return; }
                for (int option = 0; option < 2; option++)
                    if (pendingSharedField && ViewOptionRect(1, option).Contains(p))
                    { pendingSharedAdjacent = option == 1; return; }
                return;
            }
            if (flightPage == 4)
            {
                for (int i = 0; i < RaceTracks.All.Length; i++) if (FullTrackRect(i).Contains(p))
                { pendingFlightTrack = RaceTracks.GetAt(i).Id; return; }
                return;
            }
            if (flightPage == 3)
            {
                for (int i = 0; i < OldTrackIds.Length; i++) if (OldTrackRect(i).Contains(p))
                {
                    if (OldTrackIds[i] == TracksControl.UnlimitedId && flightSelection.Settings.AirRepeats != AirRepeatLimit.Unlimited) return;
                    pendingFlightTrack = OldTrackIds[i]; return;
                }
                return;
            }
            if (flightPage == 0)
            {
                if (flightChosen.Contains(p)) { flightSelection.SelectPreset(FlightSettingsStore.ChosenPresetIndex); return; }
                if (flightFastFlip.Contains(p)) { flightSelection.SelectPreset(FlightSettingsStore.FastFlipPresetIndex); return; }
                if (flightCustom.Contains(p)) { flightSelection.SelectCustom(); flightPage = 1; BlockGameplayInput(); return; }
                if (!pendingSolo && flightOldTracks.Contains(p)) { flightPage = 3; BlockGameplayInput(); return; }
                if (flightTracks.Contains(p)) { flightPage = 4; BlockGameplayInput(); return; }
                if (flightView.Contains(p)) { flightPage = pendingSolo ? 7 : pendingParty ? 6 : 5; BlockGameplayInput(); return; }
                if (flightEdit.Contains(p))
                { flightSelection.Edit(d => { }); SaveCustomFlight(); flightPage = 1; BlockGameplayInput(); return; }
                return;
            }
        }
        void ApplyFlightChoice()
        {
            if (Active || !showTracks) return;
            GameState previousSelection = Game;
            Game = CreateSelectedFlight(Game, pendingFlightTrack, flightSelection.Settings);
            if (pendingSolo)
            {
                if (!RaceTracks.IsFull(Game.Track.Id)) Game = RaceSimulation.CreateTrial(RaceTracks.GetAt(0), flightSelection.Settings);
                Game = RaceSimulation.CreateSolo(Game.Track, flightSelection.Settings, pendingPartyCamera, PartyStartLeft, PartyWindowWidth);
                Game.Players[0].CharacterIndex = previousSelection.Players[0].CharacterIndex;
            }
            else if (pendingParty)
            {
                if (!RaceTracks.IsFull(Game.Track.Id)) Game = RaceSimulation.CreateTrial(RaceTracks.GetAt(0), flightSelection.Settings);
                GameState previous = Game;
                Game = RaceSimulation.CreateParty(previous.Track, flightSelection.Settings, pendingPartyCount, pendingPartyCamera, PartyStartLeft, PartyWindowWidth);
                for (int i = 0; i < Game.Players.Length; i++) Game.Players[i].CharacterIndex = i < previousSelection.Players.Length ? previousSelection.Players[i].CharacterIndex : i % CharacterRoster.Count;
            }
            sharedField = pendingSharedField; sharedAdjacent = pendingSharedAdjacent; splitGhost = pendingSplitGhost && !sharedField; MigrateRetiredSharedHeight();
            pendingFlightTrack = Game.Track.Id;
            activeCustomFlight = flightSelection.IsCustom; activeFlightPreset = flightSelection.PresetIndex;
            legacyControls = false;
            PlayerPrefs.SetInt(FlightSettingsStore.ActiveChoiceKey, activeCustomFlight ? -1 : activeFlightPreset); PlayerPrefs.Save();
            selectedTrackId = Game.Track.Id;
            round = 1; paused = showRules = showTracks = false; suppressFrame = true;
            lastFrame = Time.realtimeSinceStartupAsDouble; BlockGameplayInput();
        }
        static GameState CreateSelectedFlight(GameState previous, string id, FlightSettings settings)
        {
            id = RaceTracks.NormalizeId(id);
            Track track = RaceTracks.IsFull(id) || id == TracksFull.PracticeId || id == TracksRhythm.PracticeId ? RaceTracks.Get(id)
                : id == TracksControl.ChallengeId ? TracksControl.Challenge(MotionMode.GravityAirFlip)
                : id == TracksControl.UnlimitedId ? TracksControl.Unlimited()
                : id == TracksTune.ChallengeId ? TracksTune.Challenge(MotionProfiles.Baseline) : TracksV4.Get(id);
            var next = RaceSimulation.CreateTrial(track, settings);
            next.Players[0].CharacterIndex = previous.Players[0].CharacterIndex;
            if (previous.Players.Length > 1) next.Players[1].CharacterIndex = previous.Players[1].CharacterIndex;
            return next;
        }
        void HandleFlightFields(Vector2 p)
        {
            var d = flightSelection.Custom;
            for (int i = 0; i < 2; i++)
            {
                int value = i;
                if (FlightCell(FlightFieldRect(0, 0), i, 2).Contains(p)) { EditFlight(v => v.First = (FirstAction)value); return; }
                if (FlightCell(FlightFieldRect(0, 1), i, 2).Contains(p)) { EditFlight(v => v.Law = value == 0 ? MotionLaw.Gravity : MotionLaw.Linear); return; }
            }
            for (int i = 0; i < 3; i++) if (FlightCell(FlightFieldRect(0, 2), i, 3).Contains(p))
            { int value = i; EditFlight(v => v.Repeats = value == 0 ? AirRepeatLimit.None : value == 1 ? AirRepeatLimit.One : AirRepeatLimit.Unlimited); return; }
            for (int i = 0; i < 8; i++)
            {
                int value = i;
                if (d.Law == MotionLaw.Linear && d.First == FirstAction.Jump && FlightCell(FlightFieldRect(0, 3), i, 8).Contains(p))
                { EditFlight(v => v.Height = value); return; }
                if (d.Law == MotionLaw.Gravity && FlightCell(FlightFieldRect(1, 0), i, 8).Contains(p))
                { EditFlight(v => { if (v.First == FirstAction.Jump) v.Jump = value; else v.Flip = value; }); return; }
                if (d.Law == MotionLaw.Gravity && FlightCell(FlightFieldRect(1, 1), i, 8).Contains(p))
                { EditFlight(v => v.Gravity = value); return; }
                if (d.Law == MotionLaw.Linear && FlightCell(FlightFieldRect(1, 2), i, 8).Contains(p))
                { EditFlight(v => v.Speed = value); return; }
            }
        }
        void EditFlight(Action<FlightDraft> edit) { flightSelection.Edit(edit); SaveCustomFlight(); Beep(tapClip); }
        void FlightButton(Rect r, string title, bool selected = false, bool enabled = true, int size = 10)
        {
            StrokeRound(r, 6, selected ? Hex(0xa5b98a) : Hex(0xe0e5d6), selected ? Hex(0xe5edd9) : Hex(0xf6f7ed));
            Text(r, title, size, !enabled ? Hex(0xa7ab9d) : selected ? ink : muted, selected, TextAnchor.MiddleCenter);
        }
        void DrawFlightMenu()
        {
            MeasureFlightMenu();
            Fill(new Rect(0, 0, width, height), new Color(.16f, .2f, .14f, .44f));
            Round(flightPanel, 12, Hex(0xfbfbf1));
            Text(flightBack, flightPage == 0 ? "×" : "‹", 23, muted, false, TextAnchor.MiddleCenter);
            Text(new Rect(flightPanel.x + 49, flightPanel.y + 10, flightPanel.width - 65, 23),
                flightPage == 1 ? "Свои настройки" : flightPage == 3 ? "Прежние участки"
                    : flightPage == 7 ? "Один · полная трасса" : flightPage == 4 ? "Трассы" : flightPage == 5 ? "Вид и посадка" : flightPage == 6 ? "Компания · 2–4 игрока" : "Управление и трасса", 17, ink, true);
            if (flightPage == 1) { DrawFlightEditor(); return; }
            if (flightPage == 3) { DrawOldFlightTracks(); return; }
            if (flightPage == 4) { DrawFullFlightTracks(); return; }
            if (flightPage == 5) { DrawViewChoices(); return; }
            if (flightPage == 7) { DrawSoloChoices(); return; }
            if (flightPage == 6) { DrawPartyChoices(); return; }
            float col = (flightPanel.width - 48) / 2, right = flightPanel.x + 32 + col;
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 43, col, 14), "ГОТОВЫЕ НАСТРОЙКИ", 8, muted);
            FlightButton(flightChosen, "Основной — прыжок и полёт", flightSelection.PresetIndex == FlightSettingsStore.ChosenPresetIndex, true, 10);
            FlightButton(flightFastFlip, "Быстрый переворот", flightSelection.PresetIndex == FlightSettingsStore.FastFlipPresetIndex, true, 10);
            FlightButton(flightCustom, "Свои настройки  ›", flightSelection.IsCustom);
            FlightButton(flightView, (pendingSolo ? "Один" : pendingParty ? "Компания " + pendingPartyCount : pendingSharedField ? "Общее поле" : pendingSplitGhost ? "Два поля + призрак" : "Два поля") + (pendingSolo ? " · камера  ›" : " · вид и посадка  ›"), false, true, 9);
            FlightButton(flightEdit, "Изменить выбранное  ›", false, true, 9);
            Text(new Rect(right, flightPanel.y + 43, col, 15), flightSelection.IsCustom ? "Свой набор" : FlightSettingsStore.PresetName(flightSelection.PresetIndex), 11, ink, true);
            label.wordWrap = true;
            Text(new Rect(right, flightPanel.y + 63, col, 39), FlightSettingsStore.Summary(flightSelection.Settings), 10, muted);
            label.wordWrap = false;
            Text(new Rect(right, flightPanel.y + 134, col - 112, 14), "ТРАССЫ", 8, muted);
            if (!pendingSolo) FlightButton(flightOldTracks, "Прежние участки ›", false, true, 8);
            StrokeRound(flightTracks, 6, Hex(0xa5b98a), Hex(0xe5edd9));
            Text(new Rect(flightTracks.x + 8, flightTracks.y + 10, flightTracks.width - 16, 20),
                PendingFlightTrackName(), 11, ink, true, TextAnchor.MiddleCenter);
            Text(new Rect(flightTracks.x + 8, flightTracks.y + 34, flightTracks.width - 16, 20),
                "Выбрать трассу  ›", 10, muted, false, TextAnchor.MiddleCenter);
            DrawFlightApplyFooter();
        }
        void DrawViewChoices()
        {
            DrawField(ViewFieldRect(0), "ВИД", new[] { "Два поля", "Два поля + призрак", "Общее поле" }, pendingSharedField ? 2 : pendingSplitGhost ? 1 : 0, true);
            DrawField(ViewFieldRect(1), "ПОСАДКА", new[] { "Напротив · верх / низ", "Рядом · лево / право" }, pendingSharedAdjacent ? 1 : 0,
                pendingSharedField, "В двух полях — напротив, верх / низ");
            label.wordWrap = true;
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 164, flightPanel.width - 32, 36),
                pendingSharedField ? "Общее поле — половина дорожки по центру. Камера следует за отстающим."
                    : pendingSplitGhost ? "Светлый призрак — живой соперник в твоём поле. Свой герой всегда сверху."
                        : "У каждого своя дорожка и камера.", 10, muted);
            label.wordWrap = false;
            FlightButton(PartyMenuButtonRect, "Компания · 2–4 игрока  ›", false, true, 11);
            FlightButton(SoloMenuButtonRect, "Один ›", false, true, 11);
            DrawFlightApplyFooter();
        }
        void DrawFullFlightTracks()
        {
            label.wordWrap = true;
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 43, flightPanel.width - 32, 28),
                FlightSettingsStore.Summary(flightSelection.Settings), 10, muted);
            label.wordWrap = false;
            for (int i = 0; i < RaceTracks.All.Length; i++)
                FlightButton(FullTrackRect(i), RaceTracks.All[i].Name, pendingFlightTrack == RaceTracks.All[i].Id, true, 11);
            DrawFlightApplyFooter();
        }
        void DrawOldFlightTracks()
        {
            label.wordWrap = true;
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 44, flightPanel.width - 32, 34),
                FlightSettingsStore.Summary(flightSelection.Settings), 10, muted);
            label.wordWrap = false;
            for (int i = 0; i < OldTrackIds.Length; i++)
                FlightButton(OldTrackRect(i), OldTrackNames[i], pendingFlightTrack == OldTrackIds[i],
                    OldTrackIds[i] != TracksControl.UnlimitedId || flightSelection.Settings.AirRepeats == AirRepeatLimit.Unlimited, 10);
            DrawFlightApplyFooter();
        }
        void DrawFlightApplyFooter()
        {
            string note = !CanApplyFlight ? "Проходы требуют повторов ∞"
                : RaceTracks.IsFull(pendingFlightTrack) ? flightSelection.IsCustom ? "Свои настройки · исходная трасса" : "36 сек · управление сохраняется"
                : "Участок: " + PendingFlightTrackName();
            Text(new Rect(flightPanel.x + 16, flightPanel.yMax - 39, flightPanel.width - 205, 27), note,
                9, !CanApplyFlight ? Hex(0x9a6844) : muted, false, TextAnchor.MiddleLeft);
            FlightButton(flightApply, pendingSolo ? "Выбрать героя →" : "К готовности →", true, CanApplyFlight, 11);
        }
        string PendingFlightTrackName()
        {
            for (int i = 0; i < OldTrackIds.Length; i++) if (OldTrackIds[i] == pendingFlightTrack) return OldTrackNames[i];
            foreach (var track in RaceTracks.All) if (track.Id == pendingFlightTrack) return track.Name;
            return "—";
        }
        void DrawField(Rect r, string title, string[] options, int selected, bool enabled, string disabled = "")
        {
            Text(new Rect(r.x, r.y, r.width, 13), title, 9, enabled ? ink : muted, enabled);
            for (int i = 0; i < options.Length; i++) FlightButton(FlightCell(r, i, options.Length), options[i], i == selected, enabled, 9);
            if (!enabled && disabled.Length > 0)
            {
                Rect cells = new Rect(r.x, r.y + 14, r.width, r.height - 15);
                Round(cells, 5, Hex(0xf0f1e7)); Text(cells, disabled, 9, Hex(0x8c9380), false, TextAnchor.MiddleCenter);
            }
        }
        void DrawFlightEditor()
        {
            var d = flightSelection.Custom; var s = d.Settings(); bool gravity = d.Law == MotionLaw.Gravity;
            string[] levels = { "0", "1", "2", "3", "4", "5", "6", "7" };
            DrawField(FlightFieldRect(0, 0), "Первый тап с опоры", new[] { "Переворот", "Обычный прыжок" }, (int)d.First, true);
            DrawField(FlightFieldRect(0, 1), "Движение", new[] { "Разгон", "Пост. скорость" }, gravity ? 0 : 1, true);
            DrawField(FlightFieldRect(0, 2), "Воздушные повторы", new[] { "0", "1", "∞" }, d.Repeats == AirRepeatLimit.None ? 0 : d.Repeats == AirRepeatLimit.One ? 1 : 2, true);
            DrawField(FlightFieldRect(0, 3), "Высота: " + Number(s.LinearJumpHeight, 0) + " ед", new[] { "48", "64", "80", "96", "112", "128", "160", "192" }, d.Height,
                !gravity && d.First == FirstAction.Jump, "Только для линейного прыжка");
            int impulse = d.First == FirstAction.Jump ? d.Jump : d.Flip;
            DrawField(FlightFieldRect(1, 0), (d.First == FirstAction.Jump ? "Сила прыжка " : "Толчок ") + impulse + " · "
                + Number(d.First == FirstAction.Jump ? s.JumpImpulse : s.FlipImpulse, 0) + " ед/с", levels, impulse, gravity, "При постоянной скорости нет толчка");
            DrawField(FlightFieldRect(1, 1), "Тяга " + d.Gravity + " · " + Number(s.GravityAcceleration, s.GravityAcceleration % 1 == 0 ? 0 : 1) + " ед/с²",
                levels, d.Gravity, gravity, "При постоянной скорости нет тяги");
            DrawField(FlightFieldRect(1, 2), "Перелёт: " + Number(212 / s.LinearSpeed, 1) + " с · " + Number(s.LinearSpeed, 1) + " ед/с",
                new[] { "1,2", "1,0", "0,9", "0,8", "0,7", "0,6", "0,5", "0,4" }, d.Speed, !gravity, "Линейная скорость не влияет на разгон");
            Rect note = FlightFieldRect(1, 3);
            label.wordWrap = true;
            Text(note, "0 — база, не отсутствие тяги. Неактивные значения запоминаются. Меняй один параметр за раз.", 9, muted);
            label.wordWrap = false;
            FlightButton(flightCopy, "Копировать", false, true, 10);
            FlightButton(flightReset, "Сброс: " + FlightSettingsStore.PresetName(d.ResetPreset), false, true, 8);
            FlightButton(flightDone, "Готово →", true);
        }
        string FlightActionHint()
        {
            var f = Game.Flight;
            string first = f.FirstAction == FirstAction.Jump ? "Тап — прыжок" : "Тап — переворот";
            if (f.AirRepeats == AirRepeatLimit.None) return first + "; в воздухе дождись посадки";
            return first + (f.Law == MotionLaw.Linear ? "; повтор направляет к другой стене" : "; повтор меняет тягу");
        }
        string FlightRule()
        {
            var f = Game.Flight;
            if (f.AirRepeats == AirRepeatLimit.None) return "Воздушных повторов нет. Дождись устойчивой посадки на стену или камень.";
            if (f.Law == MotionLaw.Linear && f.FirstAction == FirstAction.Jump)
                return "Первый повтор отменяет возврат и направляет к другой стене. Дальше — разворот. Повторов: " + FlightSettingsStore.RepeatText(f.AirRepeats) + ".";
            return (f.Law == MotionLaw.Linear ? "Повтор сразу разворачивает скорость." : "Повтор меняет тягу, сохраняя скорость.")
                + " Повторов: " + FlightSettingsStore.RepeatText(f.AirRepeats) + ". Удержание не повторяет тапы.";
        }
        string LastFlightText(PlayerState player)
        {
            if (player.LastFlight == null) return "Полёт: пока нет измерения";
            var f = player.LastFlight;
            return f.Interrupted ? "Полёт прерван ударом — не чистое измерение"
                : "Последний полёт: " + Number(f.Duration, 2) + " с · удаление " + Number(f.MaxDistance, 1) + " ед";
        }
    }
}
