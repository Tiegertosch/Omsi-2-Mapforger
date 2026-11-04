# Stabilisierung nach 1.0: Zusammenfassung (1. November 2026)

Zweig `refactor/post-1.0-stabilization` ab `main` = `6c9acdd`, Stufen 1–4 als `62a16b4` committet (2. November). Stufe 5, Teil 1 (Platzierungen je Kachel, Sichtprüfung) auf `perf/placements-and-culling` darüber; beides am 4. November nach `main` übernommen. Befunde und Plan: [RefactoringAudit.md](RefactoringAudit.md); Messverfahren und alle Zahlen: [PerformanceBaseline.md](PerformanceBaseline.md).

**Stand am 4. November:** Stufen 1–4 und Stufe 5 Teil 1 umgesetzt, automatisch geprüft (Abschnitt „Stufe 5“ unten) und vom Projektinhaber im Player getestet: auf Berlin-Spandau beim Zoomen und Drehen keine fehlenden oder aufploppenden Objekte, Bodentexturen laden schneller. Offen aus seinem Test: Hänger beim ersten Öffnen großer Karten (siehe PerformanceBaseline, „Laden großer Karten“).

## Umgesetzte Änderungen

| Nr. | Änderung | Motivation (Befund) | Dateien |
|---|---|---|---|
| 1 | Verweis-Index mit Zwischenspeicher je Datei (ADR 057): unveränderte Kacheln, Chrono- und Fahrplandateien werden beim nächsten Löschen oder Kachelwechsel nicht neu gelesen; Gültigkeit über einen Speicherzähler der Arbeitskopie sowie Größe und Zeit der Datei | D1: 0,63 s je Löschen auf Spandau | `OmsiReferenceIndex.cs`, `OmsiMapWorkspace.cs` (`Stamp`), `OmsiMapSession.cs` |
| 2 | Loch-Signatur beim Laden und Auffrischen aus derselben Methode | D2 (Fehler): erste Bearbeitung schnitt alle Geländelöcher neu (0,54 s auf Spandau) | `MapGroundRenderer.cs` |
| 3 | Geländelöcher: nur Objekte mit Loch werden gesammelt, jeder Pfadtext einmal nachgeschlagen | D4: 26 ms je Bearbeitung | `MapGroundRenderer.cs` |
| 4 | Objekt-Platzierungen: Zählen nach exaktem Pfadtext, dann je Text einmal ohne Groß-/Kleinschreibung; Arten über ein `Ordinal`-Wörterbuch | D3: 112–147 ms je Bearbeitung, davon 60 ms nur Zählen | `MapObjectRenderer.cs` |
| 5 | Material-Array der Baum-Ebenen je Art gehalten statt je Bild angelegt | D8 | `MapObjectRenderer.cs` |
| 6 | Marker-Liste und Zellraster der Kartenansicht wiederverwendet | D7: 7 Garbage-Collections in 120 Bildern (Grundorf, 8 m/px) | `MapCanvasElement.cs` |
| 7 | Umrisse in `ObjectsByTile` einmal je Pfadtext nachgeschlagen; Klick-Picking über Umrisse nutzt die Kachelgruppen samt Begrenzung | D9 | `MapCanvasElement.cs` |
| 8 | Messlauf `-benchmark` im Player | Phase E: wiederholbare Messung | `EditorShellBenchmark.cs` (neu), `EditorShellBootstrap.cs` (Aufruf, Öffnungszeit), `MapCanvasElement.cs` (`LastDrawMilliseconds`, `DrawCount`) |
| 9 | Regressionstest des Index-Zwischenspeichers | Absicherung von 1 | `OmsiMapWorkspaceTests.cs` |
| 10 | Doku: Audit, Messgrundlage, diese Zusammenfassung, ADR 057, Architecture (reservierte Assemblies, Leistung), DevelopmentSetup (Messlauf, Validierung), CLAUDE.md | Phase F | `Documentation/`, `CLAUDE.md` |

