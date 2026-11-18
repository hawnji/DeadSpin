# Testing Log

## How tests run

| Where | Command | Covers |
|---|---|---|
| Unity Test Runner | Window > General > Test Runner > EditMode > Run All | `DeadSpin.Tests.EditMode` |
| Headless (.NET 8) | `dotnet test tools/CoreTests` | Same test files, same Core sources |
| CI | `.github/workflows/core-tests.yml` on push and PR | Headless suite |
| Type check of Unity scripts | compile `Scripts/**` against UnityEngine 2021.3 reference assemblies (cloud session only, not committed) | API usage and syntax of Runtime and Editor code |

## 2026-11-18: Phase 1

### Automated: 41 passed, 0 failed (headless, .NET 8, C# 9)

| Suite | Tests | What it proves |
|---|---|---|
| `SeededRandomTests` | 6 | Same seed gives same sequence; ranges hold; 200k-sample chi-squared uniformity over 37 buckets (p = 0.001) |
| `LedgerTests` | 9 | Starting values; failed debits change nothing; exact-balance debits; winnings totals; forced debits never go negative; debt payment clamps to debt and balance; debt increase and forgiveness; invalid amounts throw; events carry the round stamp |
| `GameStateMachineTests` | 6 | Legal and illegal transitions; pause resumes only to the interrupted mode; RunEnded is terminal |
| `RoundFlowTests` | 8 | Phase order; countdown; large time steps never skip the spin; out-of-order notifications rejected; betting extension; early close; zero-length summary; abort |
| `RunTests` | 12 | Default values match the brief ($2,500 / $50,000 / 18); invalid configs rejected; config copied; seeded reproducibility; round counter; freedom; bankruptcy; money on table prevents bankruptcy; bankruptcy deferred until the spin resolves; deadline; single end event |

### Static: Unity scripts type-check

Runtime and Editor scripts compile with no errors against UnityEngine/UnityEditor reference
assemblies, both with the legacy-input branch and with no input backend defined. The Input
System branch could not be compiled outside Unity.

### Not yet verified: in-Editor checklist (Unity 6000.6.9f1)

The cloud environment cannot run Unity. These checks need to be done in the Editor:

1. Project opens with no compile errors or warnings from `Assets/_Project`.
2. **Dead Spin > Build Prototype Scene** creates `Assets/_Project/Scenes/Prototype.unity`,
   ten materials in `Assets/_Project/Materials/Prototype`, and adds the scene to Build Settings.
3. Play: the cursor locks, the HUD shows $2,500 balance, $50,000 debt, round 1 / 18.
4. WASD moves smoothly, Shift sprints, the mouse looks around, pitch stops at about ±80°.
   The player cannot walk through walls, pillars, the table or the Game Master.
5. Looking at the stool from about 2 m shows "[E] Take a seat at the table" and the stool glows.
6. E sits: the camera glides (about 0.75 s) to a view of the layout and wheel; the cursor
   appears; moving the mouse drifts the view slightly.
7. Hovering the wheel, layout, chip tray and the Game Master shows prompts; clicking shows
   their descriptions at the bottom of the screen.
8. Esc or Tab stands up; the camera glides back; walking works again.
9. Each of the four doors can be inspected.
10. Esc while walking pauses (time stops); Resume returns; Restart run resets the HUD and
    position.
11. F1 opens the dev panel. "Play an empty round" advances the round counter. Playing 18
    rounds shows TIME IS UP. "- $1,000" three times shows THE HOUSE ALWAYS WINS. "Forgive all
    debt" shows FREEDOM. "New run" starts over.
12. The Test Runner shows 41 EditMode tests passing.
13. Frame rate stays at or above 60 FPS in the Game view on a midrange PC.
