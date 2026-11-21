# UI-Rework: nächster Schritt – M0 und M1 (12. November 2026)

Empfehlung aus [ui-rework-roadmap.md](ui-rework-roadmap.md). **Stand 12. November 2026: M0 und M1 sind abgeschlossen, vom Nutzer getestet und committet (ADR 098, „M1“). Dieses Dokument bleibt als Plan und Nachweis; M2 ist umgesetzt und committet; M3 ist abgeschlossen, vom Nutzer getestet (13. November 2026) und committet; der nächste Meilenstein war M4; M4 und M5 sind inzwischen abgeschlossen, Bestandsaufnahme und Plan für M6 stehen unten (Abschnitt „M6“, 21. November 2026), Stand in [ui-rework-roadmap.md](ui-rework-roadmap.md). **M6 Schritte 0 und 1 vom Projektinhaber am 21. November 2026 begutachtet („Kontraständerungen sehen alle gut aus“), unkommittiert; nächster Schritt: M6 Schritt 2.**

## Empfehlung

Zuerst **M0 – Arbeitsstand sichern** (kurz), unmittelbar danach **M1 – Fehler der neuen Einstiege beheben**.

## Begründung

- **Warum jetzt:** Das Rework hat den Rechtsklick zum Hauptweg gemacht. An diesem Weg und an den Tasten daneben hängen sechs bestätigte Fehler und ein Risiko (Audit 5.1–5.5, 5.8, 5.9), einer davon am Player belegt (Objektauswahl ohne Projekt wirft eine Ausnahme), zwei können Arbeit kosten („Revert tile edits“ ohne Rückfrage leert die Undo-Historie; „Paste here“ kann nach einem Einfügen mit Zufallsdrehung mit einer Ausnahme abbrechen oder alte Drehungen wiederverwenden). Jeder weitere Menüeintrag (Reihen, Kreuzungen, Ampeln) würde auf diesen Fehlern aufbauen.
- **Welche Probleme es löst:** Audit, Abschnitt 5, Punkte 1–5, 8 und 9.
- **Voraussetzungen, die erfüllt sind:** Die Fehlerstellen sind mit Datei und Zeile bekannt; die betroffenen Befehle existieren und sind in der OMSI-Schicht getestet; für den wichtigsten Fehler gibt es eine Reproduktion in einem Befehl.
- **Voraussetzung, die fehlt:** 97 uncommittete Dateien (Umbau auf `ToolCatalog`), in dieser Analyse weder kompiliert noch getestet. Deshalb M0 davor.
- **Was davon abhängt:** M2 (Statuszeile) und M3 (Gerüst) setzen auf den Ausstiegen aus M1 auf; M5 (weitere Menüeinträge) auf einem fehlerfreien Menü.
- **Risiken:** `SetTool` und `PresentSummary` wirken auf jedes Werkzeug. M1 ergänzt dort nur Ausstiege und baut nichts um. Der Fehler ohne Projekt liegt auch auf `main` (Commit `bbc8304`, in keinem Release-Tag) – ob er dort getrennt behoben werden soll, entscheidet der Nutzer.

## M0 – Arbeitsplan

| Schritt | Wer | Was | Ergebnis |
|---|---|---|---|
| 0.1 | testing | Projektkopie im Scratchpad (`Assets`, `Packages`, `ProjectSettings`, `Library`); Batch-Lauf `-runTests -testPlatform EditMode` | Compiler- und Testergebnis des Arbeitsbaums |
| 0.2 | testing | `dotnet run --project Tools/LocaleCheck -- locales --strict` | 0 Fehler oder Liste |
| 0.3 | Hauptsitzung | Player-Build auf der Kopie (im Hintergrund, vorher ansagen); Screenshots Objekte, Straße, Gelände, Markierungen bei 1100 und 1600 px, en-US und de-DE, Grundorf und Spandau | Bilder, selbst angesehen |
| 0.4 | Nutzer | Test der offenen Punkte aus ProjectStatus.md (Gelände-Fenster, 210-px-Menüs, Kopfzeile, Kacheln, Straßen-Optionen, Markierungen) | Bestätigung oder Anmerkungen |
| 0.5 | Hauptsitzung | Validierungsstand in DevelopmentSetup.md; Commit nach Freigabe | sauberer Arbeitsbaum |

## M1 – Arbeitsplan

**Dateibesitz:** `EditorShellBootstrap.cs` hat genau einen Bearbeiter (backend). Der Frontend-Agent arbeitet nur in `EditorShellContextMenu.cs`, `EditorShell.uss` und den Sprachdateien. Reihenfolge: 1.1 (backend, auf `main`) → 1.2–1.4 (backend) → 1.6 (frontend) → 1.7 (testing) → 1.8 (reviewer).

