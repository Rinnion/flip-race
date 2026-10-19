using System;
using System.Collections.Generic;

// Pure deterministic rules. No Unity API, floats, physics engine, or frame clock.
// Original analytic collisions are preserved. Swept rings enqueue complete,
// piecewise-constant speed effects. Distances and times remain doubles.
namespace FlipRace.Core
{
    public enum MotionMode { Linear, Gravity, GravityAirFlip, LinearAirFlip, JumpGravityFlip, GravityUnlimited }
    public enum SupportKind { None, Wall, Stone }

    public static class RaceConfig
    {
        public const double Speed = 220;
        public const double FlipDuration = .8;
        public const double RecoveryDuration = .5;
        public const double PlayerRadius = 14;
        public const double CorridorHeight = 240;
        public const double CountdownDuration = 3;
        public const double FinishGrace = 3.5;
        public const double BoostMultiplier = 1.5;
        public const double BoostDuration = .6;
        public const double BoostTimeGain = .3;
        public const double SlowMultiplier = .5;
        public const double SlowDuration = .6;
        public const double SlowTimeLoss = .3;
        public const double GravityAcceleration = 662.5;
        public const double LinearVerticalSpeed = 265;
        public const double FinishBoostSpeed = 440;
        public const double FinishBoostDistance = 44;
    }

    public sealed class Obstacle
    {
        public string Id;
        public double X, Width, Height, GapTop, GapBottom;
        public string Kind;
    }

    public sealed class Cue
    {
        public double X;
        public string Text;
        public Cue(double x, string text) { X = x; Text = text; }
    }

    public sealed class Track
    {
        public string Id = "custom", Name = "Тестовая тропа", Focus = "";
        public double Length;
        public Obstacle[] Obstacles;
        public Cue[] Cues;
        public RiskZone[] RiskZones = Array.Empty<RiskZone>();
        public double[] SafeRoute = Array.Empty<double>();
        public double[] RiskRoute = Array.Empty<double>();
        public bool NegativeEnabled;
        public int PositiveRingCount
        {
            get { int count = 0; foreach (var zone in RiskZones) if (!zone.IsNegative) count++; return count; }
        }

        public Track(double length, Obstacle[] obstacles, Cue[] cues = null)
        {
            Length = length;
            Obstacles = obstacles;
            Cues = cues ?? Array.Empty<Cue>();
        }
    }

    public sealed class RiskZone
    {
        public string Id, ObstacleId;
        public string Kind = "positive", Placement = "before";
        public bool IsNegative { get { return Kind == "negative"; } }
        public bool IsInstant { get { return Kind == "instant" || IsLanding; } }
        public bool IsLanding { get { return Kind == "landing"; } }
        public bool IsFinish { get { return Kind == "finish"; } }
        public bool AfterObstacle { get { return Placement == "after"; } }
        public bool FreeReward;
        // Ring center and radius use world units, just like obstacle geometry.
        public double X, Y, Radius = 6, ClearX, SafeFlipX, RiskFlipX;
        // Landing uses a narrow center-X interval on the linked stone's actual
        // corridor-facing plane. Y and Radius do not define its contact shape.
        public double LandingWidth;
        // Finish contact is a center-X plane and a full center-Y span. Radius
        // does not expand this target, unlike ordinary circular rings.
        public double FinishHeight = 16;
    }

    public sealed class SpeedEffect
    {
        public string RingId;
        public bool IsNegative;
        public double Remaining, Elapsed;
        public int PulseCount = 1, CompletedPulses;
        public double PulseDuration { get { return IsNegative ? RaceConfig.SlowDuration : RaceConfig.BoostDuration; } }
        public double Duration { get { return PulseCount * PulseDuration; } }
        public double Multiplier { get { return IsNegative ? RaceConfig.SlowMultiplier : RaceConfig.BoostMultiplier; } }
    }

    public sealed class HitInfo
    {
        public string Id;
        public string Message;
        public double At;
    }

    public sealed class PlayerState
    {
        public int Id;
        public int CharacterIndex;
        public bool Ready;
        public double Distance;
        public double Y = 1;
        public bool Flipping;
        public double FlipFrom = 1;
        public double FlipTo;
        public double FlipElapsed;
        public double VerticalSpeed;
        public int Gravity = 1;
        public bool AirFlipAvailable;
        public SupportKind Support = SupportKind.Wall;
        public int SupportSide = 1;
        public string SupportObstacleId;
        // A legal walk-off clears the old face's expanded trailing slab. This
        // is local contact continuity, never an entry in HitObstacleIds.
        public string EdgeClearObstacleId;
        public bool LinearJumpActive, LinearJumpReturning;
        public int LinearJumpOriginSide;
        public double LinearJumpOriginY;
        public bool FlightActive;
        public double FlightOriginY, FlightDuration, FlightMaxDistance;
        public int FlightOriginSide;
        public string FlightOriginObstacleId;
        public FlightMeasurement LastFlight;
        public double Recovering;
        public int Collisions;
        public double? FinishTime;
        public HashSet<string> HitObstacleIds = new HashSet<string>();
        public HitInfo LastHit;
        public SpeedEffect ActiveEffect;
        // Ordinary rewards keep FIFO order. An instant bonus preempts a slow,
        // retaining that same partially served slow at the front of this queue.
        public Queue<SpeedEffect> PendingEffects = new Queue<SpeedEffect>();
        public double BoostRemaining { get { return ActiveEffect != null && !ActiveEffect.IsNegative ? ActiveEffect.Remaining : 0; } }
        public double SlowRemaining { get { return ActiveEffect != null && ActiveEffect.IsNegative ? ActiveEffect.Remaining : 0; } }
        public int BoostsEarned;
        public int InstantBoostsEarned;
        public int SlowsEarned;
        public HashSet<string> CollectedRiskIds = new HashSet<string>();
        public HashSet<string> SuccessfulRiskIds = new HashSet<string>();
        public HashSet<string> FailedRiskIds = new HashSet<string>();
        public HitInfo LastSuccess;
        public HitInfo LastInstant;
        public HitInfo LastSlow;
        public bool FinishBoost;
        public int FinishBoostsEarned;
        public double FinishBoostElapsed, FinishBoostDistanceGained;
        public HitInfo LastFinishBoost;
        public HashSet<string> NegativeRiskIds = new HashSet<string>();
        public double CurrentSpeed { get { return FinishBoost ? RaceConfig.FinishBoostSpeed
            : RaceConfig.Speed * (ActiveEffect != null ? ActiveEffect.Multiplier : 1); } }
        // Actual served motion/stops, used to stabilize analytically identical
        // finishes. Awarded but unserved boost never changes FinishTime.
        public int CompletedBoosts;
        public int CompletedSlows;
        public double BoostElapsed { get { return ServedPartial(false); } }
        public double SlowElapsed { get { return ServedPartial(true); } }
        public int CompletedRecoveries;
        public double RecoveryElapsed;
        public double BoostDistanceGained { get { return CompletedBoosts * RaceConfig.Speed * RaceConfig.BoostTimeGain
            + BoostElapsed * RaceConfig.Speed * (RaceConfig.BoostMultiplier - 1); } }
        public double SlowDistanceLost { get { return CompletedSlows * RaceConfig.Speed * RaceConfig.SlowTimeLoss
            + SlowElapsed * RaceConfig.Speed * (1 - RaceConfig.SlowMultiplier); } }

