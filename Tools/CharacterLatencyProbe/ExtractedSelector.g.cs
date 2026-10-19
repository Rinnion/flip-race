using FlipRace.Core; using FlipRace.Presentation; using UnityEngine; public sealed partial class LatencyProbeUi {        void SelectCharacter(int slot, int hero)
        {
            RaceSimulation.SetCharacter(Game, slot, hero); MarkState();
            MascotArtwork.GetFrame(hero, slot, animationTime, false, false, false); MarkFrameReady();
            lastFrame = Time.realtimeSinceStartupAsDouble;
            // The selection tap was consumed above; leave the other player's
            // simultaneous touch available in this same input frame.
            Beep(tapClip);
        }}
