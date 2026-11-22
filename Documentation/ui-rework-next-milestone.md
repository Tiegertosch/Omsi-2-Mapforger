# UI-Rework: nächster Schritt – M0 und M1 (12. November 2026)

Empfehlung aus [ui-rework-roadmap.md](ui-rework-roadmap.md). **Stand 12. November 2026: M0 und M1 sind abgeschlossen, vom Nutzer getestet und committet (ADR 098, „M1“). Dieses Dokument bleibt als Plan und Nachweis; M2 ist umgesetzt und committet; M3 ist abgeschlossen, vom Nutzer getestet (13. November 2026) und committet; der nächste Meilenstein war M4; M4 und M5 sind inzwischen abgeschlossen, Bestandsaufnahme und Plan für M6 stehen unten (Abschnitt „M6“, 21. November 2026), Stand in [ui-rework-roadmap.md](ui-rework-roadmap.md). **M6 ist abgeschlossen (22. November 2026):** Schritte 0 bis 7 vom Projektinhaber getestet und gepusht (`26d0c1c` bis `41aeba5`). Der Abschnitt „M6“ unten bleibt als Plan und Nachweis. **M7 „Dialoge vereinheitlichen“ ist in Planung (22. November 2026): Bestandsaufnahme, Plan und Entscheidungsfragen stehen unten im Abschnitt „M7 – Plan“; Umsetzung erst nach den Entscheidungen des Projektinhabers.**

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

**Abschluss (22. November 2026): M6 ist erledigt.** Schritte 0 bis 7 sind vom Projektinhaber getestet und gepusht (`26d0c1c`, `5c4a88d`, `f639e19`, `3309345`, `5fa7f58`, `41aeba5`). Offen nur außerhalb des Meilensteins: 3D-Bedienung ins GitHub-Wiki nach dem Release. Nächster Meilenstein: M7, nach Freigabe des Projektinhabers.

**Stand (21. November 2026):** Bestandsaufnahme fertig, alle sechs Entscheidungsfragen vom Projektinhaber entschieden (Abschnitt „Entscheidungen“ unten). Schritt 0 (Prüfgrundlage) und Schritt 1 (Kontrast-Tokens) sind umgesetzt, begutachtet und committet; Schritt 2 (Fokusring nur bei Tastatur, `:active`, Farb- und Schrift-Tokens) ist umgesetzt und wartet auf den Handtest, nicht committet; Schritt 3 (Skalierung) ist umgesetzt, Handtest mit 125/150 % offen; **Schritt 4 (Layout bei 100/150/200 %) ist umgesetzt und im Player geprüft (21. November 2026, ADR 098, Nachtrag „M6 Schritt 4“), Handtest offen, nicht committet**; **Schritt 5 (Statuszeile und Anleitungen) ist umgesetzt und im Player geprüft (21. November 2026, ADR 098, Nachtrag „M6 Schritt 5“), Handtest der Tooltip-Anzeige offen, nicht committet**; Schritte 6 und 7 offen. Grundlage: [DesignReview-2026-10-28.md](DesignReview-2026-10-28.md) (neun Befunde), [ui-rework-roadmap.md](ui-rework-roadmap.md) (M6), `CorporateDesign.md` (ADR 015), ADR 098, `EditorShell.uss` (972 Zeilen) und der Player `Builds/Windows/OMSIMapWorks.exe` (Stand HEAD `7257ab4`). Screenshots: `.omc/handoffs/m6/inventory/` (Liste im Handoff `.omc/handoffs/m6/HANDOFF.md`). Rechenverfahren der Kontraste wie im Design-Review (WCAG 2.x, Hex-Werte der Tokens, Glasflächen als deckend gerechnet, dazu der Fall über hellem Kartengrund).

## Ist-Stand je Befund (gemessen am 21. November 2026)

| Nr. | Befund | Ist-Stand | Beleg |
|---|---|---|---|
| 1 | Keine Windows-Skalierung | **Bestätigt, unverändert.** `EditorShellBootstrap.Awake` (Zeile 334) setzt nur `panelSettings.scale = LoadSettings().@interface.scale` (`editor-settings.json`: `1.0`, im Build eingebacken, Bereich 0,5–3 in `MapforgeEditorSettings`). Weder `scaleMode` (Standard `ConstantPixelSize`) noch `referenceDpi`/`Screen.dpi` kommen im Code vor. „Interface size“ gibt es nicht: der Settings-Dialog hat nur Sprache, „Keep the inspector at the right edge“ und „Hide DLC content“ (Bild 15). Nutzereinstellungen liegen in `PlayerPrefs` (`HideDlcKey`, Sprache über `LocalizationService`). Der Entwicklungsrechner steht auf 96 dpi (100 %), 150 % lässt sich dort nicht messen. | `EditorShellBootstrap.cs:334`, `editor-settings.json`, Bild 15 |
| 2 | Kontraste | **Bestätigt**, Tabelle unten. `text-muted` (49 Verwendungen, 29 `:disabled`-Regeln im Stylesheet) trägt echten Inhalt (Pfade, Hinweise, Detailzeilen) mit 4,33:1 auf dem Panel und 3,74:1 auf Dialogflächen; `error` (25 Verwendungen) 4,47:1. | Rechnung unten |
| 3 | Fenster verdecken die Kreuzung | **Teilweise erledigt.** Für das Ampel-Werkzeug hält `KeepClearOfToolWindow` (ADR 098, M4 Schritt 6) die Kreuzung frei. Für Spuren, Vorfahrt und Markierungen nicht belegt; die Prüfung braucht eine in MapWorks erstellte Kreuzung (das Testprojekt auf Grundorf hat keine, Schritt 4 nutzt `TestMap`). Messbar jetzt schon: Optionsspalte und Katalog überdecken das untere Drittel der Karte (Bilder 02, 03, 05, 06, 26), und bei 733 px logischer Breite überlappen sie einander (Bild 31). | Bilder 02–08, 31 |
| 4 | Farbbedeutungen | **Erledigt in Schritt 6 (22. November 2026, ADR 098 Nachtrag „M6 Schritt 6“; Handtest offen).** Bestand vorher:  `--map-selection` = Fehlerrot, `--map-tile-selected` = Warngelb, Gleispfade (`MapCanvasPaths.PathColor(2)`) ebenfalls `selectedTileColor` (Gelb). Objektpfade nehmen Blau (Info), Baumpfade Grün. Entscheidungsvorlage unten. | `EditorShell.uss:79`, `MapCanvasPaths.cs:353` |
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
 **Umgesetzt am 21. November 2026, Handtest auf einem Rechner mit 125/150 % offen (ADR 098, Nachtrag „M6 Schritt 3“). Abweichung: `ConstantPixelSize` mit selbst gerechnetem Faktor (dpi/96 x Nutzer x Standard, geklemmt auf Fensterbreite/900, nie unter 1,0) statt `ConstantPhysicalSize`; Dropdown statt Slider.** |
