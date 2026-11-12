# UI-Rework: technische und architektonische Bewertung (12. November 2026)

Grundlage und Belegstufen: siehe [ui-rework-audit.md](ui-rework-audit.md). Dieses Dokument bewertet die Struktur hinter der Oberfläche und nennt je Befund eine Korrekturrichtung. Es ändert nichts.

## 1. Bestehende Architekturentscheidungen (bleiben, bevor etwas vorgeschlagen wird)

| Entscheidung | Quelle |
|---|---|
| UI → OMSI → Core; die OMSI-Schicht kennt keine Oberfläche und kein Unity | CLAUDE.md, Architecture.md |
| Die Shell ist eine `partial`-Klasse; neue Teile als eigene Datei | CLAUDE.md |
| Schreibrechte prüft jeder Befehl selbst (`CanEdit`, `CanEditObjects`); die Undo-Historie liegt in der Shell (`PushHistory`, `StepHistory`) | Backend-Analyse, `EditorShellBootstrap.cs:6544` |
| Undo für Platzieren/Löschen über Dateistände, Verschieben/Drehen wertbasiert | CLAUDE.md, harte Regel 6 |
| Arbeitskopie nur über `OmsiMapWorkspace.Save`/`Revert` | ADR 057 |
| Layout: untere Leiste, Popups, Inspector auf Abruf, Kontextmenü; Menüleiste bleibt; nur Lucide-Icons | ADR 098 |
| Farben nur über `--mf-*`-Tokens; Orange aktiv, Rot Löschen/Fehler, Gelb bearbeitet/Warnung | ADR 015, CorporateDesign.md |
| Kataloge höchstens 210 px; einspaltige Werkzeuge und Straßenbauer so hoch wie ihr Inhalt | ADR 098 (Entscheidung des Nutzers) |
| Hinweise der Werkzeuge stehen in der Statuszeile | ADR 098 (Entscheidung des Nutzers) |
| Der Inspector wird bei jeder Auswahl gezeichnet, auch geschlossen, damit `-inspectorTo` und Prüfläufe funktionieren | ADR 098 |

Keiner der Vorschläge unten hebt eine dieser Entscheidungen auf. Zwei berühren sie und brauchen deshalb die Zustimmung des Nutzers: ein eigener Platz für Hinweise in der Statuszeile (Abschnitt 4) und die Modusanzeige an der Projektpille (Abschnitt 4).

## 2. Komponenten der Werkzeug-Fenster

Bausteine: `ToolCatalog` (Kopfzeile, Kachelbereich, Leer-Hinweis; `Options()` als Spalte), `OptionControls` (`Row`, `Group`, `Choice`, `Switch`, `Value`, `ValueBox`), `LocBindings`, `IconElement`.

| Fenster | Katalog | Optionsspalte | Optionsstil | Beleg |
|---|---|---|---|---|
| Straße | `ToolCatalog` | selbst gebaut (`RoadToolPanel.cs:93`) | Icons (`OptionControls`) | `RoadToolPanel.cs:187` |
| Objekte | `ToolCatalog` | selbst gebaut (`:97`) | Textzeilen | `ObjectLibraryPanel.cs:131` |
| Gelände | `ToolCatalog` ohne Kachel-Scroll | selbst gebaut (`:53–55`) | Textzeilen, `picker-segmented` | `TerrainToolOptions.cs:59` |
| Markierungen | `ToolCatalog` | `ToolCatalog.Options()` | Textzeilen | `MarkingToolOptions.cs:174`, `:125` |
| Reihen | – | selbst gebaut (`:37–39`) | Textzeilen | `RowToolOptions.cs` |
| Linie | – | selbst gebaut (`:30–31`) | Textzeilen, 8 Inline-Größen | `LineToolPanel.cs` |
| Vorfahrt, Spuren, Ampeln, Labels | – | `ToolCatalog.Options()` | alt | `EditorShellPriorities.cs:49`, `EditorShellLanes.cs:42`, `EditorShellSignals.cs:55`, `EditorShellLabels.cs:128` |
| Straßenbauer | eigener Kopf mit Abzeichen | eigen | alt | `RoadBuilderPanel.cs:84–102` |

