# CLAUDE.md – OMSI Mapforge

Schnelleinstieg für Claude. Ausführliche Details stehen in `Documentation/`; diese Datei fasst das Wichtigste zusammen und sagt, wo man nachliest.

## Projekt in einem Absatz

OMSI MapWorks (Entwicklungsname Mapforge, bis 25. Oktober 2026 im Player „OMSI Map Editor“, ADR 034) ist ein Windows-Desktop-Editor für Karten des Bussimulators OMSI 2, gebaut mit **Unity 6.6 (`6000.6.6f1`)**, **URP `17.6.0`** und **UI Toolkit** (UXML/USS). Er liest Karten direkt aus der externen OMSI-Installation, zeigt sie als texturierte Draufsicht und in 3D, und erlaubt Objekte zu verschieben, drehen, platzieren und löschen. Änderungen landen in einer Projekt-Arbeitskopie; die Originalkarte wird nie verändert, nur ein ausdrücklicher Export schreibt eine eigene Kartenkopie. Lizenz: AGPL-3.0 (enthält aus openOMSI portierte `.o3d`-Entwürfelung, siehe `THIRD_PARTY_NOTICES.md`). Der Projektinhaber hat die Weiterentwicklung an Claude übergeben.

## Sprache und Konventionen

- **Mit dem Nutzer Deutsch sprechen.** `Documentation/*.md` und diese Datei: Deutsch. `README.md`, Code, Kommentare, Bezeichner und UI-Texte: **Englisch**.
- C#: 4 Leerzeichen, LF, Klammern auf neuer Zeile (Allman), block-scoped Namespaces, `PascalCase` für Typen/Member, `camelCase` für Locals/Parameter, Namen ausschreiben. XML-Kommentare nur, wo der Vertrag nicht selbsterklärend ist. Fehler früh und präzise melden, nichts still verschlucken. Siehe `.editorconfig`.
- Namespaces: `OmsiMapEditor.Core`, `.OMSI`, `.OMSI.Editor`, `.UI`, `.World`, `.Editor`.
- Keine neuen Abhängigkeiten ohne klaren Bedarf; keine absoluten Benutzerpfade im Code.
- Unity-Assets immer mit ihren `.meta`-Dateien committen.

## Git

- Identität ist repo-lokal gesetzt: `tiege <6438619+Tiegertosch@users.noreply.github.com>`. **Nie mit der privaten Gmail-Adresse committen** (GitHub lehnt das ab, GH007).
- Remote: `origin` = `https://github.com/Tiegertosch/Omsi-2-Mapforger.git` (seit 24. Oktober 2026, vorher mit Bindestrich am Ende), `main` folgt `origin/main`.
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
| `Builds/Windows/OMSIMapWorks.exe` | Gebauter Player (ignoriert) |

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

Abhängigkeitsrichtung: UI → OMSI → Core; OMSI kennt UI nicht. Größte Dateien: `EditorShellBootstrap.cs` (~6.900 Zeilen, `partial`; Luftbilder in `EditorShellAerial.cs`, eigene Bilder in `EditorShellPictures.cs`), `MapCanvasElement.cs` (~3.600), `MapObjectRenderer.cs` (~1.400). Neue Shell-Teile lieber als eigene `partial`-Datei.

## Wichtige OMSI-Klassen (alle in `Scripts/OMSI/`)

