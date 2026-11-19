# Development Roadmap

Status key: ✅ done · 🔨 in progress · ⏳ not started

| Phase | Goal | Status |
|---|---|---|
| 1. Foundation | Top-down table view where the player can look over and interact with the table | ✅ verified in Editor |
| 2. Core roulette | Standard wheel, pocket generation, ball and spin, result resolution, betting layout, basic bets, payouts | ✅ verified in Editor |
| 3. Dynamic wheel | Procedural geometry, 12–100+ pockets, physical transformations, clone/remove/rearrange, rings, validation | ✅ code and tests; awaiting in-Editor check |
| 4. Betting interaction | Chips, denominations, chip kinds, stacking, timer, moving and displaced chips, insurance, double down | ✅ code and tests; awaiting in-Editor check |
| 5. Game Master and rule engine | House Authority, 44 data-driven rules, interventions, dialogue, player model, moods, personalities, conflicts | ✅ code and tests; awaiting in-Editor check |
| 6. Economy | Debt installments, grace and default, Vault, 8 contracts, penalties, rewards, mandatory wagers, bankruptcy forecast | ✅ code and tests; awaiting in-Editor check |
| 7. Special gameplay | 10 special pockets, 7 special bets, Influence and 12 abilities, table shakes, 7 events and the auction, 7 challenges | ✅ code and tests; awaiting in-Editor check |
| 8. Environment and presentation | Rooms as panels, full HUD, Game Master animation, mood lighting and wall names, synthesised audio, voice clip hook | ✅ code, type-checked; awaiting in-Editor check |
| 9. Progression and endgame | Five stages, 18 unlocks, run randomisation, four-phase final encounter, 6 endings, save and continue | ✅ code and tests; awaiting in-Editor check |
| 10. Testing and optimisation | Balance simulator (4 bot styles), invariant checks, editor report menu, 161 headless tests | ✅ headless; profiling and a PC build need the Editor |

## Milestone: playable prototype (master prompt section 23)

| Requirement | Phase | Status |
|---|---|---|
| Table camera (replaces first-person movement) | 1 | ✅ code |
| One interactive roulette table | 1–2 | ✅ code |
| Working 36/37-pocket wheel | 2 | ✅ code (37, single zero) |
| Working 72-pocket wheel | 3 | ✅ code and tests |
| Procedural pocket generation | 2–3 | ✅ code (any count; Editor-tested at 37) |
| Physical wheel transformation | 3 | ✅ code (animated in `WheelView`) |
| Ball spinning and result resolution | 2 | ✅ code |
| Basic chip pickup and placement | 4 | ✅ code |
| At least six traditional bet types | 2 | ✅ 13 types |
| Correct probability and payout calculations | 2–3 | ✅ tested (simulation + exact) |
| Starting money and debt | 1 | ✅ |
| Basic Game Master dialogue | 5 | ✅ code (trigger, mood and personality aware) |
| Ten Game Master interventions | 5 | ✅ 44 rules |
| Round progression | 1–2 | ✅ code |
| Bankruptcy condition | 1 | ✅ |
| Basic win condition | 1 | ✅ |

## Phase 1 log

- 2026-11-18: Repository created from scratch. Core run state, ledger, outcomes, state machine,
  round flow and RNG with 41 tests passing headless. Player, interaction, greybox casino,
  table, HUD and scene builder written and type-checked against UnityEngine reference
  assemblies.
- 2026-11-18: Owner verified the first-person build in the Editor (all working), then changed
  direction to a table-only game with a top-down camera. Walking removed; `TableCameraRig` and
  `TableViewNavigator` added; room relit around the table. 50 tests passing. Waiting on an
  in-Editor check of the new view.
- 2026-11-18: Owner verified the top-down view in the Editor. Phase 2 built: wheel model,
  odds, seeded outcome selection, all traditional bets, true-odds payouts, table game rules;
  procedural wheel, ball animation, betting layout, chip stacks, round controls and HUD.
  85 tests passing. Waiting on an in-Editor check.

- 2026-11-19: Phases 3 to 10 built on the `full-game` branch, one commit per phase. Core is
  complete and playable headless: 161 tests pass and the balance simulator plays 800+ full runs
  with no invariant problems. Phase 8 runtime (session, table controller, HUD, rooms, Game
  Master, environment, audio) is written and type-checked against the Unity reference
  assemblies, but has not been run in the Editor yet.

## Next

1. Owner runs the full-game checklist in TESTING_LOG.md in Unity 6000.6.9f1.
2. Fix whatever the Editor run turns up, then tune balance from play (see the balance numbers in
   TESTING_LOG.md and **Dead Spin > Simulate Runs**).
3. Replace IMGUI with the final animated UI, add recorded voice lines and real audio, profile and
   make a Windows build.