        private double ServedPartial(bool negative)
        {
            // A suspended slow has already changed actual distance. Include its
            // served part until completion replaces it with one whole credit.
            double total = Uncredited(ActiveEffect, negative);
            foreach (var effect in PendingEffects) total += Uncredited(effect, negative);
            return total;
        }
        private static double Uncredited(SpeedEffect effect, bool negative)
        {
            return effect != null && effect.IsNegative == negative
                ? Math.Max(0, effect.Elapsed - effect.CompletedPulses * effect.PulseDuration) : 0;
        }

        public PlayerState(int id) { Id = id; CharacterIndex = id; }
    }

    public sealed class GameState
    {
        public string Mode;
        public MotionMode Motion = MotionMode.Linear;
        public MotionProfile Profile = MotionProfiles.Baseline;
        public FlightSettings Flight;
        public Track Track;
        public string Phase = "ready";
        public PlayerState[] Players = { new PlayerState(0), new PlayerState(1) };
        public double Elapsed;
        public double CountdownRemaining = RaceConfig.CountdownDuration;
        // null before a finish, boxed int 0/1 for a winner, or "tie".
        public object Winner;
        public double? WinnerAt;
    }

    public static class Tracks
    {
        private static Obstacle Stone(string id, double x, string kind)
        {
            return new Obstacle { Id = id, X = x, Width = 64, Kind = kind, Height = 68 };
        }

        private static Obstacle Gate(string id, double x)
        {
            return new Obstacle { Id = id, X = x, Width = 12, Kind = "gate", GapTop = 48, GapBottom = 192 };
        }

        public static readonly Track LegacyRace = new Track(7920, new[]
        {
            Stone("first-rock", 550, "floor"),
            Stone("patience-a", 1130, "ceiling"),
            Stone("patience-b", 1490, "ceiling"),
            Stone("switch-a", 2190, "floor"),
            Stone("switch-b", 2650, "ceiling"),
            Gate("first-gate", 3400),
            Stone("patience-again-a", 4100, "floor"),
            Stone("patience-again-b", 4460, "floor"),
            Stone("switch-again-a", 5100, "ceiling"),
            Stone("switch-again-b", 5560, "floor"),
            Gate("last-gate", 6200),
            Stone("last-ceiling", 6800, "ceiling"),
            Stone("last-floor", 7260, "floor")
        }, new[]
        {
            new Cue(300, "Нажми — перелетай"),
            new Cue(980, "Два подряд: задержись"),
            new Cue(2020, "Вверх, затем вниз"),
            new Cue(3140, "Ворота — через середину"),
            new Cue(7440, "Финиш близко!")
        });

        public static readonly Track LegacyPractice = new Track(3300, new[]
        {
            Stone("practice-rock", 600, "floor"),
            Stone("practice-pair-a", 1250, "ceiling"),
            Stone("practice-pair-b", 1610, "ceiling"),
            Stone("practice-switch", 2200, "floor"),
            Gate("practice-gate", 2800)
        }, new[]
        {
            new Cue(250, "Коснись своей половины"),
            new Cue(1020, "Два камня — подожди внизу"),
            new Cue(1950, "Теперь наверх"),
            new Cue(2520, "Пролети через окно")
        });

        public static readonly Track RiskPractice = new Track(2400, new[]
        {
            Stone("practice-risk-rock", 600, "floor"), Stone("practice-after-risk", 1500, "ceiling")
        })
        {
            Id = "risk-practice", Name = "Проба риска", Focus = "Рано — безопасно. Точнее — быстрее.",
            RiskZones = new[] { Risk("practice-risk", "practice-risk-rock", 600, "floor") },
            SafeRoute = new[] { 420d, 1320 }, RiskRoute = new[] { 516d, 1320 }
        };

        public static readonly Track Hold = new Track(7920, new[]
        {
            Stone("hold-opening-a", 550, "floor"), Stone("hold-opening-b", 900, "floor"),
            Stone("hold-switch", 1250, "ceiling"), Stone("hold-risk-a", 1700, "floor"),
            Stone("hold-pair-a", 2350, "ceiling"), Stone("hold-pair-b", 2700, "ceiling"),
            Stone("hold-middle-switch", 3150, "floor"), Stone("hold-risk-b", 3900, "ceiling"),
            Stone("hold-late-pair-a", 4550, "floor"), Stone("hold-late-pair-b", 4900, "floor"),
            Stone("hold-final-pair-a", 5450, "ceiling"), Stone("hold-final-pair-b", 5800, "ceiling"),
            Stone("hold-risk-c", 6500, "floor"), Stone("hold-last-ceiling", 7150, "ceiling"),
            Stone("hold-last-floor", 7600, "floor")
        })
        {
            Id = "hold", Name = "Лесная выдержка", Focus = "Два камня подряд? Побудь на свободной стороне.",
            RiskZones = new[] { Risk("hold-ring-a", "hold-risk-a", 1700, "floor"),
                Risk("hold-ring-b", "hold-risk-b", 3900, "ceiling"), Risk("hold-ring-c", "hold-risk-c", 6500, "floor") },
            SafeRoute = new[] { 370d, 1070, 1520, 2170, 2970, 3720, 4370, 5270, 6320, 6970, 7420 },
            RiskRoute = new[] { 370d, 1070, 1616, 2170, 2970, 3816, 4370, 5270, 6416, 6970, 7420 }
        };

        public static readonly Track Switch = new Track(7920, new[]
        {
            Stone("switch-opening", 550, "floor"), Stone("switch-up", 1000, "ceiling"),
            Stone("switch-risk-a", 1450, "floor"), Stone("switch-a", 2150, "ceiling"),
            Stone("switch-b", 2600, "floor"), Stone("switch-c", 3050, "ceiling"),
            Stone("switch-risk-b", 3600, "floor"), Stone("switch-d", 4300, "ceiling"),
            Stone("switch-e", 4750, "floor"), Stone("switch-f", 5200, "ceiling"),
            Stone("switch-g", 5650, "floor"), Stone("switch-risk-c", 6200, "ceiling"),
            Stone("switch-last-floor", 6850, "floor"), Stone("switch-last-ceiling", 7300, "ceiling")
        })
        {
            Id = "switch", Name = "Череда поворотов", Focus = "Смотри вперёд: пол, потолок и снова пол.",
            RiskZones = new[] { Risk("switch-ring-a", "switch-risk-a", 1450, "floor"),
                Risk("switch-ring-b", "switch-risk-b", 3600, "floor"), Risk("switch-ring-c", "switch-risk-c", 6200, "ceiling") },
            SafeRoute = new[] { 370d, 820, 1270, 1970, 2420, 2870, 3420, 4120, 4570, 5020, 5470, 6020, 6670, 7120 },
            RiskRoute = new[] { 370d, 820, 1366, 1970, 2420, 2870, 3516, 4120, 4570, 5020, 5470, 6116, 6670, 7120 }
        };

