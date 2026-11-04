using System;
using System.Linq;
using FlipRace.Core;

static class BaselineCapture
{
    internal static object Geometry(Track t) => new {
        t.Id,t.Name,t.Focus,t.Length,t.NegativeEnabled,
        obstacles=t.Obstacles.Select(o=>new{o.Id,o.Kind,o.X,o.Width,o.Height,o.GapTop,o.GapBottom,o.TopOffsetX,o.BottomOffsetX,o.Hidden,o.HiddenRevealDistance,o.HiddenWarningDuration}).ToArray(),
        cues=t.Cues.Select(c=>new{c.X,c.Text}).ToArray(),
        rewards=t.RiskZones.Select(z=>new{z.Id,z.ObstacleId,z.Kind,z.Placement,z.FreeReward,z.X,z.Y,z.Radius,z.ClearX,z.SafeFlipX,z.RiskFlipX,z.LandingWidth,z.FinishWidth,z.FinishHeight}).ToArray(),
        t.SafeRoute,t.RiskRoute,
        profiles=t.Id==TracksRethink.Id?new[]{"Chosen"}:new[]{"Chosen","FastFlip"},
        warningMode=t.Id==TracksRethink.Id?"authored":"selected",
        cameraSpeed=t.Length/36d,cameraStartLeft=-288.8,cameraWindowWidth=1520
    };
    internal static object BotMap(Track t,FlightSettings f)
    {
        var m=StochasticTrackMap.Create(t,f);
        return new {trackId=t.Id,m.ProfileId,m.BaseTime,m.MasterTime,m.BonusGain,m.CollisionCostSum,
            inputs=m.ReferenceInputs,
            opportunities=m.Opportunities.Select(o=>new{o.Id,kind=o.Kind.ToString(),o.Position,o.EndPosition,o.ObstacleId}).ToArray(),
            rewards=m.Opportunities.Where(o=>o.Kind!=BotOpportunityKind.Obstacle).Select(o=>o.Id.Substring(o.Id.IndexOf(':')+1)).ToArray(),
            probabilities=Enum.GetValues(typeof(BotDifficulty)).Cast<BotDifficulty>().Select(d=>{
                var s=StochasticBotSettings.CreateInitial(t,f,d,0);return new{difficulty=d.ToString(),s.Quality,s.CollectProbability,s.HitProbability};}).ToArray()};
    }
}
