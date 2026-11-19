# Development Roadmap

Status key: ✅ done · 🔨 in progress · ⏳ not started

| Phase | Goal | Status |
|---|---|---|
| 1. Foundation | Top-down table view where the player can look over and interact with the table | 🔨 code complete, awaiting in-Editor verification |
| 2. Core roulette | Standard wheel, pocket generation, ball and spin, result resolution, betting layout, basic bets, payouts | ⏳ |
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
| One interactive roulette table | 1 | ✅ code (placeholder) |
| Working 36-pocket wheel | 2 | ⏳ |
| Working 72-pocket wheel | 3 | ⏳ |
| Procedural pocket generation | 2–3 | ⏳ |
| Physical wheel transformation | 3 | ⏳ |
| Ball spinning and result resolution | 2 | ⏳ |
| Basic chip pickup and placement | 4 | ⏳ |
| At least six traditional bet types | 2 | ⏳ |
| Correct probability and payout calculations | 2–3 | ⏳ |
| Starting money and debt | 1 | ✅ |
| Basic Game Master dialogue | 5 | ⏳ |
| Ten Game Master interventions | 5 | ⏳ |
| Round progression | 1–2 | 🔨 flow and counter done; needs the spin |
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

## Next: Phase 2 plan (draft)

1. Core `Pocket` and `WheelConfiguration` (ordered pockets, colours, weights, duplicates) with
   a validator, and a factory for the standard European 37-pocket layout.
2. Core bet model: a bet is a set of covered displayed numbers or a predicate (red, odd, dozen);
   payout derived from the configuration's probability with a configurable house edge.
3. Seeded weighted outcome selection, with 1,000,000-spin simulation tests per configuration.
4. Runtime wheel: procedural mesh from the configuration, rotor spin, ball animated along a
   path to the pre-selected pocket.
5. Betting layout zones on the table, a temporary click-to-bet input (physical chips arrive in
   Phase 4), wired into `RoundFlow` so a complete round plays out with real money.
