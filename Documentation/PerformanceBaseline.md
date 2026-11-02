# Leistung: Messgrundlage und Ergebnisse (1. November 2026)

Gehört zu [RefactoringAudit.md](RefactoringAudit.md) und [RefactoringSummary.md](RefactoringSummary.md). Alle Zahlen sind gemessen; wo etwas nicht gemessen werden konnte, steht es unter „Grenzen“.

## Bedingungen

| | |
|---|---|
| Rechner | AMD Ryzen 7 3700X (8 Kerne, 16 Threads), 32 GB RAM, NVIDIA GeForce RTX 5060 Ti 16 GB, Direct3D 12, Windows 11 Pro |
| Player | Release-Build (`WindowsPlayerBuild.Build`, kein Entwicklungs-Build), Unity 6000.6.6f1, URP 17.6.0, Scripting-Backend Mono, Fenster 1.600 × 900, VSync für die Messung aus |
| Vergleich | **1.0**: Code von `main` (`6c9acdd`) plus nur dem Messlauf; **neu**: Zweig `refactor/post-1.0-stabilization`. Beide Player je zweimal abwechselnd nach einem Aufwärmlauf, damit der Dateicache von Windows gefüllt ist |
| Karten | Grundorf (19 Kacheln, 1.095 Objekte, 60 Splines) und Berlin-Spandau (369 Kacheln, 29.450 Objekte, 2.486 Splines, 9.747 Objekte in Reihen, 1.447 angehängte Objekte, `[worldcoordinates]`) aus der OMSI-2-Installation; nicht weitergebbar, daher nur lokal |
| Ohne Unity | .NET Framework 4.8 mit `Editor.OMSI.dll` aus dem schnellen csc-Läufer, Mittelwert aus drei Läufen, wo nicht anders genannt |

## Szenarien und Verfahren

1. **Logik ohne Unity** (Messprogramm im Scratchpad, `Bench.cs`): Karte lesen, 2D-Caches der Splines, Reihen-Layout, lineare Objektsuche, Verweis-Index, Objekt verschieben/platzieren/löschen mit Undo/Redo und Größe des Undo-Schritts, Pinselstrich (Beginn, 30 Tupfer r = 25 m, Ende mit Anpassen an Straßen), Export. Läuft auf einem Wegwerf-Projekt im Scratchpad.
2. **Player** (`-benchmark bericht.txt`, im Repo, siehe DevelopmentSetup „Leistung messen“): Startzeit bis zur offenen Karte, Öffnen, Nachladen von Boden, Objekten und Umrissen; je 120 Bilder still und beim Verschieben von oben bei 0,1/0,4/2/8 m/px um die Kachel aus `-view`; je 120 Bilder Kreisen in 3D aus 300 und 1.500 m; 3D-Picking über 25 Strahlen; mit Projekt zehnmal das Auffrischen nach einer Objektbearbeitung (`RefreshAfterChange`, ohne Dateiänderung) samt zweitem Bild danach und dreimal der Verweis-Index.
3. **Aufschlüsselung** (nur in der Projektkopie, nicht im Repo): eine Klasse `Probe` misst einzelne Methoden (Renderer-`Update`/`LateUpdate`, `UpdatePlacements` in vier Teilen, die Schritte von `RefreshAfterChange`) und schreibt Mittelwerte ins Player-Log.
4. **Darstellung**: sechs Screenshots je Player (Grundorf von oben mit/ohne Texturen und in 3D, Spandau von oben, weit draußen bei 6 m/px, in 3D), pixelweise verglichen.

Aufruf (Spandau mit Wegwerf-Projekt, `Bench.omproj` mit `globalConfigPath` auf die Karte):

```bash
./Builds/Windows/OMSIMapWorks.exe -screen-width 1600 -screen-height 900 -screen-fullscreen 0 \
  -openProject "C:/temp/bench/Bench.omproj" -view 2402 11279 200 200 0.4 -benchmark "C:/temp/spandau.txt" -quit -logFile "C:/temp/spandau.log"
```