**Bestätigte Schulden**
- **`Options()` liefert keine scrollende Spalte.** Deshalb bauen fünf Fenster die Spalte weiter von Hand; ADR 098 und Architecture.md beschreiben sie als vereinheitlicht. *Richtung:* `Options(bool scrolling)`, dann die fünf Stellen umstellen.
- **`OptionControls` hat genau einen Nutzer** (Straße). Die Textzeile `picker-option` ist viermal kopiert (`TerrainToolOptions.cs:438`, `RowToolOptions.cs:182`, `LineToolPanel.cs:234`, `RoadBuilderPanel.cs:1049`).
- **Kacheln siebenmal von Hand** (`picker-tile` in Objekte, Straße, Gelände, Markierungen, Straßenbauer). *Richtung:* `ToolCatalog.AddTile(...)`.
- **Kopplung an die Kind-Reihenfolge:** `catalog.Panel.Insert(1, …)` in Objekten und Gelände hängt am inneren Aufbau von `ToolCatalog`. *Richtung:* benannter Platz („über den Kacheln“).
- **Leistenhöhe an sechs Stellen** (94, 54, zweimal 106, zweimal 104 px). *Richtung:* die vier `bottom`-Werte beim `GeometryChangedEvent` der Leiste setzen; `FreeMapInsets()` (`:7394`) rechnet schon so.
- **Tooltip-Titel über Text und Regex:** `LocBindings.TitledTooltip` setzt „Name: Text“ zusammen, `EditorShellTooltip` zerlegt ihn wieder (höchstens drei Wörter). Ein längerer Reitername in einer Sprache wird stumm zu Fließtext. *Richtung (Vorschlag):* Titel getrennt mitgeben, Regex als Rückfall.

## 3. Dialoge und Überlagerungen

Fünf Bauweisen ohne gemeinsame Basis:

| Bauweise | Wo |
|---|---|
| UXML `dialog-overlay` + `dialog` | Export, Settings, Check (`EditorShell.uxml:112–152`) |
| UXML-Platzhalter, Inhalt als eigene Klasse | `NewMapDialog`, `BugReportDialog`, `UpdateDialog` |
| Von Hand in `partial`-Dateien, fünfmal dasselbe Gerüst | `EditorShellContentFolders.cs:44`, `EditorShellParkList.cs:28`, `EditorShellTimetable.cs:43`, `EditorShellTrafficCurves.cs:27`, `EditorShellVehicles.cs:33` |
| Schwebendes Fenster ohne Abdeckung | `SatelliteWindow.cs:105` |
| Nicht modale Popups | Menüs, `bar-popup`, Kontextmenü, Tooltip |

- `Dialogs()` zählt zwölf Elemente von Hand auf (`EditorShellBootstrap.cs:5969–5983`). Ein neuer Dialog, der dort fehlt, schließt nicht mit Esc (Risiko).
- `CloseTopOverlay` schließt **alle** sichtbaren Dialoge auf einmal (`:5994–6010`); es gibt keinen Stapel. Was einzelne Dialoge beim Schließen aufräumen müssten (außer Luftbild und Update): unbekannt.
- **Bestätigter Fehler:** Dieselbe Liste dient zwei Zwecken – „Esc schließt“ und „Tastenkürzel gesperrt“ (`:6045`). Für das nicht modale Luftbild-Fenster ist nur der erste richtig. *Richtung:* zwei Abfragen (alle Überlagerungen für Esc, nur modale für die Sperre).
- *Richtung (P2, Vorschlag):* eine Fabrik für modale Dialoge, die sich selbst für Esc anmeldet; die fünf Kopien und die 105 Inline-Größen (Fahrplan, Fahrzeuge) ziehen dann nach.

## 4. Statuszeile als einziger Kanal

Seit „Hinweise in die Statuszeile“ teilen sich drei Dinge ein Label: Meldungen nach einer Aktion, laufende Hinweise der Werkzeuge und (versteckt) der Modus der Karte.

