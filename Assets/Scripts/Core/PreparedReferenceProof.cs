using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    // A bounded, resumable proof. It owns a JSON-validated snapshot and never
    // installs or edits the process-wide catalogue (workshop requests may overlap).
    public sealed class PreparedReferenceProof
    {
        readonly TrackPackage source, package;
        readonly FlightSettings flight;
        readonly TrackBotReference reference;
        readonly double[] warnings, inputs;
        readonly HashSet<string> rewards;
        public const int MaximumTotalSteps=200000;
        public const double MaximumRaceSeconds=120;
        int configuration, cursor, completedRuns,totalSteps;
        bool master;
        GameState game;
        StochasticBotController controller;
        public bool Complete { get; private set; }
        public bool Success { get { return Complete && Failure == null; } }
        public string Failure { get; private set; }
        public string ProfileId { get { return reference == null ? "" : reference.ProfileId; } }
        public string ContentHash { get { return package == null ? "" : package.ContentHash; } }
        public int CompletedRuns { get { return completedRuns; } }
        public int RequiredRuns { get { return warnings == null ? 0 : warnings.Length * 2; } }
        public string Progress { get { return Complete ? (Success ? "Проверено" : Failure) : (master ? "Мастер" : "Маршрут") + " · " + (completedRuns + 1) + "/" + RequiredRuns; } }

        public PreparedReferenceProof(TrackPackage candidate, FlightSettings selectedFlight)
        {
            source=candidate;flight=selectedFlight;
            try
            {
                if(candidate==null||selectedFlight==null)throw new ArgumentException("Не заданы трасса или управление.");
                if(candidate.Track.Length/RaceConfig.FinishBoostSpeed>MaximumRaceSeconds)
                    throw new ArgumentException("Трасса выходит за предел 120 секунд проверки даже при максимальной скорости. Доступна игра с людьми.");
                // This validates nonempty ordered inputs, finish=1, reward IDs,
                // exact expected B, profile restrictions and the geometry hash.
                if(!TrackJson.TryRead(TrackJson.Write(candidate,false),out package,out var error))throw new ArgumentException(error);
                if(!package.TryGetReference(flight,out reference))throw new ArgumentException("Нет эталона выбранного готового профиля.");
                StochasticTrackMap.CreateForProof(package,flight,reference); // B>0, C>0, valid B/C; never clamp.
                warnings=package.WarningMode==TrackWarningMode.Selected&&HiddenObstacles.HasHidden(package.Track)
                    ? new[]{.4,.6,.8,1.0,1.2}:new[]{.8};
                inputs=reference.Inputs;rewards=new HashSet<string>(reference.RewardIds,StringComparer.Ordinal);
                BeginRun();
            }
            catch(Exception error) { Fail(error.Message); }
        }
        void Fail(string reason)
        { Failure=(master?"Мастер: ":"Маршрут: ")+reason;Complete=true; }
        void BeginRun()
        {
            cursor=0;
            if(master)
            {
                var settings=StochasticBotSettings.CreateProofMaster(package,flight,reference);
                game=RaceSimulation.CreateStochasticBotRace(package.Track,flight,settings);
                controller=new StochasticBotController(game);
            }
            else game=RaceSimulation.CreateSolo(package.Track,flight,PartyCamera.Constant,-288.8,1520);
            HiddenObstacles.ConfigureWarning(game,warnings[configuration]);
            RaceSimulation.Press(game,0);RaceSimulation.Advance(game,RaceConfig.CountdownDuration);
        }
        // At most maxSteps ordinary simulation/controller slices. UI callers
        // should also yield on a wall-clock budget between calls (one step is atomic).
        public void Step(int maxSteps=32)
        {
            if(maxSteps<1)throw new ArgumentOutOfRangeException(nameof(maxSteps));
            try
            {
                while(!Complete&&maxSteps-->0)
                {
                    if(++totalSteps>MaximumTotalSteps) { Fail("Достигнут предел шагов проверки. Доступна игра с людьми.");break; }
                    var p=game.Players[master?1:0];
                    if(p.Status!=PlayerStatus.Running||game.Phase!="racing") { EndRun(p);continue; }
                    if(game.Elapsed>MaximumRaceSeconds) { Fail("Достигнут предел 120 секунд одного забега. Доступна игра с людьми.");break; }
                    if(master)controller.Advance(1d/120);
                    else if(cursor<inputs.Length&&inputs[cursor]-p.Distance<=1e-6)
                    {
                        if(!RaceSimulation.Press(game,0)) { Fail("Недопустимое нажатие №"+(cursor+1));break; }
                        cursor++;
                    }
                    else
                    {
                        double dt=1d/120;
                        if(cursor<inputs.Length)dt=Math.Min(dt,Math.Max(1e-10,(inputs[cursor]-p.Distance)/RaceConfig.FinishBoostSpeed));
                        RaceSimulation.Advance(game,dt);
                    }
                }
            }
            catch(Exception error) { Fail(error.Message); }
        }
        void EndRun(PlayerState p)
        {
            string context=" (предупреждение "+warnings[configuration].ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" с)";
            if(p.Status!=PlayerStatus.Finished) { Fail("Не достигнут финиш"+context);return; }
            if(p.Collisions!=0||p.SlowsEarned!=0||p.FailedRiskIds.Count!=0) { Fail("Есть удар, ловушка или сорванная награда"+context);return; }
            if(!master&&cursor!=inputs.Length) { Fail("Не выполнены все нажатия"+context);return; }
            var actual=new HashSet<string>(p.SuccessfulRiskIds,StringComparer.Ordinal);
            foreach(var z in package.Track.RiskZones)if(z.IsFinish&&p.CollectedRiskIds.Contains(z.Id))actual.Add(z.Id);
            if(master?!rewards.IsSubsetOf(actual):!actual.SetEquals(rewards)) { Fail("Не совпадают выбранные награды"+context);return; }
            int ordinary=rewards.Count-1;
            double gain=(p.BoostDistanceGained+p.FinishBoostDistanceGained)/RaceConfig.Speed;
            if(p.CompletedBoosts<ordinary||p.FinishBoostsEarned!=1||Math.Abs(p.FinishBoostElapsed-RaceConfig.FinishBoostDuration)>1e-7
                ||(!master&&(p.CompletedBoosts!=ordinary||p.ActiveEffect!=null||p.PendingEffects.Count!=0||Math.Abs(gain-reference.BonusGain)>1e-7
                    ||!p.FinishTime.HasValue||Math.Abs(p.FinishTime.Value-(package.Track.Length/RaceConfig.Speed-reference.BonusGain))>1e-6)))
            { Fail("Не завершены полные эффекты наград/финиша"+context);return; }
            completedRuns++;
            if(!master)master=true;
            else { master=false;configuration++; }
            if(configuration<warnings.Length) { BeginRun();return; }
            if(!Matches(source,flight)) { Fail("Содержимое изменилось во время проверки.");return; }
            Complete=true;
        }
        // Successful tokens cannot be fabricated by a file field. Every use
        // rechecks actual geometry and the immutable reference payload.
        internal bool Matches(TrackPackage candidate,FlightSettings selectedFlight)
        {
            if(candidate==null||package==null||reference==null||!candidate.TryGetReference(selectedFlight,out var other)
                ||!TrackPackage.MatchesProfile(reference.ProfileId,selectedFlight)
                ||TrackFingerprint.Compute(candidate.Track,candidate.ProfilePolicy,candidate.WarningMode)!=ContentHash
                ||other.TrackHash!=reference.TrackHash||other.BonusGain!=reference.BonusGain)return false;
            var a=reference.Inputs;var b=other.Inputs;if(a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;
            var ids=other.RewardIds;return ids.Length==rewards.Count&&rewards.SetEquals(ids);
        }
    }
}