| 4 | **Layout bei 1100, 150, 200 % (Befund 3, feste Breiten).** `max-width`/`max-height` in Prozent für Dialoge, Willkommensfläche rollt, Werkzeugfenster und Katalog schrumpfen (Optionsspalte darf umbrechen), Menüs positionieren sich nach der Menüleiste statt mit festem `left`, Ampel-Fenster endet über der Leiste, `KeepClearOfToolWindow` für Spuren, Vorfahrt und Markierungen prüfen und anwenden | L | `EditorShell.uss`, `EditorShellBottomBar.cs`, Dateien der Werkzeugfenster und Kreuzungs-Werkzeuge | Bilderreihe 100/150/200 % × 1100×700 und 1600×900 aller Fenster und Dialoge: nichts überlappt, nichts liegt unter der Leiste oder außerhalb; Kreuzung #472 in `TestMap` bleibt in Lanes, Priority, Markings sichtbar | **Umgesetzt am 21. November 2026, Handtest offen (ADR 098, Nachtrag „M6 Schritt 4“): Dialoge und Menüs mit `max-width`/`max-height` in Prozent, innen rollend (Settings, Export, Hilfemenü, Recent-Liste), Werkzeugfenster schrumpfen (Katalog zuerst), Menüs unter ihrem Knopf (`EditorShellMenuPlacement.cs`), Satelliten-Fenster endet über der Leiste, Leiste mit 8 px rechts, `PanIntoFreeArea` mit schrumpfendem Rand bei niedriger Fläche (Spuren, Vorfahrt, Markierungen halten die Kreuzung frei, auch bei 900 x 506 logisch). Offen: das Ampel-Fenster (780 px) deckt bei 900 px logischer Breite die Kreuzung zu (es lässt sich verschieben), „Optionsspalte darf umbrechen“ nur als Mindestbreite.** |
| 5 | **Statuszeile und Anleitungen (Befund 7, 8).** Meldungen auf einen Satz, voller Text als Tooltip, Dopplung zwischen Panel und Statuszeile entfernen (Panel: Legende plus eine Zeile; Statuszeile: nur Ergebnisse), 3D-Bedienhilfe ins Hilfemenü | M | Statuszeilen-Code in `EditorShellBootstrap.cs` bzw. den Werkzeugdateien, `locales/*` | Bilder 02, 03, 05, 06 vorher/nachher: nichts abgeschnitten, nichts doppelt; Tooltip zeigt den vollen Text; `TooltipTextTests` und Sprachtests grün | **Umgesetzt am 21. November 2026, Handtest der Tooltip-Anzeige offen (ADR 098, Nachtrag „M6 Schritt 5“): Regel „Statuszeile = Zustand oder Ergebnis in einem Satz, Anleitung = Hinweis des Werkzeugs oder Panel, Bedienung der 3D-Ansicht = Hilfemenü“; 30 Texte in acht Sprachen gekürzt, 4 Schlüssel entfernt, 1 neu; Doppelungen bei Spuren (Panel gegen Hinweis) und Linie (Liste gegen Hinweis) entfernt; Hinweis bekommt Vorrang vor der Meldung (`flex-basis: 0`); `StatusLineTextTests`.** |
| 6 | **Erledigt 22. November 2026 (Handtest offen, unkommittiert).** **Farbbedeutungen (Befund 4)**, nur nach Entscheidung 1: Tokens `--mf-color-path-rail`, Auswahl, gewählte Kachel; Nachtrag zu ADR 015 | M | `EditorShell.uss`, `MapCanvasElement.cs`, `MapCanvasPaths.cs`, `CorporateDesign.md`, `Decisions.md` | Grundorf und Berlin-Spandau (Gleise, Auswahl, Kachelrahmen) über hellem und dunklem Grund; Auswahl bleibt über Luftbild erkennbar |
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



# M7 – Dialoge vereinheitlichen: Bestandsaufnahme und Plan (22. November 2026)

**Stand: M7 Schritt 0 in Arbeit** (Entscheidungen des Projektinhabers vom 22. November 2026 stehen unten in „Entscheidungen des Projektinhabers“). Die Bestandsaufnahme darunter ist der Stand vor Schritt 0. Grundlage: HEAD `074ef75` auf `feature/ui-rework`, Roadmap M7 (`ui-rework-roadmap.md`, Abschnitt M7), ADR 098 (M3: Anmeldung der Überlagerungen; M6 Schritt 4: Klemmen), `CorporateDesign.md`. Alle Zahlen sind per Suche über `Assets/_Project/Scripts/UI`, `UI/Resources/EditorShell.uxml` und `EditorShell.uss` am 22. November 2026 gezählt. Nicht gemessen: das Aussehen im Player; die Vorher-Bilder entstehen in Schritt 0.

**Stand Schritt 5 (22. November 2026):** Die sechs Formular-Dialoge (Export, Einstellungen, Kartenprüfung, Neue Karte/Karteneinstellungen, Fehlerbericht, Updates) kommen aus der Fabrik, `EditorShell.uxml` hat keinen Dialog mehr; Handtest offen, siehe ADR 098, Nachtrag „M7 Schritt 5“, und `.omc/handoffs/m7/HANDOFF.md`. Offen: Schritt 6 (Abschluss: Zähler auf die Endwerte, Reviewer-Durchgang, Wiki-Hinweis).

