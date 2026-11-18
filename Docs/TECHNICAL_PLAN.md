# Technical Plan

Answers to the master prompt's first task (section 25). Updated as decisions change.

## 0. Starting point

`hawnji/DeadSpin` was an empty repository on 2026-11-18, so the project starts from scratch.
There was no existing Unity project, script or asset to reuse.

## 1. Unity version and packages

| Item | Choice | Why |
|---|---|---|
| Editor | **Unity 6000.6.9f1** | Requested by the owner. Pinned in `ProjectSettings/ProjectVersion.txt`. |
| Render pipeline | **URP** (Universal 3D template) | Good dark-interior lighting, volumetric-style fog and post-processing at 60 FPS on midrange PCs. HDRP is heavier than this game needs. |
| Input | **Input System** package | Rebinding and controller support later. All device reads go through `PlayerInputReader`, which also supports the legacy Input Manager. |
| Tests | **Test Framework** (NUnit) | EditMode tests for all gameplay logic. |
| Text | **TextMeshPro / uGUI** (built into Unity 6) | Final UI in Phase 8. Phase 1 uses dependency-free IMGUI. |
| Later, when needed | Cinemachine (Game Master camera moments), Addressables (content), Timeline (final encounter) | Not installed until a phase uses them, so nothing depends on missing packages. |

No third-party assets are required. Everything visual in Phase 1 is generated from primitives.

## 2. Folder structure

```
Assets/_Project/
  Scripts/
    Core/        DeadSpin.Core      Pure C#, no UnityEngine. All rules, money, odds, payouts.
      Common/      deterministic RNG, shared utilities
      Economy/     Ledger (single source of truth for money/debt), transactions
      GameState/   run, game modes, round phases, win/loss rules
      (Phase 2+)   Wheel/, Betting/, Probability/, Rules/, GameMaster/, Events/, Abilities/
    Runtime/     DeadSpin.Runtime   MonoBehaviours: presentation and input only.
      Bootstrap/   GameSession (owns the Run and the game mode)
      Config/      ScriptableObject configs
      Player/      input, movement, look, camera focus
      Interaction/ interactor, interactables, table seat
      Environment/ prototype room builder, materials, scene factory
      UI/          HUD
    Editor/      DeadSpin.Editor    Menu tools (scene builder, later wheel previewers)
  Tests/EditMode/ DeadSpin.Tests.EditMode
  Scenes/  Materials/  Prefabs/  Audio/  Art/   (created as content arrives)
Docs/            design and engineering documents
tools/CoreTests/ runs Core + EditMode tests with `dotnet test` (CI, cloud sessions)
```

## 3. Technical architecture (summary)

Full detail in [SYSTEM_ARCHITECTURE.md](SYSTEM_ARCHITECTURE.md).

- **Two layers.** `DeadSpin.Core` holds every rule that decides money or outcomes and has
  no engine reference, so it is unit-testable and cannot be affected by frame rate or physics.
  `DeadSpin.Runtime` renders and animates what Core decides.
- **One owner per piece of state.** `Ledger` owns money and debt, `Run` owns the round
  counter and outcome, `GameStateMachine` owns the game mode. Nothing else keeps a copy.
- **Result first, animation second.** The spin system will pick the winning pocket from the
  validated wheel configuration with the seeded RNG, then drive the ball animation to land
  there. Physics never decides a payout.
- **Data-driven content.** Rules, interventions, special pockets, events, contracts and
  abilities will be ScriptableObject definitions that map to Core data, so new content needs
  no code changes in the systems that run it.
- **Events over polling.** Core raises C# events (`Ledger.Changed`, `RoundFlow.PhaseChanged`,
  `Run.Ended`); Runtime listens. Update loops are limited to input, movement and camera.
- **Deterministic seeds.** Every random decision flows through `IRandomSource` (PCG32), so
  runs and simulation tests are reproducible.

## 4. Roadmap

The ten phases from the master prompt, with status, are in
[DEVELOPMENT_ROADMAP.md](DEVELOPMENT_ROADMAP.md). The first milestone is the playable
prototype (section 23), which is reached at the end of Phase 5 plus the minimum of Phase 6.

## 5. Phase 1 implementation plan