- **Bestätigt:** `ShowToolHint` schreibt über `SetStatus` (`:7784–7790`) und wird bei jeder Zeigerbewegung im Straßen- und Gelände-Werkzeug ausgelöst. Meldungen wie „Bau fehlgeschlagen“ mit Grund sind nach der nächsten Mausbewegung weg.
- **Bestätigt (Code):** Hinweise, die gesendet werden, bevor das Fenster sichtbar ist, verwirft die Sichtbarkeitsprüfung (`:7786`); der Titel der Typwahl erscheint deshalb nie, der erste Hinweis der Objektbibliothek meist nicht.
- **Bestätigt:** Nach dem Schließen eines Fensters bleibt der letzte Hinweis samt Warnfarbe stehen. Hinweise tragen keinen Schlüssel und wechseln die Sprache erst mit der nächsten Bewegung.
- **Bestätigt:** Der Modus der Karte („nur lesen“, Chrono-Datum, Variante, geänderte Dateien) ist nirgends dauerhaft sichtbar (`EditorShell.uss:552`).
- *Richtung:* in der Statuszeile ein eigener Platz für den Hinweis des offenen Werkzeugs, der beim Schließen leer wird; `ToolCatalog` merkt sich den letzten Hinweis und gibt ihn beim Einblenden ab; der Modus als kleines Abzeichen an der Projektpille. Beides ändert Entscheidungen des Nutzers im Detail und braucht seine Zustimmung.

## 5. Modus- und Zustandsmodell

- **Ein Feld, drei Ausstiege.** `activeTool` wird in `SetTool` (`:7283–7354`, acht `was*`-Flags, je ein `End*/Begin*`-Paar), in `PresentSummary` (`:1943–1994`, direkt gesetzt) und über die Esc-Kette in `OnShortcut` (rund 15 Zweige) verlassen.
- **Bestätigt:** `PresentSummary` beendet nur Straße, Markierungen, Vorfahrt, Ampeln und Spuren. Reihen, Gelände und Linie werden auf Drag gesetzt, ohne ihr `End…Tool`; Fenster und Vorschau bleiben stehen.
- **Bestätigt:** Die Wahl-Modi (`PickingElementType`, `PickingSplineType`, `PickingVarParent`, `pickingRowObject`, Platzieren) sind eine zweite Achse ohne gemeinsamen Ausstieg; `SetTool` beendet nur die Spline-Typwahl (`:7299`).
- **Risiko:** `RoadClicked` endet im `else` bei `BuildRoadTo` (`:577–578`), und `BuildRoadClick` prüft das Werkzeug nicht. Ein neues Werkzeug, das den Straßenmodus der Karte nutzt und in der Verteilung fehlt, baut beim Klick Straßen.
- **Was ein neues Werkzeug heute braucht:** Enum und Leiste, sechs Stellen in `SetTool`, `PresentSummary`, die Zeigerverteilung (`:534–579`), Tasten- und Esc-Zweige, `RefreshAfterChange`, `ShowObjectsPanel`.
- *Richtung (P1):* erst `LeaveTransientModes()` (alle Wahl-Modi, Platzieren, Kontextmenü), aufgerufen von `SetTool`, `ShowMap` und `PresentSummary`; danach eine Werkzeug-Tabelle mit `Begin`, `End`, Zeiger, Klick, Esc und Nur-lesen-Schlüssel, die diese drei Stellen nutzen. Schrittweise: die Tabelle zuerst nur für `End` und Esc, das Verhalten bleibt gleich.

## 6. Kontextmenü gegen bisherige Einstiege

| Eintrag | Bisheriger Einstieg | Ergebnis |
|---|---|---|
| Copy, Duplicate, Delete, Delete N | Tasten, Leiste, Inspector | dieselbe Funktion |
| Level ground, Delete entry point, Change type… | Inspector | dieselbe Funktion; Typwahl siehe Abschnitt 5 |
| Delete spline, Varianten-Einträge | Entf, Varianten-Panel | dieselbe Funktion |
| Rename…, Labels…, Heights…, AI paths… | Inspector | öffnen nur eine Gruppe; „paths“ tut stumm nichts, solange die Pfade nicht gelesen sind |
| **Paste here** | Strg+V | **eigener Pfad** ohne Vorbereitung (Audit, Fehler 2) |
| **Revert tile edits** | Inspector-Knopf | dieselbe Funktion, aber ohne Rückfrage; leert die Historie (Audit, Fehler 3) |
| Delete (Einstiegspunkt) | Entf löscht das Objekt | Menü löscht nur den Einstiegspunkt – vertretbar, undokumentiert |

