# CLAUDE.md – OMSI Mapforge

Schnelleinstieg für Claude. Ausführliche Details stehen in `Documentation/`; diese Datei fasst das Wichtigste zusammen und sagt, wo man nachliest.

## Projekt in einem Absatz

OMSI Mapforge (Produktname im Player: „OMSI Map Editor“) ist ein Windows-Desktop-Editor für Karten des Bussimulators OMSI 2, gebaut mit **Unity 6.6 (`6000.6.6f1`)**, **URP `17.6.0`** und **UI Toolkit** (UXML/USS). Er liest Karten direkt aus der externen OMSI-Installation, zeigt sie als texturierte Draufsicht und in 3D, und erlaubt Objekte zu verschieben, drehen, platzieren und löschen. Änderungen landen in einer Projekt-Arbeitskopie; die Originalkarte wird nie verändert, nur ein ausdrücklicher Export schreibt eine eigene Kartenkopie. Lizenz: AGPL-3.0 (enthält aus openOMSI portierte `.o3d`-Entwürfelung, siehe `THIRD_PARTY_NOTICES.md`). Der Projektinhaber hat die Weiterentwicklung an Claude übergeben.

## Sprache und Konventionen

- **Mit dem Nutzer Deutsch sprechen.** `Documentation/*.md` und diese Datei: Deutsch. `README.md`, Code, Kommentare, Bezeichner und UI-Texte: **Englisch**.
- C#: 4 Leerzeichen, LF, Klammern auf neuer Zeile (Allman), block-scoped Namespaces, `PascalCase` für Typen/Member, `camelCase` für Locals/Parameter, Namen ausschreiben. XML-Kommentare nur, wo der Vertrag nicht selbsterklärend ist. Fehler früh und präzise melden, nichts still verschlucken. Siehe `.editorconfig`.
- Namespaces: `OmsiMapEditor.Core`, `.OMSI`, `.OMSI.Editor`, `.UI`, `.World`, `.Editor`.
- Keine neuen Abhängigkeiten ohne klaren Bedarf; keine absoluten Benutzerpfade im Code.
- Unity-Assets immer mit ihren `.meta`-Dateien committen.

## Git

- Identität ist repo-lokal gesetzt: `tiege <6438619+Tiegertosch@users.noreply.github.com>`. **Nie mit der privaten Gmail-Adresse committen** (GitHub lehnt das ab, GH007).
- Remote: `origin` = `https://github.com/Tiegertosch/Omsi-2-Mapforger-.git`, `main` folgt `origin/main`.
- Arbeit in kurzen Zweigen (`feature/...`, `docs/...`), `main` bleibt stabil.
- Commit-Betreff kurz, imperativ, englisch, beschreibt das Ergebnis für den Nutzer (z. B. `Add terrain and a 3D view`, `Move models while dragging, draw tree crowns and textured .x models`).
- Nur committen/pushen, wenn der Nutzer es verlangt.
- Mit jedem Feature werden `README.md` (Abschnitt „Current status“), `Documentation/Architecture.md`, `Documentation/Roadmap.md` (Häkchen) und bei Prüfungen `Documentation/DevelopmentSetup.md` („Validierungsstand“) mitgepflegt; Grundsatzentscheidungen bekommen ein ADR in `Documentation/Decisions.md`.

## Verzeichnisse

| Pfad | Inhalt |
|---|---|
| `Assets/_Project/Scripts/<Modul>/` | Gesamter First-party-Code, je Modul eine Assembly Definition |
| `Assets/_Project/Tests/Editor/` | EditMode-Tests (`Editor.Tests`), erzeugen ihre OMSI-Beispieldateien selbst, brauchen keine Installation |
| `Assets/_Project/UI/Resources/` | `EditorShell.uxml`, `EditorShell.uss`, Theme |
| `Assets/_Project/Shaders/Resources/` | `MapGroundLayer`, `MapSplineSurface`, `MapObjectSurface` (+ Instancing-Material) |
| `Assets/_Project/Settings/Resources/editor-settings.json` | App-Startwerte (Fenster, UI-Skalierung), Vertrag `MapforgeEditorSettings` |
| `Assets/_Project/Scenes/EditorStartup.unity` | Einzige Szene |
| `Documentation/` | Architektur, ADRs, Setup/Validierung, Roadmap, Formatinventare |
| `Builds/Windows/OMSIMapEditor.exe` | Gebauter Player (ignoriert) |

