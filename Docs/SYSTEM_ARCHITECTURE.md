# System Architecture

## Layers

```
 ┌───────────────────────────── DeadSpin.Runtime (Unity) ─────────────────────────────┐
 │ GameSession ── PlayerInputReader ── FirstPersonController / PlayerLook / CameraFocus │
 │      │                 Interactor ── Interactable (Inspectable, RouletteTableStation)│
 │      │                 PrototypeHud      Environment builders (greybox)              │
 └──────┼───────────────────────────────────────────────────────────────────────────────┘
        │ owns, listens to events
 ┌──────▼──────────────────────── DeadSpin.Core (pure C#) ──────────────────────────────┐
 │ Run ── Ledger (money/debt)   RoundFlow (phases)   RunOutcomeEvaluator   IRandomSource│
 │ GameStateMachine (modes)     RunConfig                                               │
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
| `GameSession` | Runtime | Owns the Run and state machine; the only code that changes mode; applies each mode to cursor, movement, look, camera and interaction. Single instance. | Core, player components |
| `PlayerInputReader` | Runtime | Samples devices once per frame (Input System or legacy) into `PlayerInputFrame`. | none |
| `FirstPersonController` | Runtime | CharacterController movement. | PlayerInputReader |
| `PlayerLook` | Runtime | Yaw on body, pitch on pivot, smoothing. | PlayerInputReader |
| `CameraFocus` | Runtime | Blends the camera to and from focus points (table view) with cursor drift. | PlayerInputReader |
| `Interactor` | Runtime | Raycasts from crosshair or cursor; focus and use of `IInteractable`; relays inspection text. | PlayerInputReader |
| `Interactable`, `InspectableObject`, `RouletteTableStation` | Runtime | Prompts, highlight, inspection, sit at table. | Interactor, GameSession |
| `PrototypeHud` | Runtime | IMGUI HUD, pause, end screen, dev panel. Configurable UI scale. | GameSession |
| `PrototypeCasinoBuilder`, `PrototypeMaterials`, `PrototypeSceneFactory` | Runtime | Generate the greybox room, palette and player rig. | Interaction, Player |
| `PrototypeSceneMenu` | Editor | Builds and saves the prototype scene and materials. | Runtime factory |

## Mode handling

| Mode | Movement | Look | Cursor | Interaction aim | Camera | Time |
|---|---|---|---|---|---|---|
| Exploring | on | on | locked | crosshair | first person | 1 |
| AtTable | off | off | free | cursor | table view | 1 |
| Paused | off | off | free | off | unchanged | 0 |
| RunEnded | off | off | free | off | first person | 1 |

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
