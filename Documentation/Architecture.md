# Architektur

## Leitlinien

- First-party Unity-Code liegt ausschließlich in `Assets/_Project/`.
- Assembly Definitions markieren fachliche Grenzen. Module halten ihre Abhängigkeiten klein und vermeiden Querreferenzen.
- Unity-spezifische Darstellung bleibt in UI und World. OMSI-Dateiformatlogik gehört in `Editor.OMSI`.
- Fachliche Logik erhält Daten über klare Schnittstellen und vermeidet globale Singleton-Zustände.
- Die Startansicht ist ein UI Toolkit `UIDocument` mit UXML und USS; Spielmodus und Editoransicht verwenden damit dieselbe UI-Technik.

## Module

| Assembly | Zuständigkeit | Abhängigkeiten |
|---|---|---|
| `Editor.Core` | Gemeinsame Grundschicht und zentrale Datenverträge wie Projektdatei und Anwendungseinstellungen | Keine Projekt-Assembly |
| `Editor.UI` | Editoransicht, Werkzeugleisten, Dateidialoge und UI Toolkit Komponenten | `Editor.Core`, `Editor.OMSI` |
| `Editor.Geometry` | Geometrie-, Kurven- und Profilberechnungen | `Editor.Core` |
| `Editor.World` | Kartenkacheln, Koordinaten und Weltzustand | `Editor.Core` |
| `Editor.Objects` | Platzierbare Objektdefinitionen und Instanzen | `Editor.Core` |
| `Editor.OMSI` | Import/Export und OMSI-Kompatibilität | `Editor.Core` |
| `Editor.Validation` | Prüfungen, Meldungen und Integritätsberichte | `Editor.Core` |
| `Editor.OMSI.Editor` | Editor-only: Kartenübersichtsfenster, Batch-Mode-Einstieg, Build-Prüfung | `Editor.OMSI` |
| `Editor.UI.Editor` | Editor-only: URP-Setup und Windows-Build-Helfer | `Editor.Core`, `Editor.UI`, `Editor.OMSI.Editor`, URP Runtime |
| `Editor.Tests` | Editor-only: EditMode-Tests mit dem Unity Test Framework | `Editor.Core`, `Editor.OMSI`, Test Runner, NUnit |

Die OMSI-Baugruppe enthält einen plattformneutralen, schreibgeschützten Leser für `global.cfg`, Kachel-Objekt-/Splinewerte und Terrain-Höhenbereiche. Darunter liegt ein verlustfreies Dateimodell: `OmsiRecordFile` zerlegt jede OMSI-Abschnittsdatei in Vorspann und Datensätze aus Kopfzeile, Feldern und Freitext. Jede Zeile behält ihr Zeilenende, die Datei ihre Kodierung. `ToBytes()` erzeugt für alle 1.409 Textdateien der installierten Karten exakt die Originalbytes. `OmsiTileRecordLayouts` liefert die belegten Feldanzahlen der Kachel-Abschnitte, sodass Datensätze positionell gelesen werden; Abschnitte ohne belegtes Layout bleiben als Freitext erhalten. `OmsiMapReader` liest Kacheln über dieses Modell. `OmsiRoundTripCheck` prüft eine ganze Karte im Arbeitsspeicher. Auf dem Modell liegen typisierte Zugriffe (`OmsiTileRecords` mit `OmsiObjectView`, `OmsiAttachedObjectView`, `OmsiSplineAttachmentView` und `OmsiSplineView`). Sie lesen und schreiben einzelne Felder mit benannten Eigenschaften. `OmsiNumberFormat` schreibt Zahlen wie der OMSI-Editor. `OmsiIdAllocator` vergibt neue IDs oberhalb von `[NextIDCode]` und der höchsten ID in Basis- und Chrono-Kacheln und schreibt den neuen Wert in global.cfg zurück; `OmsiGlobalConfigLayouts` liefert dafür die Layouts von global.cfg. Gelesen wird über `IOmsiFileSource`: `OmsiFolderSource` liest einen Kartenordner, `OmsiMapWorkspace` legt die geänderten Dateien eines Projekts über die Originalkarte und speichert nur in die Arbeitskopie. `OmsiMapExporter` schreibt die Arbeitskopie als eigenen Kartenordner (ADR 009). Er darf im Player als Programmcode enthalten sein, damit dieser OMSI-Daten direkt aus einer externen Installation lesen kann. `OmsiTextFile` dekodiert die Textdateien unabhängig von der Standard-Codepage der Laufzeit: Byte Order Mark (UTF-16 oder UTF-8), sonst striktes UTF-8, sonst Windows-1252. `OmsiSplineTileIndex` berechnet die Spline-Linien einer Karte einmal beim Laden und ordnet die zugeschnittenen Stücke den Kacheln zu; beide Kartenübersichten zeichnen nur noch diese vorberechneten Segmente. `OmsiMapGeometry` legt die Punkte exakt auf die Gerade oder den Kreisbogen des Datensatzes. `OmsiSplineContinuity` vergleicht die Endpunkte aller verbundenen Splines und meldet Lücken, einseitige Verweise und Verweise auf nicht geladene Splines. Die Kachelkante steht in `OmsiMapSummary.TileSizeMeters`: 300 m für klassische Karten, 371,9 m für `[worldcoordinates]`-Karten (ADR 007).