- **Dateimodell:** `OmsiTextFile` (Kodierung: BOM → striktes UTF-8 → Windows-1252), `OmsiRecordFile` (verlustfrei, `ToBytes()` byte-identisch), `OmsiTileRecordLayouts` / `OmsiGlobalConfigLayouts` (Feldanzahlen), `OmsiTileRecordViews` (typisierte Views: `OmsiObjectView`, `OmsiSplineView`, …), `OmsiNumberFormat` (Zahlen wie der OMSI-Editor).
- **Lesen:** `OmsiMapReader` → `OmsiMapSummary` (u. a. `TileSizeMeters`), `OmsiTerrain` (61 × 61 Höhen), `OmsiMapGeometry`, `OmsiSplineTileIndex`, `OmsiSplineContinuity`.
- **Bearbeiten:** `IOmsiFileSource` / `OmsiFolderSource` / `OmsiMapWorkspace` (Arbeitskopie über Original), `OmsiMapSession` (`PlaceObject`, `DeleteObject`, `TransferObject`, `OmsiObjectChange` für Undo; Straßen in `OmsiMapSessionSplines.cs`: `CreateRoad`, `DeleteSpline`, `OmsiSplineChange`, `OmsiSplineEnd`), `OmsiRoadPlanner` (Bogen, Stücke, Höhenmodi, Befunde), `OmsiTerrainSampler` (Gelände über Kachelgrenzen, außerhalb = ungültig), `OmsiTileEditing` (strukturelle Änderungen), `OmsiReferenceIndex` (kartenweite ID-Verweise), `OmsiIdAllocator` (`[NextIDCode]`), `OmsiMapExporter`.
- **Modelle/Texturen:** `OmsiSceneryObject` (`.sco`), `OmsiO3dMesh` (`.o3d`, mit Entwürfelung), `OmsiXMesh` (Text-`.x`), `OmsiObjectModel` (`OmsiObjectModelCatalog`), `OmsiFootprintTracer` (Umrisse), `OmsiImage` (BMP/TGA/DDS DXT1/3/5), `OmsiGroundTextures`, `OmsiSplineProfile` (`.sli`), `OmsiSplineAttachmentLayout` (Spline-Anhängungen, Reihen).
- **Gelände und Luftbilder:** `OmsiTerrainBrush`/`OmsiTerrainGrid` (Pinsel, kartenweites Raster), `OmsiHeightMap`, `OmsiPng`, `OmsiTiff`, `OmsiGeo` (Verortung, Quellen, `OmsiGeoSettings` in `Mapforge\geo.txt`, `OmsiImageryPreferences` mit Schlüsseln nur auf dem Rechner), `OmsiAerial` (Kachelspeicher, Mosaik, `OmsiAerialJob`), `OmsiOsmMap` mit `OmsiCanvas` (Vektorgrafik) und `OmsiTrueTypeFont`, `OmsiOwnPictures`, `OmsiJson`.

## Harte Regeln (nicht verletzen)

1. **Originalkarte nie verändern.** Gespeichert wird nur in `Name.workspace` neben `Name.omproj` (atomar über Temp-Datei). Export nur in einen eigenen Ordner `Kartenname (Mapforge)` mit Markierungsdatei `mapforge-export.txt` (ADR 009).
2. **Verlustfrei:** Was der Editor nicht versteht, bleibt als Freitext unverändert erhalten. Kodierung, BOM, Zeilenenden pro Zeile bleiben erhalten (ADR 008).
3. **Keine OMSI-Spielinhalte im Player.** Der Build vergleicht alle Player-Abhängigkeiten bytegenau mit der Installation und mit der Freigabeliste `Assets/_Project/Settings/PlayerAssetReview.txt`; neue oder geänderte Player-Dateien warnen im normalen Build und stoppen `BuildRelease`. Nach Prüfung mit „Record Reviewed Player Assets“ neu schreiben. OMSI-Daten werden zur Laufzeit nur extern gelesen (ADR 005).
4. Neue Objekte am **Kachelende** anfügen, damit sich keine Indizes verschieben. Löschen nimmt folgende `[rule]`/`[varparent]`/Terrain-Datensätze mit und verweigert bei bestehenden Verweisen (ADR 011).
5. Kachelwechsel nimmt kachelgebundene Verweise mit (ADR 012, Ergänzung): angehängte Objekte, Einstiegspunkte, Chrono-Auswahlen; `[varparent]` nur bis eine Kachel weit. Was nicht mitkann, lässt das Objekt mit Koordinaten außerhalb in seiner Kachel.
6. Undo für Platzieren/Löschen über Dateistände vorher/nachher; Verschieben/Drehen wertbasiert.

## Technische Fallstricke

- **Ziel ist openOMSI** (Omsi-Hub, beim Nutzer unter `D:\openOMSI` mit eigenem `maps`-Ordner; ADR 038). Offene Formatfragen klärt openOMSIs Quelltext (github.com/openOMSI-org/openOMSI, z. B. `crates/omsi-world/src/map_index.rs`). Der Nutzer testet nur in openOMSI; „im Spiel“ heißt immer openOMSI, auch bei früheren Bestätigungen.
- Letzte Zahl eines `[spline]` (Feld 17) = Kettenabstand (Länge der Kette davor, auch Texturversatz). Reihen zählen ab diesem gespeicherten Wert und nur in der eigenen Kachel; nach Spline-Änderungen pflegt `KeepChains` ihn.

