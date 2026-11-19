# Feature Backlog

The master prompt is the source of truth for scope. Phases 1 to 10 are implemented (see the
roadmap); this list holds what is left to polish, and ideas found along the way.

## After the first Editor run
- Fix anything the full-game checklist in TESTING_LOG.md turns up.
- Balance pass from real play; compare with **Dead Spin > Simulate Runs**.
- Profile with the 100-pocket wheel and two balls; make a Windows build.

## Presentation
- Replace IMGUI with an animated UI (uGUI or UI Toolkit + TextMeshPro); keep the UI scale option.
- Recorded Game Master voice lines (one clip per dialogue line id, `gm_<id>`), real audio for each cue.
- A modelled and rigged Game Master; the procedural `GameMasterView` already exposes mood, nods and lean.
- Short visual vignettes for the rooms instead of plain panels.
- Outline highlight effect; TextMeshPro labels.

## Gameplay
- Re-bet last round's chips with one key.
- Hotkeys for abilities (data already has them) once input is rebindable.
- More rules, events, challenges and contracts: all are data in `RuleCatalog`, `EventCatalog`, `ChallengeCatalog` and `ContractCatalog`.
- Daily seeded run and a seed entry box on the title screen.

## Later / nice to have
- Controller support (input already isolated in `PlayerInputReader`).
- Key rebinding and camera pan/zoom speed in an options menu.
- Steam achievements tied to the endings and unlocks.
