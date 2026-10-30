# CLAUDE.md – OMSI Mapforge

Schnelleinstieg für Claude. Ausführliche Details stehen in `Documentation/`; diese Datei fasst das Wichtigste zusammen und sagt, wo man nachliest.

## Projekt in einem Absatz

OMSI MapWorks (Entwicklungsname Mapforge, bis 25. Oktober 2026 im Player „OMSI Map Editor“, ADR 034) ist ein Windows-Desktop-Editor für Karten des Bussimulators OMSI 2, gebaut mit **Unity 6.6 (`6000.6.6f1`)**, **URP `17.6.0`** und **UI Toolkit** (UXML/USS). Er liest Karten direkt aus der externen OMSI-Installation, zeigt sie als texturierte Draufsicht und in 3D, und erlaubt Objekte zu verschieben, drehen, platzieren und löschen. Änderungen landen in einer Projekt-Arbeitskopie; die Originalkarte wird nie verändert, nur ein ausdrücklicher Export schreibt eine eigene Kartenkopie. Ausnahme: Mit MapWorks erstellte Karten gehören dem Nutzer, öffnen sofort bearbeitbar und werden in ihren eigenen Ordner exportiert (ADR 052). Lizenz: AGPL-3.0 (enthält aus openOMSI portierte `.o3d`-Entwürfelung, siehe `THIRD_PARTY_NOTICES.md`). Der Projektinhaber hat die Weiterentwicklung an Claude übergeben.

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

Abhängigkeitsrichtung: UI → OMSI → Core; OMSI kennt UI nicht. Größte Dateien: `EditorShellBootstrap.cs` (~8.200 Zeilen, `partial`; Luftbilder in `EditorShellAerial.cs`, eigene Bilder in `EditorShellPictures.cs`), `MapCanvasElement.cs` (~3.800), `OmsiJunctionLayout.cs` (~2.100), `OmsiJunctionMarkings.cs` (~1.800), `MapObjectRenderer.cs` (~1.400). Neue Shell-Teile lieber als eigene `partial`-Datei.

## Wichtige OMSI-Klassen (alle in `Scripts/OMSI/`)

- **Dateimodell:** `OmsiTextFile` (Kodierung: BOM → striktes UTF-8 → Windows-1252), `OmsiRecordFile` (verlustfrei, `ToBytes()` byte-identisch), `OmsiTileRecordLayouts` / `OmsiGlobalConfigLayouts` (Feldanzahlen), `OmsiTileRecordViews` (typisierte Views: `OmsiObjectView`, `OmsiSplineView`, …), `OmsiNumberFormat` (Zahlen wie der OMSI-Editor).
- **Lesen:** `OmsiMapReader` → `OmsiMapSummary` (u. a. `TileSizeMeters`), `OmsiTerrain` (61 × 61 Höhen), `OmsiMapGeometry`, `OmsiSplineTileIndex`, `OmsiSplineContinuity`.
- **Bearbeiten:** `IOmsiFileSource` / `OmsiFolderSource` / `OmsiMapWorkspace` (Arbeitskopie über Original), `OmsiMapSession` (`PlaceObject`, `DeleteObject`, `TransferObject`, `OmsiObjectChange` für Undo; Straßen in `OmsiMapSessionSplines.cs`: `CreateRoad`, `DeleteSpline`, `OmsiSplineChange`, `OmsiSplineEnd`), `OmsiRoadPlanner` (Bogen, Stücke, Höhenmodi, Befunde), `OmsiTerrainSampler` (Gelände über Kachelgrenzen, außerhalb = ungültig), `OmsiTileEditing` (strukturelle Änderungen), `OmsiReferenceIndex` (kartenweite ID-Verweise), `OmsiIdAllocator` (`[NextIDCode]`), `OmsiMapExporter`.
- **Modelle/Texturen:** `OmsiSceneryObject` (`.sco`), `OmsiO3dMesh` (`.o3d`, mit Entwürfelung), `OmsiXMesh` (Text-`.x`), `OmsiObjectModel` (`OmsiObjectModelCatalog`), `OmsiFootprintTracer` (Umrisse), `OmsiImage` (BMP/TGA/DDS DXT1/3/5), `OmsiGroundTextures`, `OmsiSplineProfile` (`.sli`), `OmsiSplineAttachmentLayout` (Spline-Anhängungen, Reihen).
- **Gelände und Luftbilder:** `OmsiTerrainBrush`/`OmsiTerrainGrid` (Pinsel, kartenweites Raster), `OmsiHeightMap`, `OmsiPng`, `OmsiTiff`, `OmsiGeo` (Verortung, Quellen, `OmsiGeoSettings` in `Mapforge\geo.txt`, `OmsiImageryPreferences` mit Schlüsseln nur auf dem Rechner), `OmsiAerial` (Kachelspeicher, Mosaik, `OmsiAerialJob`), `OmsiOsmMap` mit `OmsiCanvas` (Vektorgrafik) und `OmsiTrueTypeFont`, `OmsiOwnPictures`, `OmsiJson`.

