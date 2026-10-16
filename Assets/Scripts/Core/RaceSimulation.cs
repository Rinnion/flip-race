using System;
using System.Collections.Generic;

// Pure deterministic rules. No Unity API, floats, physics engine, or frame clock.
// Original analytic collisions are preserved. Swept rings enqueue complete,
// piecewise-constant speed effects. Distances and times remain doubles.
namespace FlipRace.Core
{
    public enum MotionMode { Linear, Gravity, GravityAirFlip }

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
        public bool AfterObstacle { get { return Placement == "after"; } }
        public bool FreeReward;
        // Ring center and radius use world units, just like obstacle geometry.
        public double X, Y, Radius = 6, ClearX, SafeFlipX, RiskFlipX;
    }

    public sealed class SpeedEffect
    {
        public string RingId;
        public bool IsNegative;
        public double Remaining, Elapsed;
        public double Duration { get { return IsNegative ? RaceConfig.SlowDuration : RaceConfig.BoostDuration; } }
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
        public double Recovering;
        public int Collisions;
        public double? FinishTime;
        public HashSet<string> HitObstacleIds = new HashSet<string>();
        public HitInfo LastHit;
        public SpeedEffect ActiveEffect;
        // Waiting effects exclude ActiveEffect. Every earned impulse is served
        // completely in contact/confirmation order; opposite signs do not erase it.
        public Queue<SpeedEffect> PendingEffects = new Queue<SpeedEffect>();
        public double BoostRemaining { get { return ActiveEffect != null && !ActiveEffect.IsNegative ? ActiveEffect.Remaining : 0; } }
        public double SlowRemaining { get { return ActiveEffect != null && ActiveEffect.IsNegative ? ActiveEffect.Remaining : 0; } }
        public int BoostsEarned;
        public int SlowsEarned;
        public HashSet<string> CollectedRiskIds = new HashSet<string>();
        public HashSet<string> SuccessfulRiskIds = new HashSet<string>();
        public HashSet<string> FailedRiskIds = new HashSet<string>();
        public HitInfo LastSuccess;
        public HitInfo LastSlow;
        public HashSet<string> NegativeRiskIds = new HashSet<string>();
        public double CurrentSpeed { get { return RaceConfig.Speed * (ActiveEffect != null ? ActiveEffect.Multiplier : 1); } }
        // Actual served motion/stops, used to stabilize analytically identical
        // finishes. Awarded but unserved boost never changes FinishTime.
        public int CompletedBoosts;
        public int CompletedSlows;
        public double BoostElapsed { get { return ActiveEffect != null && !ActiveEffect.IsNegative ? ActiveEffect.Elapsed : 0; } }
        public double SlowElapsed { get { return ActiveEffect != null && ActiveEffect.IsNegative ? ActiveEffect.Elapsed : 0; } }
        public int CompletedRecoveries;
        public double RecoveryElapsed;
        public double BoostDistanceGained { get { return CompletedBoosts * RaceConfig.Speed * RaceConfig.BoostTimeGain
            + BoostElapsed * RaceConfig.Speed * (RaceConfig.BoostMultiplier - 1); } }
        public double SlowDistanceLost { get { return CompletedSlows * RaceConfig.Speed * RaceConfig.SlowTimeLoss
            + SlowElapsed * RaceConfig.Speed * (1 - RaceConfig.SlowMultiplier); } }

        public PlayerState(int id) { Id = id; CharacterIndex = id; }
    }

    public sealed class GameState
    {
        public string Mode;
        public MotionMode Motion = MotionMode.Linear;
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
            return new GameState { Mode = mode, Track = track };
        }

        public static GameState CreateTrial(Track track, MotionMode motion)
        {
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (!Enum.IsDefined(typeof(MotionMode), motion)) throw new ArgumentOutOfRangeException(nameof(motion));
            var game = new GameState { Mode = "trial", Track = track, Motion = motion };
            foreach (var player in game.Players) player.AirFlipAvailable = motion == MotionMode.GravityAirFlip;
            return game;
        }

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
            var player = game.Players[index];
            if (game.Phase == "ready" || game.Phase == "finished")
            {
                player.Ready = true;
                if (game.Players[0].Ready && game.Players[1].Ready)
                {
                    game.Players = new[] { new PlayerState(0) { Ready = true, CharacterIndex = game.Players[0].CharacterIndex,
                            AirFlipAvailable = game.Motion == MotionMode.GravityAirFlip },
                        new PlayerState(1) { Ready = true, CharacterIndex = game.Players[1].CharacterIndex,
                            AirFlipAvailable = game.Motion == MotionMode.GravityAirFlip } };
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
                if (game.Motion != MotionMode.GravityAirFlip || !player.AirFlipAvailable) return false;
                player.Gravity = -player.Gravity;
                player.FlipTo = player.Gravity > 0 ? 1 : 0;
                player.AirFlipAvailable = false;
                // The same flight continues with its incoming velocity. Neither
                // its clock nor its momentum is reset by this one air reversal.
                return true;
            }
            if (game.Motion != MotionMode.Linear)
            {
                player.Gravity = player.Y == 1 ? -1 : 1;
                player.VerticalSpeed = 0;
            }
            player.Flipping = true;
            player.FlipFrom = player.Y;
            player.FlipTo = player.Y == 1 ? 0 : 1;
            player.FlipElapsed = 0;
            return true;
        }

        public static double GetPlayerCenterY(PlayerState player)
        {
            return RaceConfig.PlayerRadius + player.Y * (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius);
        }

        private static void MovePlayer(PlayerState player, double dt, MotionMode motion)
        {
            player.Distance += player.CurrentSpeed * dt;
            var effect = player.ActiveEffect;
            if (effect != null)
            {
                effect.Elapsed += dt;
                effect.Remaining = Math.Max(0, effect.Remaining - dt);
                if (effect.Remaining < Epsilon)
                {
                    if (effect.IsNegative) player.CompletedSlows++;
                    else player.CompletedBoosts++;
                    player.ActiveEffect = player.PendingEffects.Count > 0 ? player.PendingEffects.Dequeue() : null;
                }
            }
            if (!player.Flipping) return;
            if (motion != MotionMode.Linear)
            {
                double acceleration = player.Gravity * RaceConfig.GravityAcceleration;
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
            }
        }

        private static void EnqueueEffect(PlayerState player, RiskZone zone)
        {
            var effect = new SpeedEffect { RingId = zone.Id, IsNegative = zone.IsNegative,
                Remaining = zone.IsNegative ? RaceConfig.SlowDuration : RaceConfig.BoostDuration };
            if (player.ActiveEffect == null) player.ActiveEffect = effect;
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
        }

        private static SurfaceContact NextSurface(PlayerState player, MotionMode motion)
        {
            if (motion == MotionMode.Linear || !player.Flipping) return null;
            double travel = RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius;
            double position = player.Y * travel, acceleration = player.Gravity * RaceConfig.GravityAcceleration;
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

        private static void TouchSurface(PlayerState player, SurfaceContact surface, MotionMode motion)
        {
            player.Y = surface.Side;
            player.VerticalSpeed = 0;
            if (surface.Side == (player.Gravity > 0 ? 1 : 0))
            {
                player.Flipping = false;
                player.AirFlipAvailable = motion == MotionMode.GravityAirFlip;
            }
            // At the opposite surface gravity points back into the corridor.
            // Keep the flight and spent charge: this contact is not a landing.
        }

        // Swept rectangles find the exact first contact even when one frame crosses
        // an entire obstacle. Both halves of a gate share one obstacle ID.
        private static Collision NextCollision(PlayerState player, Track track, double horizon, MotionMode motion)
        {
            var radius = RaceConfig.PlayerRadius;
            var verticalTravel = RaceConfig.CorridorHeight - 2 * radius;
            var yVelocity = player.Flipping ? (player.FlipTo - player.FlipFrom) / RaceConfig.FlipDuration : 0;
            Collision collision = null;
            foreach (var obstacle in track.Obstacles)
            {
                if (player.HitObstacleIds.Contains(obstacle.Id)) continue;
                if (!RangeTimes(player.Distance, player.CurrentSpeed, obstacle.X - radius,
                    obstacle.X + obstacle.Width + radius, horizon, out var horizontalEnter, out var horizontalLeave)) continue;
                var bandCount = obstacle.Kind == "gate" ? 2 : 1;
                for (var band = 0; band < bandCount; band++)
                {
                    double minimum, maximum;
                    if (obstacle.Kind == "gate")
                    {
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
                    if (motion == MotionMode.Linear)
                    {
                        if (!RangeTimes(player.Y, yVelocity, minimum, maximum, horizon, out var verticalEnter, out var verticalLeave)) continue;
                        time = Math.Max(horizontalEnter, verticalEnter);
                        if (time > Math.Min(horizontalLeave, verticalLeave) + Epsilon) continue;
                    }
                    else
                    {
                        time = ContinuousMotion.FirstBandContact(player.Y * verticalTravel, player.Flipping ? player.VerticalSpeed : 0,
                            player.Flipping ? player.Gravity * RaceConfig.GravityAcceleration : 0,
                            minimum * verticalTravel, maximum * verticalTravel, horizontalEnter, horizontalLeave);
                    }
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
        private static RiskEvent NextRiskEvent(PlayerState player, Track track, double horizon, MotionMode motion, SurfaceContact surface)
        {
            RiskEvent result = null;
            foreach (var zone in track.RiskZones)
            {
                if (player.SuccessfulRiskIds.Contains(zone.Id) || player.FailedRiskIds.Contains(zone.Id)
                    || player.NegativeRiskIds.Contains(zone.Id)) continue;
                bool collected = player.CollectedRiskIds.Contains(zone.Id);
                double time;
                if (collected)
                {
                    double clearX = zone.ClearX;
                    foreach (var obstacle in track.Obstacles)
                        if (obstacle.Id == zone.ObstacleId)
                            clearX = Math.Max(clearX, obstacle.X + obstacle.Width + RaceConfig.PlayerRadius + 1e-7);
                    time = Math.Max(0, (clearX - player.Distance) / player.CurrentSpeed);
                    // An after-ring is still provisional while returning toward
                    // the stone: a later touch of its trailing edge cancels it.
                    if (zone.AfterObstacle && player.Flipping)
                    {
                        if (motion == MotionMode.Linear) time = Math.Max(time, RaceConfig.FlipDuration - player.FlipElapsed);
                        else if (surface != null && surface.Side == (player.Gravity > 0 ? 1 : 0)) time = Math.Max(time, surface.Time);
                        else continue;
                    }
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
                    if (motion != MotionMode.Linear)
                    {
                        if (!RangeTimes(player.Distance, vx, zone.X - radius, zone.X + radius,
                            horizon, out double enter, out double leave)) continue;
                        double acceleration = player.Flipping ? player.Gravity * RaceConfig.GravityAcceleration : 0;
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

        private static void AdvancePlayer(PlayerState player, Track track, double dt, double elapsed, MotionMode motion)
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
                var surface = NextSurface(player, motion);
                double flightHorizon = motion == MotionMode.Linear
                    ? (player.Flipping ? RaceConfig.FlipDuration - player.FlipElapsed : double.PositiveInfinity)
                    : (surface != null ? surface.Time : double.PositiveInfinity);
                if (motion != MotionMode.Linear && player.Flipping && player.VerticalSpeed * player.Gravity < 0)
                    flightHorizon = Math.Min(flightHorizon, -player.VerticalSpeed / (player.Gravity * RaceConfig.GravityAcceleration));
                var horizon = Math.Min(remaining, Math.Min(Math.Max(0, track.Length - player.Distance) / player.CurrentSpeed,
                    Math.Min(flightHorizon,
                        player.ActiveEffect != null ? player.ActiveEffect.Remaining : double.PositiveInfinity)));
                var collision = NextCollision(player, track, horizon, motion);
                var risk = NextRiskEvent(player, track, horizon, motion, surface);
                var moving = Math.Min(collision != null ? collision.Time : horizon, risk != null ? risk.Time : horizon);
                MovePlayer(player, moving, motion);
                remaining -= moving;
                bool collided = collision != null && collision.Time <= moving + Epsilon;
                if (collided)
                {
                    player.HitObstacleIds.Add(collision.Obstacle.Id);
                    player.Collisions++;
                    player.Recovering = RaceConfig.RecoveryDuration;
                    foreach (var zone in track.RiskZones)
                        if (!zone.IsNegative && !zone.FreeReward && zone.ObstacleId == collision.Obstacle.Id
                            && !player.SuccessfulRiskIds.Contains(zone.Id)) player.FailedRiskIds.Add(zone.Id);
                    player.LastHit = new HitInfo
                    {
                        Id = collision.Obstacle.Id,
                        Message = CollisionMessage(player, collision.Obstacle),
                        At = elapsed + dt - remaining
                    };
                }
                else if (surface != null && surface.Time <= moving + Epsilon) TouchSurface(player, surface, motion);
                if (risk != null && risk.Time <= moving + Epsilon && !player.FailedRiskIds.Contains(risk.Zone.Id))
                {
                    if (risk.Zone.IsNegative)
                    {
                        player.CollectedRiskIds.Add(risk.Zone.Id);
                        player.NegativeRiskIds.Add(risk.Zone.Id);
                        player.SlowsEarned++;
                        EnqueueEffect(player, risk.Zone);
                        player.LastSlow = new HitInfo { Id = risk.Zone.Id, Message = "Замедление +0,3 сек", At = elapsed + dt - remaining };
                    }
                    else if (!risk.Clear && !risk.Zone.FreeReward) player.CollectedRiskIds.Add(risk.Zone.Id);
                    else if (risk.Zone.FreeReward || !player.HitObstacleIds.Contains(risk.Zone.ObstacleId))
                    {
                        player.CollectedRiskIds.Add(risk.Zone.Id);
                        player.SuccessfulRiskIds.Add(risk.Zone.Id);
                        player.BoostsEarned++;
                        EnqueueEffect(player, risk.Zone);
                        player.LastSuccess = new HitInfo { Id = risk.Zone.Id, Message = "Точно! Ускорение −0,3 сек", At = elapsed + dt - remaining };
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
                        - player.BoostElapsed * (RaceConfig.BoostMultiplier - 1);
                    return;
                }
            }
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
            if (finisherCount == 2 || (game.WinnerAt.HasValue && game.Elapsed >= game.WinnerAt.Value + RaceConfig.FinishGrace - Epsilon))
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
            while (dt > 0 && game.Phase == "racing")
            {
                // Stop at the earliest possible finish so a large step cannot let
                // the other player run beyond the winner's grace deadline.
                var finishHorizon = double.PositiveInfinity;
                foreach (var player in game.Players)
                    if (!player.FinishTime.HasValue)
                        finishHorizon = Math.Min(finishHorizon,
                            (game.Track.Length - player.Distance) / (game.Track.RiskZones.Length > 0
                                ? RaceConfig.Speed * RaceConfig.BoostMultiplier : player.CurrentSpeed) + player.Recovering);
                var graceHorizon = game.WinnerAt.HasValue
                    ? game.WinnerAt.Value + RaceConfig.FinishGrace - game.Elapsed : double.PositiveInfinity;
                var step = Math.Min(dt, Math.Min(finishHorizon, Math.Max(0, graceHorizon)));
                foreach (var player in game.Players) AdvancePlayer(player, game.Track, step, game.Elapsed, game.Motion);
                game.Elapsed += step;
                dt -= step;
                ResolveFinish(game);
                if (step <= 0) break;
            }
        }
    }
}
