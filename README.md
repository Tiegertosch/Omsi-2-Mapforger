# OMSI Map Editor

A Windows desktop editor foundation for creating and maintaining OMSI 2 maps. It currently contains the Unity project scaffold, a UI Toolkit shell, and a read-only Grundorf importer for map overview data. Map editing and full OMSI format support are not implemented yet.

## Requirements

- Unity 6.6, editor version `6000.6.6f1` (Windows 64-bit)
- Unity Hub to install/manage the editor
- Unity Universal Render Pipeline package `17.6.0` (pinned in `Packages/manifest.json`)
- Git 2.x
- Visual Studio with Unity workload or JetBrains Rider is recommended. Visual Studio Build Tools alone provide compilers but no IDE.

## Open the project

1. Open the installed Unity `6000.6.6f1` through Unity Hub.
2. In Unity Hub, choose **Add > Add project from disk** and select this repository root, the folder containing `Assets`, `Packages`, and `ProjectSettings`.
3. Let Unity resolve packages and finish the first import. The editor setup hook creates `Assets/_Project/Settings/EditorPipeline.asset` and assigns URP to graphics and quality settings.
4. Open `Assets/_Project/Scenes/EditorStartup.unity` and press Play to view the initial shell.
5. For a standalone player, use **File > Build Profiles**, select Windows, confirm the startup scene is included, and build. A successful Windows player build was generated in `Builds/Windows/`.

To inspect an existing OMSI map, use **File > Open OMSI map...** or the central **Open OMSI map...** button in the Windows app and select the installation's `global.cfg`. The app reads source files directly from the external OMSI installation and displays tile and record summaries. The Unity Editor also provides **OMSI Map Editor > Import > Read OMSI Map...**. OMSI game content itself must not be bundled with the player. The build helper collects the actual player dependencies and rejects any that are byte-identical to a file in the remembered installation. The earlier reviewed-asset allowlist has been removed for now. See [GrundorfFormatInventory.md](Documentation/GrundorfFormatInventory.md).

Unity writes generated project settings and `Packages/packages-lock.json` during import. The resolved package lock is checked into this repository so collaborators use the same transitive package versions.

## Project structure

- `Assets/_Project/Scripts/` holds first-party C# modules. Each module has an assembly definition.
- `Assets/_Project/UI/` contains the UI Toolkit shell and its stylesheet.
- `Assets/_Project/Scenes/` contains the startup scene.
- `Assets/_Project/Settings/` stores project-owned rendering settings; `Settings/Resources/editor-settings.json` holds application defaults read by the editor setup and the player.
- `Assets/_Project/Tests/Editor/` contains EditMode tests for the OMSI reader, geometry, and settings.
- `Packages/manifest.json` pins direct Unity packages.
- `Documentation/` records architecture, setup, decisions, and roadmap.

## Development

See [DevelopmentSetup.md](Documentation/DevelopmentSetup.md), [Architecture.md](Documentation/Architecture.md), and [Decisions.md](Documentation/Decisions.md). Run the tests in Unity through **Window > General > Test Runner > EditMode**. Keep map formats and editor state in separate modules; avoid absolute machine paths and new dependencies without a clear need.

## Current status

Unity `6000.6.6f1` imported the project, resolved URP `17.6.0`, compiled the project assemblies, and produced a Windows player. The Grundorf importer reads 19 tiles and plots object and spline placements in the Unity Editor; the standalone app can save and reopen a versioned `.omproj` file that points to an external `global.cfg`, select tiles in the map view or project browser, inspect their object, spline, and terrain summaries, fit the map to the viewport, and zoom. **Tools** checks the loaded map: it lists import warnings and verifies that connected splines meet. Maps with `[worldcoordinates]`, such as Berlin-Spandau, use their wider 371.9 m tile grid. Full model rendering and map editing remain early scaffolding. See the setup guide and [Grundorf format inventory](Documentation/GrundorfFormatInventory.md) for validation details and the player content boundary.