- Kachelkante: 300 m klassisch, **371,9 m** bei `[worldcoordinates]`-Karten wie Berlin-Spandau (ADR 007).
- Objektdrehung: OMSI = Bank, dann Pitch, dann Heading (D3D Yaw-Pitch-Roll) = Unity `Quaternion.Euler(pitch, heading, bank)` (`MapObjectRenderer.Orientation`). Positiver Pitch senkt die Vorderseite (an 16 Objekten in Spandau belegt).
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
- Log des offenen Editors: `Logs/Editor.log` im Projekt. Player-Log: `%USERPROFILE%\AppData\LocalLow\OMSI MapWorks\OMSI MapWorks\Player.log`.
- Der Nutzer bietet an, den Editor zu schließen – vorher um Speichern bitten.

**Visuelle Prüfung per Screenshot** (vom Nutzer freigegeben; bei Render-/UI-Änderungen selbst prüfen, bevor übergeben wird):

```bash
./Builds/Windows/OMSIMapWorks.exe -screen-width 1600 -screen-height 900 -screen-fullscreen 0 \
  -openMap "D:/SteamLibrary/steamapps/common/OMSI 2/maps/Grundorf/global.cfg" -view 0 -1 150 150 0.25 \
  -screenshot "<scratchpad>/grundorf.png" -quit -logFile "<scratchpad>/player.log"
```

