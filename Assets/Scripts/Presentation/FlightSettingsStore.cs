using System;
using System.Globalization;
using FlipRace.Core;

namespace FlipRace.Presentation
{
    // Pure data/codec: Unity only supplies the local string storage at the boundary.
    public sealed class FlightDraft
    {
        public static readonly double[] Gravities = { 662.5, 800, 1000, 1250, 1500, 1800, 2200, 2700 };
        public static readonly double[] FlipImpulses = { 0, 35, 70, 140, 210, 280, 350, 420 };
        public static readonly double[] JumpImpulses = { 200, 250, 300, 350, 400, 450, 500, 550 };
        public static readonly double[] Durations = { 1.2, 1, .9, .8, .7, .6, .5, .4 };
        public static readonly double[] Heights = { 48, 64, 80, 96, 112, 128, 160, 192 };
        public FirstAction First;
        public MotionLaw Law;
        public AirRepeatLimit Repeats;
        public int Gravity, Flip, Jump, Speed, Height, ResetPreset;

        public FlightDraft Copy() { return (FlightDraft)MemberwiseClone(); }
        public FlightSettings Settings()
        {
            return new FlightSettings(First, Law, Repeats, Gravities[Gravity], FlipImpulses[Flip],
                JumpImpulses[Jump], 212 / Durations[Speed], Heights[Height]);
        }
        public static FlightDraft From(FlightSettings settings, int resetPreset = 0)
        {
            int speed = -1;
            for (int i = 0; i < Durations.Length; i++) if (Math.Abs(212 / Durations[i] - settings.LinearSpeed) < 1e-8) speed = i;
            return new FlightDraft { First = settings.FirstAction, Law = settings.Law, Repeats = settings.AirRepeats,
                Gravity = Index(Gravities, settings.GravityAcceleration), Flip = Index(FlipImpulses, settings.FlipImpulse),
                Jump = Index(JumpImpulses, settings.JumpImpulse), Speed = RequireIndex(speed),
                Height = Index(Heights, settings.LinearJumpHeight), ResetPreset = resetPreset };
        }
        static int Index(double[] values, double value)
        { for (int i = 0; i < values.Length; i++) if (Math.Abs(values[i] - value) < 1e-8) return i; return RequireIndex(-1); }
        static int RequireIndex(int index)
        { if (index < 0) throw new ArgumentException("Value is not an exact flight scale entry"); return index; }
    }