- **Lehre für weitere Einträge (Reihen, Kreuzungen, Ampeln):** nur vorhandene Befehle aufrufen, nie nachbauen. Das Muster `Fill…Menu` trägt; gebraucht werden ein Treffertyp in `MapContextHit` und ein Spline-Treffer in 3D.
- **Gut:** In Straßen-, Platzier- und Wahl-Modus erscheint das Menü nicht (über `roadMode` gesperrt); verlorene Ereignis-Abmeldungen wurden nicht gefunden.

## 7. Neuzeichnen und Leistung

- **Bestätigt:** `RenderInspector` hat Nebenwirkungen auf die Karte (Markierung der Spuren im Regel-Editor, `RefreshJunctionMarkers`; `:8184–8185`, `EditorShellPaths.cs:496`). Den geschlossenen Inspector einfach zu überspringen würde sie abschalten.
- **Bestätigt:** Je Objektauswahl wird `global.cfg` gelesen (`EntryPointOf`) und zweimal der Fahrplan (`IsBusStop`, `BusStopName`); ein Rechtsklick wiederholt beides. Dazu lineare Suchen über alle Kacheln und, bei eingeschalteten Pfaden, über alle Pfade.
- **Unbekannt:** die Kosten auf Berlin-Spandau. [PerformanceBaseline.md](PerformanceBaseline.md) nennt rund 85 ms für das Auffrischen insgesamt, ohne Anteil des Inspectors.
- *Richtung (P2):* erst messen; dann die Lesungen mit dem Änderungszähler aus ADR 057 zwischenspeichern und die Karten-Nebenwirkungen aus `RenderInspector` lösen.

## 8. Reste und toter Code

- **USS ohne Verwendung (mindestens 16 Klassen):** `floating-panel-collapsed` (`:128–131`), `panel-list`, `panel-row*` (Reste des Project-Fensters), `panel-heading`, `panel-search-icon`, `picker-title`, `primary-button`, `project-tabs`, `road-picker-new`, `satellite-column(s)`, `view-control-separator`, `menu-popup-item-checked`, `menu-section-label`, `folders-left-out`.
- **Versteckt und weiter gebaut:** siehe Audit, Abschnitt 2.
- **Verwaiste Textschlüssel:** `terrain.title`, `project.empty` (Reviewer; nur `en-US` geprüft).
- **Wirkungslos:** `ShowFoldersLeftOut`.
- **Irreführende Namen:** `view-menu-button` (= Tools), `project-collapse-button` (= X des Popups), `RenderProjectBrowser`.

## 9. Testbarkeit

Die Rework-Logik steckt in privaten Instanzmethoden der Shell und ist deshalb ungetestet, obwohl sie ohne Player prüfbar wäre:

| Logik | Heute | Richtung |
|---|---|---|
| Einträge des Kontextmenüs je Auswahl | `Fill…Menu` (`EditorShellContextMenu.cs:149–241`) | reine Funktion (Auswahlart und Schalter → Liste von Schlüsseln, gefährlich ja/nein) + `ContextMenuEntriesTests` |
| Zerlegung der Tooltips | Regex in `ShowTooltip` (`EditorShellTooltip.cs:19`, `:82`) | `SplitTooltip` statisch + Test über alle Tooltips aller Sprachen |
| Taste → Werkzeug, Esc-Reihenfolge | Kette in `OnShortcut` | Tabelle + `ShortcutMapTests` |
| Leistenhöhe gegen USS-Abstände | sechs Zahlen | entfällt mit Abschnitt 2, sonst statischer Test |
| Verwaiste Textschlüssel | nur Konsolenausgabe | als Fehler, sobald die Altlast bereinigt ist |

## 10. Einstufung in einem Blick

| Stufe | Befunde |
|---|---|
| **Bestätigte Fehler** | Audit, Abschnitt 5, Punkte 1–8 |
| **Begründete Risiken** | Typwahl überlebt Kartenwechsel; `RoadClicked`-`else`; neue Dialoge ohne Esc; Kosten des Neuzeichnens auf Spandau; Regex-Titel je Sprache; Sammelschließen der Dialoge |
| **Vorschläge** | Dialog-Fabrik; `AddTile`; Alpha-Tokens; USS aufteilen; Namen bereinigen; Lesungen zwischenspeichern |
