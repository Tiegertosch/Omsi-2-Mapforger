# Refactoring-Audit nach 1.0 (1. November 2026)

Stand: Zweig `refactor/post-1.0-stabilization` ab `main` = `6c9acdd` (Release `v1.0` = Tag auf `2705aaa`). Messungen und Verfahren stehen in [PerformanceBaseline.md](PerformanceBaseline.md), umgesetzte Änderungen in [RefactoringSummary.md](RefactoringSummary.md).

Ziel des Meilensteins: Funktion von 1.0 unverändert lassen und den Code dort schneller, sparsamer und klarer machen, wo Messungen oder der Code einen Grund zeigen. Kein Umbau ohne Beleg, keine Änderung an Dateiformaten, Projekt- und Arbeitskopieformat oder Undo-Semantik.

## Vorgehen

1. Projektregeln gelesen (CLAUDE.md, Architecture, Decisions, DevelopmentSetup, Roadmap, `.editorconfig`, `Packages/manifest.json`), dann den Quelltext statt der Doku als Maßstab genommen.
2. Ausgangsstand gesichert: 308 Unity-freie EditMode-Tests grün (schneller csc-Läufer), Player-Build ohne Compiler-Warnungen.
3. Messungen ohne Unity (`Editor.OMSI` direkt auf Grundorf und Berlin-Spandau) und im Release-Player (neuer Lauf `-benchmark`), dazu eine nur in der Projektkopie eingebaute Zeitmessung einzelner Methoden (`Probe`), um Bildkosten und Bearbeitungskosten aufzuschlüsseln.
4. Befunde nach Wirkung und Risiko geordnet (unten), dann in kleinen, einzeln prüfbaren Schritten umgesetzt.

## Architektur

### Module

| Assembly | Befund |
|---|---|
| `Editor.Core` | Klein und sauber (Projekt, Einstellungen, Fehlerbericht). Keine Änderung nötig. |
| `Editor.OMSI` | 79 Dateien, ohne Unity-Abhängigkeit, gut testbar; das verlustfreie Dateimodell (`OmsiRecordFile`, `OmsiTextFile`) ist der wichtigste Vertrag und wird nicht angefasst. Größte Dateien: `OmsiJunctionLayout` (~2.100 Zeilen), `OmsiJunctionMarkings` (~1.800), `OmsiRoadTypes` (~1.500), `OmsiMapSessionSplines` (~1.200). |
| `Editor.World` | Zwei Renderer mit klarer Aufgabenteilung (Boden/Splines, Objekte). Beide laden im Hintergrund und laden im Hauptthread mit Zeitbudget hoch; Ressourcen gehören den Renderern (`owned`) und werden bei `Clear`/`OnDestroy` zerstört. Schwachstelle: Nach jeder Bearbeitung werden ganze Karten-Strukturen neu aufgebaut (siehe Leistung). |
| `Editor.UI` | Die Shell ist eine `partial`-Klasse über zwölf Dateien; `EditorShellBootstrap.cs` allein hat ~8.250 Zeilen, 141 Felder und 261 Methoden. Die Kommandozeilen-Einstiege (`Start()`) sind eine Koroutine von ~670 Zeilen mit 38 Argumenten. |
| `Editor.Geometry`, `Editor.Objects`, `Editor.Validation` | Leer bis auf die Assembly Definition. Architecture.md beschreibt ihre geplante Aufgabe so, als sei sie umgesetzt. Empfehlung: als reserviert kennzeichnen; keinen Code dorthin verschieben, nur um sie zu füllen. Die Geometrie lebt heute in `Editor.OMSI` (`OmsiMapGeometry`, `OmsiRoadPlanner`, `OmsiJunctionLayout`), weil sie OMSI-Datensätze rechnet; ein Umzug brächte nur neue Querverweise. |

Abhängigkeitsrichtung UI → World → OMSI → Core ist eingehalten; `Editor.OMSI` kennt weder Unity noch UI (geprüft: kein `using UnityEngine` in `Scripts/OMSI`).

### Shell (`EditorShellBootstrap`)