| Schritt | Wer | Aufgabe | Fundstelle | Vertrag |
|---|---|---|---|---|
| 1.1 | backend | Haltestellen-Abfrage ohne Arbeitskopie | `OmsiMapSessionTrips.cs:93–122`, `EditorShellBusStop.cs:20`, `EditorShellContextMenu.cs:175` | `IsBusStop` und `BusStopName` lesen aus der Quelle der Sitzung und werfen ohne Projekt nicht; ein Objekt, das in `Busstops.cfg` steht, wird auch ohne Projekt als Haltestelle erkannt (wenn die Quelle das hergibt), sonst `false`/`null` |
| 1.2 | backend | Einfügen über einen Weg | `EditorShellClipboard.cs:90–176`, `EditorShellRandomHeading.cs:50` | Die Vorbereitung von `BeginPaste` (Drehversätze neu würfeln oder leeren) wird eine eigene Methode; „Paste here“ ruft sie vor `PlaceClipboardAt`. `CopySelection` leert `pasteHeadingOffsets`. Kein Aufruf von `PlaceAt` außerhalb eines `try` |
| 1.3 | backend | Ausstiege ergänzen | `Bootstrap:1940–1994`, `:7283–7354` | **Zuerst** alle Aufrufer von `ShowMap`, `PresentSummary` (`:1924`, `:2219`) und `ReloadMap` auflisten und je Aufrufer festhalten, was erhalten bleiben muss (z. B. Weiterplatzieren, Werkzeug nach einem Neuladen). Dann: `PresentSummary` ruft `EndRowTool`/`EndTerrainTool`/`EndLineTool` genau dort, wo sie heute stumm auf Drag setzt (`:1992–1994`). Neue Methode `LeaveTransientModes()` (Typwahl für Objekt und Spline, Elternwahl, Reihen-Objektwahl, Kontextmenü); aufgerufen von `SetTool` und beim Wechsel der Karte oder der Chrono-Ansicht, nicht bei jedem Neuladen derselben Ansicht, wenn die Sichtung dort einen gewollten Fortbestand zeigt |
| 1.4 | backend | Kürzel und Luftbild-Fenster; Menü und Tasten | `Bootstrap:5969–6045` | Für die Sperre der Kürzel zählen nur modale Dialoge; Esc schließt weiter alle. Bei offenem Kontextmenü schließt jede Taste außer Pfeilen und Enter das Menü, bevor sie wirkt |
| 1.5 | – | entfällt in M1 | – | Entscheidung des Nutzers vom 12. November: „Revert tile edits“ wird rückgängig machbar und kommt mit M3. Bis dahin bleibt der Eintrag, wie er ist |
| 1.6 | frontend | Menü schließt bei Werkzeug- und Kartenwechsel; „AI paths…“ nicht stumm | `EditorShellContextMenu.cs:74–131`, `:182`, `:207` | `CloseContextMenu` über 1.3; der Eintrag „AI paths and rules…“ erscheint nur, wenn die Pfade gelesen sind, sonst mit Hinweis |
| 1.7 | testing | Tests und Prüfläufe | `Assets/_Project/Tests/Editor/` | siehe unten |
| 1.8 | reviewer | Prüfung des Diffs gegen diese Tabelle | Patchdatei im Scratchpad | Befunde mit Fundstelle |

**Zu 1.1:** Nach Entscheidung des Nutzers wird dieser Schritt getrennt auf `main` behoben (Zweig von `main`, Test, Commit nach Freigabe), danach wird `main` in `feature/ui-rework` übernommen. Die Schritte 1.2 bis 1.6 bleiben auf dem Rework-Zweig.

**Stand M0 (12. November 2026):** vom Nutzer freigegeben und begonnen; Ergebnisse stehen in [DevelopmentSetup.md](DevelopmentSetup.md) („Validierungsstand“), sobald die Läufe durch sind.

### Tests zu M1

| Art | Was | Erwartung |
|---|---|---|
| Unit (`Editor.OMSI`) | `IsBusStop`/`BusStopName` an einer Sitzung ohne Arbeitskopie: gewöhnliches Objekt, `bus_stop.sco`, in `Busstops.cfg` gelistetes Objekt | keine Ausnahme; richtige Antwort |
| Unit | Einfüge-Vorbereitung: Zufallsdrehung an, 3 kopieren, Strg+V einfügen, 5 kopieren, „Paste here“ (scheitert am heutigen Stand mit `ArgumentException`) | keine Ausnahme, fünf Ziele, keine alten Drehversätze |
| Prüflauf | `-openMap <Grundorf> -select 4229 -contextMenu 4229 -screenshot … -quit` (heute: Ausnahme, Lauf hängt) | Bild mit Menü „Properties…“ und Hinweis; Log ohne `Exception` |
| Prüflauf | derselbe Lauf an Berlin-Spandau und mit Projekt (Haltestelle 4889, „Rename bus stop…“) | wie in ADR 098 geprüft, unverändert |
| Prüflauf | Reihe aller elf Werkzeuge vor und nach 1.3 (`-road`, `-rows`, `-terrain`, `-markings`, `-priorities`, `-lanes`, `-signals`, `-line`, `-objects`) | Bilder ohne Unterschied |
| Voller Lauf | 697 + neue EditMode-Tests, LocaleCheck | grün |
| Hand (Nutzer) | Luftbild-Fenster offen: Strg+Z, Entf, Q/W/E. Kontextmenü offen: Entf, Strg+Z. Typwahl beginnen, Werkzeug wechseln, Objekt in der Bibliothek klicken. Gelände-Werkzeug offen, Chrono-Datum wechseln. | Kürzel wirken; Menü schließt; das Objekt wird platziert, nicht umgetypt; Gelände-Fenster ist zu |

### Akzeptanzkriterien M1

1. Auf einer Karte ohne Projekt lässt sich jedes Objekt auswählen und rechtsklicken; das Player-Log enthält keine Ausnahme.
2. „Paste here“ und Strg+V liefern für dieselbe Zwischenablage dieselben Drehungen; kein Weg wirft.
3. (entfällt in M1; „Revert tile edits“ wird in M3 rückgängig machbar.)
4. Bei offenem Luftbild-Fenster wirken Strg+Z, Entf und die Werkzeugtasten.
5. Ein offenes Kontextmenü schließt bei jeder Taste, bei Werkzeug- und Kartenwechsel.
6. Zwingt ein Karten- oder Chrono-Wechsel auf Drag, steht danach kein Fenster und keine Vorschau von Reihen, Gelände oder Linie mehr; wo ein Werkzeug ein Neuladen heute gewollt überlebt, tut es das weiter.
7. Eine begonnene Typwahl endet mit dem Werkzeugwechsel und mit dem Wechsel der Karte oder Chrono-Ansicht.
8. Alle bisherigen Tests und die neuen sind grün; die Werkzeug-Screenshots sind unverändert; der Reviewer hat keinen offenen P1/P2-Befund.
9. ADR 098 hat einen Nachtrag zu M1; DevelopmentSetup.md nennt die Prüfungen; Roadmap.md trägt den Punkt.

