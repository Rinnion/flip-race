using System.Diagnostics;
using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        const double PrestartWaitLimitSeconds = 10;
        const double PrestartFrameBudgetSeconds = .004;
        bool selectedPrestartBots, pendingPrestartBots;
        PrestartBotCache prestartCache;
        PrestartBotPreparation prestartJob;
        string prestartCacheCandidate;
        double prestartStartedAt, prestartStartRequestedAt = -1;

        public bool PendingPrestartBots => pendingPrestartBots;
        public string PrestartPreparationState { get; private set; } = "idle";
        public double PrestartPreparationSeconds { get; private set; }
        public bool PrestartUsedCache { get; private set; }
        public uint PrestartAttemptSeed { get; private set; }
        public string PrestartPreparationFailure { get; private set; } = "";

        public Rect BotAlgorithmRect(bool preliminary)
        {
            MeasureFlightMenu();
            const float gap = 4;
            float w = (flightPanel.width - 32 - gap) / 2;
            float y = flightPanel.y + (flightPage == PartyRosterPage ? 202 : 174);
            return new Rect(flightPanel.x + 16 + (preliminary ? w + gap : 0), y, w, 33);
        }

        bool BotAlgorithmPointer(Vector2 p)
        {
            if (BotAlgorithmRect(false).Contains(p)) pendingPrestartBots = false;
            else if (BotAlgorithmRect(true).Contains(p)) pendingPrestartBots = true;
            else return false;
            Beep(tapClip);
            return true;
        }

        void DrawBotAlgorithms()
        {
            FlightButton(BotAlgorithmRect(false), "Прежний бот", !pendingPrestartBots, true, 11);
            FlightButton(BotAlgorithmRect(true), "Предварительный бот", pendingPrestartBots, true, 11);
        }

        GameState CreateSelectedPrestartRace(TrackPackage package, BotDifficulty?[] slots, bool standalone)
        {
            var config = new PrestartBotConfig(package, flightSelection.Settings, slots, pendingPartyCamera,
                pendingSurfaceRun, pendingSurfaceRunSettings, pendingHiddenWarning, standalone,
                PartyStartLeft, PartyWindowWidth);
            return RaceSimulation.CreatePrestartRace(config);
        }

        void ResetPrestartPreparation()
        {
            prestartJob?.Cancel();
            prestartJob = null;
            prestartCacheCandidate = null;
            prestartStartRequestedAt = -1;
            PrestartPreparationState = "idle";
            PrestartPreparationSeconds = 0;
            PrestartUsedCache = false;
            PrestartAttemptSeed = 0;
            PrestartPreparationFailure = "";
        }

        void CancelPrestartStartRequest()
        {
            if (!Game.UsesPrestartBots || prestartStartRequestedAt < 0) return;
            prestartStartRequestedAt = -1;
            for (int slot = 0; slot < Game.Players.Length; slot++)
                if (!Game.IsBotSlot(slot)) Game.Players[slot].Ready = false;
        }

        void BeginPrestartPreparation(double now)
        {
            if (prestartCache == null) prestartCache = new PrestartBotCache();
            prestartStartedAt = now;
            PrestartAttemptSeed = NextBotSeed();
            PrestartPreparationSeconds = 0;
            PrestartUsedCache = false;
            PrestartPreparationFailure = "";
            PrestartPreparationState = "searching";
            var config = Game.PrestartConfig;
            prestartCacheCandidate = prestartCache.Candidate(config.CacheKey);
            prestartJob = new PrestartBotPreparation(config, PrestartAttemptSeed, prestartCacheCandidate);
        }

        void FailPrestartPreparation(string reason)
        {
            prestartJob?.Cancel();
            prestartJob = null;
            prestartCacheCandidate = null;
            CancelPrestartStartRequest();
            PrestartPreparationFailure = string.IsNullOrEmpty(reason) ? "Маршрут не прошёл проверку." : reason;
            PrestartPreparationState = "failed";
        }

        void UpdatePrestartPreparation(double now)
        {
            if (!Game.UsesPrestartBots || paused || showTracks || showRules || Portrait || Active) return;
            if (Game.Phase == "ready" && PrestartPreparationState == "idle") BeginPrestartPreparation(now);
            if (prestartJob == null) return;

            PrestartPreparationSeconds = now - prestartStartedAt;
            if (prestartStartRequestedAt >= 0 && now - prestartStartRequestedAt >= PrestartWaitLimitSeconds)
            {
                FailPrestartPreparation("Время подготовки истекло.");
                return;
            }

            long started = Stopwatch.GetTimestamp();
            long deadline = started + (long)(PrestartFrameBudgetSeconds * Stopwatch.Frequency);
            for (int slice = 0; slice < 512 && !prestartJob.Complete; slice++)
            {
                prestartJob.Step(1);
                if (Stopwatch.GetTimestamp() >= deadline) break;
            }
            if (!prestartJob.Complete) return;
            PrestartPreparationSeconds += (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            if (prestartCacheCandidate != null && !prestartJob.UsedCache && !prestartJob.Success)
                prestartCache.Forget(Game.PrestartConfig.CacheKey);
            if (!prestartJob.Success || !RaceRunner.InstallPrestartPlans(Game, prestartJob))
            {
                FailPrestartPreparation(prestartJob.Failure);
                return;
            }

            PrestartUsedCache = prestartJob.UsedCache;
            string route = prestartJob.ExportCache();
            prestartJob = null;
            prestartCacheCandidate = null;
            PrestartPreparationState = "ready";
            bool launch = prestartStartRequestedAt >= 0 && AllPrestartHumansReady();
            prestartStartRequestedAt = -1;
            if (launch)
            {
                string phase = Game.Phase;
                if (AllBotParty) RaceRunner.StartAllBotParty(Game);
                else
                {
                    for (int slot = 0; slot < Game.Players.Length; slot++)
                    {
                        if (Game.IsBotSlot(slot)) continue;
                        RaceRunner.Press(Game, slot);
                        break;
                    }
                }
                RecordPrestartLaunch(phase);
            }
            prestartCache.Remember(Game.PrestartConfig.CacheKey, route);
        }

        void PrestartStartPointer(int humanSlot)
        {
            string phase = Game.Phase;
            if (humanSlot >= 0)
            {
                if (RaceRunner.Press(Game, humanSlot)) Beep(tapClip);
            }
            else if (RaceRunner.StartAllBotParty(Game)) Beep(tapClip);
            if (RecordPrestartLaunch(phase)) return;
            if (prestartStartRequestedAt >= 0 || !AllPrestartHumansReady()) return;
            prestartStartRequestedAt = lastFrame;
            if (prestartJob == null) BeginPrestartPreparation(lastFrame);
        }

        bool AllPrestartHumansReady()
        {
            for (int slot = 0; slot < Game.Players.Length; slot++)
                if (!Game.IsBotSlot(slot) && !Game.Players[slot].Ready) return false;
            return true;
        }

        bool RecordPrestartLaunch(string phase)
        {
            if (Game.Phase != "countdown") return false;
            if (phase == "finished") round++;
            suppressFrame = true;
            prestartStartRequestedAt = -1;
            PrestartPreparationState = "idle";
            return true;
        }

        string PrestartActionLabel(string label)
        {
            if (!Game.UsesPrestartBots) return label;
            if (prestartStartRequestedAt >= 0) return "Подготовка…";
            if (PrestartPreparationState == "failed") return "Попробовать ещё раз →";
            return label;
        }

        string PrestartSessionStatus(string title)
        {
            if (!Game.UsesPrestartBots || Active) return title;
            string seconds = PrestartPreparationSeconds < 1
                ? Number(PrestartPreparationSeconds * 1000, 0) + " мс"
                : Number(PrestartPreparationSeconds, 1) + " с";
            switch (PrestartPreparationState)
            {
                case "searching": return "Готовим маршруты · " + seconds;
                case "ready": return (PrestartUsedCache ? "Маршруты из кэша · " : "Маршруты готовы · ") + seconds;
                case "failed": return "Маршрут пока не найден · " + seconds;
                default: return title;
            }
        }
    }
}