## Harte Regeln (nicht verletzen)

1. **Originalkarte nie verändern.** Gespeichert wird nur in `Name.workspace` neben `Name.omproj` (atomar über Temp-Datei). Export nur in einen eigenen Ordner `Kartenname (Mapforge)` mit Markierungsdatei `mapforge-export.txt` (ADR 009). **Ausnahme:** Mit MapWorks erstellte Karten (`mapworks-map.txt` oder „Created with OMSI MapWorks“ in `signalroutes.cfg`, `OmsiNewMap.IsMadeHere`) öffnen mit automatischem Projekt unter `Dokumente\OMSI MapWorks\Projects` und werden in ihren eigenen Ordner exportiert (ADR 052).
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

Weitere Argumente: `-openProject <.omproj>`, `-orbit Richtung Neigung Abstand` (3D), `-select <objektID> [weitere IDs]` (mehrere wie Strg+Klick), `-turnSelection Grad` (dreht die Auswahl gemeinsam um ihre Mitte und speichert, nur mit Projekt), `-moveSelection dx dy` (verschiebt die Auswahl um Meter nach Osten und Norden und speichert wie Ziehen, auch über Kachelgrenzen; Statusmeldung als „Move selection:“ im Player-Log), `-rows <Sceneryobjects\...\x.sco> kachelX kachelY x y [-rowsLay] [-rowsBoth] [-rowsEvery m] [-rowsHeading Grad] [-rowsChange m [<Objekt>]]` (Reihen-Werkzeug zeigt auf die Stelle, mit `-rowsLay` legt es die Reihe, `-rowsChange` wählt sie danach und ändert Abstand und optional Objekt; Statusmeldungen als „Rows:“ und „Rows changed:“ im Player-Log; nur mit Projekt), `-place <Sceneryobjects\...\x.sco>` (Platziervorschau in der Bildmitte, wartet auf die Objekte und rastet an Anhängepunkten ein; Statusmeldung als „Place:“ im Player-Log), `-tool drag|move|rotate` (Pfeile nur mit `move`, Ringe/Drehgriff nur mit `rotate`), `-objects` (Objekte-Bibliothek offen, dazu `-objectsTab`/`-objectsAt`/`-objectsSearch`; Screenshots warten auf die Vorschaubilder, außer mit `-noPreviewWait`), `-fold project|inspector|both`, `-plain` (ohne Texturen), `-check` (Kartenprüfung, `-checkTopic <Titel>` scrollt zu einem Thema), `-routes [Linie]` (Linienwege, optional hervorgehoben), `-lanes [von>nach ...|every|none]` (mit `-select` einer erzeugten Kreuzung: Werkzeug „Lanes“, Spurenden ins Log, Verbindungen wie `1.0>2.1` umschalten; „Lanes:“ im Player-Log; nur mit Projekt), `-turnLane n [Verziehung Aufstellstrecke|off]` (mit `-select` einer erzeugten Kreuzung: Linksabbiegespur an Arm n ab 1 anlegen, Längen ändern oder wegnehmen; „Turn lane:“ im Player-Log; nur mit Projekt), `-priorities [n ...]` (mit `-select` einer Kreuzung: Vorfahrt-Werkzeug, Arme ins Log, Schilder Nr. n ab 1 anklicken; „Priorities:“ im Player-Log; nur mit Projekt), `-paths [open|n]` (KI-Pfade, wartet auf sie; mit `open` oder einer Zahl zum ersten bzw. n-ten offenen Ende samt Auswahl; „Paths:“ im Player-Log), `-chrono JJJJ-MM-TT` (Chrono-Ansicht), `-chronoNew Name JJJJ-MM-TT [JJJJ-MM-TT]` / `-chronoEdit Ordner|Name` (Variante anlegen bzw. bearbeiten, vor `-select`/`-moveSelection`/`-place`; Statusmeldung als „Chrono edit:“, nur mit Projekt), `-deleteSelection` (löscht die Auswahl, „Delete selection:“), `-newMap [Vorlage]`, `-createMap <Vorlage global.cfg> <Ordner> <Name>` (legt eine Karte an wie der Dialog und öffnet sie mit eigenem Projekt unter Dokumente\OMSI MapWorks; „Create map:“ im Player-Log; Karte und Projekt danach löschen), `-exportMap` (exportiert wie der Dialog vorschlägt, eigene Karten in ihren Ordner; „Export:“ im Player-Log), `-road kachelX kachelY x1 y1 x2 y2 ... [-roadBuild]` (Straßenwerkzeug: Punkte klicken, der letzte nur zeigen, mit `-roadBuild` auch bauen; die Statusmeldung jedes Klicks steht als „Road click n:“ im Player-Log; nur mit Projekt, `-roadMode follow|smooth|manual`, `-roadShape straight|curved|free`, `-roadSnap none|all|roads,angles,guides`, `-roadParallel left|right|both [m]` mit `-roadOpposite`, `-roadBuilder ["walk 3 | drive 3.25 back | drive 3.25 | walk 3"] [-roadBuilderSelect n] [-roadBuilderLine n] [-roadBuilderSurfaces] [-roadBuilderSave Name] [-roadBuilderEdit <Splines\Mapforge\...\x.sli> [-roadBuilderReplace|-roadBuilderDelete]] [-roadBuilderFrom <x.sli>] [-undo [n]]`, `-pathRules all|n,n km/h|- Dichte|- Autos Busse Lkw` (mit `-select`/`-selectSpline` und `-paths`, `-wholeRoad` für alle Stücke der Straße: Verkehrsregeln wie „Apply“ im Inspector, „Path rules:“ im Player-Log), `-inspectorTo <Überschrift>` (Inspector zu einem Abschnitt scrollen), (Straßenbauer, ADR 049; gespeichert wird der Typ für `-road` gewählt; Edit öffnet einen eigenen Typ, Replace speichert ihn über sich, Delete löscht ihn, From zerlegt ein Profil in Spuren; `-undo` nimmt danach Schritte zurück, „Undo:“; „Road builder:“ im Player-Log), `-roadProfile <Splines\...\x.sli>`, `-roadTab <Reitername>`), `-selectSpline id [-splineHeights Start Ende]` (setzt die Höhen beider Enden samt Nachbarn, nur mit Projekt), `-moveSplineEnd id start|end|curve kachelX kachelY x y [-moveCommit] [-moveAlt]` (Spline-Ende ziehen oder mit `curve` biegen; `-moveAlt` wie gedrückte Alt-Taste), `-placeCommit` (mit `-place`: klickt die Platziervorschau, setzt also auch Kreuzungen in Straßen ein), `-testJunction <Splines\...\x.sli>` (erzeugt eine Test-Kreuzung unter `Sceneryobjects\Mapforge\<Projekt>` der Installation und platziert sie in der Bildmitte; mit `-placeCommit` eingesetzt; danach `3` oder `4` für eine T-Kreuzung oder vier Arme), `-refitTerrain` (Gelände an allen gebauten Straßen neu anpassen), `-addTile x y` / `-deleteTile x y` (Kachel hinzufügen/löschen, nur mit Projekt), `-hoverTileAdder x y` (Plus wie unter dem Mauszeiger), `-heightmapExport <datei.png>` / `-heightmapImport <datei.png> [-heightmapAdd] [-heightmapSmooth n] [-heightmapLowest m] [-heightmapHighest m]` (Höhenkarte schreiben bzw. auflegen, Statusmeldung als „Height map:“ im Player-Log), `-levelGround` (mit `-select`: Gelände unter den gewählten Objekten einebnen, Statusmeldung als „Level ground:“ im Player-Log), `-terrain raise|lower|smooth|flatten|roughen|water kachelX kachelY x y [-terrainRadius m] [-terrainStrength s] [-terrainSeconds s]` bzw. `-terrain ramp kachelX kachelY x1 y1 x2 y2` (Gelände-Werkzeug, Statusmeldung als „Terrain:“ im Player-Log; nur mit Projekt), `-markings show|auto|Von-Bis:Stil,...|area:Füllung:Ecke+Ecke+Ecke[:Winkel]|arrow:Art:Von~Bis[:Abstand]`, dazu `choose:Schlüssel` und `select:line|area|arrow:n`, mit `show,…` ohne Zeichnen (mit `-select` einer erzeugten Kreuzung: Markierungswerkzeug, Anker ins Log, Linien, Flächen und Pfeile ziehen, Menü in einem Zustand zeigen; „Markings:“ im Player-Log), `-turnArm objektID arm grad [-turnCommit]` (dreht Arm Nr. `arm` ab 1 einer im Editor erzeugten Kreuzung auf `grad` im Objekt, ohne `-turnCommit` nur Vorschau; nur mit Projekt). Luftbilder und Gelände (nur mit Projekt): `-satellite` öffnet das Fenster, dazu `-satelliteSearch <Text>`, `-satellitePlace Breite Länge [Drehung]`, `-satelliteSource <Name>|sharpest`, `-satelliteSize 0|1024|2048|4096`, `-satelliteTile x y`, `-satelliteLoad all|tile|heights [-satelliteHeights]` (wartet auf das Ende), `-satelliteFitEdges`, `-satelliteOpacity 0..1`, `-pictureLoad <Datei> [W S O N]`, `-pictureFit Drehung Ost Nord Größe`, `-tileOpacity x y Wert`, `-waterCorners x y SW SE NW NE`; Statusmeldungen als „Satellite:“ bzw. „Water:“ im Player-Log. Für Bearbeitungsansichten (Ringe, Griffe) ein Testprojekt im Scratchpad anlegen: `{"formatVersion":1,"projectName":"Test","globalConfigPath":"D:/.../maps/Grundorf/global.cfg"}` – die Arbeitskopie landet daneben, die Originalkarte bleibt unberührt. In Pfaden Schrägstriche verwenden. Bild dann mit Read ansehen. Gute Ausschnitte: Grundorf `0 -1 150 150 0.25`, Berlin-Spandau `2402 11279 200 200 0.4`.