### Was M1 bewusst nicht tut

- Kein Umbau von `SetTool` zu einer Tabelle (M3), keine Änderung an der Statuszeile (M2), keine neuen Menüeinträge (M5), keine Gestaltung.

---

# M6 – Lesbarkeit und Bedienbarkeit: Bestandsaufnahme und Plan (21. November 2026)

**Stand (21. November 2026):** Bestandsaufnahme fertig, alle sechs Entscheidungsfragen vom Projektinhaber entschieden (Abschnitt „Entscheidungen“ unten). Schritt 0 (Prüfgrundlage) und Schritt 1 (Kontrast-Tokens) sind umgesetzt, begutachtet und committet; Schritt 2 (Fokusring nur bei Tastatur, `:active`, Farb- und Schrift-Tokens) ist umgesetzt und wartet auf den Handtest, nicht committet; Schritte 3 bis 7 offen. Grundlage: [DesignReview-2026-10-28.md](DesignReview-2026-10-28.md) (neun Befunde), [ui-rework-roadmap.md](ui-rework-roadmap.md) (M6), `CorporateDesign.md` (ADR 015), ADR 098, `EditorShell.uss` (972 Zeilen) und der Player `Builds/Windows/OMSIMapWorks.exe` (Stand HEAD `7257ab4`). Screenshots: `.omc/handoffs/m6/inventory/` (Liste im Handoff `.omc/handoffs/m6/HANDOFF.md`). Rechenverfahren der Kontraste wie im Design-Review (WCAG 2.x, Hex-Werte der Tokens, Glasflächen als deckend gerechnet, dazu der Fall über hellem Kartengrund).

## Ist-Stand je Befund (gemessen am 21. November 2026)

| Nr. | Befund | Ist-Stand | Beleg |
|---|---|---|---|
| 1 | Keine Windows-Skalierung | **Bestätigt, unverändert.** `EditorShellBootstrap.Awake` (Zeile 334) setzt nur `panelSettings.scale = LoadSettings().@interface.scale` (`editor-settings.json`: `1.0`, im Build eingebacken, Bereich 0,5–3 in `MapforgeEditorSettings`). Weder `scaleMode` (Standard `ConstantPixelSize`) noch `referenceDpi`/`Screen.dpi` kommen im Code vor. „Interface size“ gibt es nicht: der Settings-Dialog hat nur Sprache, „Keep the inspector at the right edge“ und „Hide DLC content“ (Bild 15). Nutzereinstellungen liegen in `PlayerPrefs` (`HideDlcKey`, Sprache über `LocalizationService`). Der Entwicklungsrechner steht auf 96 dpi (100 %), 150 % lässt sich dort nicht messen. | `EditorShellBootstrap.cs:334`, `editor-settings.json`, Bild 15 |
| 2 | Kontraste | **Bestätigt**, Tabelle unten. `text-muted` (49 Verwendungen, 29 `:disabled`-Regeln im Stylesheet) trägt echten Inhalt (Pfade, Hinweise, Detailzeilen) mit 4,33:1 auf dem Panel und 3,74:1 auf Dialogflächen; `error` (25 Verwendungen) 4,47:1. | Rechnung unten |
| 3 | Fenster verdecken die Kreuzung | **Teilweise erledigt.** Für das Ampel-Werkzeug hält `KeepClearOfToolWindow` (ADR 098, M4 Schritt 6) die Kreuzung frei. Für Spuren, Vorfahrt und Markierungen nicht belegt; die Prüfung braucht eine in MapWorks erstellte Kreuzung (das Testprojekt auf Grundorf hat keine, Schritt 4 nutzt `TestMap`). Messbar jetzt schon: Optionsspalte und Katalog überdecken das untere Drittel der Karte (Bilder 02, 03, 05, 06, 26), und bei 733 px logischer Breite überlappen sie einander (Bild 31). | Bilder 02–08, 31 |
| 4 | Farbbedeutungen | **Bestätigt.** `--map-selection` = Fehlerrot, `--map-tile-selected` = Warngelb, Gleispfade (`MapCanvasPaths.PathColor(2)`) ebenfalls `selectedTileColor` (Gelb). Objektpfade nehmen Blau (Info), Baumpfade Grün. Entscheidungsvorlage unten. | `EditorShell.uss:79`, `MapCanvasPaths.cs:353` |
| 5 | Sterne im Aus-Zustand | **Bestätigt.** `.favorite-button` (Zeile 290) und `.dlc-mark-button` (Zeile 601) nutzen `--mf-color-border` `#414B59`: 1,72:1 auf dem Panel, 1,48:1 auf Dialogfläche; auf den Kacheln der Bibliothek kaum zu sehen. | Bilder 02, 03 |
| 6 | Fokus unsichtbar | **Bestätigt, schlimmer als beschrieben.** 12 `:focus`-Regeln, davon 6 für Textfelder, 4 für `.road-window`/`.road-section`/`.road-window-template` (Zeilen 375, 395, 500, 501), 2 für `.traffic-tile`. **0 `:active`-Regeln.** 42 Klassen haben `:hover`, aber (außer `.road-window-template` und `.traffic-tile`) weder `:focus` noch `:active`, Liste unten. | `EditorShell.uss` |
| 7 | Anleitung mehrfach | **Bestätigt.** Lanes: Hinweis im Panel und derselbe Satz in der Statuszeile („Click a junction made in this project.“, Bild 06). Markierungen ebenso (Bild 05). Objekte: Hinweis rechts in der Statuszeile, links noch „Object models ready…“ (Bild 02). | Bilder 02, 05, 06 |
| 8 | Statuszeile schneidet ab | **Bestätigt.** Bild 03: „…gradient up to 0.0 %, e…“; Bild 02: „…Click into the map …“. Bei 733 px logischer Breite wird auch die linke Hälfte gekürzt (Bilder 31, 37). `.status-message` hat `text-overflow: ellipsis`, keinen Tooltip. | Bilder 02, 03, 31 |
| 9 | Orange auf Ziersymbolen | **Bestätigt.** `.floating-panel-icon` (Zeile 135) und `.welcome-recent-icon` (Zeile 105) sind orange (Bilder 00, 19). | Bilder 00, 19 |

