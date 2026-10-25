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

Abhängigkeitsrichtung: UI → OMSI → Core; OMSI kennt UI nicht. Größte Dateien: `EditorShellBootstrap.cs` (~2.000 Zeilen), `MapCanvasElement.cs` (~1.300), `MapObjectRenderer.cs` (~900).

## Wichtige OMSI-Klassen (alle in `Scripts/OMSI/`)

- **Dateimodell:** `OmsiTextFile` (Kodierung: BOM → striktes UTF-8 → Windows-1252), `OmsiRecordFile` (verlustfrei, `ToBytes()` byte-identisch), `OmsiTileRecordLayouts` / `OmsiGlobalConfigLayouts` (Feldanzahlen), `OmsiTileRecordViews` (typisierte Views: `OmsiObjectView`, `OmsiSplineView`, …), `OmsiNumberFormat` (Zahlen wie der OMSI-Editor).
- **Lesen:** `OmsiMapReader` → `OmsiMapSummary` (u. a. `TileSizeMeters`), `OmsiTerrain` (61 × 61 Höhen), `OmsiMapGeometry`, `OmsiSplineTileIndex`, `OmsiSplineContinuity`.
- **Bearbeiten:** `IOmsiFileSource` / `OmsiFolderSource` / `OmsiMapWorkspace` (Arbeitskopie über Original), `OmsiMapSession` (`PlaceObject`, `DeleteObject`, `TransferObject`, `OmsiObjectChange` für Undo; Straßen in `OmsiMapSessionSplines.cs`: `CreateRoad`, `DeleteSpline`, `OmsiSplineChange`, `OmsiSplineEnd`), `OmsiRoadPlanner` (Bogen, Stücke, Höhenmodi, Befunde), `OmsiTerrainSampler` (Gelände über Kachelgrenzen, außerhalb = ungültig), `OmsiTileEditing` (strukturelle Änderungen), `OmsiReferenceIndex` (kartenweite ID-Verweise), `OmsiIdAllocator` (`[NextIDCode]`), `OmsiMapExporter`.
- **Modelle/Texturen:** `OmsiSceneryObject` (`.sco`), `OmsiO3dMesh` (`.o3d`, mit Entwürfelung), `OmsiXMesh` (Text-`.x`), `OmsiObjectModel` (`OmsiObjectModelCatalog`), `OmsiFootprintTracer` (Umrisse), `OmsiImage` (BMP/TGA/DDS DXT1/3/5), `OmsiGroundTextures`, `OmsiSplineProfile` (`.sli`), `OmsiSplineAttachmentLayout` (neu, Spline-Anhängungen).

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