## Testkarten

OMSI-2-Installation: `D:\SteamLibrary\steamapps\common\OMSI 2\maps` – **Grundorf** (klein, 19 Kacheln, Standardtest), **Stadtückheim**, **Berlin-Spandau** (groß, 766 Kacheln, `[worldcoordinates]`, Performance-Test). Beide Hauptkarten bei Änderungen prüfen.

## Weiterführende Dokumente

- `Documentation/Architecture.md` – Module und Datenfluss im Detail
- `Documentation/Decisions.md` – ADR 001–052
- `Documentation/DevelopmentSetup.md` – Werkzeuge, Kommandozeile, Validierungsstand
- `Documentation/Roadmap.md` – erledigt `[x]` und offen
- `Documentation/TileRecordInventory.md`, `GrundorfFormatInventory.md`, `SceneryObjectInventory.md`, `TimetableChronoInventory.md` – OMSI-Formatbefunde
- `Documentation/DesignReview-2026-10-28.md` – Design-Review der Hauptansicht (9 Befunde, offen; Skalierung und Kontraste kritisch)

## Oberfläche

Nach `Documentation/CorporateDesign.md` (vom Nutzer, ADR 015): Karte randlos, Panels schweben und sind einklappbar, Farben nur über `--mf-*`-Tokens in `EditorShell.uss`. Orange = aktiv/Hauptaktion, Rot = Löschen/Fehler, Gelb = bearbeitet/Warnung. Untere Werkzeugleiste zeigt nur fertige Werkzeuge. Werkzeuge sind Modi (Q Drag = wählen und frei ziehen, W Move = Achsenpfeile inkl. Höhe, E Rotate = Griff/Ringe). Lucide-Icons als SVG-Pfade in `LucideIcons.cs`, gezeichnet von `IconElement` (neue Icons dort eintragen, Lizenz steht in THIRD_PARTY_NOTICES); Inter (OFL) ist freigegeben, sichtbarer Name ist „OMSI MapWorks“ (ADR 034), Startbild-Logo in `Assets/_Project/UI/Branding/Logo.png`. Etappen stehen in der Roadmap. Herkunft von Objekten (Basisspiel/DLC/User-Content) kommt aus Steams Manifesten (`OmsiContentOrigins`, ADR 016); Steam liegt hier unter `C:\Program Files (x86)\Steam`, keine DLCs installiert.

