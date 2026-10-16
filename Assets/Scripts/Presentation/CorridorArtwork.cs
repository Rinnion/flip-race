using System;
using FlipRace.Core;
using UnityEngine;

namespace FlipRace.Presentation
{
    /// <summary>
    /// Procedural IMGUI corridor artwork ported from the original Canvas renderer.
    /// The caller supplies a locally upright lane matrix. This renderer mirrors
    /// player zero's world X; the caller's outer 180-degree rotation compensates it.
    /// No gameplay state is changed here.
    /// </summary>
    public sealed class CorridorArtwork
    {
        public struct Geometry
        {
            public float X, Y, Radius, Rotation, Top, Bottom, ScaleX, ScaleY, Anchor, FinishX;
            public float MotionTime, FlipProgress;
            public float PracticeMarkerX;
            public bool MirrorX, Running, Recovering, ShowPracticeMarker, AtPracticeMarker;
        }

        static readonly Color[] Ink = { Hex(0x7860a6), Hex(0xcf7962) };
        static readonly Color[] Pale = { Hex(0xe9e0f4), Hex(0xf8e0d5) };
        static readonly Color[] Confetti = { Hex(0xb6c993), Hex(0xe8ba71), Hex(0xbba5d8), Hex(0xe2a18d), Hex(0x91b9bf) };
        readonly Texture2D circle, star, leaf, floorRock, ceilingRock;
        // Explicit lane-local clipping avoids Unity GUIClip's interaction with
        // rotated/scaled matrices (the upper player's entire lane is rotated).
        Rect clipBounds;
        readonly float[] jumpRotation = new float[2];
        readonly float[] jumpRotationTime = { -1, -1 };

        public CorridorArtwork()
        {
            circle = MakeCircle();
            var sparkle = new Painter(128, 128, true);
            var points = new float[16];
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                float radius = i % 2 == 0 ? 61 : 18.3f;
                points[i * 2] = 64 + Mathf.Cos(angle) * radius;
                points[i * 2 + 1] = 64 + Mathf.Sin(angle) * radius;
            }
            sparkle.Polygon(Color.white, points);
            star = sparkle.Finish("Corridor sparkle");
            var sprout = new Painter(128, 128);
            sprout.Polygon(Color.white, 22, 102, 21, 78, 30, 53, 48, 34, 76, 20, 113, 10,
                109, 41, 97, 65, 78, 81, 55, 93);
            sprout.Line(26, 98, 99, 24, Hex(0x819b62), 5);
            leaf = sprout.Finish("Corridor leaf");
            floorRock = MakeRock(false);
            ceilingRock = MakeRock(true);
        }