Das separate Editor-Modul stellt die Kachelübersicht, den Batch-Mode-Einstieg und Build-Prüfungen bereit und bleibt Editor-only. OMSI-Spielinhalte werden nicht in den Player kopiert: Der Windows-Build ermittelt die tatsächlichen Player-Abhängigkeiten und vergleicht sie bytegenau mit der lokalen Installation. Die übrigen Fachmodule sind noch vorbereitet. `EditorShellBootstrap` lädt das UI Toolkit-Dokument zur Laufzeit; der URP-Setupcode läuft nur im Unity Editor.

Mapforge-Projekte verwenden das versionierte `.omproj`-JSON-Dokument aus `Editor.Core`. Es speichert den Projektnamen und den absoluten Pfad zu einer externen `global.cfg`; die OMSI-Karte selbst wird weder kopiert noch verändert.

## Namens- und Fehlerkonventionen

- Typen, Methoden und öffentliche Eigenschaften: `PascalCase`.
- Parameter und lokale Variablen: `camelCase`.
- Namen ausschreiben; Abkürzungen nur für etablierte Begriffe wie `OMSI` und `UI`.
- Öffentliche Schnittstellen erhalten XML-Kommentare, wenn ihr Vertrag nicht selbsterklärend ist.
- Fehler früh mit präziser Meldung melden; keine Fehler stillschweigend verschlucken.
- Keine absoluten Benutzerpfade oder unnötigen globalen Zustände.

## Abhängigkeitsrichtung

UI und Fachmodule dürfen von Core abhängen. Die UI nutzt zusätzlich `Editor.OMSI`, um Karten zu lesen und darzustellen; `Editor.OMSI` kennt die UI nicht. Core bleibt unabhängig von UI, Unity-Editor-APIs und OMSI-Dateiformaten. Module kommunizieren später über Datenverträge oder explizite Services statt gegenseitiger Implementierungsreferenzen.

## Konfiguration

`Assets/_Project/Settings/Resources/editor-settings.json` enthält die versionsfähigen Startwerte für Anwendungsname, Desktopfenster und UI-Skalierung. Der Datenvertrag ist `MapforgeEditorSettings` in `Editor.Core` und prüft die Werte beim Laden.

- Das Editor-Setup überträgt Anwendungsname, Standardfenstergröße und `startMaximized` in die Player Settings. Nach Änderungen an der Datei **OMSI Map Editor > Setup > Apply Editor Settings** ausführen.
- Der Player lädt die Datei über `Resources` und setzt `interface.scale` als Skalierung des UI Toolkit-Panels. Ungültige Werte werden gemeldet; dann gelten die eingebauten Standardwerte.
- `minimumWindowWidth`/`minimumWindowHeight` werden geprüft, aber noch nicht erzwungen, weil Unity dafür keine Player-API bietet. `interface.defaultLocale` ist für eine spätere Lokalisierung reserviert.

## Tests

EditMode-Tests liegen in `Assets/_Project/Tests/Editor/` und laufen über **Window > General > Test Runner**. Sie decken Textdekodierung und -kodierung, das verlustfreie Dateimodell, positionelles Lesen von Kachel-Datensätzen, Kachelversionen, das Kachelraster beider Koordinatenarten, Terrain-Höhen, Pfadgrenzen, Spline-Geometrie, den Spline-Kachelindex, die Spline-Verbindungsprüfung und die Einstellungsdatei ab. Die Tests erzeugen ihre OMSI-Beispieldateien selbst in einem temporären Ordner und benötigen keine OMSI-Installation.
