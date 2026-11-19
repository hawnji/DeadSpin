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

### In-Editor result (owner)

The owner ran the first-person build in Unity 6000.6.9f1 and reported that everything worked.

## 2026-11-18: Top-down table view

### Automated: 50 passed, 0 failed (headless)

Added `TableViewNavigatorTests` (9): starts on the first view, next/previous wrap, invalid
indexes rejected, zoom and pan clamped, switching views resets framing, the locked Game Master
view ignores pan and zoom, change events fire only on real changes, invalid construction
throws. `GameStateMachineTests` rewritten for the table-only modes (6).

### Static

Runtime and Editor scripts type-check with no errors against the reference assemblies (legacy
input branch and no-backend branch). The Input System branch was not compiled outside Unity.

### Not yet verified: in-Editor checklist (Unity 6000.6.9f1)

1. Run **Dead Spin > Build Prototype Scene** again (it replaces the old scene). No errors.
2. Play: the game starts looking down at the table. The cursor is visible. The HUD shows $2,500,
   $50,000, round 1 / 18 and four view tabs at the top.
3. The table is the brightest thing on screen; the room fades into darkness around it. The
   Game Master's hands rest on the far rail.
4. 1, 2, 3, 4, Tab and clicking the tabs move smoothly between Table, Betting layout, Wheel and
   The Game Master. Nothing snaps.
5. WASD / arrows and right- or middle-drag pan; the view stops at the table's edges. Scroll
   zooms in and out within limits. Switching view resets pan and zoom. The Game Master view
   cannot be panned or zoomed.
6. Hovering the wheel, layout, chip tray and Game Master makes them glow and shows a prompt by
   the cursor; clicking shows their description at the bottom.
7. Esc pauses (camera holds still, time stops); Esc or Resume continues; Restart run resets the
   HUD and returns to the Table view.
8. F1 dev panel: "Play an empty round", "- $1,000", "Forgive all debt" reach the same end
   screens as before.
9. Test Runner shows 50 EditMode tests passing.
10. 60 FPS or better in the Game view on a midrange PC.

### In-Editor result (owner)

The owner verified the top-down table view in Unity 6000.6.9f1: working.

## 2026-11-18: Phase 2, core roulette

### Automated: 85 passed, 0 failed (headless)

| Suite | Tests | What it proves |
|---|---|---|
| `WheelTests` | 9 | European layout (order, colours alternate, 18/18/1); validation catches dead, zero-only, duplicate-id and red-zero wheels; clones count in odds (13 in 4 of 72 pockets = 4/72); weights and closed pockets; seeded selection reproducible; closed/zero-weight pockets never drawn; **740,000-spin** chi-squared on the European wheel and **400,000-spin** chi-squared on a weighted 12-pocket wheel with a clone |
| `BettingTests` | 13 | Every spot exists (37 straights, 60 splits, 12 streets, 22 corners, 11 six lines, outside bets); spots stay on the felt; only touching numbers form splits/corners; shape validation; chips on the same spot stack; outside coverage; zero loses all outside bets; classic payouts reproduced exactly; every spot returns 36/37 per dollar; payouts drop when 13 is cloned (8.73 to 1); profit rounds down; unwinnable bets pay nothing |
| `TableGameTests` | 12 | Money moves to the table; min, max, funds, legality and closed-betting checks; minimum applies to the spot total; removal and clear refunds; bets lock at close; result hidden until the ball settles; 37 straights settle to exactly one win; seeded runs repeat; all-in loss is bankruptcy after the spin; wheel swaps only between rounds; **20,000-round** red-bet simulation returns 36/37 within tolerance |
| `LedgerTests` | +1 | Wagers are losses only once resolved |

### Static

Runtime and Editor scripts type-check with no errors (legacy-input and no-backend branches).
Not compiled here: the Input System branch and `Shaders/WorldText.shader`.

### Not yet verified: in-Editor checklist (Unity 6000.6.9f1)

1. Run **Dead Spin > Build Prototype Scene** again. No errors. 22 materials in `Materials/Prototype`.
2. Play: the wheel shows 37 coloured pockets with gold frets and readable numbers; the rotor
   turns slowly. The felt shows 0–36 in red/black boxes, 2 to 1, dozens and the even-money row.
   Numbers are hidden behind objects that cover them (they should not draw through things).
3. Hover a number: it glows and a prompt reads "Straight 17 pays 35 to 1 (2.7%)". Hover the line
   between 17 and 20: a small gold marker appears and both boxes glow (split, 17 to 1). Check a
   corner, a street (near edge of a column) and a six line.
4. Space starts round 1: Game Master line, then PLACE YOUR BETS with a countdown, camera moves
   to the layout.
5. Click to place $25 chips; Q/E change the chip; Shift-click takes chips back; Backspace clears.
   Balance and "On the table" update. Bets below $10 or over $10,000 per spot are refused with a
   message.
6. Space (or the timer) closes betting: the camera moves to the wheel, the ball circles, drops,
   bounces and settles. The number is shown only after it settles, and it is the pocket the ball
   is in.
7. Winnings are correct (e.g. $10 straight win returns $360; $10 on red returns $20).
8. The camera returns, the number appears under LAST NUMBERS, the round counter advances.
9. Between rounds, "Pay the house" buttons reduce the debt. Paying it off shows FREEDOM.
10. Losing everything shows THE HOUSE ALWAYS WINS after the spin; 18 rounds with debt shows TIME IS UP.
11. Pause during a spin freezes the ball; resuming continues and settles correctly.
12. Test Runner: 85 EditMode tests pass.