        static Texture2D MakeCircle()
        {
            const int size = 256;
            const float center = size / 2f;
            const float radius = center - 1;
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + .5f - center, dy = y + .5f - center;
                    float coverage = Mathf.Clamp01(radius + .5f - Mathf.Sqrt(dx * dx + dy * dy));
                    // Preserve white RGB even in transparent texels. Bilinear
                    // sampling then fades only alpha, without dark hill outlines.
                    colors[y * size + x] = new Color(1, 1, 1, coverage);
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Corridor circle", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(colors);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Draws the lane through the mascot's halo, then restores GUI state.
        /// X/Y/Rotation in the returned geometry already include player-zero mirror.
        /// Draw the mascot with Rotation degrees, followed by a local X flip when
        /// MirrorX is true, then call DrawForeground for rings/stars/confetti.
        /// </summary>
        public Geometry Draw(float w, float h, GameState game, int playerIndex, float time)
        {
            if (w <= 0 || h <= 0 || game == null) return default(Geometry);
            PlayerState player = game.Players[playerIndex];
            bool mirror = playerIndex == 0;
            float top = Mathf.Clamp(h * .095f, 13, 25), bottom = h - top;
            float sy = (bottom - top) / (float)RaceConfig.CorridorHeight;
            float anchor = Mathf.Clamp(w * .19f, 62, 195);
            float ahead = Mathf.Max(1080, w * 1.28f);
            float sx = (w - anchor - 20) / ahead;
            float y = top + (float)RaceSimulation.GetPlayerCenterY(player) * sy;
            float radius = Mathf.Clamp((float)RaceConfig.PlayerRadius * sy, 7.5f, 25);
            bool recovering = player.Recovering > 0;
            bool running = game.Phase == "racing" && !player.FinishTime.HasValue && !recovering;
            bool onCeiling = player.Support != SupportKind.None ? player.SupportSide == 0 : player.Y < .5;
            // Keep the minimum readable sprite size on phones, but place its
            // feet on the actual stone face rather than sinking into the rail.
            if (player.Support == SupportKind.Stone)
                y += (onCeiling ? 1 : -1) * (radius - (float)RaceConfig.PlayerRadius * sy);
            // Travel is the gait clock: boost/slow changes cadence naturally and
            // a paused/frozen player cannot skate its feet against the ground.
            float motionTime=(float)(player.Distance/RaceConfig.Speed);
            float stride=motionTime*25;
            float flight=Mathf.Clamp01((float)(player.FlipElapsed/RaceConfig.FlipDuration));
            if (game.Motion != MotionMode.Linear)
                flight = Mathf.Clamp01((float)(Math.Abs(player.VerticalSpeed) / Math.Sqrt(
                    game.Profile.DepartureImpulse * game.Profile.DepartureImpulse
                    + 2 * game.Profile.GravityAcceleration * (RaceConfig.CorridorHeight - 2 * RaceConfig.PlayerRadius))));
            if (game.Motion == MotionMode.LinearAirFlip) flight = Mathf.Clamp01((float)(1 - player.Y));
            float rotation = onCeiling ? 180 : 0;
            if (player.Flipping) rotation = (float)(1 - player.Y) * 180;
            if (game.Motion == MotionMode.JumpGravityFlip)
            {
                float target = player.Gravity < 0 ? 180 : 0;
                if (jumpRotationTime[playerIndex] < 0 || player.Support != SupportKind.None || game.Phase == "ready")
                    jumpRotation[playerIndex] = target;
                else jumpRotation[playerIndex] = Mathf.MoveTowardsAngle(jumpRotation[playerIndex], target,
                    Mathf.Clamp(time - jumpRotationTime[playerIndex], 0, .1f) * 720);
                jumpRotationTime[playerIndex] = time;
                rotation = jumpRotation[playerIndex];
            }
            else jumpRotationTime[playerIndex] = -1;
            if (recovering) rotation += (float)player.RecoveryElapsed * 14 * Mathf.Rad2Deg;
            // Raise the body away from its current surface on each passing step.
            // Original .035r bob was <.3px on a phone and disappeared entirely.
            float bob = running && !player.Flipping ? -Mathf.Abs(Mathf.Sin(stride))*radius*.075f*(onCeiling?-1:1) : 0;
            float finishX = WorldX(game.Track.Length, player.Distance, anchor, sx);
            var result = new Geometry
            {
                X = mirror ? w - anchor : anchor, Y = y + bob, Radius = radius,
                Rotation = mirror ? -rotation : rotation, MirrorX = mirror,
                Top = top, Bottom = bottom, ScaleX = sx, ScaleY = sy, Anchor = anchor,
                FinishX = mirror ? w - finishX : finishX, Running = running, Recovering = recovering,
                MotionTime=motionTime, FlipProgress=flight
            };
            Obstacle practiceGate = null;
            double flipMarker = 0;
            if (game.Mode == "practice" && game.Phase == "racing" && !player.FinishTime.HasValue)
                foreach (var obstacle in game.Track.Obstacles)
                    if (obstacle.Kind == "gate" && !player.HitObstacleIds.Contains(obstacle.Id)
                        && obstacle.X + obstacle.Width > player.Distance && obstacle.X - player.Distance < ahead * .75)
                    {
                        practiceGate = obstacle;
                        flipMarker = obstacle.X + obstacle.Width / 2 - RaceConfig.Speed * RaceConfig.FlipDuration / 2;
                        break;
                    }
            bool atMarker = practiceGate != null && Math.Abs(player.Distance - flipMarker) <= 15 && !player.Flipping && !recovering;
            if (practiceGate != null)
            {
                float markerX = WorldX(flipMarker, player.Distance, anchor, sx);
                result.PracticeMarkerX = mirror ? w - markerX : markerX;
                result.AtPracticeMarker = atMarker;
                result.ShowPracticeMarker = !recovering && !player.Flipping && player.Distance <= flipMarker + 15;
            }

            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            clipBounds = new Rect(0, 0, w, h);
            if (mirror) GUI.matrix = previous * Matrix4x4.TRS(new Vector3(w, 0, 0), Quaternion.identity, new Vector3(-1, 1, 1));
            Landscape(w, h, top, bottom, player.Distance, sx, time, playerIndex);
            float startX = WorldX(0, player.Distance, anchor, sx) - radius * 1.6f;
            if (startX > -20)
                for (float dash = top + 8; dash < bottom - 8; dash += 8)
                    Line(startX, dash, startX, Mathf.Min(dash + 3, bottom - 8), Hex(0xd9d1be), 1.5f);
            if (practiceGate != null)
                FlipMarker(WorldX(flipMarker, player.Distance, anchor, sx), top, bottom, Ink[playerIndex], atMarker, time);

            foreach (var obstacle in game.Track.Obstacles)
            {
                float x = WorldX(obstacle.X, player.Distance, anchor, sx);
                float width = (float)obstacle.Width * sx;
                if (x + width < -35 || x > w + 35) continue;
                bool hit = player.HitObstacleIds.Contains(obstacle.Id);
                if (obstacle.Kind == "gate")
                {
                    GateHalf(x, width, top, (float)obstacle.GapTop * sy, true, hit, time);
                    GateHalf(x, width, top + (float)obstacle.GapBottom * sy,
                        ((float)RaceConfig.CorridorHeight - (float)obstacle.GapBottom) * sy, false, hit, time);
                    if (!hit)
                    {
                        float center = top + (float)(obstacle.GapTop + obstacle.GapBottom) * sy / 2;
                        Line(x - 8, center - 4, x - 4, center, Alpha(Hex(0xc4975c), .45f), 1.5f);
                        Line(x - 4, center, x - 8, center + 4, Alpha(Hex(0xc4975c), .45f), 1.5f);
                    }
                }
                else
                {
                    bool marked = false;
                    foreach (var zone in game.Track.RiskZones)
                        if (zone.IsLanding && zone.ObstacleId == obstacle.Id) { marked = true; break; }
                    Rock(x, width, (float)obstacle.Height * sy, obstacle.Kind == "ceiling" ? top : bottom,
                        obstacle.Kind == "ceiling", hit, marked);
                }
            }
            foreach (var zone in game.Track.RiskZones)
            {
                float ringX = WorldX(zone.X, player.Distance, anchor, sx);
                if (zone.IsLanding)
                {
                    foreach (var stone in game.Track.Obstacles)
                        if (stone.Id == zone.ObstacleId)
                        {
                            bool ceiling = stone.Kind == "ceiling";
                            float face = ceiling ? top + (float)stone.Height * sy : bottom - (float)stone.Height * sy;
                            LandingPad(ringX, face, (float)zone.LandingWidth * sx, ceiling,
                                player.SuccessfulRiskIds.Contains(zone.Id), time);
                            break;
                        }
                    continue;
                }
                if (ringX < -25 || ringX > w + 25 || player.SuccessfulRiskIds.Contains(zone.Id)
                    || player.NegativeRiskIds.Contains(zone.Id)) continue;
                bool pending = player.CollectedRiskIds.Contains(zone.Id);
                bool failed = player.FailedRiskIds.Contains(zone.Id);
                float ringY = top + (float)zone.Y * sy;
                if (zone.IsFinish)
                {
                    // The vertical rail is the exact center-contact window.
                    // Separate chevrons make the small target visible on phones
                    // without enlarging its physical pickup region.
                    float half = (float)zone.FinishHeight * sy * .5f;
                    Color finishInk = Hex(0x398d80);
                    Line(ringX, ringY - half, ringX, ringY + half, finishInk, 2);
                    Line(ringX - 3, ringY - half, ringX + 3, ringY - half, finishInk, 1.5f);
                    Line(ringX - 3, ringY + half, ringX + 3, ringY + half, finishInk, 1.5f);
                    for (int i = 0; i < 2; i++)
                    {
                        float arrowX = ringX - 8 - i * 6;
                        Line(arrowX - 4, ringY - 5, arrowX, ringY, Alpha(finishInk, .7f), 1.5f);
                        Line(arrowX, ringY, arrowX - 4, ringY + 5, Alpha(finishInk, .7f), 1.5f);
                    }
                    continue;
                }
                float ringRadius = Mathf.Max(6.5f, (float)(zone.Radius + 4) * sy);
                if (zone.IsInstant)
                {
                    Color instantInk = Hex(0x398d80);
                    Ellipse(ringX, ringY, ringRadius + 5 + Mathf.Sin(time * 4) * 1.5f,
                        ringRadius + 5, Alpha(Hex(0x9ddccb), .32f));
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3, b = (i + 1) * Mathf.PI / 3;
                        Line(ringX + Mathf.Cos(a) * ringRadius, ringY + Mathf.Sin(a) * ringRadius,
                            ringX + Mathf.Cos(b) * ringRadius, ringY + Mathf.Sin(b) * ringRadius, instantInk, 1.8f);
                    }
                    Line(ringX + ringRadius * .2f, ringY - ringRadius * .58f, ringX - ringRadius * .3f, ringY + ringRadius * .06f, instantInk, 2);
                    Line(ringX - ringRadius * .3f, ringY + ringRadius * .06f, ringX + ringRadius * .3f, ringY - ringRadius * .06f, instantInk, 2);
                    Line(ringX + ringRadius * .3f, ringY - ringRadius * .06f, ringX - ringRadius * .2f, ringY + ringRadius * .58f, instantInk, 2);
                    continue;
                }
                Color ringInk = failed ? Alpha(Hex(0xa2a68e), .35f)
                    : zone.IsNegative ? Hex(0x99587f) : pending ? Hex(0xd9b56f) : Hex(0xb58a35);
                Ellipse(ringX, ringY, ringRadius + 5, ringRadius + 5,
                    Alpha(zone.IsNegative ? Hex(0xe5bad7) : Hex(0xf6df99), failed ? .05f : .25f));
                int segments = zone.IsNegative ? 4 : 28;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2 / segments - Mathf.PI / 2, b = (i + 1) * Mathf.PI * 2 / segments - Mathf.PI / 2;
                    Line(ringX + Mathf.Cos(a) * ringRadius, ringY + Mathf.Sin(a) * ringRadius,
                        ringX + Mathf.Cos(b) * ringRadius, ringY + Mathf.Sin(b) * ringRadius, ringInk, pending ? 1 : 2);
                }
                Line(ringX - ringRadius * .4f, ringY, ringX + ringRadius * .4f, ringY, ringInk, 1.6f);
                if (!zone.IsNegative)
                    Line(ringX, ringY - ringRadius * .4f, ringX, ringY + ringRadius * .4f, ringInk, 1.6f);
                if (!zone.IsNegative && !failed && !pending) Star(ringX + ringRadius + 3, ringY - ringRadius - 2, 2.7f, Hex(0xddb962), time * .5f);
                if (game.Mode == "practice" && !zone.IsNegative && !failed && !pending && zone.X - player.Distance < 650)
                {
                    float early = WorldX(zone.SafeFlipX, player.Distance, anchor, sx);
                    float late = WorldX(zone.RiskFlipX, player.Distance, anchor, sx);
                    FlipMarker(early, top, bottom, Hex(0x98ad7e), false, time);
                    FlipMarker(late, top, bottom, Hex(0xc79a45), Math.Abs(player.Distance - zone.RiskFlipX) < 18, time);
                    result.PracticeMarkerX = mirror ? w - late : late;
                    result.AtPracticeMarker = Math.Abs(player.Distance - zone.RiskFlipX) < 18;
                    result.ShowPracticeMarker = !player.Flipping && !recovering && player.Distance <= zone.RiskFlipX + 18;
                }
            }
            if (finishX < w + 40 && finishX > -50) Finish(finishX, top, bottom, sy);

