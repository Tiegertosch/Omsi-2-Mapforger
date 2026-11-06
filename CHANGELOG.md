# Changelog

## 1.1 (unreleased)

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