## Aktueller Stand (30. Oktober 2026, Feierabend)

- `main` = `origin/main` (`98eb98e`), alles vom Nutzer im Editor (meist auch in openOMSI) bestätigt. Arbeitsbaum sauber bis auf die Freigabeliste (s. u.). Neue Arbeit immer in einem neuen Zweig beginnen (`feature/...`), nach Nutzertest committen (nur eigene Dateien, nach Pfad), nach `main` übernehmen und pushen; Doku (README, Architecture, Roadmap, DevelopmentSetup, ADR, dieser Abschnitt, Wiki) jedes Mal mitpflegen.
- **Am 30. Oktober fertig geworden** (je ein Commit in `main`):
  - Richtungspfeile aus den Fahrstreifen-Verbindungen in „Auto markings“ (ADR 050, Ergänzung, `OmsiJunctionMarkings.SuggestArrows`), `cd0197c`.
  - Pfeilform wie deutsche Richtungspfeile (ADR 044, Ergänzung; aus einem Bild des Nutzers vermessen): `OmsiJunctionMarkings.ArrowShape` liefert sich überdeckende Polygone, trianguliert über `OmsiJunctionLayout.Triangulate`, Umrisse über `Border`, `715e7fa`.
  - Richtungsfahrbahnen (ADR 051), alle drei Varianten nach Wahl des Nutzers:
    - Insel (Etappe 1, `8890c31`): Mittelstreifen endet in erzeugten Kreuzungen als Insel mit runder Spitze an der Einmündung (`BuildIslands`, `AddNose` in Ringen vom Rand nach innen, damit Randsteine herumlaufen), Spuren laufen einmal gerade bis zur Spitze (`AddStraight`); `OmsiSplineProfile.Clipped` lässt senkrechte Streifen (Bordsteinkanten) ganz.
    - Linksabbiegespur (Etappe 2, `e10e94a`): per Klick je Arm im Werkzeug „Lanes“ („Left-turn lanes“, Taper/Storage, Vorgabe 20/30 m); `OmsiTurnLane` an `OmsiJunctionArm`, Zeile `MapWorks turn lanes:`; der Arm wird länger (`BuildApproaches`, `AddIslandBody`, `AddTurnLaneMarkings`), innere Spur teilt sich am Ende der Verziehung, nur die Abbiegespur biegt links ab (Spurnummer = Anzahl der Profilspuren); `OmsiJunctionArm.Mouth` für Markierungsanker, Pfeile bis über die Zufahrt (`ArrowReach`); generierte Kreuzungen schreiben `[matl]`/`[matl_alpha]` für Alpha-Texturen. Kommandozeile `-turnLane n [Verziehung Aufstellstrecke|off]`.
    - Zwei getrennte Fahrbahnen (Variante 1, `aa8ea4c`): breite Mittelstreifen (etwa 30–35 m Mitte zu Mitte), je Fahrbahn eine Kreuzung, gebaut mit „Parallel“ + „Opposite way“; `BuildJunctions` baut Parallelen durch Kreuzungen (`BuildAcross`, `BuildParallelsAcross`).
  - Eigene Karten (ADR 052, `98eb98e`): mit MapWorks erstellte Karten (`mapworks-map.txt` oder „Created with OMSI MapWorks“ in `signalroutes.cfg`, `OmsiNewMap.IsMadeHere`) öffnen sofort bearbeitbar (Projekt aus den zuletzt geöffneten, sonst automatisch unter `Dokumente\OMSI MapWorks\Projects`, `EditorShellOwnMaps.cs`) und exportieren in ihren eigenen Ordner (`OmsiMapExporter.IsInPlace`, `ReplaceOwnFolder`, Arbeitskopie danach leer); fremde Karten wie bisher. Beim Nutzer gelten Aerial, Bremenhaven_Cuxland, Road-Builder, TestMap, TestMap (Straßenmakierungen) und die alten Exporte „… (MapWorks)“ als eigene. Kommandozeile `-createMap <Vorlage> <Ordner> <Name>`, `-exportMap`.