- **Zustand:** Auswahl, Werkzeug, Undo-Verlauf, Chrono-Ansicht, Luftbilder, Straßenwerkzeug und Inspector teilen sich eine Klasse. Die Aufteilung in `partial`-Dateien ordnet nur den Text, nicht die Zuständigkeiten. Ein echter Schnitt (z. B. eigener Undo-Dienst, eigener Auswahl-Dienst) wäre der Hebel für Wartbarkeit, ist aber ein Umbau der Undo-Koordination und braucht die Zustimmung des Projektinhabers (siehe Plan, Stufe 5).
- **Auffrischen nach Bearbeitungen:** `RefreshAfterChange` ist der zentrale Weg nach Objektänderungen. Er frischt alles kartenweit auf (Platzierungen, Geländelöcher, Kachelliste, Wege), ohne zu wissen, welche Kacheln betroffen waren. Das ist korrekt und einfach, aber auf großen Karten teuer (siehe Leistung).
- **Undo/Redo:** dateibasiert für Platzieren/Löschen/Straßen/Gelände (`OmsiObjectChange.FileState` mit Vorher/Nachher-Bytes), wertbasiert für Verschieben/Drehen; höchstens 200 Schritte (`HistoryLimit`). Ein Platzieren auf Berlin-Spandau hält 519 KB (Kachel und `global.cfg` je zweimal), 200 solche Schritte also bis ~100 MB. Die Semantik ist robust (Undo verweigert, wenn eine Datei inzwischen anders ist) und bleibt.
- **Ereignisse:** Die Shell abonniert die Renderer-Ereignisse einmal in `Awake`; Renderer und Shell leben gleich lang. Keine Lecks gefunden.
- **Kopplung laut Wissensgraph (Nachtrag 10. November 2026, graphify über Code und Doku, 7.033 Knoten):** `EditorShellBootstrap` ist mit Abstand der stärkste Knoten (747 Kanten, Betweenness 0,177) und verbindet rund 70 der 234 Gruppen, weil 31 `partial`-Dateien (~18.600 Zeilen, Hauptdatei 8.379) eine Klasse bilden. Zusammengehalten werden die Teile von wenigen gemeinsamen Methoden; Aufrufe aus *anderen* Shell-Dateien: `SetStatusT`/`SetStatus` 124, `ReadOnlyMessage` 34, `PushHistory`/`ObjectEditStep` 30, `RenderProjectBrowser` 14, `RefreshAfterChange` 13, `SetTool`/`HidePopup`/`RefreshSplineViews`/`RenderInspector` je 9–12. Gut getrennt sind schon Updates, Fehlermeldung, Ordnerauswahl, eigene Bilder und Straßenbauer (je 3–10 Nachbargruppen). Noch in der Hauptdatei: Straßenwerkzeug (~44 Methoden, dazu ~10 für Einrasten/Hilfslinien/Planung, etwa Zeilen 2.900–5.600), Objektauswahl/Einrasten/Anhängen (32), Kachel- und Spline-Auswahl im Inspector (23), Chrono-Varianten anlegen/löschen/bearbeiten (15), Verlauf und Statuszeile. **Empfehlung für den UI-Umbau:** zuerst Verlauf (Undo) und Statuszeile als kleine eigene Dienste (schmale Schnittstelle, viele Nutzer), dann das Straßenwerkzeug als eigene Steuerklasse (größter Gewinn, größtes Risiko); weitere `partial`-Dateien kürzen nur den Text, entkoppeln aber nicht. Grenzen: Grundlage sind statische Verweise, keine Laufzeitmessung; die Gruppe „Shell Status and Clipboard“ ist ein Sammelbecken (über `Start()`), die Zeilenbereiche sind grob.
- **Kommandozeile:** `Start()` (~670 Zeilen) ist Testinfrastruktur und Produktcode zugleich. Ein Umzug in eine eigene `partial`-Datei wäre risikoarm, aber reine Textverschiebung; zurückgestellt, damit der Diff dieses Meilensteins prüfbar bleibt.

### Kartenansicht (`MapCanvasElement`)

- Bewährte Optimierungen vorhanden und erhalten: Gruppen je Kachel mit Begrenzung (`ObjectsByTile`, `MayBeVisible`), Marker als direkt geschriebene Quads statt Vektorkreise, ein Marker je 2 × 2 Pixel ab 2 m/px, Umrisse ab wenigen Pixeln nur als Hülle, Zeitmeldung langsamer Neuzeichnungen im Player-Log.
- `Refresh()` macht `ObjectsByTile` ungültig; neu gebaut wird erst beim nächsten Zeichnen von oben. In 3D ruft die Shell `Refresh()` jedes Bild, solange etwas ausgewählt ist; der Neubau fällt dort nicht an, weil 3D die Gruppen nicht nutzt.
- Klick-Picking von oben lief über alle Objekte und schlug für jedes den Umriss über den Pfad nach (siehe Leistung).

### Renderer