Nicht geändert: Dateiformate, `.omproj`, Arbeitskopie-Layout, Undo-Semantik und -Speicher, Abhängigkeiten, Unity-Version, Scripting-Backend, Renderer-Aufbau, Kamerasteuerung.

## Stufe 5, Teil 1: Platzierungen je Kachel und Sichtprüfung (2. November)

Vom Projektinhaber am 2. November freigegeben (Audit, Stufe 5, erste beiden Punkte).

| Nr. | Änderung | Motivation (Befund) | Dateien |
|---|---|---|---|
| 11 | Platzierungen je Kachel: Jede Kachel hält die Instanzen, die ihre Objekte, angehängten Objekte und Reihen je Art beitragen, samt einer Beschreibung, woraus sie gebaut wurden (Datensätze nach Wert und Referenz, kein Hash). Neu gebaut wird eine Kachel nur, wenn sich diese Beschreibung ändert, das Gelände unter ihren Objekten neu gelesen wurde, eine Art eintrifft, auf die sie wartet, oder eine Ziehvorschau sie verändert hat (`InvalidateTile`). Je Art werden die Segmente der Kacheln in Kartenreihenfolge zusammengesetzt, Objekte vor Reihen wie bisher. Reihen werden nur nach Änderungen an Splines oder Reihen neu gelegt | Audit, Stufe 5: `UpdatePlacements` baute nach jeder Bearbeitung alle Platzierungen neu (~60 ms auf Spandau) | `MapObjectRendererPlacements.cs` (neu), `MapObjectRenderer.cs` |
| 12 | Sichtprüfung je Kachel beim Instancing: Jede Kachel hat je eine Begrenzung für Objekte und Reihen (Kugel um jedes Modell, Bäume samt Krone). Abgeschickt werden nur Instanzen von Kacheln im Sichtkegel der Kamera; neu zusammengesetzt wird eine Art nur, wenn eine ihrer Kacheln ins Bild kommt oder es verlässt. Die Objekte werfen keine Schatten, Kacheln außerhalb können also nichts ins Bild tragen | D6: alle Instanzen jeder Art wurden jedes Bild abgeschickt | `MapObjectRendererPlacements.cs` |
| 13 | Messlauf: Das Auffrischen nach einer Bearbeitung baut die bearbeitete Kachel neu wie eine echte Bearbeitung | Vergleichbare Zahlen | `EditorShellBenchmark.cs` |

Prüfungen (2. November, ausgewertet am 4. November):

| Prüfung | Ergebnis |
|---|---|
| Player-Build | Erfolgreich; der geprüfte Player ist aus genau dem Stand im Repo gebaut (Quelldateien der Projektkopie bytegleich) |
| Darstellung | Sechs Ansichten wie oben gegen den Player von `62a16b4`: Abweichungen nur in der Statuszeile (Ladezeiten, y 887–897) |
| Bearbeitungen | Acht Fälle, je in einem Wegwerf-Projekt mit beiden Playern: Objekt über die Kachelkante verschieben (von oben und in 3D), drehen, löschen, Löschen rückgängig, Art platzieren, die Grundorf noch nicht nutzt, Reihe legen – pixelgleich. Gelände anheben weicht ab (0,08 %); derselbe Fall weicht aber auch beim alten Player zwischen zwei Läufen ab (0,55 %), weil der Pinsel in Echtzeit wirkt und die Hebung von der Bildrate abhängt |
| Statusmeldungen | Player-Logs beider Player gleich bis auf Pfade |

Ergebnisse (Spandau, A/B mit je zwei Läufen, Einzelheiten in PerformanceBaseline.md):

- Bild von oben, still und beim Verschieben bei 0,1–0,4 m/px: ~7,5–9 → ~2,5–4,3 ms; bei 2 m/px ~12–15 → ~10 ms; bei 8 m/px unverändert (dort ist die ganze Karte im Bild, die Zeit geht ins Neuzeichnen der Vektorebene).
- 3D: aus 300 m ~12 → ~9 ms, aus 1.500 m ~10–11 → ~9–10 ms.
- Nach einer Objektbearbeitung: Das Auffrischen selbst bleibt bei ~85 ms (die Platzierungen baut der Renderer erst im nächsten Bild); das Bild danach sinkt von ~70 auf ~8 ms. Eine Bearbeitung kostet damit ~90 statt ~155 ms.
- Grundorf, Laden, Speicher, Picking: unverändert innerhalb der Streuung.