- **GitHub-Wiki für den ersten Release** (Englisch): https://github.com/Tiegertosch/Omsi-2-Mapforger/wiki mit `Home`, `Features-and-Functions` (alle Funktionen erklärt, Tastenkürzel, Grenzen) und `_Sidebar`. Eigenes Repo `https://github.com/Tiegertosch/Omsi-2-Mapforger.wiki.git` (Zweig `master`); beim Klonen Identität repo-lokal auf die noreply-Adresse setzen. Entwurf und Stand liegen auch in `.omc/wiki-draft/` (ignoriert). Bei neuen Funktionen die Seite mitpflegen. `gh` ist auf diesem Rechner nicht installiert; Repo-Infos über die öffentliche API mit `curl`.
- **Release vorbereiten (nächster Schritt des Nutzers):** Die Freigabeliste `Assets/_Project/Settings/PlayerAssetReview.txt` ist seit dem 29. Oktober vom Nutzer neu geschrieben, aber nicht committet (seine Datei, nur auf seinen Wunsch committen). Vor dem Release im Unity-Editor „OMSI MapWorks > Build > Record Reviewed Player Assets...“ erneut ausführen (u. a. `EditorShell.uss` hat sich geändert), dann „Windows Player (Release)“ (`BuildRelease`).
- **Zuletzt gebauter Player** (alle Stände von heute): `C:\Users\tiege\AppData\Local\Temp\claude\c--Users-tiege-Documents-omsi-mapforge\ed1a4455-7739-4c15-86be-3fa293fd2147\scratchpad\proj\Builds\Windows\OMSIMapWorks.exe` (Projektkopie mit Library in derselben Scratchpad-Sitzung; geänderte Dateien hineinkopieren und dort bauen). Der schnelle Test-Runner liegt im Scratchpad der Sitzung `a0de2350-2090-4e41-9191-99df7df152fe` unter `fast/run.sh` (Aufruf mit allen `Omsi*`-Testklassen; zuletzt 288 grün).
- Testreste: alle gelöscht. Unter `Splines\Mapforge` und `Sceneryobjects\Mapforge` der Installation liegen nur Nutzerprojekte (`Road-Builder`, `TestMap`, `Grundorf`, `Test`, `New map`, `Dynamic`, `Bremenhaven_Cuxland`) – nicht anfassen. Eigene Prüfläufe in neuen Wegwerf-Projekten im Scratchpad und deren Ordner (auch Karten in `maps` und Projekte in `Dokumente\OMSI MapWorks`) danach löschen.
- Fertig und vom Nutzer bestätigt (Einzelheiten in Roadmap und ADRs):
  - Grundlage, Prüfen und Ansehen: verlustfreies Dateimodell, Projekte und Export (ADR 009, eigene Karten ADR 052), Kartenprüfung, Linienwege, Chrono-Ansicht, DLC-Herkunft, Startbild und Name „OMSI MapWorks“ (ADR 034).
  - Objekte: platzieren, verschieben (auch über Kachelgrenzen, ADR 012), drehen, löschen; Objekt-Menü (ADR 030); angehängte Objekte einrasten (ADR 039).
  - Straßen: Splines (ADR 021–023), Straßenmenü (ADR 029), Reihen „Rows“ (ADR 037), Kettenabstände wie openOMSI (ADR 038), Bauarten Straight/Curved/Free, Einrasten an Straßen, 90° und Hilfslinien, Parallel auch durch Kreuzungen (ADR 043, ADR 051).
  - Kreuzungen als erzeugte Objekte samt Arme drehen (ADR 024–028, 033, 035, 036), Inseln und Linksabbiegespuren (ADR 051); Fahrbahnmarkierungen „Markings“ (M) mit Linien, Sperrflächen, Zebrastreifen, Richtungspfeilen in deutscher Form und Auto markings (ADR 044, 045).
  - KI-Pfade wie die Mod „Traffic“: Anzeige „Paths“ (ADR 046), Vorfahrt „Priority“ (V, ADR 047), Spurverbindungen „Lanes“ (L, ADR 048), Pfadregeln im Inspector (ADR 050).
  - Eigene Straßentypen wie Road Builder für Cities: Skylines II (ADR 049): Spuren, Linien, Flächen, Tram, Reihen, Bearbeiten/Speichern/Löschen, aus einer Straße zerlegen.
  - Karte: neue Karte (ADR 032), Kacheln hinzufügen und löschen (ADR 031). Gelände wie openMapper (ADR 040), Luftbilder wie openMapper (ADR 041), Chrono-Varianten bearbeiten Etappe 1 (ADR 042).