- **`MapObjectRenderer`:** GPU-Instancing je Objektart (`Graphics.RenderMeshInstanced`, 1.023 je Aufruf). Die Instanzen haben eine kartengroße `worldBounds`, Unity sortiert also nichts aus; gezeichnet wird jede Instanz der Karte in jedem Bild (Spandau: ~2,2 ms Hauptthread je Bild nur für die Aufrufe). Ziehen verschiebt einzelne Instanzen (`PreviewMove`), nach dem Loslassen baut `UpdatePlacements` alle Platzierungen neu.
- **`MapGroundRenderer`:** Gelände und Splines je Kachel, Masken-Cache mit Obergrenze (`MaximumMaskTiles`) und Abwurf nach letzter Sichtbarkeit; Splines und Gelände werden schon je Kachel aufgefrischt (`RefreshSplines`, `RefreshTerrain`). Geländelöcher dagegen kartenweit (siehe Befund D2).
- **Ressourcen:** Ersetzte Meshes, Loch-Texturen und Masken werden zerstört; `refreshTextures` hält die dekodierten CPU-Bilder der beim Straßenbau nachgeladenen Spline-Texturen bis zum Kartenwechsel (bis ~4 MB je Textur). Kein Leck, aber unnötiger Speicher (Befund M2).

## Leistung: Befunde

Alle Zahlen: Ryzen 7 3700X, RTX 5060 Ti, Release-Player 1.0-Stand, 1600 × 900; Einzelheiten und Verfahren in PerformanceBaseline.md.

| Nr. | Befund | Beleg (Spandau, 29.450 Objekte, 369 Kacheln) | Ursache |
|---|---|---|---|
| D1 | Löschen und Kachelwechsel eines Objekts stocken | Verweis-Index 634 ms je Aufbau (ohne Unity), 700 ms im Player | `OmsiReferenceIndex.Build` liest und zerlegt bei jedem Löschen/Verschieben über die Kachelgrenze alle Kacheln neu |
| D2 | Die erste Bearbeitung nach dem Laden stockt eine halbe Sekunde | `RefreshTerrainHoles` 542 ms beim ersten Mal | **Fehler:** Die beim Laden gespeicherte Loch-Signatur ließ den Spline-Teil weg, die beim Auffrischen nicht; sie waren nie gleich, also wurden alle Loch-Masken der Karte neu geschnitten und hochgeladen |
| D3 | Jede Objektbearbeitung kostet ein Bild von ~130 ms | `UpdatePlacements` 112–147 ms: Zählen 60 ms, Objekte 43–60 ms, Reihen 8–23 ms | Zwei Wörterbuch-Zugriffe je Objekt mit `StringComparer.OrdinalIgnoreCase` auf Asset-Pfaden; unter Mono (Scripting-Backend des Projekts) kostet jeder Zugriff ~1,5 µs |
| D4 | Jede Objektbearbeitung kostet zusätzlich ~26 ms | `RefreshTerrainHoles` im eingeschwungenen Zustand | Wie D3: `ObjectHole(path)` je Objekt über ein `OrdinalIgnoreCase`-Wörterbuch, dazu eine Platzierung je Objekt angelegt |
| D5 | Kachelliste nach jeder Bearbeitung ~15 ms | `UpdateBrowserEntries` | 369 `File.Exists` (eine je Kachel) |
| D6 | Ruhebild 8,4 ms (Grundorf 1,7 ms) | Probe: Objekt-Zeichnen 2,2 ms, Shell 0,1 ms, Boden 0 ms; Rest Unitys Rendering | Alle Instanzen jeder Art werden jedes Bild abgeschickt (keine Kachel-Sichtprüfung); Rest ist URP/UI-Toolkit-Arbeit ohne Entwicklungs-Build nicht weiter aufzuschlüsseln |
| D7 | Weit herausgezoomtes Verschieben erzeugt Müll | Grundorf 7 Garbage-Collections in 120 Bildern bei 8 m/px | `DrawObjects` legte je Neuzeichnen ein `bool[]` mit ~360.000 Einträgen und eine Liste an |
| D8 | Kleiner Müll je Bild in 3D | Code | `new[] { kind.CrossMaterial }` je Baumart und Bild |
| D9 | Klick ohne Marker in der Nähe durchsucht alle Objekte | Code; 29.450 `OrdinalIgnoreCase`-Zugriffe je Klick (~40 ms geschätzt) | Zweiter Durchgang in `TryPickObject` ohne Kachelgrenzen |
| D10 | Erste Platzierung und erster Pinselstrich stocken | erste Platzierung 524 ms, erster Strichschluss 477 ms; danach 10 bzw. 23 ms | Einmalige Kataloge (`OmsiIdAllocator.ForMap`, Profil-/Objektkataloge); nicht wiederkehrend |
| D11 | Laden erzeugt viel kurzlebigen Speicher | verwalteter Heap wächst auf ~580 MB, belegt danach ~130 MB | Hintergrund-Lesen der Modelle und Texturen; Mono gibt den Heap nicht zurück |
| M1 | Undo-Verlauf groß auf großen Karten | 519 KB je Platzierung, 200 Schritte | Volle Datei-Stände je Schritt (gewollt, ADR 011/Undo-Regel) |
| M2 | Dekodierte Spline-Texturen bleiben im Speicher | Code (`refreshTextures`) | Merkt sich, was schon gelesen wurde, samt Bilddaten |

