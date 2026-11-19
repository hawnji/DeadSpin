# Dead Spin

A 3D roulette survival game for PC, built in Unity 6 with C#, played from a top-down view of
the roulette table. You wake in an
underground casino owing $50,000 to a Game Master who can rewrite the wheel, the table and the
rules. Win enough to buy your freedom before the rounds run out.

All currency in the game is fictional. There is no real-money gambling of any kind.

**Status:** Phase 1 (Foundation). See [Docs/DEVELOPMENT_ROADMAP.md](Docs/DEVELOPMENT_ROADMAP.md).

## Requirements

- Unity **6000.6.9f1** (pinned in `ProjectSettings/ProjectVersion.txt`)
- Packages from the **Universal 3D** template: Universal RP, Input System, Test Framework
- Optional, for headless tests: .NET 8 SDK

## First-time setup

The repository holds the game's code and docs. Unity generates the rest (`Packages/`,
`ProjectSettings/`, URP settings) the first time, so set it up like this once:

1. In Unity Hub, create a **new project** with the **Universal 3D** template and editor
   **6000.6.9f1**. Name it `DeadSpin`. Close Unity once the project has opened.
2. In a terminal inside that project folder:
   ```bash
   git init
   git remote add origin https://github.com/hawnji/DeadSpin.git
   git fetch origin
   git checkout -f -B main origin/main
   ```
   This lays the repository's files over the fresh template without touching the template's
   packages or settings.
3. Open the project in Unity again. When it has finished importing, commit the generated
   `Packages/`, `ProjectSettings/`, `Assets/Settings/` and every `.meta` file so the project
   opens identically on any machine:
   ```bash
   git add -A && git commit -m "Add Unity project settings and meta files"
   ```
4. In Unity: **Dead Spin > Build Prototype Scene**. This generates
   `Assets/_Project/Scenes/Prototype.unity` and its materials, and adds it to the build list.
5. Press **Play**.

If Unity asks to enable the new input backends, choose **Yes**. The code also works with the
legacy Input Manager or with "Both".

## Controls

| Action | Input |
|---|---|
| Inspect / select | Left click |
| Pan across the table | WASD or arrow keys, or drag with right / middle mouse |
| Zoom | Scroll wheel |
| Switch view (table, betting layout, wheel, Game Master) | 1–4, Tab, or the tabs at the top |
| Pause | Esc |
| Developer panel | F1 (Editor and development builds) |

## Tests

- **In Unity:** Window > General > Test Runner > EditMode > Run All.
- **Headless:** `dotnet test tools/CoreTests` runs the same tests against the same source files.
  GitHub Actions runs this on every push and pull request.

## Documentation

| Document | Contents |
|---|---|
| [TECHNICAL_PLAN.md](Docs/TECHNICAL_PLAN.md) | Unity version, packages, folder structure, Phase 1 plan, technical risks |
| [PROJECT_OVERVIEW.md](Docs/PROJECT_OVERVIEW.md) | The game and what is implemented now |
| [SYSTEM_ARCHITECTURE.md](Docs/SYSTEM_ARCHITECTURE.md) | Systems, responsibilities, dependencies |
| [DEVELOPMENT_ROADMAP.md](Docs/DEVELOPMENT_ROADMAP.md) | The ten phases and their status |
| [TESTING_LOG.md](Docs/TESTING_LOG.md) | What was tested, how, and results |
| [KNOWN_ISSUES.md](Docs/KNOWN_ISSUES.md) | Limitations and open bugs |
| [FEATURE_BACKLOG.md](Docs/FEATURE_BACKLOG.md) | Planned features |