## Ergebnisse ohne Unity

| Wert | Grundorf 1.0 | Grundorf neu | Spandau 1.0 | Spandau neu |
|---|---|---|---|---|
| Karte lesen (warm) | 18 ms | 18 ms | 570 ms | 537 ms |
| Zusammenfassung im Speicher | 0,4 MB | 0,4 MB | 9,8 MB | 9,8 MB |
| 2D-Caches der Splines | 0,1 ms | 0,1 ms | 4,3 ms | 4,5 ms |
| Reihen-Layout (alle Reihen) | 0,1 ms | 0,1 ms | 6,4 ms | 7,1 ms |
| Verweis-Index (Median aus 3, erster Aufbau kalt) | 18 ms | 4 ms | 634 ms | 81 ms |
| Objekt verschieben (Bestätigen) | 13 ms | 14 ms | 18 ms | 19 ms |
| Objekt platzieren (erstes Mal, liest alle IDs) | 35 ms | 37 ms | 525 ms | 521 ms |
| Objekt platzieren (zweites Mal) | – | 6 ms | 10 ms | 13 ms |
| Undo-Schritt einer Platzierung | 180 KB | 180 KB | 519 KB | 519 KB |
| Undo / Redo Platzierung | 22 / 13 ms | 14 / 15 ms | 25 / 15 ms | 23 / 14 ms |
| Objekt löschen (mit Verweis-Index) | 36 ms | 27 ms | 594 ms | 117 ms |
| Pinsel-Tupfer r = 25 m (Median / p95) | 1,7 / 3,2 ms | 1,7 / 2,2 ms | 1,6 / 2,4 ms | 1,6 / 2,2 ms |
| Strichschluss (erstes Mal, lädt Kataloge) | 35 ms | 35 ms | 477 ms | 454 ms |
| Strichschluss (zweites Mal) | – | 21 ms | 23 ms | 21 ms |
| Export | 1,0 s | 0,2 s | 9,7 s | 2,0 s |

Der Export-Code ist unverändert; seine Zeit hängt am Dateicache (kalt beim ersten, warm beim zweiten Lauf) und ist keine Verbesserung.

## Ergebnisse im Player (A/B, je zwei Läufe)

Bildzeit Mittel / p95 in ms; Neuzeichnen von oben Mittel in ms; GC = Garbage-Collections in 120 Bildern.

### Berlin-Spandau mit Projekt

| Wert | 1.0 Lauf 1 | 1.0 Lauf 2 | neu Lauf 1 | neu Lauf 2 |
|---|---|---|---|---|
| Öffnen | 1,28 s | 1,26 s | 1,27 s | 1,27 s |
| Boden / Objekte / Umrisse geladen nach | 4,1 / 8,4 / 4,5 s | 4,1 / 8,0 / 4,5 s | 4,1 / 8,1 / 4,5 s | 4,0 / 7,9 / 4,4 s |
| **Auffrischen nach Bearbeitung, Mittel / erstes Mal** | 186 / 1.193 ms | 159 / 1.074 ms | 101 / 658 ms | 86 / 543 ms |
| **Zweites Bild danach, Mittel / max** | 173 / 210 ms | 141 / 158 ms | 78 / 109 ms | 77 / 102 ms |
| **Verweis-Index, Mittel aus 3 / erster (kalt)** | 763 / 849 ms | 692 / 702 ms | 340 / 822 ms | 339 / 810 ms |
| 3D-Picking je Strahl, Mittel | 4,1 ms | 3,4 ms | 3,9 ms | 3,4 ms |
| still, 0,4 m/px | 9,5 / 12,5 | 8,8 / 10,7 | 8,8 / 10,5 | 8,3 / 10,4 |
| verschieben, 0,1 m/px | 9,2 / 11,7 | 9,1 / 11,3 | 9,7 / 12,6 | 8,9 / 11,0 |
| verschieben, 0,4 m/px | 9,4 / 10,8 | 9,7 / 11,0 | 9,6 / 11,5 | 9,5 / 11,0 |
| verschieben, 2 m/px | 14,7 / 20,3 | 13,7 / 17,0 | 16,0 / 18,9 | 12,6 / 14,6 |
| verschieben, 8 m/px (Neuzeichnen, GC) | 16,8 / 19,9 (5,5 ms, 4) | 15,9 / 19,9 (5,6 ms, 5) | 15,7 / 17,6 (5,4 ms, 1) | 15,6 / 18,4 (5,4 ms, 1) |
| 3D, 300 m | 14,2 / 18,5 | 13,4 / 16,0 | 12,9 / 14,8 | 12,8 / 15,1 |
| 3D, 1.500 m | 13,7 / 17,3 | 11,8 / 14,0 | 11,7 / 13,3 | 11,8 / 14,6 |
| Speicher zuletzt: verwaltet / Heap / Unity | 134 / 567 / 807 MB | 129 / 599 / 797 MB | 146 / 565 / 800 MB | 138 / 583 / 799 MB |

