# Development Roadmap

Status key: ✅ done · 🔨 in progress · ⏳ not started

| Phase | Goal | Status |
|---|---|---|
| 1. Foundation | Top-down table view where the player can look over and interact with the table | ✅ verified in Editor |
| 2. Core roulette | Standard wheel, pocket generation, ball and spin, result resolution, betting layout, basic bets, payouts | 🔨 code complete, awaiting in-Editor verification |
| 3. Dynamic wheel | Procedural geometry, 12–100+ pockets, physical transformations, clone/remove/rearrange, rings, validation | ⏳ |
| 4. Betting interaction | Physical chips, denominations, pickup and placement, stacking, timer, betting UI | ⏳ |
| 5. Game Master and rule engine | House Authority, data-driven rules, interventions, dialogue, adaptive behaviour, conflicts | ⏳ |
| 6. Economy | Debt, bankruptcy, contracts, penalties, rewards, round requirements | ⏳ (ledger and outcomes started in Phase 1) |
| 7. Special gameplay | Special pockets, special bets, Influence and abilities, table manipulation, events, challenges | ⏳ |
| 8. Environment and presentation | Rooms, Game Master model, lighting, materials, audio, UI polish, storytelling | ⏳ |
| 9. Progression and endgame | Five stages, roguelite unlocks, run randomisation, final encounter, endings, saves | ⏳ |
| 10. Testing and optimisation | Balancing, probability validation, profiling, stable PC build | ⏳ (testing runs throughout) |

## Milestone: playable prototype (master prompt section 23)

| Requirement | Phase | Status |
|---|---|---|
| Table camera (replaces first-person movement) | 1 | ✅ code |
| One interactive roulette table | 1–2 | ✅ code |
| Working 36/37-pocket wheel | 2 | ✅ code (37, single zero) |
| Working 72-pocket wheel | 3 | ⏳ |
| Procedural pocket generation | 2–3 | ✅ code (any count; tested in Editor at 37) |
| Physical wheel transformation | 3 | ⏳ |
| Ball spinning and result resolution | 2 | ✅ code |
| Basic chip pickup and placement | 4 | ⏳ |
| At least six traditional bet types | 2 | ✅ 13 types |
| Correct probability and payout calculations | 2–3 | ✅ tested (simulation + exact) |
| Starting money and debt | 1 | ✅ |
| Basic Game Master dialogue | 5 | ⏳ |
| Ten Game Master interventions | 5 | ⏳ |
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

## Next: Phase 3 plan (draft)

1. Core wheel transforms as pure functions: add/remove/clone/shuffle/recolour pockets, resize,
   each producing a new validated `WheelConfiguration` plus a diff.
2. Generic wheel factories for 12, 18, 24, 36, 37, 48, 72 and 100 pockets, and a betting layout
   that scales (or switches to a readable compact view) beyond 36 numbers.
3. `WheelView` animates between configurations from the diff (pockets splitting, sliding,
   emerging, diameter change), then rebuilds.
4. Multiple rings and validation of incompatible modifiers.
