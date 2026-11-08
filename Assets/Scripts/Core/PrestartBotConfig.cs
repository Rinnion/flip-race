using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlipRace.Core
{
    public sealed class PrestartBotConfig
    {
        const string AlgorithmVersion = "prestart-v1";
        readonly BotDifficulty?[] slots;
        public TrackPackage Package { get; }
        public FlightSettings Flight { get; }
        public PartyCamera Camera { get; }
        public bool SurfaceRunEnabled { get; }
        public SurfaceRunSettings SurfaceRunSettings { get; }
        public double WarningDuration { get; }
        public bool StandaloneBot { get; }
        public double CameraStartLeft { get; }
        public double WindowWidth { get; }
        public string CacheKey { get; }
        public int SlotCount => slots.Length;
        public int HumanCount { get; }

        public PrestartBotConfig(TrackPackage package, FlightSettings flight, IReadOnlyList<BotDifficulty?> slots,
            PartyCamera camera, bool surfaceRunEnabled, SurfaceRunSettings surfaceRunSettings, double warningDuration,
            bool standaloneBot = false, double cameraStartLeft = -288.8, double windowWidth = 1520)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
            if ((flight != FlightPresets.Chosen && flight != FlightPresets.FastFlip) || !package.SupportsProfile(flight))
                throw new NotSupportedException("Prestart bots require a compatible ready-made flight profile.");
            if (slots == null || slots.Count < 2 || slots.Count > 4) throw new ArgumentOutOfRangeException(nameof(slots));
            if (standaloneBot && (slots.Count != 2 || slots[0].HasValue || !slots[1].HasValue))
                throw new ArgumentException("Standalone bot races require a human followed by a bot.", nameof(slots));
            if (camera != PartyCamera.Constant && camera != PartyCamera.Lagger) throw new ArgumentOutOfRangeException(nameof(camera));
            if (!Finite(cameraStartLeft) || !Finite(windowWidth) || windowWidth <= 0) throw new ArgumentOutOfRangeException(nameof(windowWidth));
            if (warningDuration != .4 && warningDuration != .6 && warningDuration != .8 && warningDuration != 1 && warningDuration != 1.2)
                throw new ArgumentOutOfRangeException(nameof(warningDuration));
            if (surfaceRunSettings.RatePercent == 0) throw new ArgumentOutOfRangeException(nameof(surfaceRunSettings));
            Flight = flight;
            Camera = camera;
            SurfaceRunEnabled = surfaceRunEnabled;
            SurfaceRunSettings = surfaceRunSettings;
            WarningDuration = warningDuration;
            StandaloneBot = standaloneBot;
            CameraStartLeft = cameraStartLeft;
            WindowWidth = windowWidth;
            this.slots = new BotDifficulty?[slots.Count];
            var roles = new StringBuilder(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                var difficulty = slots[i];
                this.slots[i] = difficulty;
                if (difficulty.HasValue)
                {
                    PrestartBotIntent.Quality(difficulty.Value);
                    roles.Append((char)('0' + (int)difficulty.Value));
                }
                else { HumanCount++; roles.Append('h'); }
            }
            if (HumanCount == slots.Count) throw new ArgumentException("A prestart race needs at least one bot.", nameof(slots));
            if (!GeometryMatches()) throw new ArgumentException("Track content changed after validation.", nameof(package));
            CacheKey = string.Join("/", AlgorithmVersion, Package.ContentHash, Flight == FlightPresets.Chosen ? "chosen" : "fast",
                ((int)camera).ToString(CultureInfo.InvariantCulture), standaloneBot ? "bot" : "party", roles.ToString(),
                Number(cameraStartLeft), Number(windowWidth), Number(warningDuration), surfaceRunEnabled ? "on" : "off",
                surfaceRunSettings.RatePercent.ToString(CultureInfo.InvariantCulture), ((int)surfaceRunSettings.Curve).ToString(CultureInfo.InvariantCulture),
                surfaceRunSettings.CapPercent.ToString(CultureInfo.InvariantCulture), surfaceRunSettings.AirDecayPercent.ToString(CultureInfo.InvariantCulture),
                surfaceRunSettings.ResetOnCollision ? "reset" : "keep");
        }

        public BotDifficulty? GetDifficulty(int slot) => slot >= 0 && slot < slots.Length ? slots[slot] : null;

        internal bool GeometryMatches() => TrackFingerprint.Compute(Package.Track, Package.ProfilePolicy, Package.WarningMode) == Package.ContentHash;

        internal GameState BeginReplay(bool personal)
        {
            var track = Package.Track;
            var game = personal ? RaceSimulation.CreateSolo(track, Flight, PartyCamera.Lagger, CameraStartLeft, WindowWidth)
                : RaceSimulation.CreateParty(track, Flight, SlotCount, Camera, CameraStartLeft, WindowWidth);
            ApplySettings(game);
            for (int slot = 0; slot < game.Players.Length; slot++) RaceSimulation.Press(game, slot);
            RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
            return game;
        }

        internal void ApplySettings(GameState game)
        {
            RaceSimulation.ConfigureSurfaceRun(game, SurfaceRunEnabled, SurfaceRunSettings);
            HiddenObstacles.ConfigureWarning(game, WarningDuration);
        }

        internal bool Matches(GameState game) => ReferenceEquals(game.Track, Package.Track) && ReferenceEquals(game.Flight, Flight)
            && game.PartyCamera == Camera && game.PartyCameraStartLeft == CameraStartLeft && game.PartyWindowWidth == WindowWidth
            && game.Players.Length == SlotCount && game.SurfaceRunEnabled == SurfaceRunEnabled
            && game.SurfaceRunSettings.Equals(SurfaceRunSettings) && game.HiddenWarningDuration == WarningDuration;

        internal static uint SlotSeed(uint seed, int slot) => StochasticBotController.Mix(unchecked(seed + ((uint)slot + 1) * 0x9e3779b9u));
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
