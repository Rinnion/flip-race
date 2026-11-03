using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // Shared geometry for Chosen/Fast. These tapes are offline author witnesses,
    // never controllers or bot references; these two courses are human-only.
    public static class TracksHidden
    {
        public const string InterceptsId = "hidden-intercepts";
        public const string ManifestationId = "hidden-manifestation";
        public static readonly Track[] All = { Build(InterceptsId), Build(ManifestationId) };
        public static bool IsNew(string id) { return id == InterceptsId || id == ManifestationId; }
        public static Track Get(string id)
        { foreach (var track in All) if (track.Id == id) return track; return All[0]; }

        static double[] Starts(string id)
        { return id == InterceptsId ? new double[] { 400, 2800, 5200 } : new double[] { 400, 2200, 4000, 5800 }; }
        static int Gates(string id) { return id == InterceptsId ? 6 : 4; }
        static bool EntranceFloor(string id, int series)
        { return id == InterceptsId ? series % 2 == 0 : series < 2; }
        static bool Interior(string id, int series) { return id == ManifestationId && series % 2 == 1; }
        static double StoneX(string id, double start) { return start + Gates(id) * 320 + 40; }

        static Track Build(string id)
        {
            var obstacles = new List<Obstacle>(); var rewards = new List<RiskZone>(); var cues = new List<Cue>();
            var starts = Starts(id); int count = Gates(id);
            for (int s = 0; s < starts.Length; s++)
            {
                bool floor = EntranceFloor(id, s); double start = starts[s];
                cues.Add(new Cue(start - 230, id == InterceptsId ? "Перехват: переход и короткий возврат" : "Проявление: следи за контуром"));
                for (int g = 0; g < count; g++)
                {
                    if (Interior(id, s) && g < 2) continue;
                    string key = id + "-s" + s + "-g" + g; double x = start + g * 320;
                    bool fromFloor = Interior(id, s) ? floor : g % 4 < 2 ? floor : !floor;
                    obstacles.Add(g % 2 == 0 ? TracksRhythm.Passage(key, x, fromFloor)
                        : TracksRhythm.LowPassage(key, x, !fromFloor));
                }
                if (Interior(id, s)) obstacles.Add(new Obstacle { Id = id + "-inner-" + s, Kind = floor ? "floor" : "ceiling",
                    X = start + 160, Width = 64, Height = 48, Hidden = true });
                if (id == ManifestationId && s == 3) continue;
                bool exitFloor = Interior(id, s) ? !floor : count % 4 == 0 ? floor : !floor;
                double stoneX = StoneX(id, start); string stoneId = id + "-hidden-" + s;
                obstacles.Add(new Obstacle { Id = stoneId, Kind = exitFloor ? "floor" : "ceiling",
                    X = stoneX, Width = 64, Height = 48, Hidden = true });
                rewards.Add(new RiskZone { Id = id + "-landing-" + s, Kind = "landing", ObstacleId = stoneId,
                    X = stoneX + 32, Y = exitFloor ? 192 : 48, LandingWidth = 32 });
                // Air choice after the pad; taking it is independent of the pad.
                rewards.Add(new RiskZone { Id = id + "-air-" + s, Kind = "instant", X = stoneX + 260, Y = exitFloor ? 148 : 92, Radius = 8 });
                if (id == InterceptsId && s < 2)
                {
                    rewards.Add(new RiskZone { Id = id + "-pair-a-" + s, Kind = "instant", X = stoneX - 140, Y = exitFloor ? 134 : 106, Radius = 6 });
                    rewards.Add(new RiskZone { Id = id + "-pair-b-" + s, Kind = "instant", X = stoneX - 110, Y = exitFloor ? 134 : 106, Radius = 6 });
                }
            }
            rewards.Add(new RiskZone { Id = id + "-finish", Kind = "finish", X = 7790, Y = 120 });
            return new Track(7920, obstacles.ToArray(), cues.ToArray()) { Id = id,
                Name = id == InterceptsId ? "Перехваты" : "Проявление",
                Focus = id == InterceptsId ? "Три плотных серии, воздушный сбор и посадки" : "Четыре связки и пять проявляющихся камней",
                RiskZones = rewards.ToArray(), SafeRoute = SafeRoute(id, FlightPresets.Chosen),
                RiskRoute = RewardRoute(id, FlightPresets.Chosen) };
        }

        public static double[] SafeRoute(string id, FlightSettings flight)
        {
            bool fast = flight.FirstAction == FirstAction.Flip; var route = new List<double>();
            var starts = Starts(id);
            for (int s = 0; s < starts.Length; s++)
            {
                double start = starts[s];
                if (Interior(id, s)) { route.Add(start + 160 - (fast ? 70 : 84.2264150943396)); if (fast) route.Add(start + 156); }
                for (int g = 0; g < Gates(id); g++)
                {
                    if (Interior(id, s) && g < 2) continue;
                    double x = start + g * 320;
                    if (g % 2 == 0) { route.Add(x + (fast ? 5.1503213562917 : -19.1551226858691)); if (!fast) route.Add(x + 24.8448773141309); }
                    else if (fast) { route.Add(x - 60); route.Add(x - 27); }
                    else route.Add(x - 108.2264150943396);
                }
                if (id == ManifestationId && s == 3) continue;
                double stone = StoneX(id, start); route.Add(stone - (fast ? 70 : 84.2264150943396));
                if (fast) route.Add(stone - 4);
            }
            route.Sort(); return route.ToArray();
        }
        // One independent pad witness; Fast uses the third Intercepts pad.
        public static double[] RewardRoute(string id, FlightSettings flight)
        {
            bool fast = flight.FirstAction == FirstAction.Flip; var route = new List<double>(SafeRoute(id, flight));
            double stone = StoneX(id, Starts(id)[id == InterceptsId && fast ? 2 : 0]);
            route.Remove(stone - (fast ? 70 : 84.2264150943396)); if (fast) route.Remove(stone - 4);
            route.Add(stone - (fast ? 170 : 164.8188812646808)); if (fast) route.Add(stone - 41.8803888471439);
            route.Sort(); return route.ToArray();
        }
        public static double[] AirRoute(string id, FlightSettings flight)
        {
            bool fast = flight.FirstAction == FirstAction.Flip;
            var route = new List<double>(SafeRoute(id, flight)); double stone = StoneX(id, Starts(id)[0]);
            if (id == InterceptsId && !fast)
            {
                route.Remove(stone - 84.2264150943396); route.Add(stone - 230);
                route.Sort(); return route.ToArray();
            }
            if (id == InterceptsId && fast)
            {
                route.Remove(stone - 70); route.Remove(stone - 4);
                route.Add(stone - 180); route.Add(stone + 100);
                route.Sort(); return route.ToArray();
            }
            route.Add(stone + (fast ? 165 : 155)); if (fast) route.Add(stone + 198);
            route.Sort(); return route.ToArray();
        }
        public static double[] FinishRoute(string id, FlightSettings flight)
        {
            var route = new List<double>(SafeRoute(id, flight));
            route.Add(flight.FirstAction == FirstAction.Flip ? 7730 : 7674);
            route.Sort(); return route.ToArray();
        }
    }
}