**Stand Schritt 6 (22. November 2026):** Zähler auf den Endwerten (Inline-Größen 0 unmarkiert, 31 verschoben, 33 `computed`), tote USS-Regeln entfernt, Sonderform der drei Werkzeugfenster dokumentiert (ADR 098, Nachtrag „M7 Schritt 6“). Offen: Reviewer-Durchgang, Handtest, Wiki-Hinweis nach der Veröffentlichung.

## Kernzahlen

| Messgröße | Ist | Ziel (Roadmap) |
|---|---|---|
| Konstruktionen von `dialog-overlay` (Suche in `*.cs` und `*.uxml`) | **13**: 7 im Code, 6 in der UXML (dazu die USS-Regel `EditorShell.uss:889`) | 0 außerhalb der Fabrik |
| Überlagerungen mit `RegisterOverlay` | 13 (12 modal, 1 nicht modal: Luftbild-Fenster) | alle modalen über die Fabrik |
| Inline-Größen in `Scripts/UI`, enge Zählung (`style.width/height/minWidth/minHeight/maxWidth/maxHeight`) | **66** | |
| dazu `style.margin*` und `style.padding*` | 63 | |
| dazu `style.flexBasis/flexGrow/flexShrink` | 43 | |
| Summe Größe, Abstand, Flex (Zählung, die M7 meint, siehe Frage 5) | **172** in 30 Dateien | unter 20 |
| davon in Dialogdateien: Fahrzeuge 39, Fahrplan 32, Fahrplan verwalten 24, Verkehrskurven 7, Parkliste 2, Karteneinstellungen 1, Updates 1 | **106** | 0 (USS-Klassen) |
| davon Rest (Karte, Kreuzungsschnitt, Fenstergeometrie, Leiste, Inspector, Luftbild, Werkzeugfenster) | 66 | höchstens 19 |
| zusätzlich `style.left/top/right/bottom` (Platzierung, rechnerisch) | 55 | zählt nicht |
| Bauweisen für Dialoge | 3 modale (A, B, C) plus 1 Sonderform (D) | 1 plus erklärte Sonderform |

## Bestandsaufnahme: alle Dialoge und modalen Fenster

Gemeinsam ist allen 13: das Gerüst `.dialog-overlay` (`EditorShell.uss:889`, vollflächig, `--mf-color-overlay`) mit einem `.dialog` darin (`:890`, **560 px** breit, `max-width`/`max-height` 94 % aus M6 Schritt 4 in `:312`) und einer `.dialog-buttons`-Zeile (`:899`, rechtsbündig). Titel ist immer ein Label mit `.popup-title` (`:107`), Beschreibung mit `.popup-description` (`:108`).

| Nr. | Dialog | Bauweise | Fundstelle (Overlay, Inhalt, Anmeldung) | Breite | Knöpfe |
|---|---|---|---|---|---|
| 1 | Export | **A** UXML-statisch | `EditorShell.uxml:103`; `EditorShellBootstrap.cs:368` | 560 | Cancel, Export (primär) |
| 2 | Einstellungen | **A** | `EditorShell.uxml:118`; `EditorShellSettings.cs:91` | 560 | nur Close (primär) |
| 3 | Kartenprüfung | **A** | `EditorShell.uxml:143`; `EditorShellBootstrap.cs:375` | 560 | Copy report, Close |
| 4 | Neue Karte / Karteneinstellungen (ein Dialog, zwei Modi) | **B** Formklasse in leerem UXML-Overlay | Overlay `EditorShell.uxml:154`; `NewMapDialog.cs:71`; `EditorShellBootstrap.cs:392` | 880 (`.new-map-dialog`, `EditorShell.uss:917`) | Cancel, Create/Apply |
| 5 | Fehlerbericht | **B** | `EditorShell.uxml:155`; `BugReportDialog.cs:26`; `EditorShellBugReport.cs:35` | 560 | Cancel, Send |
| 6 | Updates | **B** | `EditorShell.uxml:156`; `UpdateDialog.cs:32`; `EditorShellUpdates.cs:48` (mit Aktion `CloseUpdates`) | 560 | Skip, Close, Primär je Zustand |
| 7 | Ordnerwahl | **C** handgebaut im Code | `EditorShellContentFolders.cs:44`, Anmeldung `:46` | 560 | Cancel, Apply |
| 8 | Name des Einstiegspunkts | **C** | `EditorShellEntryPoints.cs:126`, Anmeldung `:128` (mit Aktion) | 560 | Cancel, OK |
| 9 | Parkliste | **C** | `EditorShellParkList.cs:28`, `:30` | 560 | Cancel, Apply |
| 10 | Hinweis „nicht übernommene Änderungen“ (Ampel) | **C** | `EditorShellSignalNotice.cs:21`, `:23` | 560 | OK |
| 11 | Fahrplan (mit „verwalten“) | **C** | `EditorShellTimetable.cs:43`, `:45`; Inhalt in `EditorShellTimetableManage.cs` | 560 als `minWidth` inline (`:49`) | Cancel, Apply |
| 12 | Tageskurven (Verkehr) | **C** | `EditorShellTrafficCurves.cs:27`, `:29` | **640 inline** (`:33`) | Cancel, Apply |
| 13 | Fahrzeuge | **C** | `EditorShellVehicles.cs:33`, `:35` | **620 `minWidth` inline** (`:39`) | nur Close (primär) |
| – | Luftbild-Fenster | **D** schwebendes Fenster, kein Dialog | `SatelliteWindow.cs:120` (`FloatingWindow.MakeMovable`); Anmeldung nicht modal `EditorShellAerial.cs:82` | eigene | eigene Fußzeile |
| – | Straßentyp-Fenster, Ampel-Fenster (Assistent und Vorlagen sind Seiten darin) | **D** `FloatingWindowFrame` | `FloatingWindowFrame.cs:33`; `RoadTypeWindow.cs:18`, `SignalWindow.cs:16` | Fenstergröße gemerkt | Rückfrage im Fenster (`FloatingWindowFrame.cs:139`, `:154`) |