## Module (Assemblies)

| Assembly | Zuständigkeit |
|---|---|
| `Editor.Core` | Projektdatei `.omproj` (`MapforgeProject`), Einstellungen. Keine Abhängigkeit zu UI/OMSI/Unity-Editor |
| `Editor.OMSI` | Alle OMSI-Formate, Lesen/Schreiben, Bearbeitungslogik – **ohne Unity-Abhängigkeit**, daher auch außerhalb Unitys testbar |
| `Editor.World` | Unity-Rendering: `MapGroundRenderer` (Boden + Splines), `MapObjectRenderer` (Objekte, GPU-Instancing) |
| `Editor.UI` | `EditorShellBootstrap` (App-Shell, Menüs, Undo, Kommandozeilenargumente), `MapCanvasElement` (Vektorebene, Auswahl, Ziehen, 3D-Eingabe), `WindowsFileDialog` |
| `Editor.OMSI.Editor` | Editor-only: Übersichtsfenster, `OmsiMapCli` (Batch-Einstiege), `OmsiContentBoundary` (Build-Prüfung) |
| `Editor.UI.Editor` | Editor-only: `UrpProjectSetup`, `WindowsPlayerBuild` |
| `Editor.Geometry`, `Editor.Objects`, `Editor.Validation` | Noch leer/vorbereitet |

Abhängigkeitsrichtung: UI → OMSI → Core; OMSI kennt UI nicht. Größte Dateien: `EditorShellBootstrap.cs` (~2.000 Zeilen), `MapCanvasElement.cs` (~1.300), `MapObjectRenderer.cs` (~900).

## Wichtige OMSI-Klassen (alle in `Scripts/OMSI/`)

- **Dateimodell:** `OmsiTextFile` (Kodierung: BOM → striktes UTF-8 → Windows-1252), `OmsiRecordFile` (verlustfrei, `ToBytes()` byte-identisch), `OmsiTileRecordLayouts` / `OmsiGlobalConfigLayouts` (Feldanzahlen), `OmsiTileRecordViews` (typisierte Views: `OmsiObjectView`, `OmsiSplineView`, …), `OmsiNumberFormat` (Zahlen wie der OMSI-Editor).
- **Lesen:** `OmsiMapReader` → `OmsiMapSummary` (u. a. `TileSizeMeters`), `OmsiTerrain` (61 × 61 Höhen), `OmsiMapGeometry`, `OmsiSplineTileIndex`, `OmsiSplineContinuity`.
- **Bearbeiten:** `IOmsiFileSource` / `OmsiFolderSource` / `OmsiMapWorkspace` (Arbeitskopie über Original), `OmsiMapSession` (`PlaceObject`, `DeleteObject`, `TransferObject`, `OmsiObjectChange` für Undo), `OmsiTileEditing` (strukturelle Änderungen), `OmsiReferenceIndex` (kartenweite ID-Verweise), `OmsiIdAllocator` (`[NextIDCode]`), `OmsiMapExporter`.
- **Modelle/Texturen:** `OmsiSceneryObject` (`.sco`), `OmsiO3dMesh` (`.o3d`, mit Entwürfelung), `OmsiXMesh` (Text-`.x`), `OmsiObjectModel` (`OmsiObjectModelCatalog`), `OmsiFootprintTracer` (Umrisse), `OmsiImage` (BMP/TGA/DDS DXT1/3/5), `OmsiGroundTextures`, `OmsiSplineProfile` (`.sli`), `OmsiSplineAttachmentLayout` (neu, Spline-Anhängungen).