Weitere Argumente: `-openProject <.omproj>`, `-orbit Richtung Neigung Abstand` (3D), `-select <objektID> [weitere IDs]` (mehrere wie Strg+Klick), `-turnSelection Grad` (dreht die Auswahl gemeinsam um ihre Mitte und speichert, nur mit Projekt), `-moveSelection dx dy` (verschiebt die Auswahl um Meter nach Osten und Norden und speichert wie Ziehen, auch über Kachelgrenzen; Statusmeldung als „Move selection:“ im Player-Log), `-rows <Sceneryobjects\...\x.sco> kachelX kachelY x y [-rowsLay] [-rowsBoth] [-rowsEvery m] [-rowsHeading Grad] [-rowsChange m [<Objekt>]]` (Reihen-Werkzeug zeigt auf die Stelle, mit `-rowsLay` legt es die Reihe, `-rowsChange` wählt sie danach und ändert Abstand und optional Objekt; Statusmeldungen als „Rows:“ und „Rows changed:“ im Player-Log; nur mit Projekt), `-place <Sceneryobjects\...\x.sco>` (Platziervorschau in der Bildmitte, wartet auf die Objekte und rastet an Anhängepunkten ein; Statusmeldung als „Place:“ im Player-Log), `-tool drag|move|rotate` (Pfeile nur mit `move`, Ringe/Drehgriff nur mit `rotate`), `-objects` (Objekte-Bibliothek offen, dazu `-objectsTab`/`-objectsAt`/`-objectsSearch`; Screenshots warten auf die Vorschaubilder, außer mit `-noPreviewWait`), `-fold project|inspector|both`, `-plain` (ohne Texturen), `-check` (Kartenprüfung, `-checkTopic <Titel>` scrollt zu einem Thema), `-routes [Linie]` (Linienwege, optional hervorgehoben), `-chrono JJJJ-MM-TT` (Chrono-Ansicht), `-newMap [Vorlage]`, `-road kachelX kachelY x1 y1 x2 y2 ... [-roadBuild]` (Straßenwerkzeug: Punkte klicken, der letzte nur zeigen, mit `-roadBuild` auch bauen; die Statusmeldung jedes Klicks steht als „Road click n:“ im Player-Log; nur mit Projekt, `-roadMode follow|smooth|manual`, `-roadProfile <Splines\...\x.sli>`, `-roadTab <Reitername>`), `-selectSpline id [-splineHeights Start Ende]` (setzt die Höhen beider Enden samt Nachbarn, nur mit Projekt), `-moveSplineEnd id start|end|curve kachelX kachelY x y [-moveCommit] [-moveAlt]` (Spline-Ende ziehen oder mit `curve` biegen; `-moveAlt` wie gedrückte Alt-Taste), `-placeCommit` (mit `-place`: klickt die Platziervorschau, setzt also auch Kreuzungen in Straßen ein), `-testJunction <Splines\...\x.sli>` (erzeugt eine Test-Kreuzung unter `Sceneryobjects\Mapforge\<Projekt>` der Installation und platziert sie in der Bildmitte; mit `-placeCommit` eingesetzt; danach `3` oder `4` für eine T-Kreuzung oder vier Arme), `-refitTerrain` (Gelände an allen gebauten Straßen neu anpassen), `-addTile x y` / `-deleteTile x y` (Kachel hinzufügen/löschen, nur mit Projekt), `-hoverTileAdder x y` (Plus wie unter dem Mauszeiger), `-heightmapExport <datei.png>` / `-heightmapImport <datei.png> [-heightmapAdd] [-heightmapSmooth n] [-heightmapLowest m] [-heightmapHighest m]` (Höhenkarte schreiben bzw. auflegen, Statusmeldung als „Height map:“ im Player-Log), `-levelGround` (mit `-select`: Gelände unter den gewählten Objekten einebnen, Statusmeldung als „Level ground:“ im Player-Log), `-terrain raise|lower|smooth|flatten|roughen kachelX kachelY x y [-terrainRadius m] [-terrainStrength s] [-terrainSeconds s]` bzw. `-terrain ramp kachelX kachelY x1 y1 x2 y2` (Gelände-Werkzeug, Statusmeldung als „Terrain:“ im Player-Log; nur mit Projekt), `-turnArm objektID arm grad [-turnCommit]` (dreht Arm Nr. `arm` ab 1 einer im Editor erzeugten Kreuzung auf `grad` im Objekt, ohne `-turnCommit` nur Vorschau; nur mit Projekt). Für Bearbeitungsansichten (Ringe, Griffe) ein Testprojekt im Scratchpad anlegen: `{"formatVersion":1,"projectName":"Test","globalConfigPath":"D:/.../maps/Grundorf/global.cfg"}` – die Arbeitskopie landet daneben, die Originalkarte bleibt unberührt. In Pfaden Schrägstriche verwenden. Bild dann mit Read ansehen. Gute Ausschnitte: Grundorf `0 -1 150 150 0.25`, Berlin-Spandau `2402 11279 200 200 0.4`.

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

## Aktueller Stand (25. Oktober 2026)