Der erste Index-Aufbau einer Sitzung liest weiter alles (~0,8 s); die beiden folgenden brauchen bei „neu“ je ~0,1 s statt ~0,7 s. Das erste Auffrischen enthält einmalig `RefitTerrainAtJunctions` (~0,5–0,6 s, Kataloge und Zustand der Kreuzungsgelände), bei 1.0 zusätzlich das Neuschneiden aller Geländelöcher.

### Berlin-Spandau ohne Projekt

| Wert | 1.0 Lauf 1 | 1.0 Lauf 2 | neu Lauf 1 | neu Lauf 2 |
|---|---|---|---|---|
| Öffnen | 1,12 s | 1,09 s | 1,08 s | 1,25 s |
| Boden / Objekte / Umrisse geladen nach | 3,8 / 7,9 / 4,4 s | 3,8 / 7,9 / 4,3 s | 4,0 / 8,2 / 4,7 s | 4,1 / 8,0 / 4,6 s |
| still, 0,4 m/px | 8,7 / 10,3 | 8,5 / 10,6 | 8,7 / 10,7 | 8,8 / 10,5 |
| verschieben, 0,4 m/px | 9,4 / 10,9 | 9,4 / 11,0 | 9,7 / 11,3 | 10,0 / 12,3 |
| verschieben, 2 m/px | 12,8 / 15,3 | 12,9 / 15,7 | 12,5 / 14,7 | 12,7 / 14,9 |
| verschieben, 8 m/px (Neuzeichnen, GC) | 15,4 / 18,9 (5,3 ms, 4) | 15,3 / 18,1 (5,3 ms, 3) | 15,4 / 18,3 (5,2 ms, 0) | 16,0 / 19,2 (5,4 ms, 0) |
| 3D, 300 m | 12,4 / 14,2 | 12,4 / 14,3 | 12,1 / 13,7 | 12,1 / 14,2 |
| 3D, 1.500 m | 11,1 / 13,1 | 11,0 / 12,6 | 10,9 / 12,7 | 10,7 / 12,4 |

### Grundorf

| Wert | 1.0 Lauf 1 | 1.0 Lauf 2 | neu Lauf 1 | neu Lauf 2 |
|---|---|---|---|---|
| Öffnen | 0,30 s | 0,29 s | 0,30 s | 0,29 s |
| Boden / Objekte / Umrisse geladen nach | 1,4 / 1,9 / 1,3 s | 1,4 / 1,9 / 1,3 s | 1,7 / 2,0 / 1,4 s | 1,5 / 1,9 / 1,4 s |
| still, 0,4 m/px | 1,6 / 2,4 | 1,5 / 2,4 | 1,9 / 2,6 | 1,5 / 2,1 |
| verschieben, 8 m/px (GC) | 1,9 / 3,2 (6) | 2,0 / 3,0 (6) | 2,3 / 3,0 (0) | 1,8 / 2,5 (0) |
| 3D, 300 m | 4,0 / 5,5 | 3,3 / 4,2 | 3,5 / 4,6 | 3,2 / 3,7 |

