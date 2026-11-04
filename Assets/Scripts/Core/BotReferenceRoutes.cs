using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    public sealed class BotReferenceRoute
    {
        readonly double[] inputs;
        readonly string[] rewardIds;
        public double[] Inputs { get { return (double[])inputs.Clone(); } }
        public string[] RewardIds { get { return (string[])rewardIds.Clone(); } }
        public double BonusGain { get; private set; }
        internal BotReferenceRoute(double[] inputs,string[] rewardIds)
        {
            this.inputs=(double[])inputs.Clone();this.rewardIds=(string[])rewardIds.Clone();
            BonusGain=(rewardIds.Length-1)*RaceConfig.BoostTimeGain
                +RaceConfig.FinishBoostDuration*(RaceConfig.FinishBoostSpeed/RaceConfig.Speed-1);
        }
    }

    // One authored compatible reference per supported pair. Geometry and all
    // physical rewards remain untouched; this is not a runtime branch selector.
    public static class BotReferenceRoutes
    {
        public static bool Supports(string trackId,FlightSettings flight)
        { return TracksHidden.IsNew(trackId)?Same(flight,FlightPresets.Chosen)||Same(flight,FlightPresets.FastFlip)
            :trackId==TracksRethink.Id&&TracksRethink.IsSupported(flight); }
        public static BotReferenceRoute Get(string trackId,FlightSettings flight)
        {
            if(!Supports(trackId,flight))throw new NotSupportedException("No authored bot reference for this track and exact flight profile.");
            if(trackId==TracksRethink.Id) {
                var r=new List<double>(TracksRethink.BonusRoute()){2140,7674};
                r.RemoveAll(x=>x==6740||x==6784||x==7050||x==7094);r.Add(6695.1811187353192);r.Sort();
                return new BotReferenceRoute(r.ToArray(),new[]{"rethink-m2-air","rethink-m3-price-bonus","rethink-m6-landing","rethink-finish"});
            }
            bool fast=Same(flight,FlightPresets.FastFlip),intercepts=trackId==TracksHidden.InterceptsId;
            var route=new List<double>(TracksHidden.SafeRoute(trackId,flight));var rewards=new List<string>();
            double[] stones=intercepts?new double[]{2360,4760,7160}:new double[]{1720,3520,5320};
            for(int s=0;s<stones.Length;s++) {
                double stone=stones[s];Remove(route,stone-(fast?70:84.2264150943396));if(fast)Remove(route,stone-4);
                if(intercepts&&s<2) {
                    if(fast){route.Add(stone-180);route.Add(stone-70);route.Add(stone+150);route.Add(stone+200);}
                    else route.Add(stone-230);
                    rewards.Add(trackId+"-pair-a-"+s);rewards.Add(trackId+"-pair-b-"+s);
                    rewards.Add(trackId+"-landing-"+s);
                }
                else {
                    route.Add(stone-(fast?170:164.8188812646808));if(fast)route.Add(stone-41.8803888471439);
                    route.Add(stone+(fast?150:210));if(fast)route.Add(stone+200);
                    rewards.Add(trackId+"-landing-"+s);
                    if(!intercepts&&!fast&&s==1){Remove(route,3980.844877314131);Remove(route,4024.844877314131);route.Add(3940);}
                }
                if(!intercepts||fast||s==2)rewards.Add(trackId+"-air-"+s);
            }
            route.Add(fast?7730:7674);route.Sort();rewards.Add(trackId+"-finish");
            return new BotReferenceRoute(route.ToArray(),rewards.ToArray());
        }
        static void Remove(List<double> route,double x){route.RemoveAll(v=>Math.Abs(v-x)<1e-6);}
        static bool Same(FlightSettings a,FlightSettings b)
        { return a!=null&&a.FirstAction==b.FirstAction&&a.Law==b.Law&&a.AirRepeats==b.AirRepeats
            &&a.GravityAcceleration==b.GravityAcceleration&&a.FlipImpulse==b.FlipImpulse&&a.JumpImpulse==b.JumpImpulse
            &&a.LinearSpeed==b.LinearSpeed&&a.LinearJumpHeight==b.LinearJumpHeight&&a.LegacyLinear==b.LegacyLinear; }
    }
}