| Step | Files | Result |
|---|---|---|
| Project skeleton | asmdefs, `.gitignore`, `ProjectVersion.txt`, CI workflow | Compiles in Unity and headless |
| Run state | `RunConfig`, `Ledger`, `Run`, `RunOutcomeEvaluator` | $2,500 / $50,000 / 18 rounds, bankruptcy, freedom, deadline |
| Flow | `GameStateMachine`, `RoundFlow` | Explicit modes (explore, table, pause, ended) and timed round phases |
| Determinism | `SeededRandom` | Reproducible randomness for all later systems |
| Player | `PlayerInputReader`, `FirstPersonController`, `PlayerLook`, `CameraFocus` | Smooth first-person movement and camera |
| Interaction | `Interactor`, `Interactable`, `InspectableObject`, `RouletteTableStation` | Look at, highlight, inspect and sit at the table |
| World | `PrototypeCasinoBuilder`, `PrototypeMaterials`, `PrototypeSceneFactory`, `PrototypeSceneMenu` | Greybox casino generated and saved by a menu command |
| HUD | `PrototypeHud` | Money, debt, round, prompts, inspection, pause, end screen, dev panel |
| Tests | 41 EditMode tests | Ledger, outcome rules, state machine, round flow, RNG |

**Deliverable:** walk around a dim casino, approach the roulette table, sit down (the camera
glides to a table view), point at and inspect the wheel, layout, chip tray, doors and the Game
Master, stand up, pause, and see the run state on the HUD.

## 6. Critical technical risks

### Procedural wheel generation
- **Risk:** generating 12 to 100+ pocket meshes, with dividers and number labels, every time
  the Game Master changes the wheel could hitch the frame.
- **Mitigation:** build the wheel from one parametric pocket segment and reuse meshes per
  pocket count; generate geometry into reusable `Mesh` objects with `SetVertices` and
  pre-sized lists (no per-frame allocation); render numbers with one TextMeshPro atlas. Profile
  the 100-pocket case in Phase 3 before adding visual detail.
- **Risk:** labels become unreadable at 72 to 100 pockets.
- **Mitigation:** the wheel grows in diameter and adds rings rather than shrinking pockets;
  the betting UI uses a separate readable layout (section 18) rather than mirroring the wheel.

### Dynamic pocket counts
- **Risk:** a transformation leaves the logical wheel and the visible wheel out of sync, or
  happens while chips sit on a number that no longer exists.
- **Mitigation:** one `WheelConfiguration` object in Core is the only truth. The visual wheel
  is rebuilt from it, never edited directly. Transformations produce a new validated
  configuration plus a diff (added, removed, moved pockets) that drives the animation and
  tells the betting system which bets to refund or migrate. Validation rejects any
  configuration with zero active pockets, no winnable bet, or broken payouts.

### Physical chip interaction
- **Risk:** hundreds of rigidbody chips are expensive and unstable, and a chip sliding off a
  line in a shake could change a bet unfairly or ambiguously.
- **Mitigation:** chips are kinematic while resting, stacks are single objects with a count,
  and physics is enabled only during table shakes. A chip's bet is decided by which betting
  zone holds the stack's centre when betting closes, recorded in Core as a bet object; the
  physical chip is a view of that bet. A quick-bet UI covers large wagers.

### Probability calculations
- **Risk:** duplicates, weights, dead pockets, multiple balls and rings make "true odds" easy
  to get wrong, and the prompt forbids hidden manipulation.
- **Mitigation:** every outcome is drawn by weighted selection over the active pocket list,
  using exactly the weights the player can inspect. Probability per bet is computed from that
  same list. Large seeded simulation tests (e.g. 1,000,000 spins per configuration) check
  observed frequencies against computed probabilities with chi-squared tolerances. Payouts are
  calculated from the configuration, not looked up from a fixed table. Money is whole dollars
  (`long`) with the house rounding fractional payouts down, documented and tested.

### Working without the Unity Editor in cloud sessions
- **Risk:** the cloud development environment cannot launch Unity, so scenes and feel cannot
  be verified there.
- **Mitigation:** all logic lives in engine-free Core and runs under `dotnet test`. Unity-side
  scripts are type-checked against UnityEngine reference assemblies. Scenes are generated by
  code, not hand-written YAML. Each phase lists exact in-editor checks for the owner to run.
