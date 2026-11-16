using System.Text.Json.Serialization;
using FlipRace.Core;

namespace TrackWorkshop;

public sealed record AnalysisTargetRequest(string Json, string Profile, double WarningDuration, string Axis, double[] Inputs,
    [property: JsonRequired] int InputIndex, WorkshopSurfaceRunSettings SurfaceRunSettings = null);
public sealed record AnalysisTargetResult(string Kind, int InputIndex, int? FirstInputIndex, string Message);

internal static class AnalysisTarget
{
    internal static AnalysisTargetResult Resolve(AnalysisTargetRequest request, CancellationToken cancellation, TimeSpan? budget = null)
    {
        cancellation.ThrowIfCancellationRequested();
        if (request == null) throw new ArgumentException("Пустой запрос.");
        WorkshopService.ValidateAxis(request.Axis);
        WorkshopService.ValidateWarning(request.WarningDuration);
        WorkshopService.ValidateInputs(request.Inputs);
        int index = request.InputIndex;
        if (index < 0 || index >= request.Inputs.Length)
            throw new ArgumentException("Укажите номер исходного нажатия.");

        AnalysisTargetResult Pair(int first) => new("pair", index, first, "");
        AnalysisTargetResult Single() => new("single", index, null, "");
        AnalysisTargetResult Unsupported(string message) => new("unsupported", index, null, message);

        using var deadline = InputToleranceAnalysis.Deadline(cancellation, budget);
        var token = deadline.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            var track = WorkshopService.Read(request.Json).Package;
            var flight = WorkshopService.GetFlight(track, request.Profile);
            var game = WorkshopService.StartSolo(track.Track, flight, request.WarningDuration, request.SurfaceRunSettings);
            RaceSimulation.Advance(game, RaceConfig.CountdownDuration);
            var player = game.Players[0];
            InputResult previous = null;
            bool flightActiveAtPreviousPress = false;
            FlightMeasurement lastFlightAtPreviousPress = null;
            int next = 0;
            while (WorkshopService.IsRacing(game))
            {
                // A scheduler step either dispatches a press or advances Core.
                // LastFlight catches a landing even when Core then walks off a
                // stone during the same advance and is already airborne again.
                bool continuingFlight = flightActiveAtPreviousPress && player.FlightActive &&
                    player.LastFlight == lastFlightAtPreviousPress;
                if (next == index + 1 && (!continuingFlight || next == request.Inputs.Length))
                {
                    token.ThrowIfCancellationRequested();
                    return Single();
                }
                var command = WorkshopService.ReplayStep(game, request.Axis, request.Inputs, ref next, cancellationToken: token);
                if (command == null) continue;
                token.ThrowIfCancellationRequested();

                bool accepted = command.Status == "accepted";
                bool adjacentPair = accepted && previous?.Status == "accepted" && continuingFlight && command.Time > previous.Time;
                if (command.Index == index)
                {
                    if (!accepted) return Unsupported($"Исходная команда не принята: {command.Reason}.");
                    if (adjacentPair) return Pair(index - 1);
                }
                else if (command.Index == index + 1)
                    return adjacentPair ? Pair(index) : Single();

                previous = command;
                flightActiveAtPreviousPress = player.FlightActive;
                lastFlightAtPreviousPress = player.LastFlight;
            }

            token.ThrowIfCancellationRequested();
            return next > index ? Single() : Unsupported("Заезд закончился до исполнения выбранной команды.");
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            return Unsupported(InputToleranceAnalysis.BudgetMessage);
        }
    }
}
