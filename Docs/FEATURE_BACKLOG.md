# Feature Backlog

Items beyond the current phase, grouped by the phase that will deliver them. The master prompt
is the source of truth for scope; this list records decisions and extras found along the way.

## Phase 2–3: Roulette
- `WheelConfiguration` with duplicates, weights, inactive pockets, special pocket types, rings.
- Configuration diff to drive transformations and bet migration.
- Payouts computed from true probability with a configurable house edge per rule set.
- Wheel camera view follows the ball during a spin.
- Re-bet last round's chips with one key.
- Racetrack / neighbour bets (Phase 7 special bets).
- Odds overlay for the whole layout (the Wheel Analysis ability).
- Simulation tests: 1,000,000 spins per supported pocket count.

## Phase 4: Chips
- Kinematic chip stacks, physics only during table shakes.
- Quick-bet panel for large wagers.
- Chip denominations and special chip types (magnetic, sticky, weighted, fragile, protected).

## Phase 5: Game Master
- Rule and intervention definitions as ScriptableObjects; Authority costs configurable.
- Player model (favourite numbers, bet categories, streaks) feeding the intervention planner.
- Dialogue system with trigger tags, ready for voice lines.
- Fairness validator: every active configuration must keep at least one winnable bet.

## Phase 6–7: Economy and special gameplay
- Debt installments and mandatory wagers; contracts (Generous Offer, Double or Nothing, Insurance, Devil's Bargain).
- Influence, twelve abilities, special pockets, special bets, events, challenges.

## Phase 8–9: Presentation and progression
- Replace IMGUI HUD with animated UI (UI Toolkit or uGUI + TextMeshPro); UI scale setting in options.
- Rooms: Private Lounge, Shop, Vault, Records Room, Game Master's Office, as menus or short transitions away from the table.
- Five stages, roguelite unlocks, randomised runs, final four-phase encounter, five endings.
- Save system built on serialising Core state (Core already avoids Unity types).

## Later / nice to have
- Controller support (input already isolated in `PlayerInputReader`).
- Key rebinding and camera pan/zoom speed in an options menu.