        public static readonly Track Timing = new Track(7920, new[]
        {
            Stone("timing-opening", 550, "floor"), Gate("timing-gate-a", 1050),
            Stone("timing-risk-a", 1600, "floor"), Gate("timing-gate-b", 2350),
            Stone("timing-hold", 2800, "ceiling"), Stone("timing-switch", 3250, "floor"),
            Stone("timing-risk-b", 3900, "ceiling"), Gate("timing-gate-c", 4650),
            Stone("timing-late-hold", 5100, "floor"), Stone("timing-late-switch", 5500, "ceiling"),
            Stone("timing-risk-c", 6300, "floor"), Gate("timing-gate-d", 7050),
            Stone("timing-last-ceiling", 7500, "ceiling")
        })
        {
            Id = "timing", Name = "Точный перелёт", Focus = "Поймай середину окна и выбери момент для кольца.",
            RiskZones = new[] { Risk("timing-ring-a", "timing-risk-a", 1600, "floor"),
                Risk("timing-ring-b", "timing-risk-b", 3900, "ceiling"), Risk("timing-ring-c", "timing-risk-c", 6300, "floor") },
            SafeRoute = new[] { 370d, 968, 1420, 2268, 3070, 3720, 4568, 5320, 6120, 6968 },
            RiskRoute = new[] { 370d, 968, 1516, 2268, 3070, 3816, 4568, 5320, 6216, 6968 }
        };

        public static Track Race { get { return Hold; } }
        public static Track Practice { get { return RiskPractice; } }
        public static readonly Track[] All = { Hold, Switch, Timing };

        public static Track Get(string id)
        {
            if (id == RiskPractice.Id) return RiskPractice;
            foreach (var track in All) if (track.Id == id) return track;
            throw new ArgumentOutOfRangeException(nameof(id), "Unknown track");
        }

        private static RiskZone Risk(string id, string obstacleId, double obstacleX, string kind)
        {
            return new RiskZone { Id = id, ObstacleId = obstacleId, X = obstacleX - 36,
                Y = kind == "floor" ? 183 : 57, Radius = 6, ClearX = obstacleX + 64 + RaceConfig.PlayerRadius + 1,
                SafeFlipX = obstacleX - 180, RiskFlipX = obstacleX - 84 };
        }
    }

    public static class RaceSimulation
    {
        private const double Epsilon = 1e-9;

        public static GameState CreateGame(string mode = "race", string trackId = null, bool negativeEnabled = false)
        {
            if (mode != "race" && mode != "practice" && mode != "legacy" && mode != "legacy-practice")
                throw new ArgumentOutOfRangeException(nameof(mode), "Unknown game mode");
            Track track;
            if (mode == "legacy") track = Tracks.LegacyRace;
            else if (mode == "legacy-practice") track = Tracks.LegacyPractice;
            else if (trackId != null && !trackId.StartsWith("v3-", StringComparison.Ordinal)) track = Tracks.Get(trackId);
            else if (mode == "practice") track = TracksV3.StageOne(negativeEnabled);
            else track = TracksV3.Get(trackId ?? TracksV3.All[0].Id, negativeEnabled);
            return new GameState { Mode = mode, Track = track, Flight = FlightPresets.FromLegacy(MotionMode.Linear) };
        }

        public static GameState CreateTrial(Track track, MotionMode motion, MotionProfile profile = null)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (!Enum.IsDefined(typeof(MotionMode), motion)) throw new ArgumentOutOfRangeException(nameof(motion));
            var game = new GameState { Mode = "trial", Track = track, Motion = motion,
                Profile = profile ?? (motion == MotionMode.JumpGravityFlip ? MotionProfiles.Jump
                    : motion == MotionMode.GravityUnlimited ? MotionProfiles.Moderate : MotionProfiles.Baseline) };
            game.Flight = FlightPresets.FromLegacy(motion, game.Profile);
            foreach (var player in game.Players) player.AirFlipAvailable = SupportsAirFlip(game.Flight);
            return game;
        }