### Gerechnete Kontraste (WCAG, Soll 4,5:1 für Text, 3:1 für Symbole)

| Vordergrund | bg `#171C23` | Panel `#202630` | Dialog/raised `#29313D` | hover `#35475A` | Feld `#141920` |
|---|---|---|---|---|---|
| text-primary `#F2F4F7` | 15,54 | 13,80 | 11,90 | 8,66 | 16,02 |
| text-secondary `#AEB8C5` | 8,53 | 7,57 | 6,53 | 4,75 | 8,79 |
| **text-muted `#7E8A99`** | 4,88 | **4,33** | **3,74** | **2,72** | 5,03 |
| accent `#F08A32` | 6,84 | 6,07 | 5,24 | 3,81 | 7,05 |
| accent-hover `#FF9B45` | 8,17 | 7,26 | 6,26 | 4,56 | 8,43 |
| success `#55B887` | 7,00 | 6,22 | 5,37 | 3,91 | 7,22 |
| warning `#E6B450` | 8,97 | 7,97 | 6,88 | 5,00 | 9,25 |
| **error `#E06464`** | 5,03 | **4,47** | **3,86** | **2,81** | 5,19 |
| info `#65A9D9` | 6,71 | 5,96 | 5,14 | 3,74 | 6,92 |
| **border `#414B59` (Sterne aus)** | 1,94 | **1,72** | **1,48** | 1,08 | 2,00 |
| Vorschlag muted `#909CAA` | 6,13 | 5,45 | 4,70 | 3,42 | 6,32 |
| Vorschlag error `#EA7676` | 5,99 | 5,32 | 4,59 | 3,34 | 6,18 |

Weitere Werte: dunkle Schrift `#171C23` auf Orange 6,84:1, auf Orange-hover 8,17:1 (in Ordnung). Fett markiert ist, was unter der Grenze liegt. **Neu gegenüber dem Design-Review:** muted und error erreichen auch mit den Vorschlagswerten nicht 4,5:1 auf `panel-hover` (3,42 und 3,34). Wo Text auf der Hover-Fläche steht (Menüeinträge, Listenzeilen: `.menu-popup-item:hover`, `.welcome-recent-row:hover`), muss er primär oder sekundär bleiben; der Test in Schritt 0 prüft das. Orange als Symbolfarbe auf der Hover-Fläche (3,81:1) besteht die 3:1 für Symbole.

**Halbtransparente Flächen über hellem Kartengrund (neu, nicht im Review).** Die Tokens gelten nur auf der gedachten deckenden Fläche. Tatsächlich liegen `.tool-button`/`.bar-tile-button`/`.bar-round-button` mit `rgba(32,38,48,0.62)` und die Leiste mit `rgba(23,28,35,0.5)` über dem Luftbild:

| Kartengrund | Werkzeugknopf (0,62): primär / sekundär / muted | Leiste (0,5): primär / sekundär | Panel (0,94): muted |
|---|---|---|---|
| Schwarz | 16,2 / 8,9 / 5,1 | 17,5 / 9,6 | 4,45 |
| hell-grünes Luftbild `#C8D0B0` | 5,3 / 2,9 / 1,7 | 4,3 / 2,3 | 3,79 |
| Weiß (schlechtester Fall) | 4,0 / 2,2 / 1,3 | 3,0 / 1,6 | 3,62 |

Die Werkzeugknöpfe tragen Symbole und Tastenkappen; im Grundorf-Screenshot (Bild 01) ist alles lesbar, über Schnee, Beton oder hellen Dächern wäre es nicht sicher. Das Review hat diesen Fall nicht gerechnet (Entscheidung 5). Es sind gerechnete Werte, keine Messung am Bildschirm.

### Knopfklassen ohne `:focus` und `:active` (Befund 6)

