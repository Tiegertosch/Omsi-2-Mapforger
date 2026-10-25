# OMSI MapWorks

**A Windows desktop editor for exploring, checking and editing OMSI 2 maps.**

OMSI MapWorks is developed under the working name Mapforge. It reads maps and game assets from your local OMSI 2 installation. Inspect a map from above or in 3D, build roads and junctions, arrange scenery, and export your work as a separate map for OMSI. The project is under active development.

Mapforge keeps edits in a project workspace and leaves the source map untouched. Its file model preserves map data it does not yet understand, so existing records can travel through an edit and export without being rewritten.

## What you can do

- **Explore maps:** View textured terrain, roads, tracks and scenery in a continuous 2D map or a navigable 3D view. Large maps that use OMSI world coordinates are supported.
- **Inspect and validate:** Review map contents, object and spline references, IDs, junction connections, timetable data, chrono variants and DLC usage in a report that links findings to their tiles.
- **See routes:** Display timetable lines, stops and the lanes their trips follow. Preview chrono variants for a selected date; these views are read-only.
- **Edit scenery:** Place, move, rotate and delete objects, including multi-selection, undo and redo. Hang objects such as signs and lamp heads on the attachment points of others. Browse the objects available in your installation.
- **Build roads:** Create OMSI spline roads with terrain-following or manually controlled heights. Generate junctions where roads meet and fit terrain to roads built in the editor. Lay rows of objects such as lamps or hedges along roads, then change or delete them.
- **Shape the ground:** Raise, lower, smooth, flatten or roughen the terrain with a brush, or lay an even ramp between two points, the way openMapper does; level the ground under houses; dig lakes and rivers and give tiles water; export the ground as a 16-bit height map and lay a height map back over the map. Roads fitted to the terrain stay fitted.
- **Work with map tiles:** Create a map from an installed map template, then add or remove tiles as the layout grows.
- **Export safely:** Save edits in a `.omproj` project and export them into a separate OMSI map folder. The original map remains unchanged.

## Current limitations

Mapforge is still in development. Timetable and chrono data can be inspected but not edited. Editing chrono variants is not yet available. Some advanced OMSI records and tile-bound references also limit which structural edits are possible. See the [roadmap](Documentation/Roadmap.md) for project details.

Maps are written to work in [openOMSI](https://github.com/openOMSI-org/openOMSI); where it and OMSI 2 differ, openOMSI decides. OMSI 2 and its content are not included. Mapforge reads the files from an installation you provide; the game and its assets must be obtained separately.

## Requirements

- Windows 64-bit
- Unity Hub and Unity Editor **6000.6.6f1**
- An OMSI 2 installation to open and render maps
- Git 2.x
- Visual Studio with the Unity workload or JetBrains Rider (recommended)

The project pins Universal Render Pipeline **17.6.0** in `Packages/manifest.json` and includes the resolved `Packages/packages-lock.json`.

## Open the project

1. Install Unity `6000.6.6f1` through Unity Hub.
2. In Unity Hub, select **Add > Add project from disk** and choose the repository root, where `Assets`, `Packages` and `ProjectSettings` are located.
3. Wait for Unity to resolve packages and finish importing the project.
4. Open `Assets/_Project/Scenes/EditorStartup.unity` and press **Play**.

To build a standalone Windows player, open **File > Build Profiles**, select Windows, make sure the startup scene is included, and build.

## Open and export a map

1. Start Mapforge and choose **Open OMSI map**. Select the map's `global.cfg` from your OMSI installation.
2. Choose **File > Save map project as...** to create an editable project. Mapforge stores changes in a workspace beside the `.omproj` file.
3. When you're ready, choose **File > Export to OMSI...** and select a destination for the separate exported map.

You can also create a new map from the welcome screen or the **File** menu. It uses an installed map as a template for the required supporting files and settings.

## Project layout

- `Assets/_Project/Scripts/` — C# application modules, separated by assembly definitions
- `Assets/_Project/UI/` — UI Toolkit interface, styles and fonts
- `Assets/_Project/Scenes/` — startup scene
- `Assets/_Project/Settings/` — project rendering and application settings
- `Assets/_Project/Tests/Editor/` — Unity EditMode tests
- `Packages/` — pinned Unity package dependencies
- `Documentation/` — setup, architecture, design decisions, roadmap and OMSI format research

## Development

- [Development setup](Documentation/DevelopmentSetup.md)
- [Architecture](Documentation/Architecture.md)
- [Roadmap](Documentation/Roadmap.md)
- [Design decisions](Documentation/Decisions.md)
- [Corporate design](Documentation/CorporateDesign.md)
- [OMSI format research](Documentation/GrundorfFormatInventory.md), including [tile records](Documentation/TileRecordInventory.md), [scenery objects](Documentation/SceneryObjectInventory.md), [timetables and chrono variants](Documentation/TimetableChronoInventory.md)

Run the EditMode suite from Unity's **Window > General > Test Runner**. Detailed validation notes and command-line workflows are in the development setup guide.

## License

OMSI MapWorks is licensed under the [GNU Affero General Public License v3.0](LICENSE). Third-party components and attributions are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). OMSI and its content are not part of this project.
