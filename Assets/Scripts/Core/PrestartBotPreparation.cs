using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FlipRace.Core
{
    public sealed class PrestartBotPreparation
    {
        public const int MaximumCacheCharacters = 131072;
        const double SliceMilliseconds = 5;
        const string NoRoute = "В пределах бюджета полный маршрут пока не найден.";
        readonly Stopwatch clock = Stopwatch.StartNew();
        double[][][] bank;
        readonly double[][] selected, fastest;
        readonly PrestartBotEdge[][] selectedPersonal;
        int slot, finishedSlots, variant;
        bool cached, retriedJoint;
        double progress;
        PrestartBotSearch search;
        GameState personal, joint;
        PrestartReplayInput personalInput;
        IRaceScheduledInput[] personalSources, jointSources;
        PrestartBotIntent intent;
        int bestContacts;
        double bestGoals, bestTime, fastestTime;
        uint bestRank;

        public PrestartBotConfig Config { get; }
        public uint Seed { get; }
        public bool Complete { get; private set; }
        public bool Success => Plans != null;
        public bool UsedCache => Success && cached;
        public double Progress => Complete ? 1 : progress;
        public double ElapsedMilliseconds => clock.Elapsed.TotalMilliseconds;
        public string Failure { get; private set; }
        public int ExpandedStates { get; private set; }
        public int ReplaySteps { get; private set; }
        public int JointRejectedCommands { get; private set; }
        internal PrestartBotPlans Plans { get; private set; }

        public PrestartBotPreparation(PrestartBotConfig config, uint seed, string cacheText = null)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Seed = seed;
            selected = new double[config.SlotCount][];
            fastest = new double[config.SlotCount][];
            selectedPersonal = new PrestartBotEdge[config.SlotCount][];
            bank = ReadCache(cacheText);
            cached = bank != null;
            if (!cached) bank = new double[config.SlotCount][][];
            if (!Config.GeometryMatches()) Finish("Геометрия трассы изменилась. Подготовь новую попытку.");
        }

        // Count-based work keeps the search bounded even on a slow device.
        // A small CPU slice also limits managed allocations before WebGL's GC.
        public void Step(int workBudget = 64)
        {
            if (workBudget < 1 || workBudget > 512) throw new ArgumentOutOfRangeException(nameof(workBudget));
            long started = Stopwatch.GetTimestamp();
            for (int work = 0; work < workBudget && !Complete; work++)
            {
                Work();
                if ((work & 15) == 15 && 1000d * (Stopwatch.GetTimestamp() - started) / Stopwatch.Frequency >= SliceMilliseconds) break;
            }
        }

        public void Cancel() { if (!Complete) Finish("Подготовка отменена."); }

        void Work()
        {
            while (slot < Config.SlotCount && !Config.GetDifficulty(slot).HasValue) slot++;
            if (slot == Config.SlotCount) { VerifyJoint(); return; }
            var difficulty = Config.GetDifficulty(slot).Value;
            uint seed = PrestartBotConfig.SlotSeed(Seed, slot);
            if (bank[slot] == null)
            {
                if (search == null) search = new PrestartBotSearch(Config.BeginReplay(true), difficulty, seed);
                int before = search.Expansions;
                search.Step();
                ExpandedStates += search.Expansions - before;
                ShowProgress(.82 * search.Progress);
                if (!search.Complete) return;
                bank[slot] = search.Tapes();
                search = null;
                if (bank[slot].Length == 0) Finish(NoRoute);
                return;
            }
            if (personal == null)
            {
                personal = Config.BeginReplay(true);
                personalInput = new PrestartReplayInput(personal, 0, bank[slot][variant]);
                personalSources = new IRaceScheduledInput[] { personalInput };
                if (variant == 0) intent = new PrestartBotIntent(Config.Package.Track, difficulty, seed);
            }
            ReplaySteps++;
            RaceRunner.AdvanceScheduled(personal, PrestartBotSearch.Quantum, personalSources);
            ShowProgress(.82 + .18 * (variant + Math.Min(1, personal.Players[0].Distance / personal.Track.Length)) / bank[slot].Length);
            if (personal.Phase == "racing" && personal.Elapsed < PrestartBotSearch.MaximumRaceTime && personalInput.Rejected == 0) return;
            if (personal.Phase != "finished" || personal.Players[0].Status != PlayerStatus.Finished || !personalInput.AllRead || personalInput.Rejected != 0)
            {
                if (cached) RestartCold();
                else Finish(NoRoute);
                return;
            }
            var player = personal.Players[0];
            int contacts = intent.UndesiredContacts(player);
            double goals = intent.Preference(player), time = personal.Elapsed;
            uint rank = StochasticBotController.Mix(unchecked(seed + (uint)variant * 0x85ebca6bu));
            if (variant == 0 || Better(contacts, goals, time, rank))
            {
                selected[slot] = bank[slot][variant];
                selectedPersonal[slot] = personalInput.Accepted();
                bestContacts = contacts;
                bestGoals = goals;
                bestTime = time;
                bestRank = rank;
            }
            if (variant == 0 || time < fastestTime)
            {
                fastest[slot] = bank[slot][variant];
                fastestTime = time;
            }
            ResetPersonal();
            if (++variant < bank[slot].Length) return;
            slot++;
            variant = 0;
            finishedSlots++;
        }

        bool Better(int contacts, double goals, double time, uint rank)
        {
            if (contacts != bestContacts) return contacts < bestContacts;
            if (goals != bestGoals) return goals > bestGoals;
            if (Math.Abs(time - bestTime) > 1e-8) return time < bestTime;
            return rank < bestRank;
        }

        void VerifyJoint()
        {
            if (joint == null)
            {
                joint = Config.BeginReplay(false);
                jointSources = new IRaceScheduledInput[Config.SlotCount];
                for (int i = 0; i < jointSources.Length; i++)
                    if (selected[i] != null) jointSources[i] = new PrestartReplayInput(joint, i, selected[i]);
            }
            ReplaySteps++;
            RaceRunner.AdvanceScheduled(joint, PrestartBotSearch.Quantum, jointSources);
            progress = Math.Max(progress, .9 + .09 * Math.Min(1, joint.Elapsed / (joint.Track.Length / RaceConfig.Speed)));
            if (joint.Phase == "racing" && joint.Elapsed < PrestartBotSearch.MaximumRaceTime) return;
            if (joint.Phase != "finished" || !Config.GeometryMatches()) { Finish(NoRoute); return; }
            if (Config.HumanCount == 0 && !AnyFinished(joint))
            {
                if (!retriedJoint && SelectAlternateGroup())
                {
                    retriedJoint = true;
                    joint = null;
                    jointSources = null;
                    return;
                }
                Finish("При совместной проверке ни один бот не дошёл до финиша.");
                return;
            }
            var verified = new PrestartBotEdge[Config.SlotCount][];
            for (int i = 0; i < verified.Length; i++)
            {
                if (!(jointSources[i] is PrestartReplayInput input)) continue;
                JointRejectedCommands += input.Rejected;
                var actual = input.Accepted();
                verified[i] = Config.HumanCount == 0 ? actual : RetainPersonalTape(selectedPersonal[i], actual);
            }
            Plans = new PrestartBotPlans(verified, Seed);
            Finish(null);
        }

        static bool AnyFinished(GameState game)
        {
            foreach (var player in game.Players) if (player.Status == PlayerStatus.Finished) return true;
            return false;
        }

        bool SelectAlternateGroup()
        {
            bool changed = false;
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] == fastest[i]) continue;
                selected[i] = fastest[i];
                changed = true;
            }
            if (changed) return true;
            for (int i = 0; i < selected.Length; i++)
            {
                if (bank[i].Length < 2) continue;
                selected[i] = selected[i] == bank[i][0] ? bank[i][1] : bank[i][0];
                return true;
            }
            return false;
        }

        // Idle humans are a preparation assumption. Keep the complete verified
        // personal tape when their hypothetical actions removed a command.
        static PrestartBotEdge[] RetainPersonalTape(PrestartBotEdge[] personal, PrestartBotEdge[] joint)
        {
            int cursor = 0;
            for (int i = 0; i < personal.Length && cursor < joint.Length; i++)
            {
                if (Math.Abs(personal[i].Time - joint[cursor].Time) > 1e-8) continue;
                personal[i] = joint[cursor++];
            }
            return personal;
        }

        void ShowProgress(double personalFraction)
        { progress = Math.Max(progress, .9 * (finishedSlots + personalFraction) / (Config.SlotCount - Config.HumanCount)); }

        void ResetPersonal() { personal = null; personalInput = null; personalSources = null; }

        void RestartCold()
        {
            cached = false;
            bank = new double[Config.SlotCount][][];
            Array.Clear(selected, 0, selected.Length);
            Array.Clear(fastest, 0, fastest.Length);
            Array.Clear(selectedPersonal, 0, selectedPersonal.Length);
            ResetPersonal();
            slot = finishedSlots = variant = 0;
        }

        void Finish(string failure)
        {
            Complete = true;
            Failure = failure;
            clock.Stop();
            ResetPersonal();
            joint = null;
            jointSources = null;
            search = null;
        }

        public string ExportCache()
        {
            if (!Success) return null;
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            {
                writer.Write(Config.CacheKey);
                foreach (var variants in bank)
                {
                    writer.Write((byte)(variants?.Length ?? 0));
                    if (variants == null) continue;
                    foreach (var tape in variants)
                    {
                        writer.Write((ushort)tape.Length);
                        foreach (double time in tape) writer.Write(time);
                    }
                }
                writer.Flush();
                return Convert.ToBase64String(bytes.ToArray());
            }
        }

        double[][][] ReadCache(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaximumCacheCharacters) return null;
            try
            {
                using (var bytes = new MemoryStream(Convert.FromBase64String(text)))
                using (var reader = new BinaryReader(bytes, Encoding.UTF8, true))
                {
                    if (reader.ReadString() != Config.CacheKey) return null;
                    var data = new double[Config.SlotCount][][];
                    for (int slot = 0; slot < data.Length; slot++)
                    {
                        int variants = reader.ReadByte();
                        if (!Config.GetDifficulty(slot).HasValue) { if (variants != 0) return null; continue; }
                        if (variants < 1 || variants > PrestartBotSearch.VariantCount) return null;
                        data[slot] = new double[variants][];
                        for (int i = 0; i < variants; i++)
                        {
                            int length = reader.ReadUInt16();
                            if (length > PrestartBotSearch.MaximumInputs) return null;
                            var tape = new double[length];
                            double prior = -1;
                            for (int edge = 0; edge < length; edge++)
                            {
                                double time = reader.ReadDouble();
                                if (!(time >= 0 && time > prior + 1e-9 && time < PrestartBotSearch.MaximumRaceTime)) return null;
                                tape[edge] = prior = time;
                            }
                            data[slot][i] = tape;
                        }
                    }
                    return bytes.Position == bytes.Length ? data : null;
                }
            }
            catch (FormatException) { return null; }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
        }
    }
}
