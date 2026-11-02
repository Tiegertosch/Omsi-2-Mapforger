# Stabilisierung nach 1.0: Zusammenfassung (1. November 2026)

Zweig `refactor/post-1.0-stabilization` ab `main` = `6c9acdd`. Befunde und Plan: [RefactoringAudit.md](RefactoringAudit.md); Messverfahren und alle Zahlen: [PerformanceBaseline.md](PerformanceBaseline.md). Nichts ist committet; das entscheidet der Projektinhaber.

**Stand am Ende des 1. November:** erster Teil (Stufen 1–4 des Plans) umgesetzt und automatisch geprüft. Der Projektinhaber testet zuerst selbst im Player (Liste unter „Manuell zu prüfen“); danach Commit und Entscheidung über Stufe 5.

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
- **Manuell zu prüfen (Nutzertest):** Bedienen im Player auf beiden Karten: Objekte wählen (Klick auf Umrisse ohne Marker), ziehen, drehen, platzieren, löschen, Rückgängig/Wiederholen, über Kachelgrenzen verschieben, Straße bauen und löschen; Geländelöcher (Unterführungen) nach dem Verschieben eines Objekts mit Loch; Ergebnis in openOMSI.

## Verbleibende Risiken

- Der Index-Zwischenspeicher verlässt sich darauf, dass Dateien der Arbeitskopie über `OmsiMapWorkspace` geschrieben werden oder ihre Größe oder Zeit ändern (ADR 057). Der erste Aufbau einer Sitzung bleibt so lang wie bisher (Spandau ~0,7 s im Player).
- `UpdatePlacements` baut nach jeder Bearbeitung weiter alle Platzierungen neu (~60 ms auf Spandau); ein inkrementeller Neubau je Kachel ist geplant, braucht aber Zustimmung (Audit, Stufe 5).
- Das Ruhebild auf Spandau kostet weiter ~8 ms Hauptthread, überwiegend Unitys Rendering; ohne Entwicklungs-Build nicht weiter aufzuschlüsseln.

## Empfehlungen

1. Nutzertest wie oben, dann Commit dieses Zweigs.
2. Inkrementelles `UpdatePlacements` je Kachel (Audit, Stufe 5) – größter verbliebener Posten bei Bearbeitungen.
3. Kachelliste und andere kartenweite Auffrischungen auf die Kacheln einer Änderung beschränken (D5).
4. Sichtprüfung je Kachel beim Instancing (D6), danach einen Entwicklungs-Build mit dem Unity Profiler messen.
5. IL2CPP prüfen: betrifft alle Mono-Kosten (Pfadvergleiche, Matrizen), braucht aber eine C++-Werkzeugkette im Build.
6. Den Verweis-Index nach dem Öffnen eines Projekts im Hintergrund vorwärmen, damit auch das erste Löschen schnell ist; braucht Synchronisierung mit der Sitzung.