Klassen mit `:hover`-Regel und ohne Fokusregel (40 von 42 Klassen): `.bar-round-button`, `.bar-tile-button`, `.browser-tile-entry`, `.dlc-mark-on`, `.favorite-on`, `.folders-left-out`, `.inspector-group-header` (Chevron, Titel), `.inspector-record-clickable`, `.line-move`, `.line-node`, `.line-stop-remove`, `.map-control-button`, `.menu-popup-item`, `.new-map-swatch`, `.option-icon` (samt `-confirm`/`-danger`/`-primary`), `.panel-collapse-button`, `.panel-icon-button`, `.panel-tab`, `.picker-icon-tab`, `.picker-tile`, `.picker-tile-edit`, `.primary-button`, `.road-prop-fold`, `.road-section-boundary`, `.road-section-element`, `.road-window-add-toggle`, `.satellite-result`, `.signal-brush`, `.signal-grip`, `.signal-group-item`, `.signal-matrix-cell`, `.signal-movement-row`, `.signal-phase-chip`, `.signal-sub-button`, `.tool-button`, `.toolbar-menu-button`, `.view-control-button`, `.welcome-card`, `.welcome-recent-row`. Dazu Knöpfe ohne jede Zustandsregel (Grundstil: `.favorite-button`, `.dlc-mark-button`, `.popup-close`, Dialogknöpfe) und Unity-Standardelemente (`Toggle`, `DropdownField`, `Slider`). Eine Sammelregel nach Elementtyp `Button` statt nach Klassen fängt auch künftige Knöpfe (Schritt 2).

### Literale (Alpha-Tokens, Schriftgrößen-Tokens, feste Breiten)

- **Alpha:** 52 `rgba(...)`-Literale. 24 sind vollständig transparent (`rgba(0,0,0,0)` 23-mal, `rgba(240,138,50,0)` 1-mal; ein Token `--mf-color-clear` genügt), 4 stehen als Token in `:root` (`panel-glass`, `bar-glass`, `scrim`, `selection`), und **24 halbtransparente Literale liegen außerhalb von `:root`** (die Roadmap nannte 22) in 21 Zeilen: Rahmen von Leiste und Knöpfen `rgba(65,75,89,0.6/0.7/0.8)` (9), Knopfgrund `rgba(32,38,48,0.62)` (4), Statuszeile `rgba(41,49,61,0.7)`, Pille `rgba(20,25,32,0.7)`, Dialog-Scrim `rgba(10,12,15,0.7)`, Hinweisflächen Gelb/Blau/Rot 0,10–0,12 (4), Straßen-Fenster Orange 0,85 und Grau 0,35, `rgba(23,28,35,0.6/0.8)`, `rgba(0,0,0,0.35)`. Entwurf: sechs Tokens (`--mf-glass-fill`, `--mf-glass-line`, `--mf-tint-warning/-info/-error`, `--mf-scrim-dialog`); die übrigen bleiben begründete Einzelfälle.
- **Schriftgrößen:** 34 `font-size`-Literale in Pixeln: **10 px 23-mal, 9 px 3-mal** (u. a. `.picker-tile-count/-tag`, `.signal-timeline-tick`, Teile der Statuszeile; Zeilen 585, 628–634, 861), 11 px 5-mal (neben dem Token `--mf-font-secondary`), 12 px 2-mal, 26 px 1-mal (Willkommenstitel). Die Tokens kennen nur 14/13/11. 10 px trägt echten Inhalt: Pfade der letzten Dateien, `.panel-row-detail`, `.new-map-hint`, `.context-menu-reason`, `.road-prop-note`, `.object-folder-count`, `.road-section-number/-width`. Entwurf: `--mf-font-caption` (Mindestgröße, Entscheidung 3) und `--mf-font-display` (26 px).
- **Feste Breiten (Skalierung):** `.dialog` 560, `.new-map-dialog` 880 (kein `max-width`), Willkommensfläche 720, Werkzeugfenster 780 (3 Stellen), 760, 700, Optionsspalte 236, Popups 320, Inspector 300, Hilfemenü 360. Die Menüs `.file-menu` (168), `.edit-menu` (222), `.view-menu` (276), `.help-menu` (384) stehen mit festem `left`.

## Screenshots des Ist-Stands (Player, `-language en-US`, Testprojekt auf Grundorf)