- In openOMSI noch ungeprüft: Chrono-Auswahlen beim Umziehen von Objekten (ADR 012), angehängte Objekte an Reihen (ADR 039), bearbeitete Chrono-Varianten (ADR 042).
- Als Nächstes: Release (s. o.); danach laut Roadmap weitere Etappen von **Chrono-Varianten bearbeiten** (Typwechsel und Beschriftung im Inspector, Regeln, Straßen und Splines, Fahrpläne der Variante). Offen aus ADR 051: Parallelen um ausgerundete Ecken und an Straßenenden. Zurückgestellt: Google als Luftbildquelle (kein Satellit für Konten im EWR), eigene Höhenquellen, Höhenkarten als TIFF schreiben, Splines vor Kachelversion 11 und angehängte Objekte vor Version 12 (keine Beispieldaten), Gleise durch Übergangsstücke und abbiegende Gleise, schon gelegte Reihen beim Bearbeiten eines Straßentyps auf fremden Straßen.
- Hinweise:
  - Ist der Unity-Editor offen, auf einer Projektkopie testen und bauen: `Assets`, `Packages`, `ProjectSettings`, `Library` mit **robocopy in PowerShell** (in Git Bash scheitert robocopy an den Pfaden, Exit 16) nach `<scratchpad>\proj`, geänderte Dateien danach einzeln kopieren; Batch-Mode auf die Kopie. Neue Skriptdateien bekommen ihre `.meta` dort beim Build; sie ins Repo zurückkopieren und mitcommitten.
  - Player-Prüfungen per Skript im Scratchpad (Beispiele heute: `islands.sh`, `turn.sh`, `dual.sh`, `own.sh`); Builds und Prüfläufe im Hintergrund starten.
  - Inspector-Abschnitte für Screenshots mit `-inspectorTo <Überschrift>` (mit `-inspectorRedraw` danach neu gezeichnet) ansehen; das Fenster lässt sich nicht höher als 1080 px machen.
  - Hooks des Plugins legen bei `cd` in Unterordner `.omc`-Ordner an (auch unter `Assets/`); immer mit absoluten Pfaden arbeiten. Der OMC-Hook meldet bei „Wiki“ das Skill `wiki`; gemeint ist hier das GitHub-Wiki.
  - Python-Skripte mit Backslashes oder vielen Anführungszeichen als Datei schreiben (Write) statt als Bash-Heredoc.
  - Kachel-`.map`-Dateien sind teils UTF-16 – `grep` findet darin nichts, mit Python dekodieren.
  - Tests, die Inter lesen (OSM-Karte), laufen aus dem Projektordner als Arbeitsverzeichnis.
  - Overpass (OSM-Karte) ist langsam, Antworten bleiben eine Woche im Zwischenspeicher.
  - Im Nutzerprojekt bleiben drei alte Straßenknicke (neu bauen oder mit Alt ziehen).

Diesen Abschnitt bei größeren Fortschritten aktualisieren.