Zu den Bauweisen: **A** und **B** liegen als `<VisualElement class="dialog-overlay">` in der UXML (6 Stück), **C** baut Overlay, `.dialog` und Knopfzeile je Dialog mit denselben Zeilen von Hand (7 Stück: `new VisualElement { name = … }`, `AddToClassList("dialog-overlay")`, `style.display = None`, `RegisterOverlay`, `rootElement.Add`). **D** sind keine modalen Dialoge. Der Ampel-Assistent und die Vorlagen (`EditorShellSignalWizard.cs`, `EditorShellSignalTemplates.cs`) sind Seiten des Ampel-Fensters, nicht eigene Dialoge; der einzige echte Dialog dieses Bereichs ist der Hinweis (Nr. 10).

Befunde (aus dem Code gelesen, nicht im Player gemessen):

1. **Breite:** 560 (Standard), 880 (Karte), dazu 640 und 620/560 als Inline-Wert in Verkehrskurven, Fahrzeugen und Fahrplan. Keine Breitenklassen.
2. **Textfarbe über einen Umweg:** Die Klasse `.content-folders` (`EditorShell.uss:634`: nur `color` und `max-height: 92 %`) hängt an Ordnerwahl, Parkliste, Fahrplan, Verkehrskurven, Fahrzeugen und Einstiegsname („the same text colour as the other dialogs“, `EditorShellEntryPoints.cs:135`). Dialoge ohne sie (Hinweis, Export, Einstellungen) erben eine andere Textfarbe.
3. **Rollen:** Jeder Dialog rollt anders: `.dialog-scroll` in Export und Einstellungen (`EditorShell.uxml:106`, `:121`), `.new-map-body`, `.content-folders-list` mit `max-height: 420px` (`EditorShell.uss:636`), Fahrplan und Fahrzeuge inline `maxHeight = 520` (`EditorShellTimetable.cs:58`, `EditorShellVehicles.cs:48`). Feste Pixel statt Prozent: bei kleinem Fenster greift nur die Klemme `.dialog { max-height: 94 % }`.
4. **Knöpfe:** Überall Abbrechen links, primär rechts; die Beschriftung des primären Knopfes ist frei (Apply, Close, OK, Create, Send, Export). Ein-Knopf-Dialoge: Einstellungen und Fahrzeuge „Close“, Hinweis „OK“.
5. **Esc und Anmeldung (M3):** Eine zentrale Stelle: `OnShortcut` (`EditorShellBootstrap.cs:6614`) führt über `EscapeAction.CloseTopOverlay` (`:6580`, `EscapeOrder.cs:133`) zu `OverlayRegistry.CloseAllShown`. Das versteckt **alle** angemeldeten Überlagerungen auf einmal (nicht nur die oberste) und führt danach die Aufräum-Aktionen derer aus, die offen waren. Aktionen haben nur Einstiegsname (Antwort verwerfen, `mapCanvas?.Focus()`), Updates (`CloseUpdates`) und Luftbild-Fenster. Alle anderen werden nur versteckt. Ob Esc und der Abbrechen-Knopf überall dasselbe tun (Fahrplan, Fahrzeuge mit Änderungen im Dialog), ist **nicht untersucht** und gehört in Schritt 0.
6. **Tastenkürzel:** `AnyModalDialogShown` (`EditorShellOverlays.cs:27`) sperrt sie bei jeder modalen Überlagerung (`EditorShellBootstrap.cs:6635`); einheitlich.
7. **Enter:** Nur der Einstiegsname bestätigt mit Enter (`EditorShellEntryPoints.cs:147`, im Textfeld). Kein Dialog kennt einen Standardknopf.
8. **Fokus:** Beim Öffnen setzen nur Neue Karte (`NewMapDialog.cs:252`, `:266`), Fehlerbericht (`BugReportDialog.cs:91`) und Einstiegsname (`EditorShellEntryPoints.cs:110-113`) den Fokus; die anderen lassen ihn auf der Karte. Beim Schließen gibt nur der Einstiegsname ihn zurück. Keine Fokusfalle: Tab kann hinter den Dialog laufen (seit dem Fokusring aus M6 Schritt 2 sichtbar).
9. **Klick neben den Dialog:** schließt keinen (keine Pointer-Registrierung am Overlay).
10. **Skalierung (M6):** `.dialog` und `.new-map-dialog` haben `max-width`/`max-height` 94 %; die Skalierung ist auf logisch 900 px Breite geklemmt (`EditorShellScale.cs`). Dialoge ohne ScrollView (Hinweis, Einstiegsname, Parkliste-Kopf) wachsen mit dem Text.
11. **Einstellungen:** Die Kästchen „Keep the inspector…“ und „Hide DLC content“ (`.settings-toggle`) hatten laut M6-Bild 15 nicht den Stil der übrigen Schalter; Kandidat für Schritt 5.

Die Inline-Größen der Dialogdateien sind überwiegend Zeilen- und Spaltenbau: `flexDirection`, `marginBottom`, `width = 120` für Beschriftungen, `width = 26/28/60` für kleine Knöpfe und Gewichtsfelder (`EditorShellVehicles.cs:143`, `:396`, `:429`, `:459`), `flexGrow/flexShrink/flexBasis/minWidth = 0` für verkürzbare Zellen (`:340-343`, `:450-453`). Es sind wiederkehrende Muster, die in rund acht USS-Klassen aufgehen.

## Vorschlag: Dialog-Fabrik

**Ort:** neue Datei `Scripts/UI/DialogFactory.cs` (Klassen `ModalDialog`, `DialogOptions`, `DialogButton`) mit einer kleinen reinen Klasse `DialogRules` für die Tastenlogik (damit sie in EditMode-Tests ohne Panel läuft) und Anschluss im Gerüst `EditorShellOverlays.cs`. Kein neuer Namespace, keine neue Assembly. Skizze (nicht verbindlich):