        public static GameState CreateTrial(Track track, FlightSettings flight)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (flight == null) throw new ArgumentNullException(nameof(flight));
            MotionMode mode = flight.LegacyLinear ? MotionMode.Linear : flight.Law == MotionLaw.Linear ? MotionMode.LinearAirFlip
                : flight.FirstAction == FirstAction.Jump ? MotionMode.JumpGravityFlip
                : flight.AirRepeats == AirRepeatLimit.Unlimited ? MotionMode.GravityUnlimited
                : flight.AirRepeats == AirRepeatLimit.None ? MotionMode.Gravity : MotionMode.GravityAirFlip;
            var game = new GameState { Mode = "trial", Track = track, Motion = mode, Flight = flight,
                Profile = new MotionProfile("custom", "Свои настройки", flight.GravityAcceleration,
                    flight.FirstAction == FirstAction.Jump ? flight.JumpImpulse : flight.FlipImpulse) };
            foreach (var player in game.Players) player.AirFlipAvailable = SupportsAirFlip(flight);
            return game;
        }

        private static FlightSettings Settings(GameState game)
        {
            if (game.Flight == null) game.Flight = FlightPresets.FromLegacy(game.Motion, game.Profile);
            return game.Flight;
        }
        private static bool SupportsAirFlip(FlightSettings flight) { return flight.AirRepeats != AirRepeatLimit.None; }
        private static double Acceleration(PlayerState player, FlightSettings flight)
        { return flight.Law == MotionLaw.Linear ? 0 : player.Gravity * flight.GravityAcceleration; }

        public static bool SetCharacter(GameState game, int playerIndex, int characterIndex)
        {
            if ((game.Phase != "ready" && game.Phase != "finished") || playerIndex < 0 || playerIndex > 1
                || characterIndex < 0 || characterIndex > 3) return false;
            game.Players[playerIndex].CharacterIndex = characterIndex;
            game.Players[playerIndex].Ready = false;
            return true;
        }

        // A single input edge. Rejected taps are never queued.
        public static bool Press(GameState game, int index)
        {
            if (index < 0 || index >= game.Players.Length) return false;
            var flight = Settings(game);
            var player = game.Players[index];
            if (game.Phase == "ready" || game.Phase == "finished")
            {
                player.Ready = true;
                if (game.Players[0].Ready && game.Players[1].Ready)
                {
                    game.Players = new[] { new PlayerState(0) { Ready = true, CharacterIndex = game.Players[0].CharacterIndex,
                            AirFlipAvailable = SupportsAirFlip(flight) },
                        new PlayerState(1) { Ready = true, CharacterIndex = game.Players[1].CharacterIndex,
                            AirFlipAvailable = SupportsAirFlip(flight) } };
                    game.Phase = "countdown";
                    game.Elapsed = 0;
                    game.CountdownRemaining = RaceConfig.CountdownDuration;
                    game.Winner = null;
                    game.WinnerAt = null;
                }
                return true;
            }
            if (game.Phase != "racing" || player.Recovering > 0 || player.FinishTime.HasValue)
                return false;
            if (player.Flipping)
            {
                if (!SupportsAirFlip(flight) || !player.AirFlipAvailable) return false;
                if (player.LinearJumpActive)
                {
                    player.LinearJumpActive = false;
                    player.Gravity = player.LinearJumpOriginSide == 1 ? -1 : 1;
                    player.VerticalSpeed = player.Gravity * flight.LinearSpeed;
                }
                else
                {
                    player.Gravity = -player.Gravity;
                    if (flight.Law == MotionLaw.Linear) player.VerticalSpeed = -player.VerticalSpeed;
                }
                player.FlipTo = player.Gravity > 0 ? 1 : 0;
                player.AirFlipAvailable = flight.AirRepeats == AirRepeatLimit.Unlimited;
                // The same flight continues with its incoming velocity. Neither
                // its clock nor its momentum is reset by this one air reversal.
                return true;
            }
            BeginFlight(player);
            if (!flight.LegacyLinear)
            {
                int side = player.Support == SupportKind.Stone ? player.SupportSide : player.Y == 1 ? 1 : 0;
                int away = side == 1 ? -1 : 1;
                bool jump = flight.FirstAction == FirstAction.Jump;
                player.Gravity = jump ? -away : away;
                player.VerticalSpeed = away * (flight.Law == MotionLaw.Linear ? flight.LinearSpeed
                    : jump ? flight.JumpImpulse : flight.FlipImpulse);
                player.LinearJumpActive = jump && flight.Law == MotionLaw.Linear;
                player.LinearJumpReturning = false;
                player.LinearJumpOriginSide = side;
                player.LinearJumpOriginY = GetPlayerCenterY(player);
            }
            player.Support = SupportKind.None;
            player.SupportObstacleId = null;
            player.Flipping = true;
            player.FlipFrom = player.Y;
            player.FlipTo = !flight.LegacyLinear ? (player.Gravity > 0 ? 1 : 0) : player.Y == 1 ? 0 : 1;
            player.FlipElapsed = 0;
            return true;
        }

        public static double GetPlayerCenterY(PlayerState player)
        {
            return RaceConfig.PlayerRadius + player.Y * (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius);
        }

        private static void BeginFlight(PlayerState player)
        {
            player.FlightActive = true;
            player.FlightOriginY = GetPlayerCenterY(player);
            player.FlightOriginSide = player.SupportSide;
            player.FlightOriginObstacleId = player.SupportObstacleId;
            player.FlightDuration = 0;
            player.FlightMaxDistance = 0;
        }
        private static void EndFlight(PlayerState player, bool interrupted, double at)
        {
            if (!player.FlightActive) return;
            player.LastFlight = new FlightMeasurement(player.FlightDuration, player.FlightMaxDistance, interrupted,
                at, player.FlightOriginSide, player.FlightOriginObstacleId);
            player.FlightActive = false;
        }
        private static void MeasureFlight(PlayerState player, double dt, FlightSettings flight)
        {
            if (!player.FlightActive || !player.Flipping) return;
            double y = GetPlayerCenterY(player);
            double vy = flight.LegacyLinear ? (player.FlipTo - player.FlipFrom)
                * (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius) / RaceConfig.FlipDuration : player.VerticalSpeed;
            double a = flight.LegacyLinear ? 0 : Acceleration(player, flight);
            player.FlightDuration += dt;
            player.FlightMaxDistance = Math.Max(player.FlightMaxDistance, Math.Abs(y - player.FlightOriginY));
            player.FlightMaxDistance = Math.Max(player.FlightMaxDistance, Math.Abs(y + vy * dt + .5 * a * dt * dt - player.FlightOriginY));
            if (a != 0)
            {
                double vertex = -vy / a;
                if (vertex > 0 && vertex < dt) player.FlightMaxDistance = Math.Max(player.FlightMaxDistance,
                    Math.Abs(y + vy * vertex + .5 * a * vertex * vertex - player.FlightOriginY));
            }
        }
        private static double JumpApexTime(PlayerState player, FlightSettings flight)
        {
            if (!player.LinearJumpActive || player.LinearJumpReturning || !player.Flipping) return double.PositiveInfinity;
            double away = player.LinearJumpOriginSide == 1 ? -1 : 1;
            return Math.Max(0, (player.LinearJumpOriginY + away * flight.LinearJumpHeight - GetPlayerCenterY(player)) / player.VerticalSpeed);
        }

        private static void MovePlayer(PlayerState player, double dt, FlightSettings flight)
        {
            MeasureFlight(player, dt, flight);
            player.Distance += player.CurrentSpeed * dt;
            var effect = player.ActiveEffect;
            if (player.FinishBoost)
            {
                player.FinishBoostElapsed += dt;
                // Keep ordinary pulses/queues intact. Authored tracks guarantee
                // clean entry; this differential ledger also reconciles actual
                // arrival if a synthetic track still has an ordinary effect.
                player.FinishBoostDistanceGained += (RaceConfig.FinishBoostSpeed
                    - RaceConfig.Speed * (effect != null ? effect.Multiplier : 1)) * dt;
            }
            if (effect != null)
            {
                effect.Elapsed += dt;
                effect.Remaining = Math.Max(0, effect.Remaining - dt);
                int completed = effect.Remaining < Epsilon ? effect.PulseCount
                    : Math.Min(effect.PulseCount, (int)Math.Floor((effect.Elapsed + Epsilon) / effect.PulseDuration));
                int newlyCompleted = Math.Max(0, completed - effect.CompletedPulses);
                if (effect.IsNegative) player.CompletedSlows += newlyCompleted;
                else player.CompletedBoosts += newlyCompleted;
                effect.CompletedPulses = completed;
                if (effect.Remaining < Epsilon)
                {
                    player.ActiveEffect = player.PendingEffects.Count > 0 ? player.PendingEffects.Dequeue() : null;
                }
            }
            if (!player.Flipping) return;
            if (!flight.LegacyLinear)
            {
                double acceleration = Acceleration(player, flight);
                double y = GetPlayerCenterY(player) + player.VerticalSpeed * dt + .5 * acceleration * dt * dt;
                player.Y = (y - RaceConfig.PlayerRadius) / (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius);
                player.VerticalSpeed += acceleration * dt;
                if (Math.Abs(player.VerticalSpeed) < 1e-10) player.VerticalSpeed = 0;
                player.FlipElapsed += dt;
                return;
            }
            player.FlipElapsed = Math.Min(RaceConfig.FlipDuration, player.FlipElapsed + dt);
            var progress = player.FlipElapsed / RaceConfig.FlipDuration;
            player.Y = player.FlipFrom + (player.FlipTo - player.FlipFrom) * progress;
            if (player.FlipElapsed >= RaceConfig.FlipDuration - Epsilon)
            {
                player.Flipping = false;
                player.Y = player.FlipTo;
                player.FlipElapsed = RaceConfig.FlipDuration;
                player.Support = SupportKind.Wall;
                player.SupportSide = player.Y == 1 ? 1 : 0;
                player.SupportObstacleId = null;
            }
        }

        private static void EnqueueEffect(PlayerState player, RiskZone zone)
        {
            var effect = new SpeedEffect { RingId = zone.Id, IsNegative = zone.IsNegative,
                Remaining = zone.IsNegative ? RaceConfig.SlowDuration : RaceConfig.BoostDuration };
            if (player.ActiveEffect == null) player.ActiveEffect = effect;
            else if (zone.IsInstant)
            {
                if (!player.ActiveEffect.IsNegative)
                {
                    // Extend this uninterrupted 1.5x interval. Retain elapsed
                    // time and credit each full .6s pulse exactly once.
                    player.ActiveEffect.PulseCount++;
                    player.ActiveEffect.Remaining += RaceConfig.BoostDuration;
                }
                else
                {
                    int waiting = player.PendingEffects.Count;
                    player.PendingEffects.Enqueue(player.ActiveEffect);
                    for (int index = 0; index < waiting; index++)
                        player.PendingEffects.Enqueue(player.PendingEffects.Dequeue());
                    player.ActiveEffect = effect;
                }
            }
            else player.PendingEffects.Enqueue(effect);
        }

        private static bool RangeTimes(double position, double velocity, double minimum, double maximum,
            double horizon, out double enter, out double leave)
        {
            if (Math.Abs(velocity) < Epsilon)
            {
                enter = 0;
                leave = horizon;
                return position >= minimum - Epsilon && position <= maximum + Epsilon;
            }
            var first = (minimum - position) / velocity;
            var last = (maximum - position) / velocity;
            enter = Math.Max(0, Math.Min(first, last));
            leave = Math.Min(horizon, Math.Max(first, last));
            return enter <= leave + Epsilon;
        }

        private sealed class Collision
        {
            public double Time;
            public Obstacle Obstacle;
        }

        private sealed class SurfaceContact
        {
            public double Time;
            public int Side;
            public Obstacle Obstacle;
            public RiskZone Zone;
        }

        private static double StonePlane(Obstacle obstacle)
        {
            return obstacle.Kind == "floor" ? RaceConfig.CorridorHeight - obstacle.Height - RaceConfig.PlayerRadius
                : obstacle.Height + RaceConfig.PlayerRadius;
        }

        private static SurfaceContact NextLanding(PlayerState player, Track track, double horizon, FlightSettings flight)
        {
            if (!player.Flipping || flight.LegacyLinear) return null;
            double y = GetPlayerCenterY(player), vy = player.VerticalSpeed;
            double acceleration = Acceleration(player, flight);
            SurfaceContact result = null;
            foreach (var zone in track.RiskZones)
            {
                if (!zone.IsLanding || zone.LandingWidth <= 0) continue;
                foreach (var obstacle in track.Obstacles)
                {
                    if (obstacle.Id != zone.ObstacleId || (obstacle.Kind != "floor" && obstacle.Kind != "ceiling")) continue;
                    int side = obstacle.Kind == "floor" ? 1 : 0, sign = side == 1 ? 1 : -1;
                    double plane = StonePlane(obstacle);
                    int count = ContinuousMotion.QuadraticRoots(.5 * acceleration, vy, y - plane,
                        out double first, out double second);
                    if (count == 0 && sign * acceleration < 0)
                    {
                        // Frame splits can leave an exact tangent a few ulps
                        // outside the plane. Test the analytic vertex itself;
                        // this is not a sampled contact or a widened X window.
                        double vertex = -vy / acceleration;
                        if (vertex >= 0 && Math.Abs(y + vy * vertex + .5 * acceleration * vertex * vertex - plane) <= 1e-9)
                        { first = vertex; count = 1; }
                    }
                    for (int index = 0; index < count; index++)
                    {
                        double time = index == 0 ? first : second;
                        if (time < -Epsilon || time > horizon + Epsilon) continue;
                        time = Math.Max(0, Math.Min(time, horizon));
                        double approach = sign * (vy + acceleration * time);
                        // Include a true tangent approached from the corridor,
                        // but never collect merely by departing from this plane.
                        if (approach < -1e-8 || (Math.Abs(approach) <= 1e-8
                            && (time <= 1e-12 || sign * acceleration >= 0))) continue;
                        double x = player.Distance + player.CurrentSpeed * time;
                        if (x < Math.Max(obstacle.X, zone.X - zone.LandingWidth / 2) - Epsilon
                            || x > Math.Min(obstacle.X + obstacle.Width, zone.X + zone.LandingWidth / 2) + Epsilon) continue;
                        if (result == null || time < result.Time)
                            result = new SurfaceContact { Time = time, Side = side, Obstacle = obstacle, Zone = zone };
                    }
                }
            }
            return result;
        }

        private static SurfaceContact NextSurface(PlayerState player, FlightSettings flight)
        {
            if (flight.LegacyLinear || !player.Flipping) return null;
            double travel = RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius;
            double position = player.Y * travel, acceleration = Acceleration(player, flight);
            SurfaceContact result = null;
            for (int side = 0; side < 2; side++)
            {
                double boundary = side * travel;
                bool outward = side == 0 ? player.VerticalSpeed < 0 : player.VerticalSpeed > 0;
                bool acceleratingOut = player.VerticalSpeed == 0 && (side == 0 ? acceleration < 0 : acceleration > 0);
                if (Math.Abs(position - boundary) < 1e-9 && (outward || acceleratingOut))
                    return new SurfaceContact { Time = 0, Side = side };
                int count = ContinuousMotion.QuadraticRoots(.5 * acceleration, player.VerticalSpeed,
                    position - boundary, out double first, out double second);
                for (int index = 0; index < count; index++)
                {
                    double time = index == 0 ? first : second;
                    if (time > 1e-12 && (result == null || time < result.Time)) result = new SurfaceContact { Time = time, Side = side };
                }
            }
            return result;
        }

        private static void TouchSurface(PlayerState player, SurfaceContact surface, FlightSettings flight)
        {
            player.Y = surface.Obstacle == null ? surface.Side
                : (StonePlane(surface.Obstacle) - RaceConfig.PlayerRadius) / (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius);
            player.VerticalSpeed = 0;
            player.Support = SupportKind.None;
            player.SupportObstacleId = null;
            player.SupportSide = surface.Side;
            if (player.LinearJumpActive && surface.Obstacle == null && surface.Side != player.LinearJumpOriginSide)
            {
                // A wall reached before the planned apex reflects the ordinary
                // linear jump. It is not support and never restores a repeat.
                player.LinearJumpReturning = true;
                player.VerticalSpeed = (player.LinearJumpOriginSide == 1 ? 1 : -1) * flight.LinearSpeed;
                return;
            }
            if (flight.Law == MotionLaw.Linear || surface.Side == (player.Gravity > 0 ? 1 : 0))
            {
                if (flight.Law == MotionLaw.Linear) player.Gravity = surface.Side == 1 ? 1 : -1;
                player.Flipping = false;
                player.LinearJumpActive = false;
                player.AirFlipAvailable = SupportsAirFlip(flight);
                player.Support = surface.Obstacle == null ? SupportKind.Wall : SupportKind.Stone;
                player.SupportObstacleId = surface.Obstacle == null ? null : surface.Obstacle.Id;
            }
            // At the opposite surface gravity points back into the corridor.
            // Keep the flight and spent charge: this contact is not a landing.
        }

        // Swept rectangles find the exact first contact even when one frame crosses
        // an entire obstacle. Both halves of a gate share one obstacle ID.
        private static Collision NextCollision(PlayerState player, Track track, double horizon, FlightSettings flight,
            SurfaceContact landing)
        {
            var radius = RaceConfig.PlayerRadius;
            var verticalTravel = RaceConfig.CorridorHeight - 2 * radius;
            var yVelocity = player.Flipping ? (player.FlipTo - player.FlipFrom) / RaceConfig.FlipDuration : 0;
            Collision collision = null;
            foreach (var obstacle in track.Obstacles)
            {
                if (player.HitObstacleIds.Contains(obstacle.Id)) continue;
                if ((player.Support == SupportKind.Stone && player.SupportObstacleId == obstacle.Id)
                    || (player.EdgeClearObstacleId == obstacle.Id && player.Distance >= obstacle.X + obstacle.Width - Epsilon)) continue;
                if (!RangeTimes(player.Distance, player.CurrentSpeed, obstacle.X - radius,
                    obstacle.X + obstacle.Width + radius, horizon, out var horizontalEnter, out var horizontalLeave)) continue;
                var bandCount = obstacle.Kind == "gate" ? 2 : 1;
                for (var band = 0; band < bandCount; band++)
                {
                    double minimum, maximum;
                    if (obstacle.Kind == "gate")
                    {
                        // A gap reaching a corridor wall has no solid half
                        // there. A zero-height half must not hit a wall runner.
                        if ((band == 0 && obstacle.GapTop <= 0)
                            || (band == 1 && obstacle.GapBottom >= RaceConfig.CorridorHeight)) continue;
                        minimum = band == 0 ? double.NegativeInfinity : (obstacle.GapBottom - 2 * radius) / verticalTravel;
                        maximum = band == 0 ? obstacle.GapTop / verticalTravel : double.PositiveInfinity;
                    }
                    else if (obstacle.Kind == "ceiling")
                    {
                        minimum = double.NegativeInfinity;
                        maximum = obstacle.Height / verticalTravel;
                    }
                    else
                    {
                        minimum = (RaceConfig.CorridorHeight - obstacle.Height - 2 * radius) / verticalTravel;
                        maximum = double.PositiveInfinity;
                    }
                    double time;
                    if (flight.LegacyLinear)
                    {
                        if (!RangeTimes(player.Y, yVelocity, minimum, maximum, horizon, out var verticalEnter, out var verticalLeave)) continue;
                        time = Math.Max(horizontalEnter, verticalEnter);
                        if (time > Math.Min(horizontalLeave, verticalLeave) + Epsilon) continue;
                    }
                    else
                    {
                        time = ContinuousMotion.FirstBandContact(player.Y * verticalTravel, player.Flipping ? player.VerticalSpeed : 0,
                            player.Flipping ? Acceleration(player, flight) : 0,
                            minimum * verticalTravel, maximum * verticalTravel, horizontalEnter, horizontalLeave);
                        if (time <= Epsilon && player.Flipping && obstacle.Kind != "gate"
                            && Math.Abs(GetPlayerCenterY(player) - StonePlane(obstacle)) < 1e-8)
                        {
                            int sign = obstacle.Kind == "floor" ? 1 : -1;
                            double acceleration = Acceleration(player, flight);
                            bool separating = sign * player.VerticalSpeed < 0
                                || (player.VerticalSpeed == 0 && sign * acceleration < 0);
                            bool marked = false;
                            foreach (var zone in track.RiskZones)
                                if (zone.IsLanding && zone.ObstacleId == obstacle.Id) { marked = true; break; }
                            if (separating && marked)
                            {
                                // Ignore the inclusive t=0 plane while departing;
                                // a later return to the same face is still a hit.
                                time = double.PositiveInfinity;
                                int roots = ContinuousMotion.QuadraticRoots(.5 * acceleration, player.VerticalSpeed, 0,
                                    out double first, out double second);
                                for (int root = 0; root < roots; root++)
                                {
                                    double candidate = root == 0 ? first : second;
                                    if (candidate > 1e-12 && candidate >= horizontalEnter - Epsilon && candidate <= horizontalLeave + Epsilon)
                                        time = Math.Min(time, candidate);
                                }
                            }
                        }
                    }
                    if (landing != null && landing.Obstacle.Id == obstacle.Id && Math.Abs(time - landing.Time) <= Epsilon) continue;
                    if (!double.IsInfinity(time) && (collision == null || time < collision.Time))
                        collision = new Collision { Time = Math.Max(0, Math.Min(time, horizon)), Obstacle = obstacle };
                }
            }
            return collision;
        }

        private static string CollisionMessage(PlayerState player, Obstacle obstacle)
        {
            if (obstacle.Kind == "gate")
            {
                if (!player.Flipping) return "Ворота: нужно пролететь через середину";
                return player.FlipElapsed < RaceConfig.FlipDuration / 2
                    ? "Поздновато: начни перелёт чуть раньше" : "Рановато: начни перелёт чуть позже";
            }
            var blockedSide = obstacle.Kind == "floor" ? 1 : 0;
            if (player.Flipping && player.FlipTo == blockedSide) return "Рано вернулся — подожди конца камня";
            if (player.Flipping) return "Поздний переворот — нажми чуть раньше";
            return obstacle.Kind == "floor" ? "Камень внизу — пора наверх" : "Камень вверху — пора вниз";
        }

        private sealed class RiskEvent
        {
            public double Time;
            public RiskZone Zone;
            public bool Clear;
        }

        // Exact swept circle against the ring; no frame sampling or timing-only
        // shortcut. The player's physical radius is part of ring contact.
        private static RiskEvent NextRiskEvent(PlayerState player, Track track, double horizon, FlightSettings flight, SurfaceContact surface)
        {
            RiskEvent result = null;
            foreach (var zone in track.RiskZones)
            {
                if (zone.IsLanding) continue;
                if (player.SuccessfulRiskIds.Contains(zone.Id) || player.FailedRiskIds.Contains(zone.Id)
                    || player.NegativeRiskIds.Contains(zone.Id)) continue;
                bool collected = player.CollectedRiskIds.Contains(zone.Id);
                double time;
                if (zone.IsFinish)
                {
                    if (player.FinishBoostsEarned != 0 || zone.X < 0 || zone.X >= track.Length
                        || player.Distance > zone.X + Epsilon || zone.FinishHeight < 0) continue;
                    time = Math.Max(0, (zone.X - player.Distance) / player.CurrentSpeed);
                    if (time > horizon + Epsilon) continue;
                    double vy = !player.Flipping ? 0 : flight.LegacyLinear
                        ? (player.FlipTo - player.FlipFrom) * (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius)
                            / RaceConfig.FlipDuration : player.VerticalSpeed;
                    double acceleration = player.Flipping && !flight.LegacyLinear ? Acceleration(player, flight) : 0;
                    double y = GetPlayerCenterY(player) + vy * time + .5 * acceleration * time * time;
                    if (y < zone.Y - zone.FinishHeight / 2 - Epsilon
                        || y > zone.Y + zone.FinishHeight / 2 + Epsilon) continue;
                }
                else if (collected)
                {
                    double clearX = zone.ClearX;
                    foreach (var obstacle in track.Obstacles)
                        if (obstacle.Id == zone.ObstacleId)
                            clearX = Math.Max(clearX, obstacle.X + obstacle.Width + RaceConfig.PlayerRadius + 1e-7);
                    time = Math.Max(0, (clearX - player.Distance) / player.CurrentSpeed);
                    // Once the whole body has passed the linked stone, no
                    // later vertical return can touch it. Landing is not
                    // required; collision-first ordering still cancels a
                    // collected reward before this geometric boundary.
                    if (time > horizon + Epsilon) continue;
                }
                else
                {
                    double dx = player.Distance - zone.X, dy = GetPlayerCenterY(player) - zone.Y;
                    double vx = player.CurrentSpeed;
                    double vy = player.Flipping ? (player.FlipTo - player.FlipFrom)
                        * (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius) / RaceConfig.FlipDuration : 0;
                    double radius = zone.Radius + RaceConfig.PlayerRadius;
                    double c = dx * dx + dy * dy - radius * radius;
                    if (!flight.LegacyLinear)
                    {
                        if (!RangeTimes(player.Distance, vx, zone.X - radius, zone.X + radius,
                            horizon, out double enter, out double leave)) continue;
                        double acceleration = player.Flipping ? Acceleration(player, flight) : 0;
                        vy = player.Flipping ? player.VerticalSpeed : 0;
                        double contact = ContinuousMotion.FirstCircleContact(dx + vx * enter,
                            dy + vy * enter + .5 * acceleration * enter * enter, vx, vy + acceleration * enter,
                            acceleration, radius, Math.Max(0, leave - enter));
                        if (double.IsInfinity(contact)) continue;
                        time = enter + contact;
                    }
                    else if (c <= Epsilon) time = 0;
                    else
                    {
                        double a = vx * vx + vy * vy, b = 2 * (dx * vx + dy * vy);
                        double discriminant = b * b - 4 * a * c;
                        if (discriminant < 0) continue;
                        time = (-b - Math.Sqrt(discriminant)) / (2 * a);
                        if (time < -Epsilon || time > horizon + Epsilon) continue;
                        time = Math.Max(0, time);
                    }
                }
                if (result == null || time < result.Time)
                    result = new RiskEvent { Time = Math.Min(time, horizon), Zone = zone, Clear = collected };
            }
            return result;
        }

        private static void AdvancePlayer(PlayerState player, Track track, double dt, double elapsed, FlightSettings flight)
        {
            if (player.FinishTime.HasValue) return;
            var remaining = dt;
            // Do not discard positive sub-nanosecond slices: one can contain the finish.
            while (remaining > 0)
            {
                if (player.Recovering > Epsilon)
                {
                    var stopped = Math.Min(remaining, player.Recovering);
                    player.Recovering = Math.Max(0, player.Recovering - stopped);
                    player.RecoveryElapsed += stopped;
                    if (player.Recovering < Epsilon)
                    {
                        player.Recovering = 0;
                        player.RecoveryElapsed = 0;
                        player.CompletedRecoveries++;
                    }
                    remaining -= stopped;
                    continue;
                }
                var surface = NextSurface(player, flight);
                double flightHorizon = flight.LegacyLinear
                    ? (player.Flipping ? RaceConfig.FlipDuration - player.FlipElapsed : double.PositiveInfinity)
                    : (surface != null ? surface.Time : double.PositiveInfinity);
                double acceleration = Acceleration(player, flight);
                if (!flight.LegacyLinear && player.Flipping && player.VerticalSpeed * acceleration < 0)
                    flightHorizon = Math.Min(flightHorizon, -player.VerticalSpeed / acceleration);
                double apexTime = JumpApexTime(player, flight);
                flightHorizon = Math.Min(flightHorizon, apexTime);
                var horizon = Math.Min(remaining, Math.Min(Math.Max(0, track.Length - player.Distance) / player.CurrentSpeed,
                    Math.Min(flightHorizon,
                        player.ActiveEffect != null ? player.ActiveEffect.Remaining : double.PositiveInfinity)));
                Obstacle edge = null;
                if (player.Support == SupportKind.Stone)
                    foreach (var obstacle in track.Obstacles)
                        if (obstacle.Id == player.SupportObstacleId) { edge = obstacle; break; }
                double edgeTime = edge == null ? double.PositiveInfinity
                    : Math.Max(0, (edge.X + edge.Width - player.Distance) / player.CurrentSpeed);
                horizon = Math.Min(horizon, edgeTime);
                var landing = NextLanding(player, track, horizon, flight);
                if (landing != null) horizon = Math.Min(horizon, landing.Time);
                var collision = NextCollision(player, track, horizon, flight, landing);
                var risk = NextRiskEvent(player, track, horizon, flight, landing ?? surface);
                var moving = Math.Min(collision != null ? collision.Time : horizon, risk != null ? risk.Time : horizon);
                MovePlayer(player, moving, flight);
                remaining -= moving;
                bool collided = collision != null && collision.Time <= moving + Epsilon;
                if (collided)
                {
                    EndFlight(player, true, elapsed + dt - remaining);
                    player.HitObstacleIds.Add(collision.Obstacle.Id);
                    player.Collisions++;
                    player.Recovering = RaceConfig.RecoveryDuration;
                    // Cancel only contacts that were already pending before this
                    // impact. An untouched ring can still be earned after recovery.
                    foreach (var zone in track.RiskZones)
                        if (!zone.IsNegative && !zone.IsInstant && !zone.IsFinish && !zone.FreeReward && zone.ObstacleId == collision.Obstacle.Id
                            && player.CollectedRiskIds.Contains(zone.Id)
                            && !player.SuccessfulRiskIds.Contains(zone.Id)) player.FailedRiskIds.Add(zone.Id);
                    player.LastHit = new HitInfo
                    {
                        Id = collision.Obstacle.Id,
                        Message = CollisionMessage(player, collision.Obstacle),
                        At = elapsed + dt - remaining
                    };
                }
                if (landing != null && landing.Time <= moving + Epsilon)
                {
                    TouchSurface(player, landing, flight);
                    if (!player.SuccessfulRiskIds.Contains(landing.Zone.Id))
                        AwardPositive(player, landing.Zone, elapsed + dt - remaining);
                }
                else if (!collided && surface != null && surface.Time <= moving + Epsilon) TouchSurface(player, surface, flight);
                if (!player.Flipping) EndFlight(player, false, elapsed + dt - remaining);
                if (!collided && player.LinearJumpActive && !player.LinearJumpReturning && player.Flipping
                    && apexTime <= moving + Epsilon)
                {
                    player.LinearJumpReturning = true;
                    player.VerticalSpeed = -player.VerticalSpeed;
                }
                if (!collided && edge != null && edgeTime <= moving + Epsilon)
                {
                    BeginFlight(player);
                    player.Support = SupportKind.None;
                    player.SupportObstacleId = null;
                    player.EdgeClearObstacleId = edge.Id;
                    player.Flipping = true;
                    player.LinearJumpActive = false;
                    player.VerticalSpeed = flight.Law == MotionLaw.Linear
                        ? (player.SupportSide == 1 ? 1 : -1) * flight.LinearSpeed : 0;
                    player.FlipFrom = player.Y;
                    player.FlipTo = player.Gravity > 0 ? 1 : 0;
                    player.FlipElapsed = 0;
                }
                if (risk != null && risk.Time <= moving + Epsilon && !player.FailedRiskIds.Contains(risk.Zone.Id))
                {
                    if (risk.Zone.IsFinish)
                    {
                        player.CollectedRiskIds.Add(risk.Zone.Id);
                        player.SuccessfulRiskIds.Add(risk.Zone.Id);
                        player.FinishBoost = true;
                        player.FinishBoostsEarned++;
                        player.LastFinishBoost = new HitInfo { Id = risk.Zone.Id, Message = "Финишный рывок −0,1 сек",
                            At = elapsed + dt - remaining };
                    }
                    else if (risk.Zone.IsNegative)
                    {
                        player.CollectedRiskIds.Add(risk.Zone.Id);
                        player.NegativeRiskIds.Add(risk.Zone.Id);
                        player.SlowsEarned++;
                        EnqueueEffect(player, risk.Zone);
                        player.LastSlow = new HitInfo { Id = risk.Zone.Id, Message = "Замедление +0,3 сек", At = elapsed + dt - remaining };
                    }
                    else if (!risk.Clear && !risk.Zone.FreeReward && !risk.Zone.IsInstant) player.CollectedRiskIds.Add(risk.Zone.Id);
                    else
                    {
                        // A historical hit only deduplicates obstacle penalties.
                        // Pending cancellation above captures the relevant order.
                        AwardPositive(player, risk.Zone, elapsed + dt - remaining);
                    }
                }
                if (player.Distance >= track.Length - Epsilon && !collided)
                {
                    player.Distance = track.Length;
                    // Arrival was located at the actual current velocity. Reconcile
                    // its timestamp from *served* motion/stops to make exact ties
                    // independent of integration drift. Partial boosts count only
                    // the extra distance actually travelled before the finish.
                    player.FinishTime = track.Length / RaceConfig.Speed
                        + player.CompletedRecoveries * RaceConfig.RecoveryDuration + player.RecoveryElapsed
                        + (player.CompletedSlows - player.CompletedBoosts) * RaceConfig.BoostTimeGain
                        + player.SlowElapsed * (1 - RaceConfig.SlowMultiplier)
                        - player.BoostElapsed * (RaceConfig.BoostMultiplier - 1)
                        - player.FinishBoostDistanceGained / RaceConfig.Speed;
                    player.FinishBoost = false;
                    player.FlightActive = false;
                    return;
                }
            }
        }

        private static void AwardPositive(PlayerState player, RiskZone zone, double at)
        {
            player.CollectedRiskIds.Add(zone.Id);
            player.SuccessfulRiskIds.Add(zone.Id);
            player.BoostsEarned++;
            EnqueueEffect(player, zone);
            player.LastSuccess = new HitInfo { Id = zone.Id,
                Message = zone.IsInstant ? "Мгновенный рывок −0,3 сек" : "Точно! Ускорение −0,3 сек", At = at };
            if (zone.IsInstant) { player.InstantBoostsEarned++; player.LastInstant = player.LastSuccess; }
        }

        private static void ResolveFinish(GameState game)
        {
            var finisherCount = 0;
            var firstTime = double.PositiveInfinity;
            foreach (var player in game.Players)
            {
                if (!player.FinishTime.HasValue) continue;
                finisherCount++;
                firstTime = Math.Min(firstTime, player.FinishTime.Value);
            }
            if (finisherCount > 0)
            {
                game.WinnerAt = firstTime;
                var tiedCount = 0;
                foreach (var player in game.Players)
                {
                    if (!player.FinishTime.HasValue || Math.Abs(player.FinishTime.Value - firstTime) >= Epsilon) continue;
                    tiedCount++;
                    game.Winner = player.Id;
                }
                if (tiedCount == 2) game.Winner = "tie";
            }
            if (finisherCount == 2 || (game.WinnerAt.HasValue && game.Elapsed >= game.WinnerAt.Value + RaceConfig.FinishGrace))
            {
                game.Phase = "finished";
                foreach (var player in game.Players) player.Ready = false;
            }
        }

        // Advances active play, including leftover time when a countdown ends.
        public static void Advance(GameState game, double dt)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0) return;
            if (game.Phase == "countdown")
            {
                var countdownStep = Math.Min(dt, game.CountdownRemaining);
                game.CountdownRemaining = Math.Max(0, game.CountdownRemaining - countdownStep);
                dt -= countdownStep;
                if (game.CountdownRemaining <= Epsilon)
                {
                    game.CountdownRemaining = 0;
                    game.Phase = "racing";
                    foreach (var player in game.Players) player.Ready = false;
                }
            }
            if (game.Phase != "racing" || dt <= 0) return;
            var flight = Settings(game);
            double maximumSpeed = game.Track.RiskZones.Length > 0 ? RaceConfig.Speed * RaceConfig.BoostMultiplier : RaceConfig.Speed;
            foreach (var zone in game.Track.RiskZones)
                if (zone.IsFinish) { maximumSpeed = RaceConfig.FinishBoostSpeed; break; }
            while (dt > 0 && game.Phase == "racing")
            {
                // Stop at the earliest possible finish so a large step cannot let
                // the other player run beyond the winner's grace deadline.
                var finishHorizon = double.PositiveInfinity;
                foreach (var player in game.Players)
                    if (!player.FinishTime.HasValue)
                        finishHorizon = Math.Min(finishHorizon,
                            (game.Track.Length - player.Distance) / Math.Max(maximumSpeed, player.CurrentSpeed) + player.Recovering);
                var graceHorizon = game.WinnerAt.HasValue
                    ? game.WinnerAt.Value + RaceConfig.FinishGrace - game.Elapsed : double.PositiveInfinity;
                var step = Math.Min(dt, Math.Min(finishHorizon, Math.Max(0, graceHorizon)));
                foreach (var player in game.Players) AdvancePlayer(player, game.Track, step, game.Elapsed, flight);
                game.Elapsed += step;
                dt -= step;
                ResolveFinish(game);
                if (step <= 0) break;
            }
        }
    }
}
