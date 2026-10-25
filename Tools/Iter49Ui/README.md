# ITER49 UI interop checks

FIX492 current runner:22854assertions,128pawcases and864fresh visible-rectangle corner/edge/center streams through actualUpdate. Adds800x360/960x432, DPR2.5/3 (including2400x1080framebuffer), asymmetricsafearea, compacthorizontalinset40 and unchanged8desktop/Y/56size. Every edge checks ownpawfeedback as well as action/opponentisolation. See Verification/fix492-ui.json. The5286checkpoint described below is historical FIX491/ITER49 evidence and remains preserved.
Run from the Unity project root:

    ./Tools/Iter49Ui/Run.ps1

Production controller bodies are retained in generated copies. Existing managed seams redirect native Screen/DPR, Input, Time, PlayerPrefs and selection texture acquisition only. The runner snapshots every Core/Presentation/Web source and rejects concurrent source changes. Coordinate a short source freeze with the production owner before each run.

Current combined F04/F06 checkpoint: 5,286 assertions, 27 layout/menu cases, 70 labelled projection/pose fixtures, 12 real Update equivalence runs, 32 paw cases and 8 real paw Update equivalence runs. See Verification/iter49-05-report.txt and iter49-05-ui.json for exact scope and source identity. All 15 Core hashes matched accepted stage03 at this checkpoint. The coordinator reran the same 5,286 assertions after EOF cleanup in iter49-final-ui.json. The earlier F04/F05 checkpoint of 2,766 assertions is retained separately under iter49-04 evidence.

Paw cases cover both target sizes, all four views, Main/Fast, DPR1/2 and asymmetric safe insets. Actual Update events check simultaneous edges, compatibility mouse, hold/drag/phase filtering, background and A/L input, feedback, ready/menu/pause and phone portrait suppression. No GUI paw button is injected. Full Core fingerprints match across views through actual paw-driven finishes.

Synthetic projection positions are not gameplay witnesses. Real input-stream witnesses start through normal readiness and use actual Update events. Rendering/layer/icon orientation/readability and native device timing remain browser checks.

Keep historical stage02 and iter49-04 evidence and source snapshots. Current accepted evidence is separate iter49-05 output.