```csharp
internal enum DialogWidth { Narrow /*420*/, Standard /*560*/, Wide /*720*/, Form /*880*/ }
internal enum DialogButtonRole { Primary, Secondary, Danger }

internal sealed class DialogButton
{
    public string TextKey;            // Loc-Schlüssel, z. B. "timetable.apply"
    public Action Clicked;            // null = nur schließen
    public DialogButtonRole Role;
    public bool IsDefault;            // Enter löst ihn aus (Regel in DialogRules)
    public bool IsCancel;             // Esc löst ihn aus
}

internal sealed class DialogOptions
{
    public string Name;               // UXML-Name wie bisher ("timetable-dialog"), Prüfargumente und Tests brauchen ihn
    public string TitleKey;
    public string DescriptionKey;     // optional
    public DialogWidth Width = DialogWidth.Standard;
    public bool BodyScrolls = true;   // Body ist eine ScrollView mit max-height in Prozent
    public bool CloseOnBackdrop;      // Standard falsch (Frage 3)
    public IList<DialogButton> Buttons;
    public Action Closed;             // wie der dritte Parameter von RegisterOverlay
}

internal sealed class ModalDialog
{
    public VisualElement Overlay { get; }   // was die Registry sieht
    public VisualElement Body { get; }      // Inhalt kommt hierher
    public Label Title { get; }
    public Label Description { get; }
    public Button Primary { get; }          // Text und Sperre ändern (Updates)
    public IReadOnlyList<Button> Buttons { get; }
    public bool IsShown { get; }
    public static ModalDialog Create(DialogOptions options);  // baut Overlay, Titel, Beschreibung, Body, Knopfzeile
    public void Show();   // display Flex, Fokus auf erstes .dialog-autofocus-Feld sonst Standardknopf, vorherigen Fokus merken
    public void Hide();   // display None, Fokus zurück
}
```

Anschluss: In `EditorShellBootstrap` entsteht `private ModalDialog NewDialog(DialogOptions options)`, das `ModalDialog.Create`, `rootElement.Add` und `RegisterOverlay(dialog.Overlay, true, options.Closed)` in einem Zug erledigt; ein Dialog kann dann nicht mehr gebaut, aber nicht angemeldet werden (Anlass von M3). Formdialoge (Nr. 4 bis 6) setzen ihre Form in `Body` und verlieren Overlay-Name, `AddToClassList("dialog")` und Knopfzeile; Zustände (Updates ändert Text und Wirkung des primären Knopfes) laufen über `Primary`/`Buttons`.

**Verhalten, das die Fabrik allein trägt** (heute verstreut oder fehlend):

- **Esc:** unverändert über `OverlayRegistry`; zusätzlich löst Esc den `IsCancel`-Knopf aus, damit ein „Abbrechen“ mit Aufräum-Logik nicht umgangen wird. „Alle auf einmal schließen“ bleibt, bis es zwei modale Dialoge zugleich gibt (heute nie); Entscheidung in Schritt 6.
- **Enter:** `IsDefault`-Knopf, wenn der Fokus nicht in einem mehrzeiligen Feld und nicht auf einem anderen Knopf steht (Frage 2); Regel in `DialogRules.EnterActivates(...)`, getestet.
- **Fokus:** `Show` setzt den Fokus, `Hide` gibt ihn zurück (Standard Karte); Tab bleibt im Dialog (Fokusfalle am Overlay).
- **Scroll:** `Body` ist eine `ScrollView` mit `max-height` in Prozent des Overlays; Titel und Knopfzeile bleiben stehen; kein Dialog setzt `maxHeight` mehr inline.
- **Breite als Klasse:** `.dialog-narrow/-standard/-wide/-form` in `EditorShell.uss`, alle mit `max-width: 94 %` (Werte nach Frage 4).
- **Knopfrollen:** `Primary` = `.dialog-primary`, `Danger` = Rot, `Secondary` = `.map-control-button`; höchstens ein Primär je Dialog (Test).
- **Texte:** `LocBindings` für Titel, Beschreibung und Knöpfe (Sprachwechsel zur Laufzeit).

**USS-Klassen statt Inline-Größen** (neu in `EditorShell.uss`, mit den `--mf-space-*`-Tokens): `.form-row`, `.form-row-label` (120 px, schrumpft nicht), `.form-row-field`, `.form-cell-flex` (`flex: 1 1 0; min-width: 0`), `.form-small-button` (26 px), `.form-narrow-field` (60 px), `.form-section-title`, `.form-footnote`. Sie ersetzen die 106 Inline-Werte der Dialogdateien.

**Einordnung Luftbild-Fenster:** Es ist nicht modal, schwebt neben der Karte, ist verschiebbar (`FloatingWindow.MakeMovable`), hat Reiter, eigene Größe und Fußzeile; die Tasten bleiben bei der Karte (`EditorShellBootstrap.cs:6635`). Es wird deshalb **nicht** in die Fabrik gezwungen, sondern als „Werkzeugfenster“ ausdrücklich dokumentiert (ADR-098-Nachtrag M7, Architecture.md) und behält `RegisterOverlay(…, false, …)`. Straßentyp- und Ampel-Fenster bleiben auf `FloatingWindowFrame`. Ob Luftbild-Fenster und `FloatingWindowFrame` eine gemeinsame Kopfzeile bauen können, wird in Schritt 6 geprüft (Frage 6).

## Arbeitsplan

Dateibesitz: ein Bearbeiter (frontend) für `EditorShell.uss`, `EditorShell.uxml` und `DialogFactory.cs`; `EditorShellBootstrap.cs` ändern nur die Schritte 0, 5 und 6 an kleinen Stellen. Reihenfolge: Prüfgrundlage, Fabrik mit Tests (kein sichtbarer Wandel), einfache Dialoge, die zwei großen, UXML-Dialoge, Sonderform und Aufräumen. Jeder Schritt bekommt einen Player-Build im Hintergrund (vorher ansagen), Vorher-/Nachher-Bilder mit `-language en-US` bei 1100 × 700 und 1600 × 900, mit `-uiScale 1` und `-uiScale 2`. Argumente zum Auslösen (gegen den Code geprüft; Testprojekt auf Grundorf mit Arbeitskopie nötig, wo vermerkt):