Bei 1100 × 700 (Bilder 00–26) und mit der Windows-Skalierung **über die Fenstergröße nachgestellt** (Bilder 30–37). Ein Argument für die Skalierung gibt es nicht (`CommandLine.md` und Code kennen kein `-uiScale`; `panelSettings.scale` kommt nur aus der eingebackenen `editor-settings.json`). Behelf: Bei Skalierung *s* sieht das Layout so aus wie bei Fenstergröße/*s* und *s* = 1. 150 % bei 1100 × 700 sind also 733 × 467, 200 % sind 550 × 350. Das Layout stimmt, nur die Zeichen sind physisch kleiner als echt; für die Lesbarkeit selbst taugt der Behelf nicht. Für Schritt 0 wird ein Argument `-uiScale <Faktor>` vorgeschlagen.

Überlappungen und Brüche in den Bildern:

1. **Willkommensseite, 1100 × 700 (Bild 00):** Die Liste „Recent“ läuft unter die Statusleiste, die letzten Zeilen sind abgeschnitten, es gibt keinen Rollbalken. Bei 733 × 467 (Bild 35) liegt die Liste über den drei Karten, „Recent“ steht auf dem Text der Karte.
2. **Werkzeugfenster, 1100 × 700 (Bilder 02, 03, 05, 06, 26):** Optionsspalte und Katalog nehmen das untere Drittel der Karte ein (Befund 3). Der Hinweis im Katalog („Click a junction made in this project.“) liegt auf der durchscheinenden Fläche über der Karte und ist schwer zu lesen (Bild 06).
3. **Statuszeile (Bilder 02, 03, 05, 06):** abgeschnittene und doppelte Texte (Befund 7, 8).
4. **Ampel-Fenster:** bei 1100 × 700 (Bild 08) bis dicht an die Leiste, passt. Bei 733 × 467 (Bild 33) liegen „Discard“ und „Apply“ unter der Leiste und sind nicht bedienbar.
5. **Katalog und Optionen, 733 × 467 (Bilder 31, 32):** Der Katalog läuft rechts aus dem Fenster, die Reiter sind abgeschnitten, die Statuszeile ist gekürzt. Bei 550 × 350 (Bild 37) bleiben zwei Kachelspalten sichtbar, und Knöpfe fallen aus der Leiste.
6. **Dialoge „Map settings“ und „New map“ (Bilder 16, 25):** 880 px breit; bei 1100 × 700 beginnt das Fenster bei y = 20 über der Menüleiste und endet über der Statuszeile (modal, vertretbar). **Bei 733 × 467 (Bild 34) sind linke und rechte Kante abgeschnitten**, Beschriftungen und der Knopf „Apply“ fehlen teilweise.
7. **Settings-Dialog (Bild 15):** Die Kästchen für „Keep the inspector…“ und „Hide DLC content“ sind helle leere Quadrate ohne den Stil der übrigen Schalter (Kandidat für M7, kein Befund des Reviews).
8. **In Ordnung:** Inspector (Bild 19) und Popups der Leiste (Bilder 11–13) passen bei 1100 × 700 ohne Überlappung; Tastenkappen und Werkzeugleiste sind bei 100 % gut lesbar.

Per Argument nicht oder nur eingeschränkt erreichbar: `-newMap` zeigt den Dialog nur ohne geöffnete Karte (Bild 25 wurde so aufgenommen), `-projectTab paths` zeigt im Testprojekt dasselbe wie `tiles` (Bild 14 = Bild 10; keine Pfade im Projekt), Spuren/Vorfahrt/Markierungen mit gewählter Kreuzung (Testprojekt ohne in MapWorks erstellte Kreuzung). Bilder 21–24 (Fahrplan, Fahrzeuge, Fehlerbericht, Parkplatzliste) liegen vor, gehören aber nicht zu den Befunden. Bild 04 und 17 sind ohne Wirkung (identisch mit Bild 01; richtig ist `-terrain raise 0 -1 100 100`, Bild 26).

## Plan

Reihenfolge: erst die Mess- und Prüfgrundlage, dann die kleinen Token-Änderungen (kein Layoutrisiko), dann Skalierung und Layout (das teure, riskante Paket), dann Statuszeile, zuletzt die Farbbedeutungen nach Entscheidung. Jeder Schritt bekommt einen Player-Build mit Screenshot-Reihe und einen Nutzertest, wie bei M4. **Dateibesitz:** ein Bearbeiter (frontend) für `EditorShell.uss`; `EditorShellBootstrap.cs` ändern nur Schritt 0 und 3 an kleinen, getrennten Stellen; neue Teile als eigene `partial`-Datei (`EditorShellScale.cs`).

| Schritt | Inhalt | Umfang | Dateien | Prüfung und Akzeptanz |
|---|---|---|---|---|
| 0 | **Prüfgrundlage.** Argument `-uiScale <Faktor>` (setzt `panelSettings.scale` für den Lauf), Zeile im Player-Log `UI scale: dpi=… scale=…`; EditMode-Test `UiTokenContrastTests` (liest `EditorShell.uss`, rechnet WCAG für die Paare Text-Token × Flächen-Token, prüft: keine `font-size` unter der Mindestgröße, keine rohen `rgba`-Literale außerhalb `:root`). Der Test ist anfangs rot (Liste der Verstöße) und dient danach als Schranke | S | `EditorShellScale.cs` (neu), `EditorShellBootstrap.cs` (eine Zeile), `Tests/Editor/UiTokenContrastTests.cs`, `CommandLine.md` | `-uiScale 1.5` bei 1100 × 700 zeigt die Oberfläche 1,5-fach, Log-Zeile vorhanden; Test läuft in der Projektkopie und zeigt die Verstöße |
| 1 | **Kontrast-Tokens (Befund 2, 5, 9).** `text-muted` auf `#909CAA`, neues `text-disabled #7E8A99` für alle `:disabled`-Regeln, `error` auf `#EA7676`, Sterne im Aus-Zustand `text-muted`, Symbole in Panelköpfen und letzten Dateien `text-secondary`. Prüfen, dass Text auf `panel-hover` primär bleibt | S | `EditorShell.uss` | Kontrasttest grün; Bilder 00, 02, 03, 08, 19 vorher/nachher; Sterne sichtbar |
| 2 | **Fokus, Drücken, Tokens (Befund 6, Literale).** Sammelregeln `Button:focus` (2-px-Rand `accent-hover` oder Innenring ohne Layoutverschiebung), `:active` mit dunklerer Fläche; die 24 Alpha-Literale auf sechs Tokens; 10/9-px-Schrift auf `--mf-font-caption`, 26 px auf `--mf-font-display`. Tastaturreihenfolge in Dialogen und Panels prüfen (`focusable`/`tabIndex` kommen im Code bisher nicht vor) | M | `EditorShell.uss`, ggf. `EditorShellBootstrap.cs` (Fokus nur nach Tastatur, Entscheidung 6) | Tab durch Menü, Leiste, Werkzeugfenster, Dialog: Fokus auf jedem Knopf sichtbar (Handtest, dazu ein Screenshot je Klasse); keine rohen Alpha-Literale und keine Schrift unter der Mindestgröße (Test); Screenshots sonst unverändert bis auf Schriftgröße |
| 3 | **Skalierung (Befund 1).** `scaleMode = ConstantPhysicalSize`, `referenceDpi = 96`, `fallbackDpi = 96` (nach Entscheidung 2); „Interface size“ im Settings-Dialog (90–200 %, in `PlayerPrefs`, wirkt sofort); der `editor-settings.json`-Wert bleibt Standardfaktor; Klemmen nach Entscheidung 4; Texte in acht Sprachen | M | `EditorShellScale.cs`, `EditorShellSettings.cs`, `EditorShell.uxml`, `locales/*`, ggf. `MapforgeEditorSettings.cs` | Läufe `-uiScale 1.0/1.5/2.0`; Log nennt dpi; **Nutzertest auf einem Rechner mit 125/150 %** (nur dort ist `Screen.dpi` echt prüfbar); LocaleCheck `--strict` grün; Neustart behält die Einstellung |
| 4 | **Layout bei 1100, 150, 200 % (Befund 3, feste Breiten).** `max-width`/`max-height` in Prozent für Dialoge, Willkommensfläche rollt, Werkzeugfenster und Katalog schrumpfen (Optionsspalte darf umbrechen), Menüs positionieren sich nach der Menüleiste statt mit festem `left`, Ampel-Fenster endet über der Leiste, `KeepClearOfToolWindow` für Spuren, Vorfahrt und Markierungen prüfen und anwenden | L | `EditorShell.uss`, `EditorShellBottomBar.cs`, Dateien der Werkzeugfenster und Kreuzungs-Werkzeuge | Bilderreihe 100/150/200 % × 1100×700 und 1600×900 aller Fenster und Dialoge: nichts überlappt, nichts liegt unter der Leiste oder außerhalb; Kreuzung #472 in `TestMap` bleibt in Lanes, Priority, Markings sichtbar |
| 5 | **Statuszeile und Anleitungen (Befund 7, 8).** Meldungen auf einen Satz, voller Text als Tooltip, Dopplung zwischen Panel und Statuszeile entfernen (Panel: Legende plus eine Zeile; Statuszeile: nur Ergebnisse), 3D-Bedienhilfe ins Hilfemenü | M | Statuszeilen-Code in `EditorShellBootstrap.cs` bzw. den Werkzeugdateien, `locales/*` | Bilder 02, 03, 05, 06 vorher/nachher: nichts abgeschnitten, nichts doppelt; Tooltip zeigt den vollen Text; `TooltipTextTests` und Sprachtests grün |
| 6 | **Farbbedeutungen (Befund 4)**, nur nach Entscheidung 1: Tokens `--mf-color-path-rail`, Auswahl, gewählte Kachel; Nachtrag zu ADR 015 | M | `EditorShell.uss`, `MapCanvasElement.cs`, `MapCanvasPaths.cs`, `CorporateDesign.md`, `Decisions.md` | Grundorf und Berlin-Spandau (Gleise, Auswahl, Kachelrahmen) über hellem und dunklem Grund; Auswahl bleibt über Luftbild erkennbar |
| 7 | **Abschluss.** Reviewer-Durchgang gegen diese Tabelle; Bilderreihe 100/150/200 %; Validierungsstand in `DevelopmentSetup.md`; ADR-098-Nachtrag „M6“; Roadmap, README, Architecture; Freigabeliste neu schreiben lassen | S | Dokumentation, `PlayerAssetReview.txt` (durch den Nutzer) | Akzeptanz unten |

**Akzeptanz M6 (nach Roadmap, verschärft):** (1) Bei 150 % Windows-Skalierung ist die Oberfläche 1,5-fach, und „Interface size“ wirkt zusätzlich. (2) Jede Textfarbe erreicht 4,5:1 auf ihrer Fläche (Test; der Fall über hellem Kartengrund nach Entscheidung 5). (3) Tab-Fokus ist auf jedem Knopf sichtbar. (4) Bei 1100 × 700 und bei 150 % überlappt nichts, keine Bedienfläche liegt unter der Leiste. (5) Befund 4 ist entschieden und umgesetzt oder ausdrücklich verworfen. (6) Alle Tests grün (697 + neue EditMode-Tests, LocaleCheck strict).

### Risiken

- **Player-Asset-Review (harte Regel 3):** `EditorShell.uss` und `EditorShell.uxml` stehen in `PlayerAssetReview.txt` (Zeilen 16 und 17). Jede Änderung warnt im normalen Build und stoppt `BuildRelease`, bis der Nutzer „Record Reviewed Player Assets“ ausgeführt hat. Das gehört in Schritt 7 und zu den Nutzertests, nicht in jeden Zwischenstand. Neue Schriftdateien wären neue Player-Dateien: keine geplant.
- **Skalierung verändert jede feste Breite:** Schritt 3 darf nicht ohne Schritt 4 in den Nutzertest, sonst sieht der Nutzer bei 150 % Dialoge, die aus dem Fenster laufen (Bild 34).
- **`Screen.dpi` im Windows-Player** ist nicht gemessen (Entwicklungsrechner 100 %). Deshalb die Log-Zeile aus Schritt 0 und ein Nutzertest auf einem Rechner mit Skalierung; Rückfall bleibt der manuelle Faktor.
- **`:focus` in UI Toolkit** bleibt nach einem Mausklick bestehen: ein Fokusring nach jedem Klick wäre Rauschen (Entscheidung 6). Die Kartenfläche darf den Tastaturfokus für Q/W/E nicht verlieren.
- **Layout-Brüche in zusammenhängenden Fenstern** (Straßen- und Ampel-Fenster haben eigene Größenlogik und Ziehgriffe): nach jedem Schritt die ganze Bilderreihe, nicht nur Stichproben.
- **Halbtransparente Flächen:** höhere Deckkraft verdeckt mehr Karte (Entscheidung 5). Schriftkontraste lassen sich nur gegen reale Kartenbilder prüfen; in die Reihe gehört ein heller Ausschnitt (Berlin-Spandau).
- **Tests brauchen Unity:** Die neuen Tests laufen in der Projektkopie im Scratchpad (Editor oft offen). Der Kontrasttest liest die USS als Datei.
- **Sprachen:** neue Texte („Interface size“, Tooltips der Statuszeile) in alle acht Sprachen, LocaleCheck `--strict`.

## Entscheidungsfragen an den Projektinhaber (am 21. November 2026 entschieden, siehe unten)

1. **Befund 4, Farbbedeutungen.** (A) Wie im Review: Auswahl hell (`#F2F4F7` mit dunklem Rand), gewählte Kachel orange, Gleise in eigenem Violett (`#B48EDB`); Rot bleibt nur Löschen/Fehler, Gelb nur bearbeitet/Warnung. (B) Nur Gleise und Kachelrahmen ändern, Rot als „Auswahl“ ausdrücklich ins Corporate Design aufnehmen. (C) Nichts ändern, nur dokumentieren. **Empfehlung A:** löst den Konflikt am Gleis im Spandau-Bild und hält die Bedeutung der Farben rein; der Preis ist ein neuer Farbton für Gleise und eine geänderte Auswahlfarbe, an die man sich gewöhnen muss. Nachtrag zu ADR 015.
2. **Skalierungsverhalten.** (A) Automatisch nach Windows-Anzeigeskalierung (`ConstantPhysicalSize`, 96 dpi) plus Faktor „Interface size“ 90–200 % in den Einstellungen. (B) Nur manuell, Standard 100 %. (C) Nur automatisch. **Empfehlung A:** 100 % heißt „wie Windows“, wer es anders will, stellt es ein; Rückfall auf 96 dpi, wenn Unity 0 meldet.
3. **Mindestschriftgröße.** Heute 9 oder 10 px an 26 Stellen. **Empfehlung 11 px** als Untergrenze bei 100 % (`--mf-font-caption`), 12–13 px für Fließtext; Ausnahme nur für Tastenkappen und Zählmarken (10 px, ohne Informationsverlust). Folge: einige Kacheln und Zeilen wachsen um 1 px, das Layout wird in Schritt 2 mitgeprüft.
4. **Kleine Fenster bei hoher Skalierung.** Bei 150 % bleiben von 1100 × 700 logisch 733 × 467. (A) Fenster und Dialoge schrumpfen und rollen (`max-width` in Prozent), die Skalierung wird so geklemmt, dass die logische Breite nie unter 900 px fällt. (B) Wie A ohne Klemmen. (C) Mindestfenstergröße nach Skalierung erhöhen. **Empfehlung A:** nie unbedienbar, die Einstellung bleibt als Wunsch gespeichert.
5. **Deckkraft der Glasflächen.** Über hellem Kartengrund fällt der gerechnete Kontrast der Werkzeugknöpfe auf 4,0:1 und der Leiste auf 3,0:1 (Weiß, schlechtester Fall). (A) Werkzeugknöpfe von 0,62 auf 0,85 und Leiste von 0,5 auf 0,7 anheben (weniger Karte scheint durch). (B) Lassen, Kontrast nur auf dem gedachten dunklen Grund fordern. **Empfehlung A**, nach Bildvergleich über dem hellsten Berliner Ausschnitt; der Inhaber sieht beide Varianten im Screenshot, bevor es bleibt.
6. **Fokusanzeige.** (A) Ring bei jedem `:focus` (einfach, erscheint auch nach Mausklick). (B) Ring nur bei Tastaturbedienung (Klasse, die ein Tab-Druck setzt und ein Mausklick entfernt; mehr Code in der Shell). **Empfehlung B:** der Nutzer arbeitet überwiegend mit der Maus, Dauerringe nach jedem Klick würden stören. Ist der Aufwand größer als gedacht, ist A der Rückfall.


## Entscheidungen des Projektinhabers (21. November 2026)

Alle sechs Fragen sind entschieden; ADR 098, Nachtrag „M6 Entscheidungen“.

| Nr. | Frage | Entscheidung |
|---|---|---|
| 1 | Farbbedeutungen | **A.** Auswahl hell (`#F2F4F7`) mit dunklem Rand, gewählte Kachel orange, Gleise violett `#B48EDB`. Rot bleibt nur Löschen/Fehler, Gelb nur bearbeitet/Warnung. Nachtrag zu ADR 015 folgt in Schritt 6. |
| 2 | Skalierung | **A.** Automatisch nach Windows-DPI (`ConstantPhysicalSize`, 96 dpi) plus Faktor 90 bis 200 % in den Einstellungen; Rückfall 96 dpi, wenn Unity 0 meldet. |
| 3 | Mindestschrift | **11 px** als Untergrenze (`--mf-font-caption`), 12 bis 13 px für Fließtext; Ausnahme nur Tastenkappen und Zählmarken mit 10 px. |
| 4 | Kleine Fenster | **A.** Fenster und Dialoge schrumpfen und rollen; die Skalierung wird so geklemmt, dass die logische Breite nie unter 900 px fällt (die Einstellung bleibt als Wunsch gespeichert). |
| 5 | Deckkraft Glasflächen | **A.** Werkzeugknöpfe 0,62 auf 0,85, Leiste 0,5 auf 0,7, nach Bildvergleich über einem hellen Berlin-Spandau-Ausschnitt. |
| 6 | Fokusanzeige | **B.** Fokusring nur bei Tastaturbedienung (Schritt 2). |
