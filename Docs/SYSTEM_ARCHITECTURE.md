# System Architecture

## Layers

```
 ┌──────────────────────────────── DeadSpin.Runtime (Unity) ────────────────────────────────┐
 │ GameSession (title, saves, meta) ── PlayerInputReader ── TableCameraRig ── Interactor       │
 │      │ ticks, listens                                                                       │
 │ RouletteTableController ── WheelView / BallSpinAnimator / BettingLayoutView / ChipStackView │
 │ GameHud (table, rooms, offers, screens)   GameMasterView   EnvironmentDirector   AudioDirector│
 └──────┼───────────────────────────────────────────────────────────────────────────────────────┘
        │ owns one CasinoRun; calls its API; renders its events
 ┌──────▼──────────────────────────────── DeadSpin.Core (pure C#) ─────────────────────────────┐
 │ CasinoRun (round director, IGameHost) ── Run / Ledger / RoundFlow / TableGame / Wheel        │
 │   RuleEngine + RuleCatalog ── GameMasterBrain / AuthorityPool / PlayerProfile / DialogueBank │
 │   Contracts / PaymentSchedule ── Abilities / Inventory / Shop / SpecialPockets / Events /     │
 │   Challenges ── Stages / FinalEncounter / Endings / CasinoRecords / MetaProgress / RunSetup   │
 │ SaveSystem + MiniJson          RunSimulator (bots, invariants)                                │
 └──────────────────────────────────────────────────────────────────────────────────────────────┘
 DeadSpin.Editor → PrototypeSceneMenu, BalanceSimulatorMenu
 DeadSpin.Tests.EditMode → Core (also run headless by tools/CoreTests)
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
| `Pocket`, `WheelConfiguration`, `StandardWheels` | Core | The wheel as data: ordered pockets with number, colour, integer weight, active flag. Validation. European factory. | none |
| `WheelOdds`, `OutcomeSelector` | Core | Exact odds from the configuration; one weighted seeded draw picks the winning pocket. | Wheel, IRandomSource |
| `BetSelection`, `StandardLayout`, `BetSpot` | Core | What a bet covers; every legal spot on the felt with its position in layout units. | Wheel |
| `PayoutPolicy` | Core | Payout ratio from true odds and house edge; profit rounded down. | Wheel, Betting |
| `BetBook`, `TableGame`, `RoundResult` | Core | Placing/removing chips through the Ledger, min/max limits, spin protocol (pick result → animate → settle), history. | Run, Ledger, Wheel, Betting |
| `WheelView`, `WheelMeshBuilder` | Runtime | Procedural bowl and rotor for any pocket count; numbers as depth-tested labels. | WheelConfiguration |
| `BallSpinAnimator` | Runtime | Rotor and ball motion planned to land on a given pocket. | WheelView |
| `BettingLayoutView`, `BetSpotTarget`, `ChipStackView` | Runtime | Felt layout from `StandardLayout`, hover coverage, click to bet, pooled chip stacks. | TableGame |
| `RouletteTableController` | Runtime | Bridges `TableGame` to the views, camera and announcements. | everything above |
| `TableHud`, `HudSkin` | Runtime | Round phase and countdown, Game Master line, chips, history, debt payments; shared IMGUI style. | RouletteTableController |
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

## Systems added in Phases 3 to 10

| System | Type | Responsibility |
|---|---|---|
| `WheelTransforms`, `WheelFactory`, `WheelChange` | Core | Pure, validated wheel changes (clone, remove, add, swap, shuffle, resize, recolour, rings) and the diff the view animates |
| `TableModifiers`, `PayoutModifier`, chip kinds, shakes | Core | Limits, closed sections, payout conditions (with hidden ones and loopholes), balls, chip physics properties |
| `RuleDefinition`, `RuleEffects`, `RuleCatalog`, `RuleEngine` | Core | 44 data-driven rules; composition by priority, conflicts, durations, fairness checks |
| `GameMasterBrain`, `AuthorityPool`, `PlayerProfile`, `DialogueBank` | Core | His budget, intervention planning against the player's habits, moods, personalities, lines |
| `CasinoRun` | Core | The round director: owns every system, runs a round (stage, final phase, events, interventions, betting, spin, special pockets, contracts, challenges, installments), exposes the player API and events |
| `ContractBook`, `PaymentSchedule`, `BankruptcyForecast` | Core | Contracts, installments with grace and default, warnings |
| `AbilitySystem`, `Inventory`, `InfluenceWallet`, `Shop` | Core | Influence, 12 abilities, charges, cooldowns, special chips, shop shelf |
| `SpecialPocketCatalog`, `EventDirector`, `AuctionState`, `ChallengeBoard` | Core | Special pockets, events, the auction, challenges |
| `StageTable`, `FinalEncounter`, `EndingCatalog`, `CasinoRecords` | Core | Five stages, the four-phase final table, six endings, the rooms' content |
| `MetaProgress`, `UnlockCatalog`, `RunSetup` | Core | Marks, unlocks, run randomisation |
| `SaveSystem`, `MiniJson` | Core | Run and meta saves as JSON (including RNG state), between rounds only |
| `RunSimulator` | Core | Bots play whole runs; invariant checks; reports |
| `GameSession` | Runtime | Title, new/continue, autosave, meta save, modes, ticks the run |
| `RouletteTableController` | Runtime | Bets with chip kinds, special bets, ability targeting, spin and respin, wheel animation, the Game Master's lines and the feed |
| `GameHud` (3 partial files) | Runtime | Every screen and panel (IMGUI) |
| `GameMasterView`, `EnvironmentDirector`, `AudioDirector` | Runtime | Figure animation and mood, lighting and wall names, synthesised sound and voice hook |
| `BalanceSimulatorMenu` | Editor | Simulate runs, write `Docs/BALANCE_REPORT.md`; delete save files |

Older rows above that mention `TableHud` and `PrototypeHud` are superseded by `GameHud`.
