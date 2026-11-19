# System Architecture

## Layers

```
 ┌───────────────────────────── DeadSpin.Runtime (Unity) ─────────────────────────────┐
 │ GameSession ── PlayerInputReader ── TableCameraRig (uses Core TableViewNavigator)     │
 │      │                 Interactor ── Interactable (InspectableObject)               │
 │      │                 PrototypeHud      Environment builders (greybox)              │
 └──────┼───────────────────────────────────────────────────────────────────────────────┘
        │ owns, listens to events
 ┌──────▼──────────────────────── DeadSpin.Core (pure C#) ──────────────────────────────┐
 │ Run ── Ledger (money/debt)   RoundFlow (phases)   RunOutcomeEvaluator   IRandomSource│
 │ GameStateMachine (modes)     RunConfig            TableViewNavigator (camera rules)   │
 └──────────────────────────────────────────────────────────────────────────────────────┘
 DeadSpin.Editor → PrototypeSceneMenu (uses Runtime factory)
 DeadSpin.Tests.EditMode → Core
```

Rule: **Core never references Unity**; Runtime may reference Core; Editor may reference both.
The `noEngineReferences` flag on `DeadSpin.Core.asmdef` enforces this.

## Systems implemented (Phase 1)

| System | Type | Responsibility | Depends on |
|---|---|---|---|
| `RunConfig` / `RunConfigAsset` | Core data / ScriptableObject | Starting money, debt, rounds, minimum bet, phase timings, seed. Validates itself. | none |
| `Ledger` | Core | Only store of balance and debt. Credit, debit, forced debit, pay/increase/forgive debt. Logs every change. | none |
| `Run` | Core | Aggregate for one playthrough: config copy, ledger, round flow, RNG, rounds played, outcome. | Ledger, RoundFlow, RunOutcomeEvaluator |
| `RunOutcomeEvaluator` | Core | Pure win/loss rules: freedom, bankruptcy, deadline. | none |
| `RoundFlow` | Core | Round phases. Timed phases tick; spin and resolution advance only when their systems report back. | RunConfig |
| `GameStateMachine` | Core | Top-level modes with an explicit transition table. | none |
| `SeededRandom` | Core | PCG32 RNG with unbiased ranges. | none |
| `TableViewNavigator` | Core | Camera view selection, zoom and pan limits for the table view. | none |
| `GameSession` | Runtime | Owns the Run and state machine; the only code that changes mode; applies each mode to camera controls, interaction and time scale. Single instance. | Core, table rig |
| `PlayerInputReader` | Runtime | Samples devices once per frame (Input System or legacy) into `PlayerInputFrame`. | none |
| `TableCameraRig` | Runtime | Top-down camera: view presets in table space, smoothed pan, zoom, pitch and FOV, subtle cursor drift. | PlayerInputReader, TableViewNavigator |
| `Interactor` | Runtime | Raycasts through the cursor; focus and use of `IInteractable`; relays inspection text. | PlayerInputReader |
| `Interactable`, `InspectableObject` | Runtime | Prompts, highlight, inspection. | Interactor |
| `PrototypeHud` | Runtime | IMGUI HUD, pause, end screen, dev panel. Configurable UI scale. | GameSession |
| `PrototypeCasinoBuilder`, `PrototypeMaterials`, `PrototypeSceneFactory` | Runtime | Generate the greybox room, palette and table camera. | Interaction, Player |
| `PrototypeSceneMenu` | Editor | Builds and saves the prototype scene and materials. | Runtime factory |

## Mode handling

| Mode | Camera controls | Interaction | Cursor | Time |
|---|---|---|---|---|
| AtTable | on | on | visible | 1 |
| Paused | off (holds pose) | off | visible | 0 |
| RunEnded | off | off | visible | 1 |

## Planned systems (master prompt section 19) and where they will live

| System | Core part | Runtime part | Phase |
|---|---|---|---|
| Roulette manager / wheel generator | `WheelConfiguration`, `Pocket`, modifiers, validator | procedural mesh, transformation animation | 2–3 |
| Spin | outcome selection (weighted, seeded) | wheel/ball animation driven to the result | 2 |
| Betting manager | bet types, coverage sets, validation, payouts | betting layout, zones | 2, 4 |
| Probability manager | per-pocket and per-bet odds | odds display | 2–3 |
| Rule manager | rule definitions, conflicts, priority, lifetime | rule visuals/audio | 5 |
| Game Master AI | Authority, intervention planner, player model | dialogue presentation, animation | 5 |
| Economy | contracts, penalties, installments (extends Ledger) | contract UI | 6 |
| Influence / abilities, events, challenges, special pockets | definitions and effects | presentation | 7 |
| Audio, UI manager, casino rooms | none | presentation | 8 |
| Progression, save | stages, unlocks, run randomisation, serialisable state | menus | 9 |