Weitere Argumente: `-openProject <.omproj>`, `-orbit Richtung Neigung Abstand` (3D), `-select <objektID> [weitere IDs]` (mehrere wie Strg+Klick), `-turnSelection Grad` (dreht die Auswahl gemeinsam um ihre Mitte und speichert, nur mit Projekt), `-moveSelection dx dy` (verschiebt die Auswahl um Meter nach Osten und Norden und speichert wie Ziehen, auch über Kachelgrenzen; Statusmeldung als „Move selection:“ im Player-Log), `-rows <Sceneryobjects\...\x.sco> kachelX kachelY x y [-rowsLay] [-rowsBoth] [-rowsEvery m] [-rowsHeading Grad] [-rowsChange m [<Objekt>]]` (Reihen-Werkzeug zeigt auf die Stelle, mit `-rowsLay` legt es die Reihe, `-rowsChange` wählt sie danach und ändert Abstand und optional Objekt; Statusmeldungen als „Rows:“ und „Rows changed:“ im Player-Log; nur mit Projekt), `-place <Sceneryobjects\...\x.sco>` (Platziervorschau in der Bildmitte, wartet auf die Objekte und rastet an Anhängepunkten ein; Statusmeldung als „Place:“ im Player-Log), `-tool drag|move|rotate` (Pfeile nur mit `move`, Ringe/Drehgriff nur mit `rotate`), `-objects` (Objekte-Bibliothek offen, dazu `-objectsTab`/`-objectsAt`/`-objectsSearch`; Screenshots warten auf die Vorschaubilder, außer mit `-noPreviewWait`), `-fold project|inspector|both`, `-plain` (ohne Texturen), `-check` (Kartenprüfung, `-checkTopic <Titel>` scrollt zu einem Thema), `-routes [Linie]` (Linienwege, optional hervorgehoben), `-priorities [n ...]` (mit `-select` einer Kreuzung: Vorfahrt-Werkzeug, Arme ins Log, Schilder Nr. n ab 1 anklicken; „Priorities:“ im Player-Log; nur mit Projekt), `-paths [open|n]` (KI-Pfade, wartet auf sie; mit `open` oder einer Zahl zum ersten bzw. n-ten offenen Ende samt Auswahl; „Paths:“ im Player-Log), `-chrono JJJJ-MM-TT` (Chrono-Ansicht), `-chronoNew Name JJJJ-MM-TT [JJJJ-MM-TT]` / `-chronoEdit Ordner|Name` (Variante anlegen bzw. bearbeiten, vor `-select`/`-moveSelection`/`-place`; Statusmeldung als „Chrono edit:“, nur mit Projekt), `-deleteSelection` (löscht die Auswahl, „Delete selection:“), `-newMap [Vorlage]`, `-road kachelX kachelY x1 y1 x2 y2 ... [-roadBuild]` (Straßenwerkzeug: Punkte klicken, der letzte nur zeigen, mit `-roadBuild` auch bauen; die Statusmeldung jedes Klicks steht als „Road click n:“ im Player-Log; nur mit Projekt, `-roadMode follow|smooth|manual`, `-roadShape straight|curved|free`, `-roadSnap none|all|roads,angles,guides`, `-roadParallel left|right|both [m]` mit `-roadOpposite`, `-roadProfile <Splines\...\x.sli>`, `-roadTab <Reitername>`), `-selectSpline id [-splineHeights Start Ende]` (setzt die Höhen beider Enden samt Nachbarn, nur mit Projekt), `-moveSplineEnd id start|end|curve kachelX kachelY x y [-moveCommit] [-moveAlt]` (Spline-Ende ziehen oder mit `curve` biegen; `-moveAlt` wie gedrückte Alt-Taste), `-placeCommit` (mit `-place`: klickt die Platziervorschau, setzt also auch Kreuzungen in Straßen ein), `-testJunction <Splines\...\x.sli>` (erzeugt eine Test-Kreuzung unter `Sceneryobjects\Mapforge\<Projekt>` der Installation und platziert sie in der Bildmitte; mit `-placeCommit` eingesetzt; danach `3` oder `4` für eine T-Kreuzung oder vier Arme), `-refitTerrain` (Gelände an allen gebauten Straßen neu anpassen), `-addTile x y` / `-deleteTile x y` (Kachel hinzufügen/löschen, nur mit Projekt), `-hoverTileAdder x y` (Plus wie unter dem Mauszeiger), `-heightmapExport <datei.png>` / `-heightmapImport <datei.png> [-heightmapAdd] [-heightmapSmooth n] [-heightmapLowest m] [-heightmapHighest m]` (Höhenkarte schreiben bzw. auflegen, Statusmeldung als „Height map:“ im Player-Log), `-levelGround` (mit `-select`: Gelände unter den gewählten Objekten einebnen, Statusmeldung als „Level ground:“ im Player-Log), `-terrain raise|lower|smooth|flatten|roughen|water kachelX kachelY x y [-terrainRadius m] [-terrainStrength s] [-terrainSeconds s]` bzw. `-terrain ramp kachelX kachelY x1 y1 x2 y2` (Gelände-Werkzeug, Statusmeldung als „Terrain:“ im Player-Log; nur mit Projekt), `-markings show|auto|Von-Bis:Stil,...|area:Füllung:Ecke+Ecke+Ecke[:Winkel]|arrow:Art:Von~Bis[:Abstand]`, dazu `choose:Schlüssel` und `select:line|area|arrow:n`, mit `show,…` ohne Zeichnen (mit `-select` einer erzeugten Kreuzung: Markierungswerkzeug, Anker ins Log, Linien, Flächen und Pfeile ziehen, Menü in einem Zustand zeigen; „Markings:“ im Player-Log), `-turnArm objektID arm grad [-turnCommit]` (dreht Arm Nr. `arm` ab 1 einer im Editor erzeugten Kreuzung auf `grad` im Objekt, ohne `-turnCommit` nur Vorschau; nur mit Projekt). Luftbilder und Gelände (nur mit Projekt): `-satellite` öffnet das Fenster, dazu `-satelliteSearch <Text>`, `-satellitePlace Breite Länge [Drehung]`, `-satelliteSource <Name>|sharpest`, `-satelliteSize 0|1024|2048|4096`, `-satelliteTile x y`, `-satelliteLoad all|tile|heights [-satelliteHeights]` (wartet auf das Ende), `-satelliteFitEdges`, `-satelliteOpacity 0..1`, `-pictureLoad <Datei> [W S O N]`, `-pictureFit Drehung Ost Nord Größe`, `-tileOpacity x y Wert`, `-waterCorners x y SW SE NW NE`; Statusmeldungen als „Satellite:“ bzw. „Water:“ im Player-Log. Für Bearbeitungsansichten (Ringe, Griffe) ein Testprojekt im Scratchpad anlegen: `{"formatVersion":1,"projectName":"Test","globalConfigPath":"D:/.../maps/Grundorf/global.cfg"}` – die Arbeitskopie landet daneben, die Originalkarte bleibt unberührt. In Pfaden Schrägstriche verwenden. Bild dann mit Read ansehen. Gute Ausschnitte: Grundorf `0 -1 150 150 0.25`, Berlin-Spandau `2402 11279 200 200 0.4`.

