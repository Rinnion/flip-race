# Final six-course UI check

Run this after release approval activates `TracksRhythm.All`:

```powershell
dotnet run --project Tools/RhythmUiCheck/RhythmUiCheck.csproj --configuration Release
```

The check intentionally fails if only the three historical courses are active. It verifies the actual catalogue IDs/order, `Get` resolution, reward budgets, course descriptions, six full and seven previous card rectangles at 568×320, 844×390 and 1280×720, and the real `FlightPointer` selection handler at the center and corners of every full card. Selecting a course must retain the current preset/custom draft, active game, selected heroes and ready state; a tap in the grid gap must do nothing.

Only pure production rectangle and menu-selection code is invoked. It does not run native Unity lifecycle, drawing, frame timing, browser input or persistence APIs, so browser review remains a separate requirement.

Output is `Verification/rhythm-final-ui-layout.json`. Historical stage2 UI exports are not overwritten.