| Dialog | Argument |
|---|---|
| Fahrplan | `-timetable [Linie]` (`EditorShellTimetable.cs:609`; Projekt, bearbeitbar), dazu `-timetableShift`, `-timetableSeries`, `-timetableApply` |
| Fahrzeuge | `-vehicles [Gruppe]` (`EditorShellVehicles.cs:487`; Projekt, bearbeitbar) |
| Parkliste | `-parkList` (`EditorShellParkList.cs:174`) |
| Tageskurven | `-trafficCurves`, `-trafficCurveSet`, `-trafficCurvesApply` (`EditorShellTrafficCurves.cs:143`) |
| Karteneinstellungen | `-mapSettings` (`EditorShellMapSettings.cs:69`; Projekt) |
| Ordnerwahl | `-contentFolders` (`EditorShellContentFolders.cs:281`) |
| Einstellungen | `-openSettings` (`EditorShellLocalization.cs:156`) |
| Neue Karte | `-newMap [Vorlage]` (`EditorShellBootstrap.cs:1077`; nur ohne geöffnete Karte) |
| Fehlerbericht | `-bugReport` (`:1052`) |
| Updates | `-checkUpdates` (`EditorShellUpdates.cs:113`; braucht Netz, sonst Fehlerzustand als Bild) |
| Kartenprüfung | `-check [-checkTopic Titel]` (`:1370`) |
| Luftbild-Fenster | `-satellite` mit `-satelliteTab`, `-satelliteClose` (`EditorShellAerial.cs:1389`, `EditorShellPlacement.cs:263`) |
| Export | nur `-exportMap` (öffnet und führt aus, `:1359`); **kein** Argument, das den Dialog nur zeigt |
| Einstiegsname, Ampel-Hinweis | **kein** Argument |

Es fehlen also drei Argumente für die Prüfung: `-exportDialog`, `-entryNameDialog`, `-signalNotice` (zeigen den Dialog ohne Wirkung). Sie kommen in Schritt 0 und in `CommandLine.md`.

| Schritt | Inhalt | Umfang | Dateien | Prüfung und Akzeptanz |
|---|---|---|---|---|
| 0 | **Prüfgrundlage.** Vorher-Bilder aller 13 Dialoge (100 % und 200 %, 1100 × 700); die drei fehlenden Argumente; Test `DialogCodeRulesTests` (liest die `*.cs` in `Scripts/UI`, zählt `dialog-overlay` außerhalb der Fabrik und Inline-Größen; anfangs rot, danach Schranke); offene Fragen der Bestandsaufnahme klären (Esc gegen Abbrechen in Fahrplan und Fahrzeugen; Fokus nach dem Schließen) | S | `Tests/Editor/DialogCodeRulesTests.cs` (neu), `EditorShellBootstrap.cs` (3 Argumente), `CommandLine.md` | 13 Bilder vorhanden und angesehen; Test zeigt 13 und 172; Befunde in den Plan zurückgetragen |
| 1 | **Fabrik und Regeln.** `DialogFactory.cs`, `DialogRules`, USS-Klassen `.dialog-narrow/-standard/-wide/-form` und `.form-*`, `NewDialog` im Gerüst; **noch kein Dialog umgestellt** | M | `DialogFactory.cs`, `EditorShellOverlays.cs`, `EditorShell.uss`, `Tests/Editor/DialogRulesTests.cs` | Unit-Tests für Enter-Regel, Rollen (höchstens ein Primär), Fokusrückgabe; Build ohne Warnung; Bilder unverändert |
| 2 | **Einfache Dialoge:** Ampel-Hinweis (kleinster, erste Probe), Einstiegsname (Enter und Aufräum-Aktion), Parkliste, Ordnerwahl. Die Klasse `.content-folders` als Farbumweg fällt weg (Farbe kommt aus `.dialog`) | M | `EditorShellSignalNotice.cs`, `EditorShellEntryPoints.cs`, `EditorShellParkList.cs`, `EditorShellContentFolders.cs`, `EditorShell.uss` | Bilder `-parkList`, `-contentFolders` vorher/nachher gleich bis auf Textfarbe; Einstiegsname und Hinweis von Hand (Enter, Esc, Fokus); Tests grün |
| 3 | **Verkehrskurven und Fahrzeuge.** Breite 640/620 auf Klasse, Zeilenbau (Fahrzeuge 39, Kurven 7) auf `.form-*`; `maxHeight = 520` fällt zugunsten der prozentualen Rolle | M | `EditorShellTrafficCurves.cs`, `EditorShellVehicles.cs`, `EditorShell.uss` | `-trafficCurves`, `-vehicles`, `-vehicles <Gruppe>` bei 100/200 %, 1100 und 1600 px: nichts abgeschnitten, Knopfzeile sichtbar; `-trafficCurvesApply` im Log unverändert |
| 4 | **Fahrplan (riskantester Schritt).** `EditorShellTimetable.cs` (32) und `EditorShellTimetableManage.cs` (24) bauen Zeilen für viele Zustände (Linien, Fahrten, Serien, Verschieben, Umbenennen, Löschen, neue Linie). **Zwei Stufen:** (a) nur Rahmen auf die Fabrik, Inhalt unverändert; (b) Inline-Werte in `.form-*`, Zeile für Zeile mit Bild | L | beide Dateien, `EditorShell.uss` | `-timetable`, `-timetable <Linie>`, `-timetableShift`, `-timetableSeries`, `-timetableApply`; Bilder je Zustand vor/nach in 100/200 %; die geschriebenen Fahrplandateien im Testprojekt bytegleich zum Vorher-Lauf |
| 5 | **UXML- und Formdialoge:** Export, Einstellungen, Kartenprüfung, Neue Karte/Karteneinstellungen, Fehlerbericht, Updates. Die sechs UXML-Overlays fallen weg, die Fabrik baut sie in den `SetUp…`-Methoden; Formklassen behalten Inhalt, verlieren Overlay-Name und Knopfzeile; Schalter der Einstellungen im Stil der übrigen | L | `EditorShell.uxml`, `EditorShellBootstrap.cs`, `EditorShellSettings.cs`, `EditorShellBugReport.cs`, `EditorShellUpdates.cs`, `NewMapDialog.cs`, `BugReportDialog.cs`, `UpdateDialog.cs` | `-newMap`, `-mapSettings`, `-openSettings`, `-check`, `-bugReport`, `-checkUpdates`, `-exportDialog` vorher/nachher; Sprachwechsel zur Laufzeit; Elementnamen unverändert (Test) |
| 6 | **Sonderform und Aufräumen.** Luftbild-Fenster und `FloatingWindowFrame` als Sonderform dokumentiert (Architecture.md, ADR-098-Nachtrag „M7“); `.dialog-overlay` nur noch in Fabrik und USS; tote Klassen und Inline-Werte entfernen; die übrigen 66 Inline-Größen klassifizieren (berechnet mit Kommentar oder in USS); Test auf die Akzeptanz scharf stellen | M | `SatelliteWindow.cs`, `FloatingWindowFrame.cs`, Dokumentation, `DialogCodeRulesTests.cs` | Test grün; Bilder aller Dialoge bei 100/150/200 %; Review durch reviewer; Validierungsstand in `DevelopmentSetup.md` |
| 7 | **Abschluss.** Reviewer gegen diese Tabelle; ADR 098 Nachtrag „M7“, Roadmap (Häkchen), README, Architecture, `CommandLine.md`; „Record Reviewed Player Assets“ (durch den Nutzer) | S | Dokumentation, `PlayerAssetReview.txt` | Akzeptanz unten |