Nicht auffällig (gemessen): Lesen der Karte (Spandau 0,53 s), 2D-Caches der Splines (4 ms), Reihen-Layout (6–7 ms), Pinsel-Tupfer (1,6 ms, p95 2,4 ms), Undo/Redo selbst (14–25 ms), 2D-Neuzeichnen (Spandau 0,2–5,4 ms je nach Maßstab), 3D-Picking (3,4 ms je Strahl).

## Risiken

| Bereich | Risiko | Gegenmaßnahme |
|---|---|---|
| Verweis-Index mit Zwischenspeicher | Ein veralteter Eintrag könnte ein Löschen erlauben, das einen Verweis bricht (Karte beschädigt) | Gültigkeit über Zähler der Arbeitskopie **und** Größe/Zeit der Datei; Test vergleicht nach jeder Änderung mit einem frisch gebauten Index; Gegenprobe ohne Zähler schlägt fehl |
| Pfadvergleiche | Abweichende Groß-/Kleinschreibung desselben Pfads | Jeder Text wird weiterhin ohne Groß-/Kleinschreibung nachgeschlagen; nur Wiederholungen desselben Textes gehen über `Ordinal` |
| Lochsignatur | Löcher nach dem Laden nicht neu geschnitten, obwohl nötig | Signatur beim Laden und Auffrischen jetzt von derselben Methode; Inhalt unverändert |
| Interaktive Darstellung | EditMode-Tests prüfen kein Rendering | Screenshots von Grundorf und Spandau vor/nach, Benchmark-Lauf, Nutzertest |

## Priorisierter Plan

1. **Korrektheit und Lecks:** D2 (Lochsignatur) – umgesetzt.
2. **Große Engpässe:** D1 (Verweis-Index zwischenspeichern), D3/D4 (Pfad-Nachschlagen) – umgesetzt.
3. **Unnötiges Auffrischen:** D9 (Klick über Kachelgruppen) – umgesetzt. D5 (Kachelliste) – offen: eine Auflistung der Arbeitskopie ist bei vielen Luftbildern nicht sicher schneller; besser wäre, nur die von einer Änderung betroffenen Kacheln aufzufrischen (Änderungen kennen ihre Kacheln).
4. **Allokationen:** D7, D8 – umgesetzt.
5. **Zuständigkeiten (braucht Zustimmung):**
   - `UpdatePlacements` inkrementell je Kachel: Frames je Kachel zwischenspeichern und nur geänderte Kacheln neu rechnen, Arrays in gleicher Reihenfolge zusammensetzen. Reduziert die verbleibenden ~50 ms auf Spandau weiter, ändert aber den Kern des Objekt-Renderers. – Umgesetzt am 2. November (`MapObjectRendererPlacements.cs`, RefactoringSummary „Stufe 5“).
   - Sichtprüfung je Kachel oder Art beim Instancing (D6): Instanzen nach Kacheln gruppieren und nur sichtbare abschicken. Ändert die Zeichenstruktur. – Umgesetzt am 2. November, je Kachel.
   - Shell: Undo-Verlauf und Auswahl als eigene Dienste; `Start()` in eine eigene `partial`-Datei.
   - IL2CPP statt Mono: würde alle Zahlen oben verbessern, ist aber eine Build-Änderung (C++-Werkzeugkette, längere Builds) und braucht Prüfung des ganzen Players.
6. **Benennung und Doku:** reservierte Assemblies in Architecture.md als solche kennzeichnen.

## Betroffene Dateien

| Datei | Änderung |
|---|---|
| `Scripts/OMSI/OmsiReferenceIndex.cs` | `OmsiReferenceCache`, Aufbau je Datei, Wiederverwendung unveränderter Dateien |
| `Scripts/OMSI/OmsiMapWorkspace.cs` | `Stamp(path)`: Speicherzähler, Größe, Zeit |
| `Scripts/OMSI/OmsiMapSession.cs` | Sitzung hält den Zwischenspeicher |
| `Scripts/World/MapGroundRenderer.cs` | gemeinsame Lochsignatur; nur Objekte mit Loch, Pfade einmal nachgeschlagen |
| `Scripts/World/MapObjectRenderer.cs` | Zählen und Art-Nachschlagen über `Ordinal`-Zwischenspeicher; Material-Array der Baum-Ebenen gehalten |
| `Scripts/UI/MapCanvasElement.cs` | Marker-Puffer wiederverwendet; Umrisse einmal je Pfadtext; Klick-Picking über Kachelgruppen; Zeichenzeit für den Benchmark |
| `Scripts/UI/EditorShellBenchmark.cs` (neu), `EditorShellBootstrap.cs` | Lauf `-benchmark` |
| `Tests/Editor/OmsiMapWorkspaceTests.cs` | Test des Index-Zwischenspeichers |
