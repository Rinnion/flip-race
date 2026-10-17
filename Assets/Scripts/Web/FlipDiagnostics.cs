using System;
using System.Runtime.InteropServices;
using FlipRace.Core;
using FlipRace.Presentation;
using UnityEngine;

namespace FlipRace.Web
{
    // Read-only local diagnostics, exposed on the canvas for repeatable browser QA.
    // No network, commands, storage, or gameplay changes.
    public sealed class FlipDiagnostics : MonoBehaviour
    {
        [Serializable] sealed class PlayerSnapshot
        {
            public bool ready, flipping, airFlipAvailable, finishBoost, hasFlightMeasurement, flightInterrupted;
            public double distance, y, recovering, finish;
            public int collisions, character, rings, instantBoosts, finishBoosts, slowRings, pendingRings, failedRings, queuedEffects;
            public double boost, slow, speed, flipElapsed, verticalSpeed, centerY;
            public int gravity;
            public string support, supportObstacle, lastInstant, lastFinishBoost;
            public int supportSide;
            public double flightDuration, flightMaxDistance, flightEndedAt;
        }
        [Serializable] sealed class Snapshot
        {
            public string mode, phase, winner, track, motion, profile, version;
            public double gravityAcceleration, departureImpulse;
            public string firstAction, motionLaw;
            public int airRepeatLimit;
            public double flipImpulse, jumpImpulse, linearSpeed, linearJumpHeight;
            public bool paused, negativeEnabled, customFlight, settingsVisible;
            public double elapsed, countdown;
            public float fullscreenX, fullscreenY, fullscreenWidth, fullscreenHeight;
            public float settingsCopyX, settingsCopyY, settingsCopyWidth, settingsCopyHeight;
            public string settingsCopyText;
            public PlayerSnapshot[] players;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void FlipReport(string json);
        [DllImport("__Internal")] public static extern float FlipPixelRatio();
#else
        public static float FlipPixelRatio() { return 1f; }
#endif
        FlipRaceGame game;
        double nextReport;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() { new GameObject("FlipDiagnostics").AddComponent<FlipDiagnostics>(); }
        void LateUpdate()
        {
            if (Time.realtimeSinceStartupAsDouble < nextReport) return;
            nextReport = Time.realtimeSinceStartupAsDouble + .05;
            if (game == null) game = FindFirstObjectByType<FlipRaceGame>();
            if (game == null || game.Game == null) return;
            var state = game.Game;
            var flight = state.Flight ?? FlightPresets.FromLegacy(state.Motion, state.Profile);
            // This public rectangle is already measured in CSS pixels relative
            // to the canvas origin. The HTML bridge must not scale it by DPR.
            Rect fullscreen = game.FullscreenRect;
            Rect settingsCopy = game.FlightCopyRect;
            var snapshot = new Snapshot {mode = state.Mode, phase = state.Phase,
                track = state.Track.Id,
                motion = state.Motion.ToString(),
                profile = state.Profile.Id, version = Application.version,
                gravityAcceleration = flight.GravityAcceleration,
                departureImpulse = flight.FirstAction == FirstAction.Jump ? flight.JumpImpulse : flight.FlipImpulse,
                firstAction = flight.FirstAction.ToString(), motionLaw = flight.Law.ToString(), airRepeatLimit = (int)flight.AirRepeats,
                flipImpulse = flight.FlipImpulse, jumpImpulse = flight.JumpImpulse,
                linearSpeed = flight.LinearSpeed, linearJumpHeight = flight.LinearJumpHeight,
                negativeEnabled = state.Track.NegativeEnabled,
                winner = state.Winner == null ? "" : state.Winner.ToString(), paused = game.IsPaused,
                elapsed = state.Elapsed, countdown = state.CountdownRemaining,
                fullscreenX = fullscreen.x, fullscreenY = fullscreen.y,
                fullscreenWidth = fullscreen.width, fullscreenHeight = fullscreen.height,
                customFlight = game.IsCustomFlight, settingsVisible = game.FlightSettingsVisible,
                settingsCopyX = settingsCopy.x, settingsCopyY = settingsCopy.y,
                settingsCopyWidth = settingsCopy.width, settingsCopyHeight = settingsCopy.height,
                settingsCopyText = game.FlightSettingsVisible ? game.FlightCopyText : "",
                players = new PlayerSnapshot[2]};
            for (int i = 0; i < 2; i++)
            {
                var p = state.Players[i];
                int pending = 0;
                foreach (string id in p.CollectedRiskIds)
                    if (!p.SuccessfulRiskIds.Contains(id) && !p.FailedRiskIds.Contains(id) && !p.NegativeRiskIds.Contains(id)) pending++;
                snapshot.players[i] = new PlayerSnapshot {ready=p.Ready, distance=p.Distance, y=p.Y,
                    recovering=p.Recovering, flipping=p.Flipping, collisions=p.Collisions, finish=p.FinishTime ?? -1,
                    character=p.CharacterIndex, rings=p.BoostsEarned, instantBoosts=p.InstantBoostsEarned, pendingRings=pending,
                    finishBoost=p.FinishBoost, finishBoosts=p.FinishBoostsEarned,
                    failedRings=p.FailedRiskIds.Count, boost=p.BoostRemaining, slow=p.SlowRemaining,
                    speed=p.CurrentSpeed, slowRings=p.SlowsEarned, queuedEffects=p.PendingEffects.Count, flipElapsed=p.FlipElapsed,
                    verticalSpeed=p.VerticalSpeed, gravity=p.Gravity, airFlipAvailable=p.AirFlipAvailable,
                    support=p.Support.ToString(), supportSide=p.SupportSide, supportObstacle=p.SupportObstacleId,
                    lastInstant=p.LastInstant == null ? "" : p.LastInstant.Id,
                    lastFinishBoost=p.LastFinishBoost == null ? "" : p.LastFinishBoost.Id,
                    hasFlightMeasurement=p.LastFlight != null, flightInterrupted=p.LastFlight != null && p.LastFlight.Interrupted,
                    flightDuration=p.LastFlight == null ? 0 : p.LastFlight.Duration,
                    flightMaxDistance=p.LastFlight == null ? 0 : p.LastFlight.MaxDistance,
                    flightEndedAt=p.LastFlight == null ? 0 : p.LastFlight.EndedAt,
                    centerY=FlipRace.Core.RaceSimulation.GetPlayerCenterY(p)};
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            FlipReport(JsonUtility.ToJson(snapshot));
#endif
        }
    }
}