## Kompatibilitätsprüfungen

| Prüfung | Ergebnis | Wie |
|---|---|---|
| Verlustfreies Dateimodell | Bestanden: Grundorf 21, Berlin-Spandau 583 Dateien (Kacheln, Chrono-Kacheln, `global.cfg`) bytegleich zurückgeschrieben | Prüfprogramm ohne Unity auf `Editor.OMSI.dll` (Scratchpad, `Verify.cs`) |
| Projekt und Arbeitskopie | Bestanden: Wegwerf-Projekt geöffnet, Objekt verschoben und gedreht, platziert, gelöscht, Löschen rückgängig; Karte neu gelesen mit denselben Objekten und Werten; genau zwei bearbeitete Dateien (`global.cfg`, Kachel) | dasselbe |
| Export | Bestanden: Export enthält jede Originaldatei; abweichend nur die bearbeiteten Dateien und `mapforge-export.txt` | dasselbe, SHA-256 aller Dateien |
| Originalkarte unberührt | Bestanden: Grundorf 164, Berlin-Spandau 2.440 Dateien vor und nach dem Lauf gleich (SHA-256); `maps` enthält keine neuen Ordner | dasselbe |
| Verweise beim Löschen | Bestanden: Index mit Zwischenspeicher gleicht nach Platzieren, Rückgängig, zwei gleich langen Speichervorgängen mit gleicher Dateizeit und einer Änderung außerhalb des Editors einem frisch gebauten; ein Löschen mit Verweis wird weiter verweigert | `ReferenceIndexOfASessionFollowsEveryChangeOfItsFiles`; Gegenprobe ohne Speicherzähler schlägt fehl |
| EditMode-Tests | 309 von 309 grün (308 bisherige, 1 neuer) | schneller csc-Läufer ohne Unity; `MapforgeEditorSettingsTests` braucht Unity und lief nicht mit |
| Player-Build | Erfolgreich, keine Compiler-Warnungen, keine neuen Player-Dateien (nur Skripte geändert) | `WindowsPlayerBuild.Build` auf der Projektkopie |
| Darstellung | Pixelgleich mit dem 1.0-Player in sechs Ansichten: Grundorf von oben mit und ohne Texturen und in 3D, Berlin-Spandau von oben, weit draußen (6 m/px, Marker über das Zellraster) und in 3D | Screenshots beider Player, Vergleich je Pixel (größte Abweichung 0) |
| Laufzeitfehler | Keine Ausnahmen in den Player-Logs aller Mess- und Screenshot-Läufe | Player-Logs |

## Ergebnisse

Gemessene Verbesserungen (Einzelheiten und Streuung in PerformanceBaseline.md):

- Löschen und Kachelwechsel auf Berlin-Spandau: Verweis-Index 634 → 80 ms (ohne Unity, ab dem zweiten Aufbau einer Sitzung), Löschen gesamt 594 → 117 ms.
- Erste Objektbearbeitung nach dem Laden: Geländelöcher 542 → 13 ms (Fehler behoben).
- Jede Objektbearbeitung auf Spandau: `UpdatePlacements` ~125 → ~60 ms, Geländelöcher 26 → 11–13 ms, das Bild nach dem Auffrischen 134 → 79 ms.
- Weit herausgezoomtes Verschieben (8 m/px): Garbage-Collections je 120 Bilder in Grundorf 6 → 0, in Spandau 3–5 → 0–1.
- Im A/B-Vergleich beider Player (je zwei Läufe, Spandau mit Projekt): Auffrischen nach einer Objektbearbeitung im Mittel 159–186 → 86–101 ms, das Bild danach 141–173 → 77–78 ms, Verweis-Index im Mittel aus drei Aufbauten 692–763 → 339–340 ms.
- Unverändert innerhalb der Streuung: Bildzeiten still, beim Verschieben und in 3D, Lade- und Öffnungszeiten, Speicher.