## Harte Regeln (nicht verletzen)

1. **Originalkarte nie verändern.** Gespeichert wird nur in `Name.workspace` neben `Name.omproj` (atomar über Temp-Datei). Export nur in einen eigenen Ordner `Kartenname (Mapforge)` mit Markierungsdatei `mapforge-export.txt` (ADR 009).
2. **Verlustfrei:** Was der Editor nicht versteht, bleibt als Freitext unverändert erhalten. Kodierung, BOM, Zeilenenden pro Zeile bleiben erhalten (ADR 008).
3. **Keine OMSI-Spielinhalte im Player.** Der Build vergleicht alle Player-Abhängigkeiten bytegenau mit der Installation; OMSI-Daten werden zur Laufzeit nur extern gelesen (ADR 005).
4. Neue Objekte am **Kachelende** anfügen, damit sich keine Indizes verschieben. Löschen nimmt folgende `[rule]`/`[varparent]`/Terrain-Datensätze mit und verweigert bei bestehenden Verweisen (ADR 011).
5. Kachelwechsel nur ohne kachelgebundene Verweise (Einstiegspunkt, Chrono, angehängtes Objekt, `[varparent]`) – sonst bleibt das Objekt mit Koordinaten außerhalb in seiner Kachel (ADR 012).
6. Undo für Platzieren/Löschen über Dateistände vorher/nachher; Verschieben/Drehen wertbasiert.

## Technische Fallstricke

- Kachelkante: 300 m klassisch, **371,9 m** bei `[worldcoordinates]`-Karten wie Berlin-Spandau (ADR 007).
- Gezeichnet wird **relativ zur untersten Kachelecke**, weil Weltkoordinaten für `float` zu groß sind.
- Texturen zeichnet eine orthografische Unity-Kamera **unter** der UI-Toolkit-Vektorebene; Kartenansicht und Eltern sind durchsichtig. Bearbeitet wird nur in der Draufsicht; 3D nutzt dieselbe Kamera perspektivisch (ADR 013).
- Überblendete Materialien (`[matl_alpha]` 2, `[matl_transmap]`) werden in zwei Durchgängen gezeichnet: deckende Texel mit Tiefe, Rest überblendet. Ganz ohne Tiefe verdecken sich die Teile eines Objekts falsch (Fehler vom 22. Oktober am Wartehäuschen).
- `[useTextTexture]`/`[useScriptTexture]`: Die Datei ist nur ein Platzhalter (z. B. Zeichensatz) – nicht zeichnen.
- Unity streicht im Build ungenutzte Shader-Varianten → Instancing-Material liegt in `Resources` und wird kopiert. Neue Shader-Keywords/-Varianten im Build prüfen!
- Texturen max. 1.024 px; DXT1/DXT5 unverändert, BMP/TGA komprimiert hochladen. JPEG/PNG über `com.unity.modules.imageconversion`.
- Vektorebene meldet Neuzeichnungen > 50 ms im Player-Log; Berlin-Spandau liegt knapp an dieser Grenze.

## Bauen und Testen

Unity: `D:\Unity Hub\Unity 6.6\6000.6.6f1\Editor\Unity.exe`. Befehle siehe `Documentation/DevelopmentSetup.md` → „Kommandozeile“.

**Der Nutzer hat den Unity-Editor oft offen → Projekt ist für Batch-Mode gesperrt.** Dann:

- **Schnelle Logiktests ohne Unity:** `Editor.Core`, `Editor.OMSI` und die Tests mit Roslyn csc kompilieren (`C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe`), NUnit aus `Library/PackageCache/com.unity.ext.nunit@*/net472/unity-custom/`, mit kleinem Reflection-Runner im Scratchpad ausführen (Hilfsskripte sind nicht im Repo).
- **Unity Test Runner / Player-Build:** `Assets`, `Packages`, `ProjectSettings`, `Library` (~1,1 GB) ins Scratchpad kopieren und Batch-Mode auf die Kopie (`-runTests -testPlatform EditMode` bzw. `-executeMethod OmsiMapEditor.Editor.WindowsPlayerBuild.Build`).
- Build dauert ~3 Min: **immer im Hintergrund starten und das vorher ansagen** (ein stiller Vordergrund-Build wirkte wie ein Hänger).
- Log des offenen Editors: `Logs/Editor.log` im Projekt. Player-Log: `%USERPROFILE%\AppData\LocalLow\OMSI Map Editor\OMSI Map Editor\Player.log`.
- Der Nutzer bietet an, den Editor zu schließen – vorher um Speichern bitten.

