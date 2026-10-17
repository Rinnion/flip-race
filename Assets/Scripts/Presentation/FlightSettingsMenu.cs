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
        Rect flightPanel, flightBack, flightApply, flightCustom, flightLegacy, flightEdit, flightReset, flightDone, flightCopy;

        public bool FlightSettingsVisible { get { return showTracks && Trial && flightPage == 1 && !Portrait; } }
        public Rect FlightCopyRect { get { return flightCopy; } }
        public string FlightCopyText { get { return flightSelection == null ? "" : FlightSettingsStore.CopyText(flightSelection.Custom); } }
        public bool IsCustomFlight { get { return activeCustomFlight; } }

        void InitializeFlightMenu()
        {
            FlightDraft custom;
            FlightSettingsStore.TryDecode(PlayerPrefs.GetString(FlightSettingsStore.Key, ""), out custom);
            flightSelection = new FlightSelection(custom);
            int saved = PlayerPrefs.GetInt("flip-race-flight-choice-v1", 0);
            activeCustomFlight = saved == -1; activeFlightPreset = saved >= 0 && saved <= 9 ? saved : 0;
            if (activeCustomFlight) flightSelection.SelectCustom(); else flightSelection.SelectPreset(activeFlightPreset);
        }
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
            bool dense = flightPanel.height < 260;
            float lastPreset = flightPanel.y + 60 + (dense ? 62 : 76);
            flightCustom = new Rect(flightPanel.x + 16, lastPreset + 8, col, dense ? 24 : 28);
            flightLegacy = new Rect(flightCustom.x, flightCustom.yMax + 8, col, 24);
            flightEdit = new Rect(flightPanel.x + 32 + col, flightPanel.y + 106, col, 24);
        }
        Rect FlightPresetRect(int i)
        {
            float col = (flightPanel.width - 48) / 2, cell = (col - 8) / 2;
            float h = flightPanel.height < 260 ? 27 : 34;
            return new Rect(flightPanel.x + 16 + (i % 2) * (cell + 8), flightPanel.y + 60 + (i / 2) * (h + 8), cell, h);
        }
        Rect FlightSegmentRect(int i)
        {
            float col = (flightPanel.width - 48) / 2, x = flightPanel.x + 32 + col;
            if (flightPage == 2) return new Rect(x, flightPanel.y + 121 + i * 30, col, 26);
            return new Rect(x + i * (col + 6) / 3, flightPanel.y + 151, (col - 12) / 3, 27);
        }
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
            for (int i = 0; i < (flightPage == 2 ? 6 : 4); i++)
                if (FlightPresetRect(i).Contains(p)) { flightSelection.SelectPreset(i + (flightPage == 2 ? 4 : 0)); return; }
            if (flightPage == 0)
            {
                if (flightCustom.Contains(p)) { flightSelection.SelectCustom(); flightPage = 1; BlockGameplayInput(); return; }
                if (flightLegacy.Contains(p)) { flightPage = 2; BlockGameplayInput(); return; }
                if (flightEdit.Contains(p))
                { flightSelection.Edit(d => { }); SaveCustomFlight(); flightPage = 1; BlockGameplayInput(); return; }
            }
            for (int i = 0; i < 3; i++) if (FlightSegmentRect(i).Contains(p))
            {
                string id = flightPage == 2 ? (i == 0 ? TracksV4.OpenId : i == 1 ? TracksTune.ChallengeId : TracksV4.ChallengeId)
                    : i == 0 ? TracksV4.OpenId : i == 1 ? TracksControl.ChallengeId : TracksControl.UnlimitedId;
                if (id == TracksControl.UnlimitedId && flightSelection.Settings.AirRepeats != AirRepeatLimit.Unlimited) return;
                pendingFlightTrack = id; return;
            }
        }
        void ApplyFlightChoice()
        {
            if (Active || !showTracks) return;
            var settings = flightSelection.Settings;
            Track track = pendingFlightTrack == TracksControl.ChallengeId ? TracksControl.Challenge(MotionMode.GravityAirFlip)
                : pendingFlightTrack == TracksControl.UnlimitedId ? TracksControl.Unlimited()
                : pendingFlightTrack == TracksTune.ChallengeId ? TracksTune.Challenge(MotionProfiles.Baseline)
                : TracksV4.Get(pendingFlightTrack);
            int first = Game.Players[0].CharacterIndex, second = Game.Players[1].CharacterIndex;
            Game = RaceSimulation.CreateTrial(track, settings);
            Game.Players[0].CharacterIndex = first; Game.Players[1].CharacterIndex = second;
            activeCustomFlight = flightSelection.IsCustom; activeFlightPreset = flightSelection.PresetIndex;
            legacyControls = !activeCustomFlight && activeFlightPreset >= 4;
            PlayerPrefs.SetInt("flip-race-flight-choice-v1", activeCustomFlight ? -1 : activeFlightPreset); PlayerPrefs.Save();
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
                flightPage == 1 ? "Свои настройки" : flightPage == 2 ? "Прежние настройки" : "Управление и участок", 17, ink, true);
            if (flightPage == 1) { DrawFlightEditor(); return; }
            float col = (flightPanel.width - 48) / 2, right = flightPanel.x + 32 + col;
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 43, col, 14), "ГОТОВЫЕ НАСТРОЙКИ", 8, muted);
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
            Text(new Rect(right, flightPanel.y + (flightPage == 2 ? 105 : 134), col, 14), "УЧАСТОК", 8, muted);
            string[] ids = flightPage == 2 ? new[] { TracksV4.OpenId, TracksTune.ChallengeId, TracksV4.ChallengeId }
                : new[] { TracksV4.OpenId, TracksControl.ChallengeId, TracksControl.UnlimitedId };
            string[] names = flightPage == 2 ? new[] { "Свободный полёт", "Кольца и ускорители", "Прежний участок" }
                : new[] { "Свободно", "Посадки", "Проходы" };
            for (int i = 0; i < 3; i++) FlightButton(FlightSegmentRect(i), names[i], pendingFlightTrack == ids[i],
                ids[i] != TracksControl.UnlimitedId || flightSelection.Settings.AirRepeats == AirRepeatLimit.Unlimited, 9);
            if (flightPage == 0)
            {
                string note = !CanApplyFlight ? "Проходы требуют повторов ∞. Выбери другой участок или настройку."
                    : pendingFlightTrack == TracksTune.ChallengeId ? "Выбрано: кольца и ускорители"
                    : pendingFlightTrack == TracksV4.ChallengeId ? "Выбран прежний участок"
                    : "Новый набор сначала пробуй свободно.";
                label.wordWrap = true;
                Text(new Rect(right, flightPanel.y + 184, col, 29), note, 9, !CanApplyFlight ? Hex(0x9a6844) : muted);
                label.wordWrap = false;
            }
            Text(new Rect(flightPanel.x + 16, flightPanel.yMax - 39, flightPanel.width - 205, 27), "Для обоих · старт после готовности", 9, muted, false, TextAnchor.MiddleLeft);
            FlightButton(flightApply, "К готовности →", true, CanApplyFlight, 11);
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
