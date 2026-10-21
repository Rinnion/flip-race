# Shared camera check

Run from the Unity project directory:

```powershell
dotnet run --project Tools/SharedCameraCheck/SharedCameraCheck.csproj -c Release
```

The default output is `Verification/shared-camera-final.json`. An optional first argument supplies a different output path. The tool compiles production Core and the pure production `SharedFieldView`; it does not load Unity or draw a frame. The earlier `shared-camera-check.json/.txt` capture is preserved as the original 19/45 checkpoint.

There are three deliberately separate kinds of evidence:

1. Actual games use production `Press` / `Advance`, independent distance-triggered actions for both players, and the unchanged mixed course. Snapshot bisection locates an action but never injects a predicted state. The routes cover full/safe in both player assignments, a real missed small-stone action followed by an overtake, and full/no-action with eight actual collisions and the normal finish grace.
2. Camera-only boundary fixtures set positions solely to check projection algebra, identity, finish bounds, and equal horizontal projection at heights 132.5 and 289. They are not gameplay witnesses.
3. The visibility calculation is a conservative geometric estimate at 330 world/s, using the complete physical obstacle union and 20 px right margin. It does not claim that an unchanged distance-input array is physically valid under an artificial permanently active boost. The final 25/47 projection calls the production helper. The original 19/45 is an explicitly named counterfactual using the old formula and the same states; neither changes simulation state.

Offscreen means the centre is left of the field, matching `IsPlayerVisible`. Entry/exit times are interpolated between samples no more than 5 ms apart. Counters include repeated observations, not hundreds of thousands of independent gameplay cases. The exact maximum clean finish gap is 616 world; a finite sample can report slightly less.

The old safe Fast input 6080 is an early setup for the ceiling stone at 6800. A separate actual game delays that action to 6500 and still finishes 36 s with zero hits/rewards. This optional tool witness demonstrates that 6080 is not a mandatory deadline; it does not edit the published route or geometry.

See `Verification/SCREEN-00-CAMERA.md` for the measured limitations. Successful scripted return from offscreen is not a claim that a human can see or control the hidden obstacle sequence.