## Testkarten

OMSI-2-Installation: `D:\SteamLibrary\steamapps\common\OMSI 2\maps` – **Grundorf** (klein, 19 Kacheln, Standardtest), **Stadtückheim**, **Berlin-Spandau** (groß, 766 Kacheln, `[worldcoordinates]`, Performance-Test). Beide Hauptkarten bei Änderungen prüfen.

## Weiterführende Dokumente

- `Documentation/Architecture.md` – Module und Datenfluss im Detail
- `Documentation/Decisions.md` – ADR 001–013
- `Documentation/DevelopmentSetup.md` – Werkzeuge, Kommandozeile, Validierungsstand
- `Documentation/Roadmap.md` – erledigt `[x]` und offen
- `Documentation/TileRecordInventory.md`, `GrundorfFormatInventory.md`, `SceneryObjectInventory.md`, `TimetableChronoInventory.md` – OMSI-Formatbefunde

## Oberfläche

Nach `Documentation/CorporateDesign.md` (vom Nutzer, ADR 015): Karte randlos, Panels schweben und sind einklappbar, Farben nur über `--mf-*`-Tokens in `EditorShell.uss`. Orange = aktiv/Hauptaktion, Rot = Löschen/Fehler, Gelb = bearbeitet/Warnung. Untere Werkzeugleiste zeigt nur fertige Werkzeuge. Werkzeuge sind Modi (Q Drag = wählen und frei ziehen, W Move = Achsenpfeile inkl. Höhe, E Rotate = Griff/Ringe). Lucide-Icons als SVG-Pfade in `LucideIcons.cs`, gezeichnet von `IconElement` (neue Icons dort eintragen, Lizenz steht in THIRD_PARTY_NOTICES); Inter (OFL) ist freigegeben, sichtbarer Name ist „OMSI MapWorks“ (ADR 034), Startbild-Logo in `Assets/_Project/UI/Branding/Logo.png`. Etappen stehen in der Roadmap. Herkunft von Objekten (Basisspiel/DLC/User-Content) kommt aus Steams Manifesten (`OmsiContentOrigins`, ADR 016); Steam liegt hier unter `C:\Program Files (x86)\Steam`, keine DLCs installiert.

## Aktueller Stand (27. Oktober 2026)

