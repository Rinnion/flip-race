using System;

namespace FlipRace.Core
{
    public static partial class TracksRhythm
    {
        // Optional late-input branches of two ordinary compact gates. Normal
        // safe/full routes avoid both; no obstacle or positive reward is moved.
        static Track WithNegatives(Track track)
        {
            double middleX, finalX, finalY;
            if (track.Id == CascadeId) { middleX = 3977; finalX = 7705; finalY = 200; }
            else if (track.Id == DenseId) { middleX = 3831; finalX = 7709; finalY = 40; }
            else if (track.Id == WavesId) { middleX = 3385; finalX = 7725; finalY = 200; }
            else throw new ArgumentException("Unknown rhythm negative track: " + track.Id);
            var zones = new RiskZone[track.RiskZones.Length + 2];
            Array.Copy(track.RiskZones, zones, track.RiskZones.Length);
            zones[zones.Length - 2] = new RiskZone { Id = track.Id + "-hasty-middle", Kind = "negative", X = middleX, Y = 200, Radius = 8 };
            zones[zones.Length - 1] = new RiskZone { Id = track.Id + "-hasty-final", Kind = "negative", X = finalX, Y = finalY, Radius = 8 };
            track.RiskZones = zones;
            track.NegativeEnabled = true;
            return track;
        }

        public static double[] NegativeRoute(string id, FlightSettings flight)
        {
            double firstX;
            if (id == CascadeId) firstX = 7625;
            else if (id == DenseId) firstX = 7629;
            else if (id == WavesId) firstX = 7645;
            else throw new ArgumentException("Unknown rhythm negative route: " + id);
            var route = FullRoute(id, flight);
            bool fast = IsFastWitness(flight);
            // The ordinary last-gate action(s) precede the independent finish
            // preparation. Preserve that finish input and all earlier actions.
            int first = route.Length - (fast ? 2 : 3);
            route[first] = firstX + (fast ? 44 : 46);
            if (!fast) route[first + 1] = firstX + 70;
            return route;
        }
    }
}