Lesart: Bild-, Lade- und Neuzeichenzeiten liegen bei beiden Playern innerhalb der Streuung zwischen zwei Läufen desselben Players (bis ~2 ms je Bild, bis ~0,3 s beim Laden); die Änderungen zielten nicht darauf. Messbar besser sind die Bearbeitungswege und die Garbage-Collections beim Verschieben weit draußen.

## Aufschlüsselung (Spandau mit Projekt, Probe)

| Teil | 1.0 | neu |
|---|---|---|
| `UpdatePlacements` gesamt | 112–147 ms | 57–65 ms (erstes Mal 94 ms) |
| davon Reihen-Layout (nur wenn neu nötig) | 8–23 ms | 8–13 ms |
| davon Zählen der Arten | 60–67 ms | 21–23 ms |
| davon Objekte und Anhängsel | 43–60 ms | 30–33 ms |
| davon Reihen-Instanzen | 8–9 ms | 3–4 ms |
| `RefreshTerrainHoles` erstes Mal / danach | 542 / 26 ms | 13 / 11–13 ms |
| Kachelliste (`UpdateBrowserEntry` für alle) | 14–24 ms | 16–19 ms (unverändert) |
| `ApplySelection` | 7–18 ms | 8–9 ms (unverändert) |
| je Bild still: Objekte zeichnen (`LateUpdate`) | 2,1–2,2 ms | unverändert |
| je Bild still: Boden `Update` / Shell `LateUpdate` | < 0,2 ms | unverändert |

## Verbleibende Engpässe

1. `UpdatePlacements` baut nach jeder Bearbeitung alle Platzierungen neu (~60 ms auf Spandau). Nächster Schritt: je Kachel zwischenspeichern (Audit, Stufe 5).
2. Erster Verweis-Index einer Sitzung (~0,8 s) und einmalige Kataloge beim ersten Platzieren, ersten Pinselstrich und ersten Auffrischen (je ~0,5 s auf Spandau).
3. Ruhebild Spandau ~8,5 ms Hauptthread, davon ~2,2 ms Instancing-Aufrufe für alle Instanzen der Karte, der Rest Unitys Rendering und UI Toolkit.
4. Verschieben bei 8 m/px auf Spandau 15–16 ms je Bild, davon ~5,5 ms Neuzeichnen der Vektorebene.
5. Laden erzeugt ~450 MB kurzlebigen verwalteten Speicher; der Mono-Heap bleibt danach bei ~570–600 MB.

## Grenzen

- Kein Unity Profiler, Profile Analyzer oder Memory Profiler: Der Projektinhaber hatte den Editor offen, gemessen wurde mit Release-Playern aus einer Projektkopie. Render-Thread-Zeit, GC-Allokation je Bild und Grafikspeicher liefert ein Release-Player nicht (im Bericht „n/a“).
- Ein Rechner, eine Auflösung. Stadtückheim, ein dichtes Szenario mit vielen gleichen Objekten jenseits von Spandau, eine große erzeugte Kreuzung, Straßen-Vorschau beim Ziehen, Spurverbindungen, Markierungen und Kartenwechsel sind nicht als Szenario im Messlauf; die Straßen- und Kreuzungswege wurden in diesem Meilenstein nicht geändert. Verfahren für sie: im Player mit `-road`/`-testJunction`/`-markings` (DevelopmentSetup) und der Zeitmessung `Probe` einer Projektkopie wie oben.
- Kalte Dateicaches verzerren Ladezeiten stark (Grundorf einmal 27 statt 1,4 s); verglichen wurde nur nach einem Aufwärmlauf.
