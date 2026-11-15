using System.Text.Json.Serialization;
using FlipRace.Core;

namespace TrackWorkshop;

// The public names mirror the game's settings and preserve the effective
// values even when the master switch is off.
public sealed record WorkshopSurfaceRunSettings(
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] int RatePercent,
    [property: JsonRequired] string Curve,
    [property: JsonRequired] int CapPercent,
    [property: JsonRequired] int AirDecayPercent,
    [property: JsonRequired] bool ResetOnCollision)
{
    internal static WorkshopSurfaceRunSettings Legacy { get; } = FromCore(false, SurfaceRunSettings.Default);

    internal static WorkshopSurfaceRunSettings FromCore(bool enabled, SurfaceRunSettings settings) =>
        new(enabled, settings.RatePercent, settings.Curve.ToString(), settings.CapPercent,
            settings.AirDecayPercent, settings.ResetOnCollision);

    internal SurfaceRunSettings ToCore()
    {
        var curve = Curve switch
        {
            "Linear" => SurfaceRunCurve.Linear,
            "Exponential" => SurfaceRunCurve.Exponential,
            _ => throw new ArgumentException("Кривая разгона должна быть Linear или Exponential.")
        };
        try { return new(RatePercent, curve, CapPercent, AirDecayPercent, ResetOnCollision); }
        catch (ArgumentOutOfRangeException error)
        { throw new ArgumentException($"Недопустимое значение параметра разгона {error.ParamName}.", error); }
    }
}