**Visuelle Prüfung per Screenshot** (vom Nutzer freigegeben; bei Render-/UI-Änderungen selbst prüfen, bevor übergeben wird):

```bash
./Builds/Windows/OMSIMapEditor.exe -screen-width 1600 -screen-height 900 -screen-fullscreen 0 \
  -openMap "D:/SteamLibrary/steamapps/common/OMSI 2/maps/Grundorf/global.cfg" -view 0 -1 150 150 0.25 \
  -screenshot "<scratchpad>/grundorf.png" -quit -logFile "<scratchpad>/player.log"
```

Weitere Argumente: `-openProject <.omproj>`, `-orbit Richtung Neigung Abstand` (3D). In Pfaden Schrägstriche verwenden. Bild dann mit Read ansehen. Gute Ausschnitte: Grundorf `0 -1 150 150 0.25`, Berlin-Spandau `2402 11279 200 200 0.4`.

## Testkarten

OMSI-2-Installation: `D:\SteamLibrary\steamapps\common\OMSI 2\maps` – **Grundorf** (klein, 19 Kacheln, Standardtest), **Stadtückheim**, **Berlin-Spandau** (groß, 766 Kacheln, `[worldcoordinates]`, Performance-Test). Beide Hauptkarten bei Änderungen prüfen.

## Weiterführende Dokumente

- `Documentation/Architecture.md` – Module und Datenfluss im Detail
- `Documentation/Decisions.md` – ADR 001–013
- `Documentation/DevelopmentSetup.md` – Werkzeuge, Kommandozeile, Validierungsstand
- `Documentation/Roadmap.md` – erledigt `[x]` und offen
- `Documentation/TileRecordInventory.md`, `GrundorfFormatInventory.md`, `SceneryObjectInventory.md`, `TimetableChronoInventory.md` – OMSI-Formatbefunde

## Aktueller Stand (22. Oktober 2026)

- Zweig `feature/drag-preview-trees` (noch nicht nach `main` gemergt, nicht gepusht).
- Zuletzt fertig und geprüft (22. Oktober): angehängte Objekte und Spline-Anhängungen, Rückseiten-Culling, Glas/`[matl_transmap]`, Bäume in 3D als gekreuzte Flächen. 116 Tests grün, Player-Screenshots geprüft. Prüfstelle für Transparenz: 70er-Wartehäuschen in Grundorf, Kachel `0 -1` bei x 264 / y 31 (`-view 0 -1 264 31 0.02 -orbit 110 12 7`).
- Danach korrigiert (`9c98fa3`): Verdeckung überblendeter Materialien in zwei Durchgängen, Skripttexturen wie Anzeigetafeln werden nicht mehr gezeichnet. 117 Tests grün, Screenshots geprüft.
- Noch nicht im Player mit der Maus geprüft: Mitbewegen der Modelle beim Ziehen, Bedienung der 3D-Ansicht.
- Hinweis: Kachel-`.map`-Dateien sind teils UTF-16 – `grep` findet darin nichts, mit Python dekodieren.
- Naheliegende nächste Schritte laut Roadmap: Fahrplandaten (TTData) lesend importieren, Haltestellen/Linien anzeigen, Chrono-Varianten als Ebenen; Objekte in 3D wählen; Gelände unter Straßen ausschneiden; Splines anlegen/löschen.

Diesen Abschnitt bei größeren Fortschritten aktualisieren.
