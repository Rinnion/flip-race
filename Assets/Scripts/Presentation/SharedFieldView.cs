using System;
using FlipRace.Core;

namespace FlipRace.Presentation
{
    public enum SharedRewardState { Available, Pending, Succeeded, Failed, Negative }

    /// <summary>Read-only projection and personal availability for one shared world.</summary>
    public readonly struct SharedFieldView
    {
        public readonly double Width, Height, Top, Bottom, ScaleX, ScaleY;
        public readonly double CameraLeft, AnchorX, LeaderCapX, Gap;
        readonly double x0, x1, y0, y1;

        SharedFieldView(double width, double height, GameState game)
        {
            Width=width; Height=height;
            Top=Clamp(height*.095,13,25); Bottom=height-Top;
            ScaleY=(Bottom-Top)/RaceConfig.CorridorHeight;
            // Exactly the old lane's horizontal scale; height never changes it.
            double oldAnchor=Clamp(width*.19,62,195);
            ScaleX=(width-oldAnchor-20)/Math.Max(1080,width*1.28);
            // 19% hid a clean slower route for 4–7 seconds on phones. Keep
            // the original scale and move the pair anchor/cap to 25%/47%.
            AnchorX=width*.25; LeaderCapX=width*.47;
            x0=game.Players[0].Distance; x1=game.Players[1].Distance;
            y0=RaceSimulation.GetPlayerCenterY(game.Players[0]);
            y1=RaceSimulation.GetPlayerCenterY(game.Players[1]);
            double c0=Clamp(x0,0,game.Track.Length), c1=Clamp(x1,0,game.Track.Length);
            double midpoint=(c0+c1)*.5, leader=Math.Max(c0,c1);
            CameraLeft=Math.Min(game.Track.Length-AnchorX/ScaleX,
                Math.Max(midpoint-AnchorX/ScaleX,leader-LeaderCapX/ScaleX));
            Gap=Math.Abs(x0-x1);
        }

        public static SharedFieldView Create(double width,double height,GameState game)
        {
            if(game==null||width<=82||height<=26) return default(SharedFieldView);
            return new SharedFieldView(width,height,game);
        }
        public double WorldX(double x) { return (x-CameraLeft)*ScaleX; }
        public double WorldY(double y) { return Top+y*ScaleY; }
        public double PlayerX(int player) { return WorldX(player==0?x0:x1); }
        public double PlayerY(int player) { return WorldY(player==0?y0:y1); }
        public bool IsPlayerVisible(int player) { double x=PlayerX(player); return ScaleX>0&&x>=0&&x<=Width; }

        public static SharedRewardState RewardState(PlayerState player,string id)
        {
            if(player.FailedRiskIds.Contains(id)) return SharedRewardState.Failed;
            if(player.NegativeRiskIds.Contains(id)) return SharedRewardState.Negative;
            if(player.SuccessfulRiskIds.Contains(id)) return SharedRewardState.Succeeded;
            if(player.CollectedRiskIds.Contains(id)) return SharedRewardState.Pending;
            return SharedRewardState.Available;
        }
        public static bool MarkerAvailable(PlayerState player,string id)
        { return RewardState(player,id)==SharedRewardState.Available; }
        public static bool ObjectVisible(GameState game,RiskZone zone)
        {
            if(zone.IsLanding) return true; // Physical rail survives its personal rewards.
            for(int i=0;i<2;i++) {
                var state=RewardState(game.Players[i],zone.Id);
                if(state==SharedRewardState.Available||state==SharedRewardState.Pending) return true;
            }
            return false;
        }
        static double Clamp(double value,double min,double max) { return Math.Max(min,Math.Min(max,value)); }
    }
}
