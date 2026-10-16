using System;
using System.Runtime.InteropServices;
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
            public bool ready, flipping;
            public double distance, y, recovering, finish;
            public int collisions, character, rings, slowRings, pendingRings, failedRings, queuedEffects;
            public double boost, slow, speed, flipElapsed;
        }
        [Serializable] sealed class Snapshot
        {
            public string mode, phase, winner, track;
            public bool paused, negativeEnabled;
            public double elapsed, countdown;
            public float fullscreenX, fullscreenY, fullscreenWidth, fullscreenHeight;
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
            // This public rectangle is already measured in CSS pixels relative
            // to the canvas origin. The HTML bridge must not scale it by DPR.
            Rect fullscreen = game.FullscreenRect;
            var snapshot = new Snapshot {mode = state.Mode, phase = state.Phase,
                track = state.Track.Id,
                negativeEnabled = state.Track.NegativeEnabled,
                winner = state.Winner == null ? "" : state.Winner.ToString(), paused = game.IsPaused,
                elapsed = state.Elapsed, countdown = state.CountdownRemaining,
                fullscreenX = fullscreen.x, fullscreenY = fullscreen.y,
                fullscreenWidth = fullscreen.width, fullscreenHeight = fullscreen.height,
                players = new PlayerSnapshot[2]};
            for (int i = 0; i < 2; i++)
            {
                var p = state.Players[i];
                int pending = 0;
                foreach (string id in p.CollectedRiskIds)
                    if (!p.SuccessfulRiskIds.Contains(id) && !p.FailedRiskIds.Contains(id) && !p.NegativeRiskIds.Contains(id)) pending++;
                snapshot.players[i] = new PlayerSnapshot {ready=p.Ready, distance=p.Distance, y=p.Y,
                    recovering=p.Recovering, flipping=p.Flipping, collisions=p.Collisions, finish=p.FinishTime ?? -1,
                    character=p.CharacterIndex, rings=p.BoostsEarned, pendingRings=pending,
                    failedRings=p.FailedRiskIds.Count, boost=p.BoostRemaining, slow=p.SlowRemaining,
                    speed=p.CurrentSpeed, slowRings=p.SlowsEarned, queuedEffects=p.PendingEffects.Count, flipElapsed=p.FlipElapsed};
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            FlipReport(JsonUtility.ToJson(snapshot));
#endif
        }
    }
}