**Akzeptanz M7:** (1) Keine von Hand gebaute `dialog-overlay` außerhalb der Fabrik (UXML und Code; Test). (2) Unter 20 Inline-Größen in `Scripts/UI` nach der in Frage 5 gewählten Zählung. (3) Jeder der 13 Dialoge: Esc schließt, Enter wirkt wie festgelegt, Fokus beim Öffnen gesetzt und beim Schließen zurückgegeben, Tab bleibt im Dialog. (4) Bilder bei 100/150/200 % ohne Abschneiden und ohne Knopfzeile unter dem Rand. (5) Alle EditMode-Tests grün, `LocaleCheck --strict` 0/0. (6) Das Luftbild-Fenster ist als Sonderform dokumentiert.

### Risiken

- **Fahrplan-Dialog (Schritt 4):** größter und zustandsreichster Dialog (56 Inline-Werte, Zeilenbau je Zustand in zwei Dateien, Umbenennen und Löschen mit Rückfragen, Änderungen im Dialog vor dem Anwenden). Fehler zeigen sich nur in seltenen Zuständen. Gegenmaßnahme: zwei Stufen, Bild je Zustand, Byte-Vergleich der geschriebenen Dateien.
- **Esc schließt alle Überlagerungen zugleich** (`OverlayRegistry.CloseAllShown`): bei einem späteren zweiten modalen Dialog (Rückfrage über einem Dialog) wäre das falsch. Heute nie zwei zugleich; Test und Entscheidung in Schritt 6.
- **Fokusfalle:** UI Toolkit hat keine eingebaute; ein Tab-Umlauf per `KeyDownEvent` am Overlay kann mit `EditorShellFocusRing.cs` kollidieren. In Schritt 1 an einem Dialog prüfen, bevor sie überall gilt.
- **Elementnamen:** Prüfargumente, Tests und Bindungen suchen nach Namen (`export-dialog`, `settings-dialog`, `timetable-dialog` …). `DialogOptions.Name` hält sie; ein Test in Schritt 0 hält die 13 Namen fest.
- **Sprachwechsel zur Laufzeit** der heute in der UXML mit Text gebauten Dialoge (Export, Einstellungen, Kartenprüfung): die Fabrik bindet über `LocBindings`; LocaleCheck und `TooltipTextTests` zeigen verwaiste Schlüssel.
- **Player-Asset-Review (harte Regel 3):** `EditorShell.uss` und `EditorShell.uxml` stehen in `PlayerAssetReview.txt`; jede Änderung warnt im normalen Build und stoppt `BuildRelease`, bis der Nutzer „Record Reviewed Player Assets“ ausgeführt hat (Schritt 7).
- **Viel Fläche, wenig sichtbarer Gewinn** (Roadmap): sichtbar werden vor allem einheitliche Breiten und Knopfzeilen; die Vorher-/Nachher-Bilder dokumentieren es.
- **Tests brauchen Unity:** wie bei M6 laufen die neuen Tests in der Projektkopie im Scratchpad; `DialogRules` bleibt rein.

## Entscheidungsfragen an den Projektinhaber