- `main` = `origin/main` mit Etappe 2 der KI-Pfade; alles gemergt und gepusht, kein offener Zweig. Neue Arbeit immer in einem neuen Zweig beginnen (`feature/...`), nach Nutzertest committen, nach `main` übernehmen und pushen.
- Freigabeliste der Player-Dateien zuletzt vom Nutzer am 25. Oktober neu geschrieben (`7f7a648`); neue oder geänderte Player-Dateien (Shader, USS, UXML, `Resources`) erfordern das erneut.
- Fertig und vom Nutzer bestätigt (Einzelheiten in Roadmap und ADRs):
  - Grundlage, Prüfen und Ansehen: verlustfreies Dateimodell, Projekte und Export, Kartenprüfung (u. a. Junctions, Made objects, Chain offsets), Linienwege, Chrono-Ansicht schreibgeschützt (ADR 020), DLC-Herkunft, Startbild und Name „OMSI MapWorks“ (ADR 034).
  - Objekte: platzieren, verschieben (auch über Kachelgrenzen mit kachelgebundenen Verweisen, ADR 012), drehen, löschen; Objekt-Menü (ADR 030); angehängte Objekte einrasten (ADR 039), auch am ersten Objekt einer Reihe (ADR 039, Ergänzung).
  - Straßen und Kreuzungen: Straßenbau als Splines (ADR 021–023), Kreuzungen als erzeugte Objekte samt Arme drehen und Aufräumen (ADR 024–028, 033, 035, 036), Straßenmenü (ADR 029), Reihen-Werkzeug „Rows“ mit Bearbeiten (ADR 037), Kettenabstände wie openOMSI (ADR 038), Bauarten Straight/Curved/Free mit Eckausrundung, Einrasten an Straßen, 90° und Hilfslinien, Parallel (ADR 043; Parallelen noch nicht durch Kreuzungen), Fahrbahnmarkierungen auf erzeugten Kreuzungen mit Werkzeug „Markings“ (M) wie Town Road Lane (ADR 044; Etappe 1 auch in openOMSI bestätigt, Etappe 2 mit Teilstücken, Schnittpunkt-Ankern, Auto markings und Favoriten in `main`; Etappe 3 mit Sperrflächen und Zebrastreifen sowie Etappe 4 mit Richtungspfeilen am 27. Oktober vom Nutzer bestätigt und in `main`; Menü neu wie das Straßenmenü, ADR 045, im Zweig `feature/marking-menu`).
  - Karte: neue Karte (ADR 032), Kacheln hinzufügen und löschen (ADR 031); Löschen führt Kettenabstände und Reihen der Nachbarn nach (ADR 038, Ergänzung).
  - Gelände wie openMapper (ADR 040): Pinsel und Rampe, „Level ground“, Höhenkarte (PNG schreiben; PNG oder TIFF lesen, `OmsiTiff`), See und Fluss, Wasser mit Eckhöhen.
  - Luftbilder wie openMapper (ADR 041): Verortung, Quellen ohne Schlüssel und mit Schlüssel (Mapbox, MapTiler, eigene Server), Geländehöhen mit Kantenanpassung, eigene Bilder, OpenStreetMap-Karte mit Namen, Bilder bis 4.096 px mit vollen Bildern beim Hineinzoomen, Deckkraft je Kachel.
  - Chrono-Varianten bearbeiten, Etappe 1 (ADR 042, 26. Oktober): „Edit“ und „New variant...“ im Chrono-Bereich; Objekte platzieren, verschieben, drehen, löschen in die Änderungsliste der Variante, Basisobjekte als Kopie mit neuer ID (`OmsiMapSessionChrono.cs`, Shell: `CanEditObjects` neben `CanEdit`).
  - KI-Pfade nach dem Vorbild der Mod „Traffic“ (Plan in der Roadmap): Etappe 1 Pfade anzeigen mit „Paths“, offenen Enden und Regeln im Inspector (ADR 046), Etappe 2 Vorfahrt mit dem Werkzeug „Priority“ (V), auch in openOMSI bestätigt; erzeugte Kreuzungen setzen sie selbst (ADR 047). Grundlage: Vorfahrt kommt nur aus `[rule] priority` (ohne Regel 128, Originalkarten 192/64), `[blockpath]` wirkt nicht.
- In openOMSI noch ungeprüft: Chrono-Auswahlen beim Umziehen von Objekten (ADR 012), angehängte Objekte an Reihen (ADR 039), bearbeitete Chrono-Varianten (ADR 042).
- Nächster Punkt: Etappe 3 der KI-Pfade, **Fahrstreifen verbinden** wie „Lane Connector“, nur auf erzeugten Kreuzungen (Zeile `MapWorks lanes:` in der `.sco`). Danach laut Roadmap weitere Etappen von **Chrono-Varianten bearbeiten** (Typwechsel und Beschriftung im Inspector, Regeln, Straßen und Splines, Fahrpläne der Variante). Zurückgestellt: Google als Luftbildquelle (kein Satellit für Konten im EWR), eigene Höhenquellen, Höhenkarten als TIFF schreiben, Splines vor Kachelversion 11 und angehängte Objekte vor Version 12 (keine Beispieldaten).
- Hinweise:
  - Hooks des Plugins legen bei `cd` in Unterordner `.omc`-Ordner an (auch unter `Assets/`); immer mit absoluten Pfaden arbeiten.
  - Kachel-`.map`-Dateien sind teils UTF-16 – `grep` findet darin nichts, mit Python dekodieren.
  - Tests, die Inter lesen (OSM-Karte), laufen aus dem Projektordner als Arbeitsverzeichnis.
  - Overpass (OSM-Karte) ist langsam, Antworten bleiben eine Woche im Zwischenspeicher; Niedersachsens Geodienste waren am 25. Oktober in Wartung (der Editor speichert nur echte Bilder).
  - Im Nutzerprojekt bleiben drei alte Straßenknicke (neu bauen oder mit Alt ziehen).

Diesen Abschnitt bei größeren Fortschritten aktualisieren.