## Einordnung der Belege

- **Gemessen:** die Punkte unter „Ergebnisse“ (Messlauf, Zeitmessung einzelner Methoden in der Projektkopie, Unity-freies Messprogramm).
- **Nur durch Code-Lektüre gestützt:** kein Müll mehr durch das Material-Array in 3D (Änderung 5; der Release-Player liefert keine Zahl je Bild); schnelleres Klick-Picking über Umrisse (Änderung 7; in der Benchmark-Szene nicht gemessen, geschätzt ~40 ms je Klick auf Spandau weniger).
- **Annahmen, nicht geprüft:** Verhalten auf langsameren Rechnern und Festplatten (nur ein Rechner gemessen); andere Karten als Grundorf und Berlin-Spandau.
- **Manuell zu prüfen (Nutzertest):** Bedienen im Player auf beiden Karten: Objekte wählen (Klick auf Umrisse ohne Marker), ziehen, drehen, platzieren, löschen, Rückgängig/Wiederholen, über Kachelgrenzen verschieben, Straße bauen und löschen; Geländelöcher (Unterführungen) nach dem Verschieben eines Objekts mit Loch; Ergebnis in openOMSI. Für Stufe 5 zusätzlich: am Bildrand und in 3D schnell drehen und zoomen, ohne dass Objekte fehlen oder aufploppen; Reihen an Straßen ändern; Gelände unter Objekten anheben (Objekte stehen danach auf dem neuen Gelände); eine Art platzieren, die die Karte noch nicht nutzt.

## Verbleibende Risiken

- Der Index-Zwischenspeicher verlässt sich darauf, dass Dateien der Arbeitskopie über `OmsiMapWorkspace` geschrieben werden oder ihre Größe oder Zeit ändern (ADR 057). Der erste Aufbau einer Sitzung bleibt so lang wie bisher (Spandau ~0,7 s im Player).
- Platzierungen je Kachel (Stufe 5): Ändert sich etwas, wovon eine Kachel abhängt, ohne in ihrer Beschreibung zu stehen, bliebe sie veraltet. Beschrieben sind ihre Datensätze, die Reihenanfänge, das Gelände unter ihren Objekten und fehlende Arten; ein Kartenwechsel verwirft alles. Abgesichert durch die acht Bearbeitungsfälle oben.
- Sichtprüfung (Stufe 5): Ein Modell, das weit über seine Begrenzung hinausreicht, könnte am Bildrand fehlen; die Begrenzung ist deshalb eine Kugel um das ganze Modell.
- Das Auffrischen nach einer Bearbeitung kostet auf Spandau weiter ~85 ms (Kachelliste, Umrisse, Geländelöcher, Auswahl); siehe D5.

## Empfehlungen

1. ~~Nutzertest, Commit und Übernahme nach `main`~~ – erledigt am 4. November. Als Nächstes: Laden großer Karten (Karte im Hintergrund öffnen, weniger Müll beim Modell-Lesen, Objekte nahe dem Ausschnitt zuerst).
2. ~~Inkrementelles `UpdatePlacements` je Kachel~~ – umgesetzt (Stufe 5, Teil 1).
3. Kachelliste und andere kartenweite Auffrischungen auf die Kacheln einer Änderung beschränken (D5) – jetzt der größte Posten bei Bearbeitungen.
4. ~~Sichtprüfung je Kachel beim Instancing (D6)~~ – umgesetzt (Stufe 5, Teil 1); einen Entwicklungs-Build mit dem Unity Profiler messen bleibt offen.
5. IL2CPP prüfen: betrifft alle Mono-Kosten (Pfadvergleiche, Matrizen), braucht aber eine C++-Werkzeugkette im Build.
6. Den Verweis-Index nach dem Öffnen eines Projekts im Hintergrund vorwärmen, damit auch das erste Löschen schnell ist; braucht Synchronisierung mit der Sitzung.
