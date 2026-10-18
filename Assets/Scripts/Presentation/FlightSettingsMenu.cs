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
        string pendingFlightTrack;
        Rect flightPanel, flightBack, flightApply, flightCustom, flightLegacy, flightEdit, flightReset, flightDone, flightCopy, flightChosen, flightOldTracks;

        public bool FlightSettingsVisible { get { return showTracks && Trial && flightPage == 1 && !Portrait; } }
        public Rect FlightCopyRect { get { return flightCopy; } }
        public string FlightCopyText { get { return flightSelection == null ? "" : FlightSettingsStore.CopyText(flightSelection.Custom); } }
        public bool IsCustomFlight { get { return activeCustomFlight; } }

        void InitializeFlightMenu()
        {
            FlightDraft custom;
            FlightSettingsStore.TryDecode(PlayerPrefs.GetString(FlightSettingsStore.Key, ""), out custom);
            flightSelection = new FlightSelection(custom);
            int saved = FlightSettingsStore.ValidActiveChoice(PlayerPrefs.GetInt(FlightSettingsStore.ActiveChoiceKey, FlightSettingsStore.ChosenPresetIndex));
            activeCustomFlight = saved == -1; activeFlightPreset = saved;
            if (activeCustomFlight) flightSelection.SelectCustom(); else flightSelection.SelectPreset(activeFlightPreset);
        }
        static Track InitialFlightTrack()
        { return TracksFull.All.Length > 0 ? TracksFull.All[0] : TracksFull.Practice(); }
        void SaveCustomFlight()
        { PlayerPrefs.SetString(FlightSettingsStore.Key, FlightSettingsStore.Encode(flightSelection.Custom)); PlayerPrefs.Save(); }
        void BeginFlightMenu()
        {
            if (flightSelection == null) InitializeFlightMenu();
            if (activeCustomFlight) flightSelection.SelectCustom(); else flightSelection.SelectPreset(activeFlightPreset);
            pendingFlightTrack = Game.Track.Id; flightPage = 0;
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
            flightChosen = new Rect(flightPanel.x + 16, flightPanel.y + 60, col, 28);
            flightCustom = new Rect(flightPanel.x + 16, flightPanel.y + 172, col, 28);
            flightLegacy = new Rect(flightCustom.x, flightPanel.y + 208, col, 24);
            flightEdit = new Rect(flightPanel.x + 32 + col, flightPanel.y + 106, col, 24);
            flightOldTracks = new Rect(flightPanel.xMax - 126, flightPanel.y + 131, 110, 20);
        }
        Rect FlightPresetRect(int i)
        {
            float col = (flightPanel.width - 48) / 2, cell = (col - 8) / 2;
            float start = flightPage == 2 ? 60 : 96, h = flightPage == 2 ? 34 : 30;
            return new Rect(flightPanel.x + 16 + (i % 2) * (cell + 8), flightPanel.y + start + (i / 2) * (h + 8), cell, h);
        }
        Rect FlightSegmentRect(int i)
        {
            float col = (flightPanel.width - 48) / 2, x = flightPanel.x + 32 + col;
            return new Rect(x, flightPanel.y + 121 + i * 30, col, 26);
        }
        Rect FullTrackRect(int i)
        {
            float col = (flightPanel.width - 48) / 2;
            return new Rect(flightPanel.x + 32 + col, flightPanel.y + 154 + i * 28, col, 24);
        }
        Rect OldTrackRect(int i)
        {
            float col = (flightPanel.width - 48) / 2;
            return new Rect(flightPanel.x + 16 + i % 2 * (col + 16), flightPanel.y + 94 + i / 2 * 38, col, 31);
        }
        static readonly string[] OldTrackIds = { TracksV4.OpenId, TracksControl.ChallengeId, TracksControl.UnlimitedId,
            TracksTune.ChallengeId, TracksV4.ChallengeId, TracksFull.PracticeId };
        static readonly string[] OldTrackNames = { "Свободный полёт", "Точные посадки", "Проходы по высоте",
            "Кольца и ускорители", "Прежний участок", "Проба посадки и цепочки" };
        Rect FlightFieldRect(int column, int row)
        {
            float col = (flightPanel.width - 48) / 2, rh = (flightPanel.height - 98) / 4;
            return new Rect(flightPanel.x + 16 + column * (col + 16), flightPanel.y + 49 + row * rh, col, rh - 3);
        }
        Rect FlightCell(Rect field, int index, int count)
        { float cell = (field.width - (count - 1) * 3) / count; return new Rect(field.x + index * (cell + 3), field.y + 14, cell, field.height - 15); }
        bool CanApplyFlight { get { return pendingFlightTrack != TracksControl.UnlimitedId || flightSelection.Settings.AirRepeats == AirRepeatLimit.Unlimited; } }

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
            if (flightPage == 3)
            {
                for (int i = 0; i < OldTrackIds.Length; i++) if (OldTrackRect(i).Contains(p))
                {
                    if (OldTrackIds[i] == TracksControl.UnlimitedId && flightSelection.Settings.AirRepeats != AirRepeatLimit.Unlimited) return;
                    pendingFlightTrack = OldTrackIds[i]; return;
                }
                return;
            }
            for (int i = 0; i < (flightPage == 2 ? 6 : 4); i++)
                if (FlightPresetRect(i).Contains(p)) { flightSelection.SelectPreset(i + (flightPage == 2 ? 4 : 0)); return; }
            if (flightPage == 0)
            {
                if (flightChosen.Contains(p)) { flightSelection.SelectPreset(FlightSettingsStore.ChosenPresetIndex); return; }
                if (flightCustom.Contains(p)) { flightSelection.SelectCustom(); flightPage = 1; BlockGameplayInput(); return; }
                if (flightLegacy.Contains(p)) { flightPage = 2; BlockGameplayInput(); return; }
                if (flightOldTracks.Contains(p)) { flightPage = 3; BlockGameplayInput(); return; }
                if (flightEdit.Contains(p))
                { flightSelection.Edit(d => { }); SaveCustomFlight(); flightPage = 1; BlockGameplayInput(); return; }
                for (int i = 0; i < TracksFull.All.Length; i++) if (FullTrackRect(i).Contains(p))
                { pendingFlightTrack = TracksFull.All[i].Id; return; }
                return;
            }
            for (int i = 0; i < 3; i++) if (FlightSegmentRect(i).Contains(p))
            { pendingFlightTrack = i == 0 ? TracksV4.OpenId : i == 1 ? TracksTune.ChallengeId : TracksV4.ChallengeId; return; }
        }
        void ApplyFlightChoice()
        {
            if (Active || !showTracks) return;
            var settings = flightSelection.Settings;
            Track track = TracksFull.IsFull(pendingFlightTrack) || pendingFlightTrack == TracksFull.PracticeId ? TracksFull.Get(pendingFlightTrack)
                : pendingFlightTrack == TracksControl.ChallengeId ? TracksControl.Challenge(MotionMode.GravityAirFlip)
                : pendingFlightTrack == TracksControl.UnlimitedId ? TracksControl.Unlimited()
                : pendingFlightTrack == TracksTune.ChallengeId ? TracksTune.Challenge(MotionProfiles.Baseline)
                : TracksV4.Get(pendingFlightTrack);
            int first = Game.Players[0].CharacterIndex, second = Game.Players[1].CharacterIndex;
            Game = RaceSimulation.CreateTrial(track, settings);
            Game.Players[0].CharacterIndex = first; Game.Players[1].CharacterIndex = second;
            activeCustomFlight = flightSelection.IsCustom; activeFlightPreset = flightSelection.PresetIndex;
            legacyControls = !activeCustomFlight && activeFlightPreset >= 4 && activeFlightPreset <= 9;
            PlayerPrefs.SetInt(FlightSettingsStore.ActiveChoiceKey, activeCustomFlight ? -1 : activeFlightPreset); PlayerPrefs.Save();
            selectedTrackId = track.Id;
            round = 1; paused = showRules = showTracks = false; suppressFrame = true;
            lastFrame = Time.realtimeSinceStartupAsDouble; BlockGameplayInput();
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
                flightPage == 1 ? "Свои настройки" : flightPage == 2 ? "Прежние настройки"
                    : flightPage == 3 ? "Прежние участки" : "Управление и трасса", 17, ink, true);
            if (flightPage == 1) { DrawFlightEditor(); return; }
            if (flightPage == 3) { DrawOldFlightTracks(); return; }
            float col = (flightPanel.width - 48) / 2, right = flightPanel.x + 32 + col;
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 43, col, 14), "ГОТОВЫЕ НАСТРОЙКИ", 8, muted);
            if (flightPage == 0)
                FlightButton(flightChosen, "Профиль трасс · 350 / 662,5 / ∞", flightSelection.PresetIndex == FlightSettingsStore.ChosenPresetIndex, true, 9);
            for (int i = 0; i < (flightPage == 2 ? 6 : 4); i++)
            {
                int index = i + (flightPage == 2 ? 4 : 0);
                FlightButton(FlightPresetRect(i), FlightSettingsStore.PresetName(index), flightSelection.PresetIndex == index,
                    true, index >= 8 ? 8 : 9);
            }
            if (flightPage == 0)
            {
                FlightButton(flightCustom, "Свои настройки  ›", flightSelection.IsCustom);
                FlightButton(flightLegacy, "Прежние 6 вариантов  ›", false, true, 9);
                FlightButton(flightEdit, flightSelection.PresetIndex == 8 ? "Взять значения в свои  ›" : "Изменить выбранное  ›", false, true, 9);
            }
            Text(new Rect(right, flightPanel.y + 43, col, 15), flightSelection.IsCustom ? "Свой набор" : FlightSettingsStore.PresetName(flightSelection.PresetIndex), 11, ink, true);
            label.wordWrap = true;
            Text(new Rect(right, flightPanel.y + 63, col, 39), FlightSettingsStore.Summary(flightSelection.Settings), 10, muted);
            label.wordWrap = false;
            if (flightPage == 0)
            {
                Text(new Rect(right, flightPanel.y + 134, col - 112, 14), "ТРИ ТРАССЫ", 8, muted);
                FlightButton(flightOldTracks, "Прежние участки ›", false, true, 8);
                for (int i = 0; i < TracksFull.All.Length; i++)
                    FlightButton(FullTrackRect(i), TracksFull.All[i].Name, pendingFlightTrack == TracksFull.All[i].Id, true, 10);
            }
            else
            {
                Text(new Rect(right, flightPanel.y + 105, col, 14), "ПРЕЖНИЕ УЧАСТКИ", 8, muted);
                string[] ids = { TracksV4.OpenId, TracksTune.ChallengeId, TracksV4.ChallengeId };
                string[] names = { "Свободный полёт", "Кольца и ускорители", "Прежний участок" };
                for (int i = 0; i < 3; i++) FlightButton(FlightSegmentRect(i), names[i], pendingFlightTrack == ids[i], true, 9);
            }
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
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 211, flightPanel.width - 32, 16),
                "Выбор участка сохраняет управление. Новый набор сначала пробуй свободно.", 9, muted);
            DrawFlightApplyFooter();
        }
        void DrawFlightApplyFooter()
        {
            string note = !CanApplyFlight ? "Проходы требуют повторов ∞"
                : TracksFull.IsFull(pendingFlightTrack) ? "36 сек · для профиля 350 / 662,5 / ∞"
                : "Участок: " + PendingFlightTrackName();
            Text(new Rect(flightPanel.x + 16, flightPanel.yMax - 39, flightPanel.width - 205, 27), note,
                9, !CanApplyFlight ? Hex(0x9a6844) : muted, false, TextAnchor.MiddleLeft);
            FlightButton(flightApply, "К готовности →", true, CanApplyFlight, 11);
        }
        string PendingFlightTrackName()
        {
            for (int i = 0; i < OldTrackIds.Length; i++) if (OldTrackIds[i] == pendingFlightTrack) return OldTrackNames[i];
            foreach (var track in TracksFull.All) if (track.Id == pendingFlightTrack) return track.Name;
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
            Text(note, d.ResetPreset == 8 ? "Взяты значения старого линейного. Свой перелёт считает фактическую дистанцию."
                : "0 — база, не отсутствие тяги. Неактивные значения запоминаются. Меняй один параметр за раз.", 9, muted);
            label.wordWrap = false;
            FlightButton(flightCopy, "Копировать", false, true, 10);
            FlightButton(flightReset, d.ResetPreset == 8 ? "Взять значения" : "Сброс: " + FlightSettingsStore.PresetName(d.ResetPreset), false, true, 8);
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