- `feature/small-items` (enthält `feature/road-menu` und `feature/new-map-dialog`) ist nach Test und Freigabe durch den Nutzer in `main` übernommen und nach `origin` gepusht. Neue Arbeit in einem neuen Zweig beginnen.
- Erledigt und vom Nutzer im Editor bzw. Spiel bestätigt: Kartenprüfung, Haltestellen und Linienwege („Routes“, Nachführen nach Bearbeitung), Chrono-Ansicht mit Schreibschutz und „Base map“ (ADR 020), DLC-Markierung von Hand (nur für Ordner jenseits des Basisspiels), neue Karte mit Einstiegspunkt „Start“ lädt in OMSI (ohne Einstiegspunkt stürzt OMSI ab), Geländelöcher nach Verschieben, Freigabeliste für Player-Dateien (ADR 005 ergänzt).
- Außerdem auf dem Zweig: Vektorebene in Token-Farben, Gruppen und nEditor-Vorschaubilder in der Bibliothek, Geländelöcher und Leitungen/Zäune (ADR 018), schnelleres Zeichnen, Masken nur nahe der Ansicht, `[worldcoordinates]` als Web Mercator (ADR 019).
- In Arbeit auf `feature/roads`: Straßenbau nach den Briefings des Nutzers (Zusammenfassung in `Documentation/Strassenbau.md`; Straßen als OMSI-Splines, ADR 021). Etappe 1 und Geländeanpassung (ADR 022) vom Nutzer im Editor und in OMSI geprüft; nach dem OMSI-Test (Graswand am Rand, weil OMSI unter `[spline_terrain_align_2]` ausschneidet) liegt das Gelände 5 cm unter dem Profilrand, flach über mindestens eine Rasterdiagonale, und der Editor schneidet die Profilkontur aus; im Spiel bestätigt. Spline-Griffe wie MoveIt (ADR 023) vom Nutzer geprüft; Etappe 2 fertig (2a Smooth, 2b Enden ziehen, 2c Bauen in 3D), Nutzertest steht aus; Alles gemergt und gepusht (`efb0cc9`). Auf `feature/junctions`: Etappe 3a, Anschluss an Kreuzungspunkte (ADR 024); nach Nutzertest nachgebessert und vom Nutzer gesehen: Rauten folgen beim Ziehen, Gelände bleibt hinter Straßenenden an Kreuzungen unverändert (ADR 022), Straßen mit fester Richtung an beiden Enden bekommen zwei Bögen statt eines Knicks, Warnung bei Radius unter halber Profilbreite, Punkte direkt hinter der Fahrtrichtung abgelehnt (`c2da166`). Etappe 3b fertig und vom Nutzer im Editor bestätigt: Thema „Junctions“ in der Kartenprüfung, Gelände an im Editor platzierten Kreuzungen mit angeschlossener Straße mit Geländeanpassung (Entscheidung des Nutzers, ADR 022). Vom Nutzer im Spiel getestet (Straßen ohne Lücken, Gelände passt); danach Platzierhöhe für Kreuzungen und Rampe zwischen Straßen auf verschiedener Höhe nachgebessert und vom Nutzer gesehen. Restkanten an sehr steilen Stellen später mit Gelände-Werkzeugen (Roadmap). Nach Freigabe des Nutzers in `main` übernommen und gepusht (`50ae256`). Neue Arbeit in einem neuen Zweig beginnen. Auf `feature/junction-objects`, in `main` übernommen und gepusht: Etappe 4 Kreuzungen als Objekte, alles vom Nutzer getestet: 4a Kreuzung in Straße einsetzen (ADR 025), 4b erzeugte Objekte unter `Sceneryobjects\Mapforge\<Projekt>` (ADR 026, im Spiel bestätigt), 4c T-Kreuzung und Kreuzung mit vier Armen aus Profilen (ADR 027, im Spiel bestätigt), 4d Kreuzungen beim Straßenbau, gemischte Profile, breite Straßen, Mittelstreifen bei T-Kreuzungen, Übergangsstücke, Radweg-Gehwege an Ecken (ADR 028), letzter Commit `e4ed006`. Offen laut Roadmap: erzeugte Kreuzungen nach Verschieben eines Arms neu erzeugen, mehrere Kreuzungen auf einer neuen Straße. Neue Arbeit in einem neuen Zweig beginnen. Im Nutzerprojekt bleiben drei alte Knicke (neu bauen oder mit Alt ziehen).
- Seit 25. Oktober 2026 in `main`: Startbild mit dem Logo des Nutzers statt des Unity-Logos (`Assets/_Project/UI/Branding/Logo.png`), Freigabeprüfung zählt Startbild und Icons mit (ADR 005), Produktname „OMSI MapWorks“ (ADR 034; technische Namen behalten „Mapforge“, Einstellungen werden aus „OMSI Map Editor“ übernommen). Freigabeliste der Player-Dateien vom Nutzer neu geschrieben.
- Seit 24. Oktober 2026 in `main`, alles vom Nutzer im Editor bestätigt: Straßenmenü (ADR 029), Objekt-Menü (ADR 030), Kacheln hinzufügen und löschen (ADR 031), Dialog für neue Karten (ADR 032, Linksverkehr = `[LHT]`), mehrere Objekte gemeinsam drehen, Höhe eines Spline-Gelenks im Inspector (`PlanJointHeight`; berührende Enden ohne ID zählen als Nachbarn, Anschlusspunkte von Kreuzungen eben), mehrere Kreuzungen auf einer neuen Straße (ADR 033, `BuildJunctions`), knickfrei ziehen mit zwei Bögen (Alt, ADR 023 ergänzt, `PlanSmooth`, `ApplyJointMove`).
- Offen laut Roadmap: angehängte Objekte (`[attachObj]`) anlegen, Gelände-Werkzeuge, Chrono-Varianten bearbeiten. Seit 25. Oktober 2026 in `main`, vom Nutzer im Editor und im Spiel bestätigt: Arme erzeugter Kreuzungen drehen (ADR 035, `OmsiJunctionRebuild`, Griffe an den Armen, Straßen werden geschnitten und mit mindestens 25 m Radius angeschlossen). Ebenso unbenutzte erzeugte Objekte aufräumen (ADR 036, Thema „Made objects“ der Kartenprüfung, vom Nutzer bestätigt) und Umziehen mit kachelgebundenen Verweisen (ADR 012, Ergänzung; Einstiegspunkte folgen ihrem Objekt immer, im Spiel bestätigt; angehängte Objekte im Spiel bestätigt, Chrono-Auswahlen im Spiel noch ungeprüft) und das Reihen-Werkzeug „Rows“ (T, ADR 037: Reihen entlang von Straßen anlegen und löschen, ein `[splineAttachement]` je Kachel ohne Wiederholungen, Modellvorschau, R dreht; im Editor bestätigt, im Spiel ungeprüft; Reihen bleiben stehen, wenn sich die Kette vor ihrem Spline ändert, `KeepRowsInPlace`, vom Nutzer bestätigt). Seit 25. Oktober 2026 in `main`: vorhandene Reihen bearbeiten (ADR 037, zweite Ergänzung, `OmsiAttachmentRows.RowOf`/`Replan`, `ChangeAttachmentRow`) und Kettenabstände pflegen, Reihen wie openOMSI auslegen (ADR 038, Thema „Chain offsets“ mit „Store offsets“; Reihen über Kachelgrenzen vom Nutzer in openOMSI bestätigt). Seit 25. Oktober 2026 in `main`: Gelände-Werkzeuge wie openMapper, Etappe 1 Pinsel und Rampe (ADR 040, Grundlage `Mapforge\Terrain` für die Straßenanpassung, nicht exportiert; vom Nutzer in openOMSI bestätigt); ebenso Etappe 2 „Level ground“ im Inspector (vom Nutzer bestätigt); ebenso Etappe 3 Höhenkarte als PNG wie openMapper (vom Nutzer bestätigt); als Nächstes Etappe 4 See und Fluss. Ebenso in `main`: angehängte Objekte anlegen (ADR 039: beim Platzieren an freien Anhängepunkten einrasten, Inspector-Liste „Attached objects“ mit Delete, Löschen nimmt angehängte Objekte mit; vom Nutzer im Editor und im Spiel bestätigt). Neue Arbeit in einem neuen Zweig beginnen.
- Zurückgestellt: Splines vor Kachelversion 11 (keine Beispieldaten).
- Hinweis: Hooks des Plugins legen bei `cd` in Unterordner `.omc`-Ordner an (auch unter `Assets/`); immer mit absoluten Pfaden arbeiten.
- Hinweis: Kachel-`.map`-Dateien sind teils UTF-16 – `grep` findet darin nichts, mit Python dekodieren.

Diesen Abschnitt bei größeren Fortschritten aktualisieren.