## 2026-11-19: Phases 3 to 10, the full game

### Automated: 161 passed, 0 failed (headless, .NET 8)

New suites on top of Phases 1 and 2:

| Suite | Tests | What it proves |
|---|---|---|
| `DynamicWheelTests` (Phase 3) | 14 | Every wheel preset and size is valid; generated wheels alternate colours; 72- and 100-pocket simulations match calculated odds; payouts stay honest on every size; multiple balls; clone, remove, add, swap, shuffle, resize, colour reversal, weights and rings are validated and pure; the layout adapts and expands mid-run |
| `RuleEngineTests` (Phase 5) | 11 | Catalog covers every intervention type; every rule activates or gives a reason; timed rules expire; conflicts resolve by priority; fairness guards (minimum bet, payable debt); the red tax's combination loophole; the Game Master's brain targets habits and never overspends; a dialogue line exists for every trigger |
| `CampaignTests` (Phases 4 to 9) | 40 (+parameterised cases) | Stages, installments with grace and default, voluntary payments, the Vault, bankruptcy warnings, contracts, Debt Freeze, abilities (cost, cooldown, House Inspection, Chip Anchor vs shakes, Rule Cancel, consumables, insurance), special pockets (relief, tax, Influence, ticket, bomb, curse), events (House Collects, Double Spin, the auction), challenges, the final encounter and His Final Offer, endings, records, lounge, shop, Marks and unlocks, JSON, save and load continuing the same seeded game, saving refused mid-round |
| `CampaignSimulationTests` (Phase 10) | 4 styles x 40 runs | Every simulated run reaches an ending; money, debt and Influence never go negative; money at risk matches the chips on the table; the wheel and table modifiers stay valid; no exceptions |

### Balance simulator: 200 runs per bot style (seeded, headless)

| Bot | Win rate | Endings | Average rounds | Notes |
|---|---|---|---|---|
| Cautious (small outside bets) | 0% | 115 house always wins, 85 The Deal | 15.6 | Survives longest; often takes His Final Offer |
| Balanced | 0% | 166 house always wins, 34 deadline | 15.8 | Most runs end at the stage-4 installment (round 15) |
| Aggressive | 1.0% | 192 house always wins, 2 freedom, 6 deadline | 13.3 | |
| Bold (all-in long shots) | 3.5% | 193 house always wins, 7 freedom | 1.4 | Lives or dies in the first rounds |

0 invariant problems across 800 runs. Turning about $2,500 into $50,000 at near-fair odds is
close to the mathematical limit for scripted play, so these numbers are a floor, not a target.
Regenerate them any time with **Dead Spin > Simulate Runs** (writes `Docs/BALANCE_REPORT.md`).

### Static: Unity scripts type-check

All Runtime and Editor scripts (including the new `GameSession`, `RouletteTableController`,
`GameHud`, `GameMasterView`, `EnvironmentDirector`, `AudioDirector` and `BalanceSimulatorMenu`)
compile with no errors against the Unity reference assemblies, with the legacy-input branch and
with no input backend defined. Not compiled here: the Input System branch and shaders.

### Not yet verified: full-game checklist (Unity 6000.6.9f1)

1. **Dead Spin > Build Prototype Scene** again. No errors. Press Play: the title screen shows
   New run, Continue (greyed out), Unlocks and Quit.
2. New run: the table HUD shows balance, debt, Influence, round 1 / 18, bet limits and the stage
   name. The Game Master greets you. Rooms buttons sit under the status panel.
3. Space starts the round. Place bets, change chip value (Q/E) and kind; click a special-bet box
   (Neighbours asks for a number, Combination opens a picker). Stage 1 refuses special bets.
4. Close betting: the ball spins and settles; result, special pocket messages and loopholes show
   at the top; history updates.
5. Over the rounds he announces rules (top-right list, hidden ones counted), sometimes mid-bet;
   the wheel visibly transforms and the layout rebuilds. Rule ends appear in the feed.
6. Abilities bar: Wheel Analysis adds odds to the hover text; House Inspection reveals hidden
   rules and the wheel; Lucky Coin asks for a number; right-click Insurance or Double Down to pick
   a bet. Disabled buttons explain why on hover.
7. Rooms: Lounge rest (+1 Influence once per stage); Shop buys; Vault deposit, withdraw and pay
   debt; Records unlock by stage and loopholes; Office shows contracts from stage 2.
8. Offers: a challenge or contract panel appears on the right; an auction lets you bid.
9. Installments are collected after rounds 7, 11 and 15; a miss adds a late fee, a second miss
   ends the run. Warnings appear under the rooms.
10. Lights flicker more and tint by stage; names appear on the walls from stage 3; the Game
    Master's eyes change with his mood and he grows toward the final table.
11. Round 16 onward: the final table phases; before the last spin, His Final Offer blocks play
    until you choose.
12. The ending screen shows the ending, stats and Marks; Unlocks spends Marks; a new run uses
    them. Esc > Save and return to title, then Continue resumes between rounds.
13. Sounds: chips, ball roll and clicks, win and loss, transformation, shakes, heartbeat when
    time or money runs low, ambient drone, a murmur when he speaks.
14. **Dead Spin > Simulate Runs (100 per style)** writes `Docs/BALANCE_REPORT.md`.
15. Test Runner: 161 EditMode tests pass.
