using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
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
            public int coins, collisions, character, rings, instantBoosts, finishBoosts, slowRings, pendingRings, failedRings, queuedEffects;
            public double boost, slow, speed, flipElapsed, verticalSpeed, centerY, finishBoostElapsed, finishBoostDistanceGained;
            public int gravity;
            public string support, supportObstacle, lastInstant, lastFinishBoost, status;
            public int supportSide;
            public double flightDuration, flightMaxDistance, flightEndedAt;
        }
        [Serializable] sealed class Snapshot
        {
            public string mode, phase, winner, track, motion, profile, version;
            public string fieldView, seating, fieldHeight;
            public bool bot;
            public string botDifficulty, botControllerKind;
            public uint botSessionSeed;
            public long botAttemptSequenceIndex;
            public uint botSeed;
            public int botCursor, botGoalsTotal, botGoalsCompleted, botObstaclesTotal, botPositiveGoalsTotal;
            public BotTelemetry botTelemetry;
            public BotPerformanceSummary botPerformance;
            public double previousDiagnosticsMilliseconds, previousTerminalExportMilliseconds;
            public int previousDiagnosticsFrame;
            public double gravityAcceleration, departureImpulse;
            public string firstAction, motionLaw;
            public int airRepeatLimit;
            public int menuPage;
            public double flipImpulse, jumpImpulse, linearSpeed, linearJumpHeight;
            public bool pawsVisible, paused, negativeEnabled, customFlight, settingsVisible, characterSelectionVisible;
            public double elapsed, countdown, realtime;
            public int frame;
            public float fullscreenX, fullscreenY, fullscreenWidth, fullscreenHeight;
            public float settingsCopyX, settingsCopyY, settingsCopyWidth, settingsCopyHeight;
            public string settingsCopyText;
            public PlayerSnapshot[] players;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void FlipReport(string json,int frame,int cohort,uint seed);
        [DllImport("__Internal")] static extern void FlipBotPerformance(string json);
        [DllImport("__Internal")] public static extern float FlipPixelRatio();
#else
        public static float FlipPixelRatio() { return 1f; }
#endif
        FlipRaceGame game;
        double nextReport;
        double previousDiagnosticsMilliseconds,previousTerminalExportMilliseconds;
        int previousDiagnosticsFrame=-1;
        BotFramePerformance performanceAttempt;
        bool performancePublished;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap() { new GameObject("FlipDiagnostics").AddComponent<FlipDiagnostics>(); }
        void LateUpdate()
        {
            if (Time.realtimeSinceStartupAsDouble < nextReport) return;
            nextReport = Time.realtimeSinceStartupAsDouble + .05;
            if (game == null) game = FindFirstObjectByType<FlipRaceGame>();
            if (game == null || game.Game == null) return;
            long diagnosticStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            int diagnosticFrame=Time.frameCount;
            double terminalExportMilliseconds=-1;
            bool diagnosticCompleted=false;
            try
            {
            var currentPerformance=game.BotPerformance;
            if(!ReferenceEquals(currentPerformance,performanceAttempt))
            {
                performanceAttempt=currentPerformance;performancePublished=false;
                previousDiagnosticsMilliseconds=previousTerminalExportMilliseconds=0;previousDiagnosticsFrame=-1;
#if UNITY_WEBGL && !UNITY_EDITOR
                FlipBotPerformance("");
#endif
            }
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
                fieldView = game.FieldViewName, seating = game.SeatingName, fieldHeight = game.FieldHeightName,
                bot = state.IsBot, botDifficulty = state.IsBot ? state.BotDifficulty.ToString() : "",
                botControllerKind = state.StochasticBot == null ? "" : "StochasticBotController",
                botSeed = game.BotAttemptSeed, botSessionSeed = game.BotSessionSeed,
                botAttemptSequenceIndex = game.BotAttemptSequenceIndex,
                botCursor = game.BotActionCursor, botGoalsTotal = game.BotGoalsTotal,
                botGoalsCompleted = game.BotGoalsCompleted, botObstaclesTotal = game.BotObstacleCount,
                botPositiveGoalsTotal = game.BotPositiveGoalCount, botTelemetry = ReadBotTelemetry(game),
                botPerformance=ReadBotPerformance(game),
                previousDiagnosticsMilliseconds=previousDiagnosticsMilliseconds,
                previousTerminalExportMilliseconds=previousTerminalExportMilliseconds,previousDiagnosticsFrame=previousDiagnosticsFrame,
                menuPage = game.FlightMenuPage, pawsVisible = game.PawsVisible,
                gravityAcceleration = flight.GravityAcceleration,
                departureImpulse = flight.FirstAction == FirstAction.Jump ? flight.JumpImpulse : flight.FlipImpulse,
                firstAction = flight.FirstAction.ToString(), motionLaw = flight.Law.ToString(), airRepeatLimit = (int)flight.AirRepeats,
                flipImpulse = flight.FlipImpulse, jumpImpulse = flight.JumpImpulse,
                linearSpeed = flight.LinearSpeed, linearJumpHeight = flight.LinearJumpHeight,
                negativeEnabled = state.Track.NegativeEnabled,
                winner = state.Winner == null ? "" : state.Winner.ToString(), paused = game.IsPaused,
                elapsed = state.Elapsed, countdown = state.CountdownRemaining, realtime = Time.realtimeSinceStartupAsDouble, frame = Time.frameCount,
                fullscreenX = fullscreen.x, fullscreenY = fullscreen.y,
                fullscreenWidth = fullscreen.width, fullscreenHeight = fullscreen.height,
                customFlight = game.IsCustomFlight, settingsVisible = game.FlightSettingsVisible,
                characterSelectionVisible = game.CharacterSelectionVisible,
                settingsCopyX = settingsCopy.x, settingsCopyY = settingsCopy.y,
                settingsCopyWidth = settingsCopy.width, settingsCopyHeight = settingsCopy.height,
                settingsCopyText = game.FlightSettingsVisible ? game.FlightCopyText : "",
                players = new PlayerSnapshot[state.Players.Length]};
            for (int i = 0; i < state.Players.Length; i++)
            {
                var p = state.Players[i];
                int pending = 0;
                foreach (string id in p.CollectedRiskIds)
                    if (!p.SuccessfulRiskIds.Contains(id) && !p.FailedRiskIds.Contains(id) && !p.NegativeRiskIds.Contains(id)) pending++;
                snapshot.players[i] = new PlayerSnapshot {ready=p.Ready, distance=p.Distance, y=p.Y,
                    recovering=p.Recovering, flipping=p.Flipping, collisions=p.Collisions, finish=p.FinishTime ?? -1,
                    character=p.CharacterIndex, coins=p.CoinsEarned, rings=p.BoostsEarned, instantBoosts=p.InstantBoostsEarned, pendingRings=pending,
                    finishBoost=p.FinishBoost, finishBoosts=p.FinishBoostsEarned,
                    failedRings=p.FailedRiskIds.Count, boost=p.BoostRemaining, slow=p.SlowRemaining,
                    finishBoostElapsed=p.FinishBoostElapsed, finishBoostDistanceGained=p.FinishBoostDistanceGained,
                    speed=p.CurrentSpeed, slowRings=p.SlowsEarned, queuedEffects=p.PendingEffects.Count, flipElapsed=p.FlipElapsed,
                    verticalSpeed=p.VerticalSpeed, gravity=p.Gravity, airFlipAvailable=p.AirFlipAvailable,
                    status=p.Status.ToString(), support=p.Support.ToString(), supportSide=p.SupportSide, supportObstacle=p.SupportObstacleId,
                    lastInstant=p.LastInstant == null ? "" : p.LastInstant.Id,
                    lastFinishBoost=p.LastFinishBoost == null ? "" : p.LastFinishBoost.Id,
                    hasFlightMeasurement=p.LastFlight != null, flightInterrupted=p.LastFlight != null && p.LastFlight.Interrupted,
                    flightDuration=p.LastFlight == null ? 0 : p.LastFlight.Duration,
                    flightMaxDistance=p.LastFlight == null ? 0 : p.LastFlight.MaxDistance,
                    flightEndedAt=p.LastFlight == null ? 0 : p.LastFlight.EndedAt,
                    centerY=FlipRace.Core.RaceSimulation.GetPlayerCenterY(p)};
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            FlipReport(WithLayout(JsonUtility.ToJson(snapshot)),diagnosticFrame,game.BotDiagnosticCohort(diagnosticFrame),game.BotAttemptSeed);
            if(currentPerformance!=null&&currentPerformance.Sealed&&!performancePublished)
            {
                long exportStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                FlipBotPerformance(PerformanceTerminalJson(game));
                terminalExportMilliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-exportStarted)*1000d/System.Diagnostics.Stopwatch.Frequency;
                performancePublished=true;
            }
#endif
            diagnosticCompleted=true;
            }
            finally
            {
                double milliseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-diagnosticStarted)*1000d/System.Diagnostics.Stopwatch.Frequency;
                previousDiagnosticsMilliseconds=milliseconds;previousDiagnosticsFrame=diagnosticFrame;
                if(terminalExportMilliseconds>=0)previousTerminalExportMilliseconds=terminalExportMilliseconds;
                game.RecordBotDiagnosticsCost(diagnosticFrame,milliseconds,!diagnosticCompleted);
            }
        }
        // Unity's serializer does not support jagged arrays. Append only these
        // numeric, read-only rectangles as ordinary JSON arrays for browser QA.
        string WithLayout(string json)
        {
            var output = new StringBuilder(json.Length + 500);
            output.Append(json, 0, json.Length - 1).Append(",\"characterRects\":[");
            for (int player = 0; player < (game.Game.IsBot?1:game.Game.Players.Length); player++)
            {
                if (player > 0) output.Append(',');
                output.Append('[');
                for (int hero = 0; hero < CharacterRoster.Count; hero++)
                {
                    if (hero > 0) output.Append(',');
                    AppendRect(output, game.CharacterScreenRect(player, hero));
                }
                output.Append(']');
            }
            output.Append("],\"readyRects\":[");
            AppendPlayerRects(output, game.ReadyScreenRect);
            output.Append("],\"pawRects\":[");
            AppendPlayerRects(output, game.PawScreenRect);
            output.Append("],\"trackMenuRect\":"); AppendRect(output, game.TrackMenuButtonRect);
            output.Append(",\"sessionRect\":"); AppendRect(output, game.SessionBarRect);
            output.Append(",\"solo\":").Append(game.SoloEnabled ? "true" : "false");
            output.Append(",\"soloMenuRect\":"); AppendRect(output, game.SoloMenuButtonRect);
            output.Append(",\"botMenuRect\":"); AppendRect(output, game.BotMenuButtonRect);
            output.Append(",\"botDifficultyRects\":[");
            for(int i=0;i<5;i++){if(i>0)output.Append(',');AppendRect(output,game.BotDifficultyRect(i));}
            output.Append(']');
            output.Append(",\"botRetryRect\":"); AppendRect(output, game.BotRetryRect);
            output.Append(",\"botTrackChoiceRect\":"); AppendRect(output, game.BotTrackChoiceRect);
            output.Append(",\"soloTrackChoiceRect\":"); AppendRect(output, game.SoloTrackChoiceRect);
            output.Append(",\"soloCountdownRect\":"); AppendRect(output, game.SoloCountdownRect);
            output.Append(",\"viewMenuRect\":"); AppendRect(output, game.ViewMenuButtonRect);
            output.Append(",\"viewOptionRects\":[");
            for (int row = 0; row < 2; row++)
            {
                if (row > 0) output.Append(','); output.Append('[');
                AppendRect(output, game.ViewOptionRect(row, 0)); output.Append(','); AppendRect(output, game.ViewOptionRect(row, 1)); if (row == 0) { output.Append(','); AppendRect(output, game.ViewOptionRect(row, 2)); } output.Append(']');
            }
            output.Append(']');
            output.Append(",\"sharedFieldRect\":"); AppendRect(output, game.SharedFieldRect);
            output.Append(",\"controlAreas\":[");
            AppendPlayerRects(output, game.ControlAreaRect);
            output.Append(']');
            if (game.SplitGhostEnabled)
            {
                output.Append(",\"ghostPoints\":[");
                for (int observer = 0; observer < 2; observer++)
                {
                    if (observer > 0) output.Append(',');
                    Vector2 point = game.GhostScreenPoint(observer);
                    output.Append('[').Append(point.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                        .Append(point.y.ToString("R", CultureInfo.InvariantCulture)).Append(']');
                }
                output.Append(']');
            }
            if (game.SharedEnabled)
            {
                Rect field = game.SharedFieldRect;
                var view = SharedFieldView.Create(field.width, field.height, game.Game);
                output.Append(",\"cameraLeft\":").Append(view.CameraLeft.ToString("R", CultureInfo.InvariantCulture));
                output.Append(",\"scaleX\":").Append(view.ScaleX.ToString("R", CultureInfo.InvariantCulture));
                output.Append(",\"projectedPlayers\":[");
                for (int player = 0; player < game.Game.Players.Length; player++)
                {
                    if (player > 0) output.Append(',');
                    output.Append('[').Append((field.x + view.PlayerX(player)).ToString("R", CultureInfo.InvariantCulture))
                        .Append(',').Append((field.y + view.PlayerY(player)).ToString("R", CultureInfo.InvariantCulture)).Append(']');
                }
                output.Append(']');
            }
                        output.Append(",\"party\":").Append(game.PartyEnabled ? "true" : "false");
            output.Append(",\"partyCamera\":\"").Append(game.Game.PartyCamera.ToString()).Append('"');
            output.Append(",\"logicalWindowWidth\":").Append(game.Game.PartyWindowWidth.ToString("R", CultureInfo.InvariantCulture));
            output.Append(",\"partyMenuRect\":"); AppendRect(output, game.PartyMenuButtonRect);
            output.Append(",\"partyCountRects\":[");
            for(int i=0;i<3;i++){if(i>0)output.Append(',');AppendRect(output,game.PartyCountRect(i));}
            output.Append("],\"partyCameraRects\":[");
            for(int i=0;i<2;i++){if(i>0)output.Append(',');AppendRect(output,game.PartyCameraRect(i));}
            output.Append("],\"partyLegacyRect\":"); AppendRect(output,game.PartyLegacyRect);
            return output.Append('}').ToString();
        }
        void AppendPlayerRects(StringBuilder output, Func<int,Rect> rectangle)
        { for(int i=0;i<(game.Game.IsBot?1:game.Game.Players.Length);i++){if(i>0)output.Append(',');AppendRect(output,rectangle(i));} }
        [Serializable] sealed class TimingSummary
        {
            public int count, over50;
            public double sumMilliseconds,maximumMilliseconds,p95UpperBoundMilliseconds;
        }
        [Serializable] sealed class BotPerformanceSummary
        {
            public int capacity,storedFrames,totalFrames,activeFrames,countdownFrames,pausedFrames,suppressedFrames,zeroPlanActiveFrames,overflowFrames,terminalFrame,captureFaultFrames,activeCaptureFaultFrames;
            public bool terminal,sealedSamples,complete;
            public double histogramBinMilliseconds;
            public string scope;
            public TimingSummary advance,diagnostics,combined;
        }
        static TimingSummary Timing(BotTimingDistribution d)
        {return new TimingSummary{count=d.Count,over50=d.Over50,sumMilliseconds=d.Sum,maximumMilliseconds=d.Maximum,p95UpperBoundMilliseconds=d.P95UpperBound};}
        static BotPerformanceSummary ReadBotPerformance(FlipRaceGame game)
        {
            var p=game.BotPerformance;if(p==null)return null;
            return new BotPerformanceSummary{capacity=BotFramePerformance.Capacity,storedFrames=p.StoredCount,totalFrames=p.TotalFrames,activeFrames=p.ActiveFrames,
                countdownFrames=p.CountdownFrames,pausedFrames=p.PausedFrames,suppressedFrames=p.SuppressedFrames,zeroPlanActiveFrames=p.ZeroPlanActiveFrames,overflowFrames=p.OverflowFrames,
                terminal=p.Terminal,sealedSamples=p.Sealed,complete=p.Sealed&&p.OverflowFrames==0&&p.ActiveCaptureFaultFrames==0,terminalFrame=p.TerminalFrame,captureFaultFrames=p.CaptureFaultFrames,activeCaptureFaultFrames=p.ActiveCaptureFaultFrames,histogramBinMilliseconds=BotTimingDistribution.BinMilliseconds,
                scope="All active bot frames, including zero-plan frames and the terminal transition. Advance includes ordinary shared simulation. Diagnostics includes construction, JSON and synchronous publication ONLY; async HTML callbacks are measured separately in the persistent terminal export. Paused/countdown/suppressed cohorts are separate; post-bot-terminal continuation is excluded. P95 is a .05ms histogram upper bound, saturated-bin bound uses exact maximum. Previous completed diagnostic cost is reported by frame; persistent terminal export cost appears in subsequent scalar reports until the next attempt and is outside the frozen active cohort.",
                advance=Timing(p.Advance),diagnostics=Timing(p.Diagnostics),combined=Timing(p.Combined)};
        }
        // Pure managed terminal serialization is shared with the scoped check.
        static string PerformanceTerminalJson(FlipRaceGame game)
        {
            var p=game.BotPerformance;if(p==null||!p.Sealed)return "";
            var b=new StringBuilder(p.StoredCount*150+512);
            b.Append("{\"schema\":1,\"attemptSeed\":").Append(game.BotAttemptSeed).Append(",\"sessionSeed\":").Append(game.BotSessionSeed)
                .Append(",\"attemptSequence\":").Append(game.BotAttemptSequenceIndex).Append(",\"capacity\":").Append(BotFramePerformance.Capacity)
                .Append(",\"totalFrames\":").Append(p.TotalFrames).Append(",\"overflowFrames\":").Append(p.OverflowFrames)
                .Append(",\"captureFaultFrames\":").Append(p.CaptureFaultFrames).Append(",\"activeCaptureFaultFrames\":").Append(p.ActiveCaptureFaultFrames)
                .Append(",\"terminalFrame\":").Append(p.TerminalFrame).Append(",\"frames\":[");
            var frames=p.CopySamples();
            for(int i=0;i<frames.Length;i++)
            {
                if(i>0)b.Append(',');var f=frames[i];
                b.Append("{\"frame\":").Append(f.Frame).Append(",\"cohort\":\"").Append(f.Cohort).Append("\",\"dt\":").Append(f.Delta.ToString("R",CultureInfo.InvariantCulture))
                    .Append(",\"elapsed\":").Append(f.Elapsed.ToString("R",CultureInfo.InvariantCulture)).Append(",\"advanceInvoked\":").Append(f.AdvanceInvoked?"true":"false")
                    .Append(",\"planCalls\":").Append(f.PlanCalls).Append(",\"advanceMilliseconds\":").Append(f.AdvanceMilliseconds.ToString("R",CultureInfo.InvariantCulture))
                    .Append(",\"diagnosticsMilliseconds\":").Append(f.DiagnosticsMilliseconds.ToString("R",CultureInfo.InvariantCulture)).Append(",\"diagnosticCalls\":").Append(f.DiagnosticCalls).Append(",\"captureFault\":").Append(f.CaptureFault?"true":"false").Append('}');
            }
            return b.Append("]}").ToString();
        }
        [Serializable] sealed class BotDecisionSnapshot
        {
            public string id, kind, intent, outcome, unreachableReason;
            public double probability, roll, atDistance, atTime, horizonEndDistance;
            public double[] selectedInputs;
        }
        [Serializable] sealed class BotPlanSnapshot
        {
            public int number, candidates, forecastSteps, rollbacks;
            public double atDistance, horizonEndDistance, milliseconds;
        }
        [Serializable] sealed class BotTelemetry
        {
            public double q, pCollect, pHit, maximumRequestedHorizon, maximumTrialHorizon, maximumTrialOvershoot;
            public int decisionCount, planningCalls, candidateSimulations, forecastSteps, forecastObservations, horizonViolations, forecastRollbacks, suppressedInputs;
            public string lastPlanningFailure;
            public BotDecisionSnapshot[] decisions;
            public BotPlanSnapshot lastPlan;
            public BotPlanSnapshot[] planningSamples;
        }
        static BotPlanSnapshot PlanSnapshot(BotPlanningSample p)
        { return p==null?null:new BotPlanSnapshot{number=p.Number,atDistance=p.AtDistance,horizonEndDistance=p.HorizonEndDistance,candidates=p.Candidates,forecastSteps=p.ForecastSteps,rollbacks=p.Rollbacks,milliseconds=p.Milliseconds}; }
        static BotTelemetry ReadBotTelemetry(FlipRaceGame game)
        {
            if(!game.BotEnabled || game.Game.StochasticBot==null)return null;
            var decisions=game.BotDecisions;var samples=game.BotPlanningSamples;
            var result=new BotTelemetry{q=game.BotQuality,pCollect=game.BotCollectProbability,pHit=game.BotHitProbability,
                decisionCount=decisions.Count,planningCalls=game.BotPlanningCalls,candidateSimulations=game.BotCandidateSimulations,
                forecastSteps=game.BotForecastSteps,forecastObservations=game.BotForecastObservations,horizonViolations=game.BotForecastHorizonViolations,
                forecastRollbacks=game.BotForecastRollbacks,suppressedInputs=game.BotSuppressedInputs,maximumRequestedHorizon=game.BotMaximumRequestedHorizon,
                maximumTrialHorizon=game.BotMaximumTrialHorizon,maximumTrialOvershoot=game.BotMaximumTrialOvershoot,
                lastPlanningFailure=game.BotLastPlanningFailure,lastPlan=PlanSnapshot(game.BotLastPlan),
                decisions=new BotDecisionSnapshot[decisions.Count],planningSamples=new BotPlanSnapshot[samples.Count]};
            for(int i=0;i<decisions.Count;i++)
            {var d=decisions[i];result.decisions[i]=new BotDecisionSnapshot{id=d.Id,kind=d.Kind.ToString(),intent=d.Intent,probability=d.Probability,roll=d.Roll,atDistance=d.AtDistance,atTime=d.AtTime,horizonEndDistance=d.HorizonEndDistance,outcome=d.Outcome,unreachableReason=d.UnreachableReason,selectedInputs=d.SelectedInputs};}
            for(int i=0;i<samples.Count;i++)result.planningSamples[i]=PlanSnapshot(samples[i]);
            return result;
        }
        static void AppendRect(StringBuilder output, Rect rect)
        {
            output.Append('[').Append(rect.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(rect.y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(rect.width.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(rect.height.ToString("R", CultureInfo.InvariantCulture)).Append(']');
        }
    }
}