            if (player.BoostRemaining > 0 || player.FinishBoost)
            {
                bool instant = player.FinishBoost || player.LastInstant != null && game.Elapsed - player.LastInstant.At < RaceConfig.BoostDuration;
                Color trail = instant ? Hex(0x398d80) : Hex(0xd6ab5b);
                Ellipse(anchor, y, radius * 2, radius * 1.8f, Alpha(instant ? Hex(0x9ddccb) : Hex(0xffd779), .27f));
                for (int i = 0; i < 4; i++)
                {
                    float drift = (time * 105 + i * 19) % 72;
                    float lineY = y + (i - 1.5f) * radius * .58f;
                    Line(anchor - radius - drift, lineY, anchor - radius - drift - 17, lineY,
                        Alpha(trail, (1 - drift / 72) * .65f), 1.5f);
                }
            }
            if (player.SlowRemaining > 0)
            {
                Ellipse(anchor, y, radius * 2, radius * 1.8f, Alpha(Hex(0xd5a3c3), .22f));
                for (int i = 0; i < 3; i++)
                {
                    float drift = (time * 26 + i * 17) % 50;
                    float cx = anchor - radius - drift, cy = y + (i - 1) * radius * .75f;
                    Color drag = Alpha(Hex(0x99587f), (1 - drift / 50) * .6f);
                    Line(cx - 4, cy, cx + 4, cy, drag, 2);
                }
            }

