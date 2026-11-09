# Changelog

## Unreleased

### Roads

- **Cant and skew of a spline.** The inspector of a selected spline shows the cant (banking across the road, in percent) and, in tiles of version 14 and later, the skew at its start and end, and changes them in one undo step. A cant given at a joined end goes to the spline joined there too, so the road stays level across the joint.

### Objects

- **Random heading.** Tick *Random heading* in the Objects tool and every object you put down, and every object of a pasted group, gets a heading of its own; with several objects selected, *Randomize headings* in the inspector turns each to a new random heading in one undo step. Trees and bushes look more natural that way.
- **Copy, paste and duplicate objects.** *Ctrl+C* copies the selected objects; *Ctrl+V* shows the copies as outlines at the pointer, *R* turns them together, a click puts them down (*Shift+click* keeps them in hand for more), *Esc* stops. *Ctrl+D* copies and puts the copies 5 m aside at once. The copies keep their labels (the text of a stop sign or the line plate), pitch and bank, rules and the objects hung on them, all with new IDs; they can land in another tile. A paste is one undo step and happens all or not at all. Junctions made by MapWorks cannot be copied (they each have their own object file), and in a chrono variant only the objects themselves are pasted, without attached objects and rules.

## 1.1.2

Terrain heights from the world no longer have spikes, and water is laid flat.

### Terrain heights

- **Spikes are removed.** The world's elevation data holds single pixels far off their surroundings (a pit of 200 m, say). They are replaced by the median of their neighbours; every other height stays exactly as it was.
- **Water is laid flat.** Lakes, rivers, docks and the sea, as OpenStreetMap has them, are levelled: the water surface comes from the banks, and the bed lies 1.5 m under it, so the blocks tens of metres too high that the data has over water are gone. Turn it off with *Level water* in the Terrain tab of *Satellite and terrain*. It needs a connection to OpenStreetMap; if there is none, the heights load as they are and the status line says so. The message names the surface, and the *Height* field of *Add water* in the inspector proposes it for each tile.
- The inspector shows the new range of heights of a tile after loading heights, painting terrain and undoing it.

### Good to know

- Projects and settings from 1.1.1 open as before. Heights you have already loaded stay as they are; load them again to get the levelled water.
- One body of water gets one surface, so a long river that really falls is too flat over some kilometres. Rivers that OpenStreetMap only has as a line are not levelled.

## 1.1.1

OMSI MapWorks speaks eight languages, and chrono variants can now be edited much further.

### Languages

- **Choose your language** under *Settings > General > Language*: English, German, Czech, French, Italian, Polish, Portuguese and Spanish. By default MapWorks starts in the language of Windows.
- English and German are written by hand. The other six are machine translations; if you find a clumsy or wrong text, please correct it (see `locales/README.md`) or open an issue.
- The whole interface changes at once, including menus, panels, tooltips, the status bar and the map check report. Error messages that come from reading or writing OMSI files stay English.
- A new language is only a folder of text files plus a few lines in `locales/languages.json`; no program change is needed.

### Chrono variants

- **Change the type and the labels** of objects and of objects attached to them (for example the line signs at a stop), in the base map and in a variant. Renaming a stop sign in a variant shows up in openOMSI.
- **Traffic rules and right of way** can be set in a variant. The variant's change list records them, and the map on a date shows them.
- **Change the type of a road or delete it** in a variant (*Change type...*, *Delete in variant*). The base map stays as it is.
- **Build new roads** in a variant, for example a diversion around a building site. They are stored as a separate chain of roads in the variant's change list.
- **Switch lines on and off** per variant. Stops, links and trips are not written by the editor.

### Good to know

- Projects, maps and settings from 1.1 open as before.
- Apart from the labels, the new chrono variant edits have so far been checked in MapWorks itself, not yet in openOMSI.

## 1.1

Place any map exactly on the Earth, a much calmer Satellite and terrain window, smooth zooming into aerial pictures, and faster, lighter loading of large maps.

### Satellite and terrain

- **Exact place for maps without world coordinates.** Pick a point of your map (a crossing, a corner of a building) as the *reference point*, paste where it lies on the Earth (`52.5200, 13.4050` as web maps copy it), set the map's scale, nudge the map by metres, or turn it. Choosing a reference point never moves the map.
- **Fit by two points.** Pick two points in the map, paste both positions, and MapWorks moves, turns and, if you like, scales the map to meet them.
- **Pictures follow along.** Aerial pictures you have already loaded move with every change of the place, so you see at once whether they fit; load them again when the place is right. They also follow the terrain when heights are loaded.
- **A calmer window.** *Satellite and terrain* now has four tabs: **Place**, **Pictures**, **Terrain** and **More**. On first opening you only see what you need to put the map on the Earth; the exact tools are folded away below it.
- **Smooth zooming into pictures.** Zooming in no longer stalls for half a second at every new tile. The full-size pictures are prepared while the pictures load, and tiles that just left the view keep theirs.
- **Zoom copies stay out of your project.** The prepared pictures take about 7 MB a tile, so they live in a cache folder on your computer (`%LOCALAPPDATA%\OMSI MapWorks\Cache\Sharp`), held below 2 GB. The *More* tab shows how much they take and can clear them; clearing is safe.
- Maps with world coordinates (such as Berlin-Spandau) still place themselves.

### Large maps

- **Maps open in the background.** The window keeps responding while a large map loads, and the scenery nearest the view appears first.
- **Faster and lighter.** Loading Berlin-Spandau for the first time takes about 20 seconds instead of 30, and needs less memory for models and textures.
- **Smoother editing.** Only the tiles in view are drawn, and an edit rebuilds only the tiles it touched: a frame from above costs a third of what it did, and the frame after an edit about a ninth.
- Cross-reference lookups across the whole map are cached, and a wrongly detected hole signature was fixed.

### Fixes

- The *New map* dialog said it would ask where to save the project; it makes the project for you, and now says so.
- The tick box *Fit the scale too* looks like the other tick boxes.

### Good to know

- Projects, maps and settings from 1.0 open as before. The place a map was given in 1.0 is read unchanged.
- The pictures are still kept in your project and are not exported.
