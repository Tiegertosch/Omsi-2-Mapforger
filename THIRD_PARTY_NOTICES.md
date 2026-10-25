# Third-party notices

## openOMSI

The vertex unscrambling of OMSI `.o3d` meshes in `Assets/_Project/Scripts/OMSI/OmsiO3dMesh.cs` is ported from openOMSI:

- Source: <https://github.com/openOMSI-org/openOMSI>, file `crates/omsi-o3d/src/lib.rs`, commit `23dd0a9e909c57edc22f6c4294c690e739667c9b`
- Copyright: the openOMSI contributors
- License: GNU Affero General Public License v3.0 (see [LICENSE](LICENSE))

Format facts in the documentation also draw on openOMSI's `docs/FORMATS.md`; each document links to it where it does.

## openMapper

The terrain tool in `Assets/_Project/Scripts/OMSI/OmsiTerrainBrush.cs` follows openMapper's Terrain tool: the brush modes with their rates and soft rim, the rolls of the Roughen brush, the ramp, the map-wide sample grid whose tile edges are set in both tiles, and the rule that the ground stays 12 cm below a road beside it.

- Source: <https://github.com/openOMSI-org/openMapper>, file `src/editor.rs` (`brush_terrain`, `ramp`, `rolls`, `world_sample`, `set_world_sample`, `ground_cap`), commit `f9e2aa4ab410196f639ec30912833cb43cb20c68`

The aerial imagery and terrain heights in `Assets/_Project/Scripts/OMSI/OmsiGeo.cs` and `OmsiAerial.cs`, and the "Satellite and terrain" window in `Assets/_Project/Scripts/UI/SatelliteWindow.cs`, `WorldMapElement.cs` and `EditorShellAerial.cs`, follow openMapper's geo module: the placement of a map on WGS 84, the list of sources that need no key with their bounds and resolutions, the choice of the sharpest source, the tile cache and its request limits, the mosaic that enlarges coarser tiles where finer ones are missing, the per-pixel sampling of tile pictures and terrain points, the user's own pictures stretched over tiles and lined up by eye (`OmsiOwnPictures.cs`, `EditorShellPictures.cs`), the map drawn from OpenStreetMap's data with its colours, order and labels (`OmsiOsmMap.cs`, `OmsiRaster.cs`, `OmsiTrueType.cs`), and the window's layout.

- Source: <https://github.com/openOMSI-org/openMapper>, files `src/osm_map.rs`, `src/geo/mod.rs`, `src/geo/own.rs`, `src/geo/sources.rs`, `src/geo/fetch.rs`, `src/geo/imagery.rs`, `src/geo/elevation.rs`, `src/geo/job.rs` and `src/ui_geo.rs`, commit `f9e2aa4ab410196f639ec30912833cb43cb20c68`

The pictures, heights and OpenStreetMap data themselves are not part of OMSI MapWorks; OpenStreetMap's data is © OpenStreetMap contributors under the ODbL. They are fetched at run time from the services named in the window, under each service's own terms, and stay in the user's project.
- Copyright: the openMapper contributors
- License: GNU Affero General Public License v3.0 (see [LICENSE](LICENSE))

## Lucide

The icon path data in `Assets/_Project/Scripts/UI/LucideIcons.cs` comes from Lucide:

- Source: <https://lucide.dev>, package `lucide-static` 0.454.0
- License: ISC License

```text
ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## Inter

The user interface font in `Assets/_Project/UI/Resources/Fonts/` is Inter 4.1 (Regular and SemiBold). `Inter-Regular-Outlines.bytes` there is an unchanged copy of `Inter-Regular.ttf`, kept as raw data so the names on the drawn OpenStreetMap map can be drawn from its outlines:

- Source: <https://github.com/rsms/inter>, release v4.1
- Copyright (c) 2016 The Inter Project Authors
- License: SIL Open Font License 1.1; the full text ships next to the fonts as `Inter-LICENSE.txt`

## OMSI

OMSI and its content are not part of this project. Mapforge reads OMSI files from the user's own installation and does not ship them.