            float shadowY = onCeiling ? top + 3 : bottom - 3;
            if (player.Support == SupportKind.Stone)
                shadowY = top + ((float)RaceSimulation.GetPlayerCenterY(player)
                    + (onCeiling ? -1 : 1) * (float)RaceConfig.PlayerRadius) * sy;
            float shadowBreath=running&&!player.Flipping?1-Mathf.Abs(Mathf.Sin(stride))*.12f:1;
            Ellipse(anchor, shadowY, radius * 1.5f*shadowBreath, Mathf.Max(1.5f, radius * .18f), Alpha(Ink[playerIndex], player.Flipping ? .05f : .13f));
            if (running && !player.Flipping)
                for (int i = 0; i < 3; i++)
                {
                    float drift = (motionTime * 40 + i * 11) % 33;
                    Ellipse(anchor - radius*.6f - drift, shadowY + (onCeiling ? 1 : -1) * (2 + drift * .11f),
                        1.3f + drift * .06f, 1.1f+drift*.025f, Alpha(Hex(0xc6bda9), (1 - drift / 33) * .32f));
                }
            if (player.Flipping && !recovering)
            {
                for (int i = 4; i >= 1; i--)
                {
                    float lag=i*.045f;
                    float past=Mathf.Max(0,flight-lag);
                    float pastY=top+((float)RaceConfig.PlayerRadius+
                        Mathf.Lerp((float)player.FlipFrom,(float)player.FlipTo,past)*
                        ((float)RaceConfig.CorridorHeight-(float)RaceConfig.PlayerRadius*2))*sy;
                    // Gravity trails show the current velocity tangent, including
                    // braking/reversal; a linear FlipFrom/FlipTo trail points the
                    // wrong way after an air flip.
                    if (game.Motion != MotionMode.Linear)
                        pastY = Mathf.Clamp(y - (float)player.VerticalSpeed * i * .025f * sy,
                            top + (float)RaceConfig.PlayerRadius * sy, bottom - (float)RaceConfig.PlayerRadius * sy);
                    Ellipse(anchor-i*radius*.32f,pastY,radius*(.35f+i*.045f),radius*.40f,
                        Alpha(Ink[playerIndex],(5-i)*.045f));
                }
            }
            // A quiet halo leaves the moving feet, silhouette and scarf readable.
            Ellipse(anchor, y - (onCeiling ? -1 : 1) * radius * .12f, radius * 1.50f, radius * 1.50f,
                Alpha(Pale[playerIndex], recovering ? .12f : .32f));
            GUI.matrix = previous;
            GUI.color = previousColor;
            return result;
        }

        public void DrawForeground(float w, float h, GameState game, int playerIndex, float time, Geometry geometry)
        {
            if (w <= 0 || h <= 0 || game == null) return;
            PlayerState player = game.Players[playerIndex];
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            clipBounds = new Rect(0, 0, w, h);
            if (playerIndex == 0) GUI.matrix = previous * Matrix4x4.TRS(new Vector3(w, 0, 0), Quaternion.identity, new Vector3(-1, 1, 1));
            float x = geometry.Anchor;
            float y = geometry.Top + (float)RaceSimulation.GetPlayerCenterY(player) * geometry.ScaleY;
            float radius = geometry.Radius;
            HitInfo burstEvent = player.LastFinishBoost != null ? player.LastFinishBoost : player.LastInstant;
            if (burstEvent != null && game.Elapsed - burstEvent.At < .45)
            {
                float progress = (float)((game.Elapsed - burstEvent.At) / .45);
                float burst = radius * (1.5f + progress * 2.2f);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4;
                    Line(x + Mathf.Cos(a) * burst, y + Mathf.Sin(a) * burst,
                        x + Mathf.Cos(a) * (burst + 5), y + Mathf.Sin(a) * (burst + 5),
                        Alpha(Hex(0x398d80), 1 - progress), 2);
                }
            }
            if (player.Recovering > 0)
                for (int i = 0; i < 3; i++)
                {
                    float angle = time * 6 + i * Mathf.PI * 2 / 3;
                    Star(x + Mathf.Cos(angle) * radius * 1.9f, y + Mathf.Sin(angle) * radius * 1.75f,
                        radius * .34f, Hex(0xe2b460), angle);
                }
            else if (!player.Flipping && game.Phase == "racing" && !player.FinishTime.HasValue)
            {
                float begin = player.Y < .5 ? .15f : Mathf.PI + .15f;
                float end = player.Y < .5 ? Mathf.PI - .15f : Mathf.PI * 2 - .15f;
                const int segments = 20;
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.Lerp(begin, end, i / (float)segments), b = Mathf.Lerp(begin, end, (i + 1f) / segments);
                    Line(x + Mathf.Cos(a) * radius * 1.9f, y + Mathf.Sin(a) * radius * 1.9f,
                        x + Mathf.Cos(b) * radius * 1.9f, y + Mathf.Sin(b) * radius * 1.9f, Alpha(Ink[playerIndex], .5f), 1.35f);
                }
            }
            if (player.FinishTime.HasValue)
                for (int i = 0; i < 30; i++)
                {
                    float cx = ((i * 113 + playerIndex * 37 + Mathf.Sin(time + i) * 15) % w + w) % w;
                    float cy = geometry.Top + ((time * (24 + i % 6 * 4) + i * 43) % (geometry.Bottom - geometry.Top));
                    if (!FullyInside(new Rect(cx - 3, cy - 3, 6, 6))) continue;
                    Matrix4x4 matrix = GUI.matrix;
                    GUI.matrix = matrix * Matrix4x4.TRS(new Vector3(cx, cy, 0),
                        Quaternion.Euler(0, 0, (time * (i % 2 == 1 ? 1 : -1) + i) * Mathf.Rad2Deg), Vector3.one);
                    // Already bounded in unrotated lane coordinates.
                    RawFill(new Rect(-2.5f, -1.5f, 5, 3), Confetti[i % Confetti.Length]);
                    GUI.matrix = matrix;
                }
            GUI.matrix = previous;
            GUI.color = previousColor;
        }

        void Landscape(float w, float h, float top, float bottom, double distance, float sx, float time, int index)
        {
            Fill(new Rect(0, 0, w, h), Hex(0xfaf7ed));
            const float span = 230;
            float drift = (float)(distance * sx * .17);
            int first = Mathf.FloorToInt(drift / span) - 1;
            for (int i = first; i <= first + Mathf.CeilToInt(w / span) + 2; i++)
            {
                float x = i * span - drift;
                int variation = Math.Abs(i % 5);
                Ellipse(x + 100, bottom + 48, 170, 77 + variation * 9, i % 2 != 0 ? Hex(0xe9eddf) : Hex(0xeff0e3));
                Ellipse(x + 62, top - 48, 135, 65 + variation * 7, Hex(0xf1ede2));
                float blossomY = top + (bottom - top) * (.32f + variation % 3 * .13f);
                Star(x + 96, blossomY, 3, Hex(0xdad8c5), .12f);
                Ellipse(x + 165, blossomY + 16, 1.5f, 1.5f, Hex(0xd9dccb));
                Leaf(x + 35, bottom - 9, 10, -.6f, Hex(0xd4dfbd));
            }
            float wind = (time * 5 + (float)(distance * sx * .08)) % (w + 80);
            float windY = top + (bottom - top) * .57f;
            Line(w - wind, windY, w - wind + 18, windY, Hex(0xe5dfd1), 1);
            // Surface fills cover hill overflow and preserve the flat collision planes.
            Fill(new Rect(0, 0, w, top), Hex(0xd9e2c7));
            Fill(new Rect(0, bottom, w, h - bottom), Hex(0xd9e2c7));
            Fill(new Rect(0, top - 3, w, 3), Hex(0xb5c3a3));
            Fill(new Rect(0, bottom, w, 3), Hex(0xb5c3a3));
            Fill(new Rect(0, top - 5, w, 2), Hex(0xeff3df));
            Fill(new Rect(0, bottom + 3, w, 2), Hex(0xeff3df));
            float grassDrift = (float)(distance * sx);
            for (int i = Mathf.FloorToInt(grassDrift / 54) - 1; i < (grassDrift + w) / 54 + 1; i++)
            {
                float x = i * 54 - grassDrift;
                Ellipse(x + 13, bottom + 9, 2.1f, 1.3f, Hex(0xb8c6a8));
                Ellipse(x + 32, top - 11, 3, 1.2f, Hex(0xbcc9ad));
                Line(x + 41, bottom + 12, x + 43, bottom + 8, Hex(0xb0c29e), 1.2f);
                Line(x + 43, bottom + 8, x + 46, bottom + 11, Hex(0xb0c29e), 1.2f);
            }
            Fill(new Rect(0, 0, w, 2), Alpha(Ink[index], .35f));
            Fill(new Rect(0, h - 2, w, 2), Alpha(Ink[index], .35f));
        }

        void LandingPad(float x, float face, float width, bool ceiling, bool claimed, float time)
        {
            float inside = ceiling ? -1 : 1;
            Color edge = claimed ? Hex(0x72958a) : Hex(0x318a78);
            // Width is exactly the center-X landing interval, with no circle or
            // pickup halo extending it. The rail remains after the one-off reward.
            if (!claimed)
                Fill(new Rect(x - width / 2, ceiling ? face - 5 : face, width, 5),
                    Alpha(Hex(0x9ddccb), .6f + Mathf.Sin(time * 4) * .15f));
            Line(x - width / 2, face, x + width / 2, face, edge, 2.5f);
            Line(x - width / 2, face, x - width / 2, face + inside * 5, edge, 1.5f);
            Line(x + width / 2, face, x + width / 2, face + inside * 5, edge, 1.5f);
            float cy = face + inside * 8;
            if (claimed)
                Ellipse(x, cy, 2, 1.5f, edge);
            else
            {
                Line(x + 2, cy - 4, x - 2, cy, edge, 1.5f);
                Line(x - 2, cy, x + 2, cy, edge, 1.5f);
                Line(x + 2, cy, x - 2, cy + 4, edge, 1.5f);
            }
        }

        void Rock(float x, float width, float height, float surface, bool inverted, bool hit, bool marked)
        {
            GUI.color = new Color(1, 1, 1, hit ? .26f : 1);
            // Texture's nominal rock bounds are [16,112], with padding for strokes.
            Texture(new Rect(x - width / 6, inverted ? surface - height / 6 : surface - height * 7 / 6,
                width * 4 / 3, height * 4 / 3), inverted ? ceilingRock : floorRock,
                inverted ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1));
            GUI.color = Color.white;
            if (marked)
            {
                float face = surface + (inverted ? height : -height);
                Fill(new Rect(x, inverted ? face - 4 : face, width, 4), Alpha(Hex(0xa2b2a6), hit ? .26f : 1));
                Line(x, face, x + width, face, Hex(0x7b9181), 1.5f);
            }
            if (!hit)
            {
                float direction = inverted ? 1 : -1;
                Ellipse(x + width * .18f, surface + direction * (height + 1), 2.5f, 1.8f, Hex(0xb6c88b));
                Ellipse(x + width * .3f, surface + direction * (height + 1.7f), 3, 2.3f, Hex(0xb6c88b));
            }
        }

        void GateHalf(float x, float width, float y, float height, bool fromCeiling, bool hit, float time)
        {
            if (height <= 0) return;
            float alpha = hit ? .23f : 1;
            float visualWidth = Mathf.Max(7, width), left = x - (visualWidth - width) / 2;
            Round(new Rect(left, y, visualWidth, height), 3, Alpha(Hex(0xad7a47), alpha));
            Round(new Rect(left + 1, y + 1, visualWidth - 2, height - 2), 2, Alpha(Hex(0xd9a05b), alpha));
            // Clip stripe centerlines analytically, avoiding nested GUI groups under mirrors.
            Rect interior = new Rect(left + 1.4f, y + 1.4f, visualWidth - 2.8f, Mathf.Max(0, height - 2.8f));
            for (float offset = -visualWidth; offset < height + visualWidth; offset += 14)
            {
                Vector2 a = new Vector2(left - 3, y + offset + 9), b = new Vector2(left + visualWidth + 3, y + offset);
                if (ClipLine(interior, ref a, ref b)) Line(a.x, a.y, b.x, b.y, Alpha(Hex(0xf6d692), alpha), 3.2f);
            }
            float tip = fromCeiling ? y + height : y;
            Round(new Rect(left - 2, tip - 3, visualWidth + 4, 6), 2, Alpha(Hex(0xb68a51), alpha));
            Round(new Rect(left - 1, tip - 2, visualWidth + 2, 4), 1, Alpha(Hex(0xedbf73), alpha));
            if (!hit) Ellipse(left + visualWidth / 2, tip, 1.7f, 1.7f, Mathf.Sin(time * 4) > 0 ? Hex(0xfff8d6) : Hex(0xf1dfb3));
        }

        void Finish(float x, float top, float bottom, float scale)
        {
            float flagWidth = Mathf.Max(15, scale * 25), tile = flagWidth / 2;
            Fill(new Rect(x - 4, top, flagWidth + 8, bottom - top), Alpha(Hex(0xecdfb9), .55f));
            for (int row = 0; row < (bottom - top) / tile; row++)
                for (int col = 0; col < 2; col++)
                    Fill(new Rect(x + col * tile, top + row * tile, tile, Mathf.Min(tile, bottom - top - row * tile)),
                        Alpha((row + col) % 2 == 1 ? Hex(0x786d59) : Hex(0xfffdf0), .55f));
        }

        void FlipMarker(float x, float top, float bottom, Color ink, bool active, float time)
        {
            float radius = active ? 5.5f + Mathf.Sin(time * 10) : 4;
            Color color = Alpha(ink, active ? 1 : .75f);
            for (int i = 0; i < 2; i++)
            {
                float surface = i == 0 ? top : bottom, direction = i == 0 ? 1 : -1;
                Line(x - 11, surface + direction * 2, x + 11, surface + direction * 2, color, 3);
                // Filled triangle made from horizontal scan rows is small and crisp.
                for (int row = 0; row < 9; row++)
                {
                    float half = radius * (1 - row / 9f);
                    Fill(new Rect(x - half, surface + direction * (6 + row) - .5f, half * 2, 1.4f), color);
                }
            }
        }

        void Star(float x, float y, float radius, Color color, float rotation)
        {
            // The four points fit within a radius under any rotation.
            if (!FullyInside(new Rect(x - radius, y - radius, radius * 2, radius * 2))) return;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.Euler(0, 0, rotation * Mathf.Rad2Deg), Vector3.one);
            GUI.color = color;
            GUI.DrawTexture(new Rect(-radius, -radius, radius * 2, radius * 2), star);
            GUI.color = Color.white;
            GUI.matrix = previous;
        }

        void Leaf(float x, float y, float size, float rotation, Color color)
        {
            // Decorative edge leaves may disappear at the edge instead of spilling
            // into the other lane. Large scenery receives exact texture clipping.
            if (!FullyInside(new Rect(x - size, y - size, size * 2, size * 2))) return;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.TRS(new Vector3(x, y, 0), Quaternion.Euler(0, 0, rotation * Mathf.Rad2Deg), Vector3.one);
            GUI.color = color;
            GUI.DrawTexture(new Rect(-size * .6f, -size * .7f, size * 1.3f, size * 1.3f), leaf);
            GUI.color = Color.white;
            GUI.matrix = previous;
        }

        void Ellipse(float x, float y, float rx, float ry, Color color)
        {
            if (rx <= 0 || ry <= 0) return;
            GUI.color = color;
            Texture(new Rect(x - rx, y - ry, rx * 2, ry * 2), circle, new Rect(0, 0, 1, 1));
            GUI.color = Color.white;
        }

        void Round(Rect r, float radius, Color color)
        {
            if (r.width <= 0 || r.height <= 0) return;
            radius = Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2);
            Fill(new Rect(r.x + radius, r.y, r.width - radius * 2, r.height), color);
            Fill(new Rect(r.x, r.y + radius, r.width, r.height - radius * 2), color);
            Ellipse(r.x + radius, r.y + radius, radius, radius, color);
            Ellipse(r.xMax - radius, r.y + radius, radius, radius, color);
            Ellipse(r.x + radius, r.yMax - radius, radius, radius, color);
            Ellipse(r.xMax - radius, r.yMax - radius, radius, radius, color);
        }

        void Line(float x1, float y1, float x2, float y2, Color color, float thickness)
        {
            Vector2 a = new Vector2(x1, y1), b = new Vector2(x2, y2);
            float half = thickness / 2;
            Rect inset = new Rect(clipBounds.x + half, clipBounds.y + half,
                Mathf.Max(0, clipBounds.width - thickness), Mathf.Max(0, clipBounds.height - thickness));
            if (!ClipLine(inset, ref a, ref b)) return;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.TRS(new Vector3(a.x, a.y, 0),
                Quaternion.Euler(0, 0, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg), Vector3.one);
            // Endpoints were clipped before this local rotation.
            RawFill(new Rect(0, -half, Vector2.Distance(a, b), thickness), color);
            GUI.matrix = previous;
        }

        void Fill(Rect rect, Color color)
        {
            rect = Intersection(rect, clipBounds);
            RawFill(rect, color);
        }

        static void RawFill(Rect rect, Color color)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void Texture(Rect rect, Texture2D texture, Rect uv)
        {
            if (rect.width <= 0 || rect.height <= 0) return;
            Rect visible = Intersection(rect, clipBounds);
            if (visible.width <= 0 || visible.height <= 0) return;
            Rect croppedUv = new Rect(
                uv.x + uv.width * ((visible.x - rect.x) / rect.width),
                uv.y + uv.height * ((rect.yMax - visible.yMax) / rect.height),
                uv.width * visible.width / rect.width,
                uv.height * visible.height / rect.height);
            GUI.DrawTextureWithTexCoords(visible, texture, croppedUv, true);
        }

        bool FullyInside(Rect rect)
        {
            return rect.xMin >= clipBounds.xMin && rect.yMin >= clipBounds.yMin
                && rect.xMax <= clipBounds.xMax && rect.yMax <= clipBounds.yMax;
        }

        static Rect Intersection(Rect a, Rect b)
        {
            float x = Mathf.Max(a.xMin, b.xMin), y = Mathf.Max(a.yMin, b.yMin);
            return new Rect(x, y, Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - x),
                Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - y));
        }

        static float WorldX(double position, double distance, float anchor, float sx) { return anchor + (float)(position - distance) * sx; }
        static Color Hex(uint value) { return new Color((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f); }
        static Color Alpha(Color color, float alpha) { color.a = alpha; return color; }

        static bool ClipLine(Rect rect, ref Vector2 a, ref Vector2 b)
        {
            float start = 0, end = 1;
            Vector2 delta = b - a;
            if (!Clip(-delta.x, a.x - rect.xMin, ref start, ref end) || !Clip(delta.x, rect.xMax - a.x, ref start, ref end)
                || !Clip(-delta.y, a.y - rect.yMin, ref start, ref end) || !Clip(delta.y, rect.yMax - a.y, ref start, ref end)) return false;
            b = a + delta * end;
            a += delta * start;
            return true;
        }

        static bool Clip(float p, float q, ref float start, ref float end)
        {
            if (Mathf.Abs(p) < .0001f) return q >= 0;
            float value = q / p;
            if (p < 0) { if (value > end) return false; start = Mathf.Max(start, value); }
            else { if (value < start) return false; end = Mathf.Min(end, value); }
            return true;
        }

        static Texture2D MakeRock(bool ceiling)
        {
            var p = new Painter(128, 128);
            float[] points = { 16, 112, 16, 42.88f, 32.32f, 16, 86.08f, 16, 112, 37.12f, 112, 112 };
            p.Polygon(ceiling ? Hex(0x99aaa7) : Hex(0xa2aaa5), points);
            Color outline = ceiling ? Hex(0x788e88) : Hex(0x838f82);
            for (int i = 0; i < points.Length; i += 2)
            {
                int next = (i + 2) % points.Length;
                p.Line(points[i], points[i + 1], points[next], points[next + 1], outline, 3);
            }
            p.Polygon(new Color(1, 1, 1, .15f), 19, 43, 35, 19, 83, 19, 71, 56);
            p.Line(83, 25, 71, 56, Alpha(Hex(0x7e9182), .36f), 2.6f);
            p.Line(71, 56, 85, 74, Alpha(Hex(0x7e9182), .36f), 2.6f);
            p.Polygon(Hex(0xb3c38f), 19, 104, 109, 104, 110, 116, 18, 116);
            return p.Finish(ceiling ? "Ceiling rock" : "Floor rock");
        }

        sealed class Painter
        {
            const int Supersample = 2;
            readonly int width, height;
            readonly Color[] pixels;
            public Painter(int w, int h, bool transparentWhite = false)
            {
                width = w * Supersample; height = h * Supersample; pixels = new Color[width * height];
                if (transparentWhite)
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(1, 1, 1, 0);
            }
            void Put(int x, int y, Color color)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return;
                int index = (height - y - 1) * width + x;
                Color previous = pixels[index];
                float alpha = color.a + previous.a * (1 - color.a);
                if (alpha <= 0) return;
                pixels[index] = new Color((color.r * color.a + previous.r * previous.a * (1 - color.a)) / alpha,
                    (color.g * color.a + previous.g * previous.a * (1 - color.a)) / alpha,
                    (color.b * color.a + previous.b * previous.a * (1 - color.a)) / alpha, alpha);
            }
            public void Ellipse(float cx, float cy, float rx, float ry, Color color)
            {
                cx *= Supersample; cy *= Supersample; rx *= Supersample; ry *= Supersample;
                for (int y = Mathf.FloorToInt(cy - ry); y <= cy + ry; y++)
                    for (int x = Mathf.FloorToInt(cx - rx); x <= cx + rx; x++)
                        if ((x - cx) * (x - cx) / (rx * rx) + (y - cy) * (y - cy) / (ry * ry) <= 1) Put(x, y, color);
            }
            public void Line(float x1, float y1, float x2, float y2, Color color, float thickness)
            {
                int steps = Mathf.CeilToInt(Vector2.Distance(new Vector2(x1, y1), new Vector2(x2, y2)) * Supersample);
                for (int i = 0; i <= steps; i++)
                {
                    float t = steps == 0 ? 0 : i / (float)steps;
                    Ellipse(Mathf.Lerp(x1, x2, t), Mathf.Lerp(y1, y2, t), thickness / 2, thickness / 2, color);
                }
            }
            public void Polygon(Color color, params float[] points)
            {
                int count = points.Length / 2;
                float minX = width, maxX = 0, minY = height, maxY = 0;
                for (int i = 0; i < count; i++)
                {
                    minX = Mathf.Min(minX, points[i * 2] * Supersample); maxX = Mathf.Max(maxX, points[i * 2] * Supersample);
                    minY = Mathf.Min(minY, points[i * 2 + 1] * Supersample); maxY = Mathf.Max(maxY, points[i * 2 + 1] * Supersample);
                }
                for (int y = Mathf.FloorToInt(minY); y <= maxY; y++)
                    for (int x = Mathf.FloorToInt(minX); x <= maxX; x++)
                    {
                        bool inside = false;
                        for (int i = 0, j = count - 1; i < count; j = i++)
                        {
                            float ix = points[i * 2] * Supersample, iy = points[i * 2 + 1] * Supersample;
                            float jx = points[j * 2] * Supersample, jy = points[j * 2 + 1] * Supersample;
                            if ((iy > y) != (jy > y) && x < (jx - ix) * (y - iy) / (jy - iy) + ix) inside = !inside;
                        }
                        if (inside) Put(x, y, color);
                    }
            }
            public Texture2D Finish(string name)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                texture.SetPixels(pixels); texture.Apply(false, true); return texture;
            }
        }
    }
}