1. **Reihenfolge und Beschriftung der Knöpfe.** Heute steht überall der abbrechende Knopf links und der primäre rechts, die Beschriftung des primären ist frei (Apply, OK, Create, Export, Send, Close). (A) So lassen, in der Fabrik festschreiben (Abbrechen links, Primär rechts; Ein-Knopf-Dialoge „Close“, Hinweise „OK“). (B) Wie A, plus einheitlich „OK“ und „Cancel“, außer bei Aktionen mit eigenem Namen (Export, Create, Send). (C) Primär links (Windows-Stil „OK, Cancel“). **Empfehlung A:** nichts ändert sich für den Nutzer, die Fabrik hält es fest; B kostet Texte in acht Sprachen ohne Gewinn.
2. **Enter-Verhalten.** Heute bestätigt nur der Einstiegsname mit Enter. (A) Enter löst überall den primären Knopf aus, außer im mehrzeiligen Feld und auf einem anderen Knopf. (B) Enter nur in Dialogen mit genau einem Textfeld (Einstiegsname, Fehlerbericht), sonst nicht. (C) Enter nie, nur Maus und Tab. **Empfehlung B:** kein versehentliches „Apply“ im Fahrplan oder bei der Ordnerwahl, deckt aber die Fälle, in denen man tippt und bestätigt.
3. **Schließen per Klick neben den Dialog.** Heute schließt kein Dialog so. (A) Nie, nur Knopf oder Esc. (B) Nur bei Dialogen ohne Eingaben (Hinweis, Kartenprüfung, Updates). (C) Bei allen außer denen mit Änderungen im Dialog (Fahrplan, Fahrzeuge, Kurven, Neue Karte). **Empfehlung A:** ein Fehlklick darf keine Eingaben verwerfen; Esc und Knopf reichen.
4. **Einheitliche Breiten.** Heute 560 (Standard), 640 (Kurven), 620 und 560 als Mindestbreite (Fahrzeuge, Fahrplan), 880 (Karte). (A) Vier Klassen: Schmal 420 (Hinweis, Einstiegsname), Standard 560, Breit 720 (Fahrplan, Fahrzeuge, Kurven), Formular 880 (Karte, Karteneinstellungen); alle mit `max-width: 94 %`. (B) Drei Klassen ohne Schmal (560/720/880). (C) Alles 560 außer der Karte mit 880 (Fahrplan rollt mehr). **Empfehlung A:** kleine Dialoge wirken sonst verloren breit, die großen behalten, was sie haben; sichtbar ändern sich nur Hinweis, Einstiegsname und Kurven.
5. **Zählung der „Inline-Größen“ für die Akzeptanz „unter 20“.** Enge Zählung (nur Breite/Höhe/min/max) heute 66, mit Abständen 129, mit Flex 172. (A) Breite Zählung (172); rechnerische Werte (Fortschrittsbalken in Prozent, Einrückung nach Tiefe, Geometrie von Kreuzungsschnitt und Fenstern beim Ziehen) sind begründete Ausnahmen mit Kommentar `// computed` und vom Test ausgenommen. (B) Enge Zählung (66 auf unter 20): leichter, aber Abstände und Flex blieben inline. (C) Alles ohne Ausnahme: unter 20 nur mit Umbau von Kreuzungsschnitt und Fenstergeometrie, die nicht zu M7 gehören. **Empfehlung A:** das ist, was M7 meint (keine gestalterischen Werte im Code); die Ausnahmen sind im Test sichtbar.
6. **Reichweite.** (A) Nur die 13 modalen Dialoge; Luftbild-, Straßentyp- und Ampel-Fenster bleiben dokumentierte Sonderform. (B) Zusätzlich die Rückfrage im Ampel- und Straßentyp-Fenster (`FloatingWindowFrame`) auf dieselben Knopfrollen und denselben Fokusbaustein. (C) Zusätzlich das Luftbild-Fenster auf die Kopfzeile des `FloatingWindowFrame`. **Empfehlung A**, B und C als Folgeschritt, wenn Schritt 6 zeigt, dass es ohne Risiko geht: Die Fenster sind abgenommen und haben eigenes Verhalten (Verschieben, Größe, Rückfrage vor dem Verlassen).

Nicht gefragt (steht fest): Dialoge bleiben modal mit gesperrten Tastenkürzeln (M3); Esc schließt weiter; Texte in acht Sprachen, `LocaleCheck --strict` bleibt Pflicht; keine neuen Farben, nur `--mf-*`-Tokens.

## Entscheidungen des Projektinhabers (22. November 2026)

| Nr. | Frage | Entscheidung |
|---|---|---|
| 1 | Reihenfolge und Beschriftung der Knöpfe | **A.** Wie heute: Abbrechen links, Primär rechts; in der Fabrik festschreiben. Ein-Knopf-Dialoge „Close“, Hinweise „OK“. |
| 2 | Enter-Verhalten | **B.** Enter bestätigt nur in Dialogen mit genau einem Textfeld (Einstiegsname, Fehlerbericht), sonst nicht. |
| 3 | Klick neben den Dialog | **A.** Schließt nie; nur Knopf oder Esc. |
| 4 | Breiten | **A.** Vier Klassen: Schmal 420 / Standard 560 / Breit 720 / Formular 880, alle mit `max-width: 94 %`. |
| 5 | Zählung der Inline-Größen | **A.** Breite Zählung (172, Ziel unter 20); berechnete Werte als `// computed`-Ausnahmen, vom Test ausgenommen. |
| 6 | Reichweite | **A.** Nur die 13 modalen Dialoge; Luftbild-, Straßentyp- und Ampel-Fenster bleiben dokumentierte Sonderform. |

## M7 Schritt 0 – Befunde (22. November 2026)

- **Esc gegen Abbrechen (Fahrplan, Fahrzeuge):** Dasselbe. Fahrplan: „Cancel“ ruft `CloseTimetable` = `HidePopup`; Esc ruft `OverlayRegistry.CloseAllShown` = `Hide` je Überlagerung. Der Fahrplan-Entwurf (`timetableLines`, `timetableTrips`, `timetableDirty*`) wird beim nächsten `OpenTimetable` geleert; geschrieben wird nur über „Apply“. Fahrzeuge: Änderungen wirken sofort als Undo-Schritte (`ChangeVehicles`), „Close“ ruft `HidePopup`, Esc dasselbe; es gibt nichts zu verwerfen. Keiner der beiden meldet eine `Closed`-Aktion an. Auch beim Einstiegsnamen (Antwort verwerfen, Fokus an die Karte) und bei Updates (`CloseUpdates` bricht die Prüfung ab) tun Esc (über `Closed`) und Knopf dasselbe. Folge für die Fabrik (Schritt 1): Esc muss den Abbrechen-Knopf nicht eigens auslösen, solange jede Aufräumarbeit als `Closed` angemeldet ist; ein Test soll das für alle 13 Dialoge festhalten.
- **Prüfargumente:** `-exportDialog`, `-entryNameDialog` (mit Projekt), `-signalNotice` sind umgesetzt (`EditorShellBootstrap.cs`, `CommandLine.md`).
- **Test `DialogCodeRulesTests`:** zwei Obergrenzen (13 `dialog-overlay`-Konstruktionen, 172 Inline-Größen in `Scripts/UI`); Zeilen mit `// computed` sind ausgenommen.
