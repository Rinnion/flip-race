# V4 authored trial proof

Run with .NET 10:

```powershell
dotnet run --project Tools/TrackProofV4/TrackProofV4.csproj
```

Recorded result: **PASS 17 221 checks**. The project compiles the production Core sources directly; it does not replace motion, contact, reward, or finish logic. Runtime geometry is in `Assets/Scripts/Core/TracksV4.cs`.

## Routes

All inputs below are horizontal world-distance coordinates for each player. Time excludes the readiness countdown. Input placement follows actual `CurrentSpeed`; a new free reward inside a step is handled by solving the input time with calls to production `Advance` on copied state. It does not divide every input distance by base speed.

| Route | Motion mode | Finish | Positive / negative | Collisions |
|---|---|---:|---:|---:|
| Safe | All three | 20.0 s | 0 / 0 | 0 |
| Ideal | GravityAirFlip | 18.8 s | 4 / 0 | 0 |
| Hasty return | GravityAirFlip | 19.4 s | 3 / 1 | 0 |
| Late return | GravityAirFlip | 19.1 s | 3 / 0 | 0 |

```text
Safe:  1320, 1690, 1990, 2280, 2640, 3600, 3960
Ideal: 1000, 1057.2, 1393, 1670, 1990, 2280, 2520, 3000, 3083.6, 3600, 3960
Hasty: same as Ideal, replace 2520 with 2465
Late:  same as Ideal, replace 2520 with 2620
```

Safe uses only surface departures in all three modes. Ideal uses two airborne repeats, at 1057.2 and 3083.6. Removing either repeat from that authored route loses a positive or causes a collision. Every earned effect completes before the prepared finish: earned/completed counts agree, no active effect, empty FIFO.

The challenge is 4400 units long (20 seconds at base speed), with ten stones of heights 48 and 92. The first possible stone contact is at x=886, after 4.027 seconds. It has two immediate free positives, two positives linked to clean stone clearance, and one visible negative trap. The free comparison is a separate empty 2200-unit / 10-second segment.

## Ideal timing

| Distance | Elapsed seconds | Input |
|---:|---:|---|
| 1000 | 4.545455 | Surface departure |
| 1057.2 | 4.805455 | Low airborne return, 0.26 s after departure |
| 1393 | 6.031818 | Surface departure |
| 1670 | 7.153030 | Surface departure |
| 1990 | 8.445455 | Surface departure |
| 2280 | 9.763636 | Surface departure |
| 2520 | 10.854545 | Surface departure |
| 3000 | 12.736364 | Surface departure |
| 3083.6 | 13.116364 | High airborne return, 0.38 s after departure |
| 3600 | 15.163636 | Surface departure |
| 3960 | 16.800000 | Surface departure |

The quick three-departure sequence has actual gaps 1.292425 and 1.318182 seconds. Maximum interior interval is 2.047273 seconds, first input is at 4.545455 seconds, and final input to finish is 2 seconds.

## Coverage and limits

- Safe routes in all three modes and the ideal route pass with 30/60/144 Hz and 0.317 / 2.5-second outer simulation steps.
- Every input of those routes passes an **individual ±40 ms elapsed-time perturbation**, with all other inputs still placed by distance. This is not a guarantee for arbitrary combinations of timing errors.
- Free positives are confirmed at airborne contact. Linked positives are pending at contact, then confirmed after clean clearance. A late linked-before attempt is canceled by its linked collision; an earlier free positive remains confirmed.
- Hasty return enters the negative and misses the linked-after reward. Late return misses both. Neither causes a collision, and their effects complete before finish.
- Recovery samples shift one input by multiples of 17.6 world units from −176 to +176, retaining input order. These are **distance perturbations**, not measured elapsed-time offsets under boost or slow.
- Those samples produced 244 first-collision states: safe Linear 47, safe Gravity 62, safe GravityAirFlip 55, ideal GravityAirFlip 80. From the captured state, the search waits the actual remaining recovery penalty and an additional 150 ms reaction time, then uses only inputs accepted by the current production controls and available charge.
- Each recovery success requires reaching beyond the next stone's trailing edge without another collision. The search considers no input, one input delayed up to 2.5 seconds, or a second accepted input 0.15–1.8 seconds later. A collision with the final stone has no next stone to test.
- Recovery results cover those sampled states; they are not an exhaustive proof for every possible error or a claim about human playability. Browser input/readability and player preference are separate checks.

No full V4 race tracks are included in this stage. The runtime API remains `OpenId`, `ChallengeId`, `Open()`, `Challenge()`, `Get(id)`, and `IsOpen(id)`.
