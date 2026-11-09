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
- Scripting-Backend ist Mono: `OrdinalIgnoreCase`-Wörterbücher kosten je Zugriff ~1,5 µs. In Schleifen über alle Objekte einen Pfad einmal je Text nachschlagen und in einem `Ordinal`-Wörterbuch merken (Architecture.md, „Leistung und Messung“).
- Dateien der Arbeitskopie nur über `OmsiMapWorkspace.Save`/`Revert` schreiben; der Verweis-Index der Sitzung erkennt Änderungen an deren Zähler (ADR 057).

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

Grundargumente: `-openMap <global.cfg>`, `-openProject <.omproj>`, `-view kachelX kachelY Breite Höhe Zoom`,
`-screenshot <Datei>`, `-quit`, `-logFile <Datei>`, `-language en-US` (bei jeder Prüfung setzen).
**Alle weiteren Argumente** (Werkzeuge, Straßenbau, Chrono, Gelände, Luftbilder, Karteneinstellungen …)
stehen in `Documentation/CommandLine.md`. Neue Argumente dort eintragen, nicht hier.

Hinweise: In Pfaden Schrägstriche verwenden. Für Bearbeitungsansichten ein Testprojekt im Scratchpad anlegen
(`{"formatVersion":1,"projectName":"Test","globalConfigPath":"D:/.../maps/Grundorf/global.cfg"}`), die
Arbeitskopie landet daneben, die Originalkarte bleibt unberührt. Bild danach mit Read ansehen.
Gute Ausschnitte: Grundorf `0 -1 150 150 0.25`, Berlin-Spandau `2402 11279 200 200 0.4`.

## Testkarten

OMSI-2-Installation: `D:\SteamLibrary\steamapps\common\OMSI 2\maps` – **Grundorf** (klein, 19 Kacheln, Standardtest), **Stadtückheim**, **Berlin-Spandau** (groß, 766 Kacheln, `[worldcoordinates]`, Performance-Test). Beide Hauptkarten bei Änderungen prüfen.

## Weiterführende Dokumente

- `Documentation/Architecture.md` – Module und Datenfluss im Detail
- `Documentation/Internationalisierung.md` – Abschluss der Mehrsprachigkeit: Technik, Architektur, Ausnahmen, neue Sprache anlegen
- `Documentation/Decisions.md` – ADR 001–068
- `Documentation/DevelopmentSetup.md` – Werkzeuge, Kommandozeile, Validierungsstand
- `Documentation/Roadmap.md` – erledigt `[x]` und offen
- `Documentation/ProjectStatus.md` – laufender Stand, Releases, Zweige, offene Nutzertests
- `Documentation/CommandLine.md` – alle Player-Argumente für Prüfläufe und Screenshots
- `Documentation/TileRecordInventory.md`, `GrundorfFormatInventory.md`, `SceneryObjectInventory.md`, `TimetableChronoInventory.md` – OMSI-Formatbefunde
- `Documentation/RefactoringAudit.md`, `PerformanceBaseline.md`, `RefactoringSummary.md` – Stabilisierung nach 1.0: Befunde, Messverfahren und -werte, umgesetzte Änderungen
- `Documentation/DesignReview-2026-10-28.md` – Design-Review der Hauptansicht (9 Befunde, offen; Skalierung und Kontraste kritisch)

## Oberfläche

Nach `Documentation/CorporateDesign.md` (vom Nutzer, ADR 015): Karte randlos, Panels schweben und sind einklappbar, Farben nur über `--mf-*`-Tokens in `EditorShell.uss`. Orange = aktiv/Hauptaktion, Rot = Löschen/Fehler, Gelb = bearbeitet/Warnung. Untere Werkzeugleiste zeigt nur fertige Werkzeuge. Werkzeuge sind Modi (Q Drag = wählen und frei ziehen, W Move = Achsenpfeile inkl. Höhe, E Rotate = Griff/Ringe). Programm-Icon „MW“ aus dem Logo (`Assets/_Project/UI/Branding/Icon.png` und `Tools/Icon/MapWorks.ico`, beide von `Tools/Icon/MakeIcon.ps1`, ADR 056). Lucide-Icons als SVG-Pfade in `LucideIcons.cs`, gezeichnet von `IconElement` (neue Icons dort eintragen, Lizenz steht in THIRD_PARTY_NOTICES); Inter (OFL) ist freigegeben, sichtbarer Name ist „OMSI MapWorks“ (ADR 034), Startbild-Logo in `Assets/_Project/UI/Branding/Logo.png`. Etappen stehen in der Roadmap. Herkunft von Objekten (Basisspiel/DLC/User-Content) kommt aus Steams Manifesten (`OmsiContentOrigins`, ADR 016); Steam liegt hier unter `C:\Program Files (x86)\Steam`, keine DLCs installiert.

## Aktueller Stand

Laufender Stand (Releases, Zweige, offene Nutzertests, Prüfwerkzeuge früherer Sitzungen) steht in
`Documentation/ProjectStatus.md`. Dort bei größeren Fortschritten aktualisieren, nicht hier.
Offene Funktionen: `Documentation/OMSI MapWorks – Feature Parity.md`.
