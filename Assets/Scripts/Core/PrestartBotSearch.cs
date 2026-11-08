using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // The planner rolls stable preferences from actual obstacle and reward IDs.
    // It never reads a reference route or an authored choice of reward IDs.
    internal sealed class PrestartBotIntent
    {
        readonly HashSet<string> desiredHits = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> desiredRewards = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> ordinaryRewards = new HashSet<string>(StringComparer.Ordinal);
        readonly uint seed;

        internal PrestartBotIntent(Track track, BotDifficulty difficulty, uint seed)
        {
            this.seed = seed;
            double quality = Quality(difficulty);
            foreach (var obstacle in track.Obstacles)
                if (Roll(obstacle.Id, 0x94d049bbu) < .2 * (1 - quality)) desiredHits.Add(obstacle.Id);
            foreach (var zone in track.RiskZones)
            {
                if (zone.IsCoin || zone.IsNegative) continue;
                ordinaryRewards.Add(zone.Id);
                if (Roll(zone.Id, 0x369dea0fu) < quality) desiredRewards.Add(zone.Id);
            }
        }

        internal static double Quality(BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Novice: return .5;
                case BotDifficulty.Amateur: return .65;
                case BotDifficulty.Confident: return .75;
                case BotDifficulty.Strong: return .9;
                case BotDifficulty.Master: return 1;
                default: throw new ArgumentOutOfRangeException(nameof(difficulty));
            }
        }

        double Roll(string id, uint salt)
        {
            unchecked
            {
                uint hash = 2166136261u ^ seed ^ salt;
                foreach (char c in id) hash = (hash ^ c) * 16777619u;
                return (StochasticBotController.Mix(hash) + .5) / 4294967296d;
            }
        }

        internal double Rank(PlayerState player, int inputCount)
        {
            double score = -player.SlowsEarned * 150 - player.FailedRiskIds.Count * 7 - inputCount * .4;
            foreach (string id in player.HitObstacleIds) score += desiredHits.Contains(id) ? 155 : -145;
            foreach (string id in player.SuccessfulRiskIds)
                if (ordinaryRewards.Contains(id)) score += desiredRewards.Contains(id) ? 52 : -46;
            return score;
        }

        internal int UndesiredContacts(PlayerState player)
        {
            int contacts = player.SlowsEarned;
            foreach (string id in player.HitObstacleIds) if (!desiredHits.Contains(id)) contacts++;
            return contacts;
        }

        internal double Preference(PlayerState player)
        {
            double score = 0;
            foreach (string id in player.HitObstacleIds) if (desiredHits.Contains(id)) score++;
            foreach (string id in player.SuccessfulRiskIds)
                if (ordinaryRewards.Contains(id)) score += desiredRewards.Contains(id) ? 1 : -1;
            return score;
        }
    }

    internal sealed class PrestartBotSearch
    {
        internal const double Quantum = .125;
        internal const double MaximumRaceTime = 120;
        internal const int MaximumInputs = 512;
        internal const int VariantCount = 3;
        const int Width = 10;
        const int MaxExpansions = 24000;
        const int TailGenerations = 5;

        sealed class Edge
        {
            internal readonly double Time;
            internal readonly Edge Prior;
            internal readonly int Count;
            internal Edge(double time, Edge prior) { Time = time; Prior = prior; Count = (prior?.Count ?? 0) + 1; }
        }

        sealed class Node
        {
            internal readonly GameState Game;
            internal readonly Edge Tape;
            internal readonly double Score;
            internal readonly int Sequence;
            internal Node(GameState game, Edge tape, double score, int sequence)
            { Game = game; Tape = tape; Score = score; Sequence = sequence; }
        }

        readonly PrestartBotIntent intent;
        readonly double[] landmarks;
        List<Node> frontier = new List<Node>(Width);
        List<Node> candidates = new List<Node>(Width * 2);
        readonly Dictionary<ulong, int> buckets = new Dictionary<ulong, int>(Width * 2);
        readonly List<Node> finishes = new List<Node>(Width * 2);
        int frontierIndex, nextAction, generation, firstFinish = -1;
        public bool Complete { get; private set; }
        public int Expansions { get; private set; }
        public double Progress => Complete ? 1 : Math.Min(.98, frontier[0].Game.Players[0].Distance / frontier[0].Game.Track.Length);

        internal PrestartBotSearch(GameState game, BotDifficulty difficulty, uint seed)
        {
            intent = new PrestartBotIntent(game.Track, difficulty, seed);
            landmarks = new double[game.Track.Obstacles.Length + game.Track.RiskZones.Length];
            int index = 0;
            foreach (var obstacle in game.Track.Obstacles) landmarks[index++] = ObstacleGeometry.Left(obstacle);
            foreach (var reward in game.Track.RiskZones) landmarks[index++] = reward.X;
            Array.Sort(landmarks);
            frontier.Add(new Node(game, null, 0, 0));
        }

        // Each work item has at most one clone, one legal input and one short
        // physics advance, so callers can yield between bounded batches.
        internal void Step()
        {
            if (Complete) return;
            Node node = frontier[frontierIndex];
            if (nextAction == 0)
            {
                nextAction = 1;
                Expand(node, false);
                return;
            }
            if (Expansions < MaxExpansions && (node.Tape?.Count ?? 0) < MaximumInputs && CanUseInput(node.Game)) Expand(node, true);
            nextAction = 0;
            if (++frontierIndex == frontier.Count || Expansions >= MaxExpansions) FinishGeneration();
        }

        bool CanUseInput(GameState game)
        {
            if (!RaceSimulation.CanPress(game, 0)) return false;
            double x = game.Players[0].Distance;
            int next = Array.BinarySearch(landmarks, x - 70);
            if (next < 0) next = ~next;
            return next < landmarks.Length && landmarks[next] <= x + 340;
        }

        void Expand(Node node, bool press)
        {
            var game = RaceSnapshot.Clone(node.Game);
            Edge edge = node.Tape;
            if (press)
            {
                if (!RaceSimulation.Press(game, 0)) return;
                edge = new Edge(game.Elapsed, edge);
            }
            RaceSimulation.Advance(game, Quantum);
            Expansions++;
            var p = game.Players[0];
            double score = intent.Rank(p, edge?.Count ?? 0);
            if (p.Status == PlayerStatus.Finished)
            {
                finishes.Add(new Node(game, edge, intent.Preference(p), Expansions));
                if (firstFinish < 0) firstFinish = generation;
                return;
            }
            if (p.Status != PlayerStatus.Running || game.Elapsed >= MaximumRaceTime) return;
            score += p.Distance;
            ulong key = StateKey(p);
            if (buckets.TryGetValue(key, out int index))
            {
                if (score > candidates[index].Score) candidates[index] = new Node(game, edge, score, Expansions);
                return;
            }
            buckets.Add(key, candidates.Count);
            candidates.Add(new Node(game, edge, score, Expansions));
        }

        void FinishGeneration()
        {
            generation++;
            if (candidates.Count == 0 || Expansions >= MaxExpansions || (firstFinish >= 0 && generation > firstFinish + TailGenerations))
            { Complete = true; return; }
            candidates.Sort(Compare);
            frontier.Clear();
            for (int i = 0; i < Math.Min(Width, candidates.Count); i++) frontier.Add(candidates[i]);
            candidates.Clear();
            buckets.Clear();
            frontierIndex = 0;
        }

        internal double[][] Tapes()
        {
            finishes.Sort(CompareFinish);
            var tapes = new List<double[]>(VariantCount);
            var outcomes = new List<PlayerState>(VariantCount);
            // Preserve the best completion. Other cache slots first preserve
            // different physical outcomes already found in this same beam.
            AddTapes(tapes, outcomes, distinctOutcome: true);
            AddTapes(tapes, outcomes, distinctOutcome: false);
            return tapes.ToArray();
        }

        void AddTapes(List<double[]> tapes, List<PlayerState> outcomes, bool distinctOutcome)
        {
            foreach (var finish in finishes)
            {
                if (tapes.Count == VariantCount) return;
                var player = finish.Game.Players[0];
                if (distinctOutcome && outcomes.Exists(prior => SameOutcome(prior, player))) continue;
                var tape = new double[finish.Tape?.Count ?? 0];
                for (var edge = finish.Tape; edge != null; edge = edge.Prior) tape[edge.Count - 1] = edge.Time;
                if (tapes.Exists(prior => SameTape(prior, tape))) continue;
                tapes.Add(tape);
                outcomes.Add(player);
            }
        }

        static bool SameOutcome(PlayerState a, PlayerState b) => a.SlowsEarned == b.SlowsEarned
            && a.HitObstacleIds.SetEquals(b.HitObstacleIds) && a.SuccessfulRiskIds.SetEquals(b.SuccessfulRiskIds);

        static bool SameTape(double[] a, double[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static int Compare(Node a, Node b)
        {
            int score = b.Score.CompareTo(a.Score);
            return score != 0 ? score : a.Sequence.CompareTo(b.Sequence);
        }

        int CompareFinish(Node a, Node b)
        {
            int contacts = intent.UndesiredContacts(a.Game.Players[0]).CompareTo(intent.UndesiredContacts(b.Game.Players[0]));
            if (contacts != 0) return contacts;
            int goals = b.Score.CompareTo(a.Score);
            if (goals != 0) return goals;
            int elapsed = a.Game.Elapsed.CompareTo(b.Game.Elapsed);
            return elapsed != 0 ? elapsed : a.Sequence.CompareTo(b.Sequence);
        }

        static ulong StateKey(PlayerState p)
        {
            unchecked
            {
                const ulong prime = 1099511628211UL;
                ulong key = (ulong)(int)(p.Distance / 11);
                key = (key ^ (ulong)(int)(p.Y * 18)) * prime;
                key = (key ^ (ulong)(int)(p.VerticalSpeed / 65)) * prime;
                key = (key ^ (ulong)(p.Gravity + 2 + (p.Flipping ? 4 : 0) + (p.AirFlipAvailable ? 8 : 0))) * prime;
                key = (key ^ (ulong)((int)p.Support * 2 + p.SupportSide)) * prime;
                key = (key ^ (ulong)(int)(p.Recovering * 16)) * prime;
                key = (key ^ (ulong)(int)(p.SurfaceRunReserve * 30)) * prime;
                key = (key ^ (ulong)(int)((p.ActiveEffect?.Remaining ?? 0) * (p.ActiveEffect?.IsNegative == true ? -10 : 10))) * prime;
                return (key ^ (ulong)(p.FinishBoost ? 1 : 0)) * prime;
            }
        }
    }
}