    public static class FlightSettingsStore
    {
        public const string Key = "flip-race-custom-flight-v1";
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        public static FlightSettings Preset(int index)
        {
            switch (index)
            {
                case 0: return FlightPresets.Moderate;
                case 1: return FlightPresets.Linear;
                case 2: return FlightPresets.Jump;
                case 3: return FlightPresets.Unlimited;
                case 4: case 5: case 6: case 7:
                    return FlightPresets.FromLegacy(MotionMode.GravityAirFlip, MotionProfiles.All[index - 4]);
                case 8: return FlightPresets.FromLegacy(MotionMode.Linear);
                case 9: return FlightPresets.FromLegacy(MotionMode.Gravity);
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        public static string PresetName(int index)
        {
            string[] names = { "Меньше толчок", "Линейный", "Прыжок → флип", "Много флипов",
                "База", "Сильнее тяга", "Толчок", "Тяга + толчок", "Линейный без повтора", "Разгон без повтора" };
            return names[index];
        }
        // Store physical values, not ordinal meanings that a future scale could change.
        public static string Encode(FlightDraft draft)
        {
            var s = draft.Settings();
            return string.Join("|", new[] { "1", ((int)s.FirstAction).ToString(Invariant), ((int)s.Law).ToString(Invariant),
                ((int)s.AirRepeats).ToString(Invariant), s.GravityAcceleration.ToString("R", Invariant),
                s.FlipImpulse.ToString("R", Invariant), s.JumpImpulse.ToString("R", Invariant),
                s.LinearSpeed.ToString("R", Invariant), s.LinearJumpHeight.ToString("R", Invariant), draft.ResetPreset.ToString(Invariant) });
        }
        public static bool TryDecode(string text, out FlightDraft draft)
        {
            draft = FlightDraft.From(FlightPresets.Moderate);
            if (string.IsNullOrEmpty(text) || text.Length > 512) return false;
            try
            {
                string[] v = text.Split('|'); if (v.Length != 10 || v[0] != "1") return false;
                int first = int.Parse(v[1], Invariant), law = int.Parse(v[2], Invariant), repeats = int.Parse(v[3], Invariant);
                int reset = int.Parse(v[9], Invariant); if (reset < 0 || reset > 9) return false;
                var s = new FlightSettings((FirstAction)first, (MotionLaw)law, (AirRepeatLimit)repeats,
                    double.Parse(v[4], Invariant), double.Parse(v[5], Invariant), double.Parse(v[6], Invariant),
                    double.Parse(v[7], Invariant), double.Parse(v[8], Invariant));
                draft = FlightDraft.From(s, reset); return true;
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is OverflowException)
            { return false; }
        }
        public static string RepeatText(AirRepeatLimit repeat)
        { return repeat == AirRepeatLimit.Unlimited ? "∞" : repeat == AirRepeatLimit.One ? "1" : "0"; }
        static string N(double value) { return value.ToString("0.###", Invariant).Replace('.', ','); }
        public static string Summary(FlightSettings s)
        {
            return (s.FirstAction == FirstAction.Jump ? "Прыжок" : "Переворот") + " · "
                + (s.Law == MotionLaw.Linear ? "линейный " + N(212 / s.LinearSpeed) + " с"
                    : "g " + N(s.GravityAcceleration) + " · толчок " + N(s.FirstAction == FirstAction.Jump ? s.JumpImpulse : s.FlipImpulse))
                + " · повтор " + RepeatText(s.AirRepeats)
                + (s.Law == MotionLaw.Linear && s.FirstAction == FirstAction.Jump ? " · h " + N(s.LinearJumpHeight) : "");
        }
        public static string CopyText(FlightDraft draft)
        {
            var s = draft.Settings();
            return "Флип! — свои настройки\nПервый тап: " + (s.FirstAction == FirstAction.Jump ? "обычный прыжок" : "сразу переворот")
                + "\nДвижение: " + (s.Law == MotionLaw.Linear ? "постоянная скорость" : "разгон")
                + "\nВоздушные повторы: " + RepeatText(s.AirRepeats)
                + "\nТяга: уровень " + draft.Gravity + ", " + N(s.GravityAcceleration) + " ед/с²"
                + "\nТолчок переворота: уровень " + draft.Flip + ", " + N(s.FlipImpulse) + " ед/с"
                + "\nСила прыжка: уровень " + draft.Jump + ", " + N(s.JumpImpulse) + " ед/с"
                + "\nЛинейный перелёт: " + N(212 / s.LinearSpeed) + " с через 212 ед; скорость " + N(s.LinearSpeed) + " ед/с"
                + "\nВысота линейного прыжка: " + N(s.LinearJumpHeight) + " ед"
                + "\nНеактивные параметры сохранены, но не влияют на выбранное движение.\nНабор: " + Encode(draft);
        }
    }

    // Selecting a preset never overwrites the remembered custom set.
    public sealed class FlightSelection
    {
        public FlightDraft Custom { get; private set; }
        public int PresetIndex { get; private set; }
        public bool IsCustom { get { return PresetIndex < 0; } }
        public FlightSettings Settings { get { return IsCustom ? Custom.Settings() : FlightSettingsStore.Preset(PresetIndex); } }
        public FlightSelection(FlightDraft custom) { Custom = custom.Copy(); PresetIndex = 0; }
        public void SelectPreset(int index) { FlightSettingsStore.Preset(index); PresetIndex = index; }
        public void SelectCustom() { PresetIndex = -1; }
        public void Edit(Action<FlightDraft> edit)
        {
            var draft = IsCustom ? Custom.Copy() : FlightDraft.From(Settings, PresetIndex);
            edit(draft); draft.Settings(); Custom = draft; PresetIndex = -1;
        }
        public void ResetCustom()
        { Custom = FlightDraft.From(FlightSettingsStore.Preset(Custom.ResetPreset), Custom.ResetPreset); PresetIndex = -1; }
    }
}
