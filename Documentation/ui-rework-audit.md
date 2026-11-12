# UI-Rework: Bestandsaufnahme (12. November 2026)

**Gegenstand:** Zweig `feature/ui-rework` (ADR 098), Stand `75601d7` plus uncommitteter Arbeitsbaum (97 Dateien: Umbau auf `ToolCatalog`, zehn gelöschte Textschlüssel in acht Sprachen, drei Dokumente).
**Art:** Analyse ohne Codeänderung. Zugehörig: [ui-rework-architecture-review.md](ui-rework-architecture-review.md), [ui-rework-feature-matrix.md](ui-rework-feature-matrix.md), [ui-rework-roadmap.md](ui-rework-roadmap.md), [ui-rework-next-milestone.md](ui-rework-next-milestone.md).
**Nicht wiederholt, sondern vorausgesetzt:** ADR 098 in [Decisions.md](Decisions.md) (Spezifikation und Verlauf), [DesignReview-2026-10-28.md](DesignReview-2026-10-28.md), [CorporateDesign.md](CorporateDesign.md), [RefactoringAudit.md](RefactoringAudit.md).

## Methode und Belegstufen

- Vier Fachanalysen liefen parallel und rein lesend (Frontend, Backend, Testing, Reviewer); die Hauptsitzung hat die Einstiegspunkte selbst erfasst und die Befunde 1–4, 7 und 8 aus Abschnitt 5 sowie die Modusanzeige und `ShowFoldersLeftOut` selbst am Code nachgelesen. Alles Übrige stützt sich auf die Agentenberichte und ist dort mit Fundstelle belegt.
- Ein Befund wurde zusätzlich am vorhandenen Player belegt (`Builds/Windows`, `Editor.UI.dll` vom 12. November 14:41, also mit `ToolCatalog`): Abschnitt 5, Punkt 1.
- **Bestätigt** = am Code (oder Player) belegt. **Risiko** = begründet, nicht bewiesen. **Vorschlag** = Verbesserung ohne Fehlerbild. **Unbekannt** = nicht prüfbar ohne Lauf oder Nutzertest.
- Nicht gelaufen: Kompilieren des Arbeitsbaums, die 697 Unity-Tests, LocaleCheck, Screenshots der Panels. Die 697 grünen Tests in ADR 098 stammen von vor dem `ToolCatalog`-Umbau.

## 1. Technik und Aufbau (A1)

- **Technik:** Unity 6.6, UI Toolkit zur Laufzeit. Eine UXML-Datei (`Assets/_Project/UI/Resources/EditorShell.uxml`, 157 Zeilen) und eine USS-Datei (`EditorShell.uss`, rund 700 Zeilen, Tokens `--mf-*`). Fast die gesamte Oberfläche entsteht in C# (`Assets/_Project/Scripts/UI/`, 88 Dateien).
- **Shell:** `EditorShellBootstrap` ist eine `partial`-Klasse über rund 60 Dateien; die Hauptdatei hat rund 8.700 Zeilen und etwa 135 Felder, alle Teile zusammen etwa 500 Felder (Näherung). Die Kartenfläche ist `MapCanvasElement` (+ `MapCanvasPaths`, `MapCanvasSignals`, `MapCanvasTiles`).
- **Fester Rahmen (UXML):** Menüleiste (File, Edit, Tools, Help) → Arbeitsbereich mit Karte, Inspector-Rahmen, `tool-panel-host`, `bar-popup` und `bottom-bar` → Menü-Popups → drei UXML-Dialoge (Export, Settings, Check) und drei Dialog-Platzhalter.
- **Navigation:**
  - *Werkzeuge* sind Modi: `EditorTool` mit elf Werten (`MapToolbar.cs:11`), dazu „Objects“ und „Delete“ als Knöpfe. Umschaltung zentral in `SetTool` (`EditorShellBootstrap.cs:7283`).
  - *Listen der Karte* sind sechs Reiter (`ProjectTab`, `EditorShellProjectTabs.cs:17`), die als ein Popup über der Leiste erscheinen (`EditorShellBottomBar.cs`).
  - *Eigenschaften* zeigt der Inspector rechts, nur auf Abruf (`EditorShellInspector.cs`, Gruppen `group-<id>`).
  - *Kontextmenü* per Rechtsklick für Objekt, Mehrfachauswahl, Spline und Kachel (`EditorShellContextMenu.cs:149–241`).
  - *Tasten* in einer Kette in `OnShortcut` (`EditorShellBootstrap.cs:6019–6280`).
- **Zustand und Datenbindung:** kein Bindungssystem. Nach jeder Auswahl und jeder Änderung zeichnen `RenderInspector` und `RenderProjectBrowser` neu (zusammen rund 48 Aufrufstellen); das Popup zeichnet nur, wenn es offen ist (`:7952`), der Inspector immer.
- **Gespeichert** wird von der Oberfläche wenig: PlayerPrefs für Inspector-Gruppen, Favoriten, Sprache, DLC-Schalter, zuletzt geöffnete Dateien, Updates. Offen/zu von Inspector und Popup wird nicht gespeichert (war es auch vorher nicht).

## 2. Stand des Reworks (A2)

Statusangaben nur mit Grundlage; „vom Nutzer getestet“ steht getrennt, weil ADR 098 und [ProjectStatus.md](ProjectStatus.md) das so führen. „bestätigt“ heißt: ADR 098 nennt die Etappe als vom Nutzer getestet – nicht, dass jeder einzelne Weg darin geprüft wurde.

| Bereich | Status | Grundlage | Nutzertest |
|---|---|---|---|
| Untere Leiste (Ansichten, Kachelknopf, Projektpille, Werkzeuge, runde Knöpfe, Statusstreifen) | Completed | `EditorShellBottomBar.cs`, `EditorShell.uxml:36–66`, Commit `cb3a032` | bestätigt (ADR 098, Nachtrag 3) |
| Popups statt Project-Fenster | Completed | `EditorShellProjectTabs.cs`, `RenderProjectBrowser` `:7941` | bestätigt |
| Inspector auf Abruf | Completed | `OpenInspector`, `EditorShell.uxml:17` | bestätigt |
| Kontextmenü Objekt/Spline/Kachel in der Draufsicht | Completed, mit Fehlern (Abschnitt 5) | `EditorShellContextMenu.cs` | bestätigt, aber Fehler 1, 2, 5 dort nicht aufgefallen |
| Esc schließt Dialoge und Popup | Completed, mit Nebenwirkung (Abschnitt 5, Punkt 4) | `CloseTopOverlay` `:5994` | bestätigt |
| Tooltips im Player | Completed | `EditorShellTooltip.cs`, Commit `044323a` | bestätigt |
| Kataloge mit einer Kopfzeile, 210 px, Kacheln 70 px (Objekte, Straße, Gelände) | Completed | Commit `75601d7` | **offen** (ProjectStatus.md, „Offen“) |
| Straßen-Optionen als Icons (`OptionControls`) | Completed | `RoadToolPanel.cs`, `OptionControls.cs` | **offen** |
| Menü *Tools*, DLC-Schalter global | Completed | `EditorShell.uxml:90–96`, `EditorShellSettings.cs` | **offen**; DLC nur als Dialog geprüft (keine DLCs installiert) |
| Gemeinsamer Baustein `ToolCatalog` (Objekte, Straße, Gelände, Markierungen) | In Progress | uncommittet; `ToolCatalog.cs`, `PickerHeader.cs` gelöscht | **offen**; weder kompiliert noch getestet in dieser Analyse |
| Optionsspalten der übrigen Werkzeuge im neuen Stil | In Progress | nur Hülle `ToolCatalog.Options()` bei Vorfahrt, Spuren, Ampeln, Labels, Markierungen; Gelände, Objekte, Reihen, Linie bauen selbst (Review, Abschnitt 2) | – |
| Straßenbauer-Fenster | Not Started | alter Kopf `tool-panel-header` (`RoadBuilderPanel.cs:84–102`) | – |
| Dialoge (Fahrplan, Fahrzeuge, Parkliste, Kurven, Karteneinstellungen, Luftbild …) | Not Started | fünf Bauweisen, vom Rework nur über Esc berührt | – |
| Kontextmenü: Reihen, Kreuzungen, Ampeln; Splines in 3D | Not Started | Roadmap.md:56, ADR 098 „Grenzen“ | – |
| Inspector als Panel am Zeiger | Not Started, Entscheidung offen | Roadmap.md:56 („prüfen“) | – |
| Topographie-Knopf (Höhenlinien) | Blocked (Funktion existiert nicht; laut Nutzer „später“) | ADR 098, Nachtrag 1 | – |
| Verhalten bei 1100 px Breite | Unknown | ADR 098 „Grenzen“: knapp; nicht gemessen | – |
| Befunde des Design-Reviews vom 28. Oktober | Not Started (7 offen, 2 teilweise) | Abschnitt 4 | – |

**Provisorien im Code** (keine `TODO`-Marken im UI-Code gefunden; die Provisorien stehen in ADR 098):
- Versteckt, aber weiter gebaut und beschrieben: `.tool-label`, `.tool-keycap` (`EditorShell.uss:204–205`, `MapToolbar.cs:94–99`), `.status-pointer`, `.status-map-state` (`:549`, `:552`).
- Vier gekoppelte Abstände zur Leiste: `bottom: 106px` (`EditorShell.uss:115`, `:117`) und `104px` (`:218`, `:649`) gegen `height: 94px` (`:142`).
- `ShowFoldersLeftOut` zählt und verwirft das Ergebnis (`EditorShellContentFolders.cs:125–135`).
- UXML-Namen der alten Oberfläche leben weiter: `view-menu-button` heißt *Tools*, `project-collapse-button` ist das X des Popups, `floating-panel*` trägt Inspector, Popup und Luftbild-Fenster.

**Abweichung vom Gedächtnis früherer Sitzungen:** Dort gilt Etappe 1 als uncommittet und ungetestet. Das Repository zeigt sie committet (`cb3a032`) und ADR 098 als vom Nutzer getestet; maßgeblich ist das Repository.

## 3. Was durch das Rework nicht mehr sichtbar oder erreichbar ist (A2/A3)

Alle Funktionen des Project-Fensters, der Ansichtsknöpfe und der Katalogköpfe haben einen neuen Einstieg (Einzelnachweise in der [Funktionsmatrix](ui-rework-feature-matrix.md)). Ohne Ersatz sind:

| Was | Stufe | Beleg | Einordnung |
|---|---|---|---|
| Daueranzeige des Modus: „nur lesen“, Chrono-Datum, „Variante X wird bearbeitet“, Zahl geänderter Dateien | bestätigt | `.status-map-state { display: none }` (`EditorShell.uss:552`), `UpdateMapStateLabel` schreibt weiter (`:7230–7247`) | Folge von Nachtrag 2 (Projektangabe gestrichen), in ADR 098 nicht bedacht |
| Hinweis „N Ordner ausgelassen“ an Objekt- und Straßenmenü | bestätigt | `ShowFoldersLeftOut` ohne Wirkung | ein leerer Katalog wirkt wie ein Fehler |
| Titel der Typwahl („Change type…“, Variantenname) | bestätigt (Code) | Hinweis wird gesendet, bevor das Panel sichtbar ist, und verworfen (`ShowToolHint` `:7786`) | Folge von „Hinweise in die Statuszeile“ |
| Profilname mit Nutzungszahl im Straßenkopf | bestätigt | nur noch Kachel-Hover und Tooltip | gewollt (Kopf gestrichen) |
| Farblegende, Zeigerkoordinaten | bestätigt | ADR 098 | vom Nutzer bestellt |

## 4. Designsystem und Bedienung (A4)

**Konsistent umgesetzt**
- Farben der Panels laufen über Tokens; in C# stehen Farben nur für Daten (Signalgruppen, Zeichenebenen), nicht für Panel-Styling.
- Ein Icon-System (`LucideIcons.cs`, 79 Icons, alle verwendet; `IconElement`).
- Ein Tooltip-Weg (`EditorShellTooltip.cs`) für alle Elemente; Texte über `LocBindings` und acht Sprachen.
- Werkzeughierarchie: Leiste → Fenster über der Leiste → Statuszeile; Kataloge einheitlich 210 px mit Icon-Reitern.

**Uneinheitlich oder offen**
- **Alpha-Farben außerhalb der Tokens:** 22 Literale in der USS, meist `--mf-color-border` und `--mf-color-panel` mit Deckkraft (z. B. `:142`, `:148–199`); `#c84040` und `#d9a92e` (`:297–298`) weichen von Fehler- und Warnfarbe ab.
- **Schriftgrößen:** Tokens 14/13/11 px, daneben 29 Literale (18 × 10 px, 3 × 9 px).
- **Zustände:** `:hover` 51-mal, `:disabled` 17-mal, `:focus` nur an fünf Textfeldern, `:active` nie. Knöpfe der Leiste, Reiter und Icon-Optionen haben weder Fokus- noch Drück-Zustand.
- **Zwei Optionsstile nebeneinander:** Icons (`icon-options`, nur Straße) gegen Textzeilen (`picker-option*`, alle anderen); drei Spaltenbreiten (300 px, `tool-options-scroll`, 380 px bei Ampeln).
- **Inline-Größen in C#:** 105 Zuweisungen, fast alle in Fahrplan, Fahrzeugen und Linie (`EditorShellVehicles.cs`, `EditorShellTimetable.cs`, `EditorShellTimetableManage.cs`).
- **Dialoge:** fünf Bauweisen (Review, Abschnitt 3).
- **Fenstergrößen:** kompakte Leiste unter 1400 px (`EditorShellBottomBar.cs:24`), Mindestgröße 1100 × 700. Ob Katalog (780 px) plus Optionsspalte bei 1100 px in allen Werkzeugen passen: unbekannt.
- **Tastatur:** Werkzeugtasten vollständig (Q W E R T G M V L A B 3, Entf, Strg+C/V/D/Z/Y); Tab-Fokus unsichtbar; das Kontextmenü hat keine Pfeiltasten-Bedienung (kein Code dafür gefunden).

**Abgleich mit dem Design-Review vom 28. Oktober** (dort Status „offen“)

| Nr. | Befund | Stand heute | Beleg |
|---|---|---|---|
| 1 | Keine Windows-Skalierung | offen; das Rework hat Knöpfe zusätzlich verkleinert (38–42 px) | `panelSettings.scale` fest (`EditorShellBootstrap.cs:325`) |
| 2 | Kontrast gedämpfte Schrift, Fehlerrot | offen; neu: halbtransparente Flächen, Kontrast gegen die Karte unbekannt | `EditorShell.uss:16`, `:21` |
| 3 | Panels verdecken die Kreuzung | teilweise (Kataloge 210 px; einspaltige Werkzeuge unverändert hoch) | ADR 098 |
| 4 | Rot/Gelb mehrdeutig | offen | `EditorShell.uss:78`, `MapCanvasPaths.cs:316` |
| 5 | Sterne im Aus-Zustand | offen | `EditorShell.uss:266`, `:342` |
| 6 | Tastaturfokus | offen | siehe oben |
| 7 | Dreifache Anleitung | teilweise (Hinweise in der Statuszeile); Lanes/Priority unbekannt | `ShowToolHint` |
| 8 | Statuszeile schneidet ab | offen, verschärft: eine Zeile, 28 px, jetzt auch Träger der Werkzeug-Hinweise | `EditorShell.uss:548` |
| 9 | Orange auf Zier-Symbolen | offen | `EditorShell.uss:102`, `:123` |

## 5. Fehler und Fehlerrisiken

Reihenfolge nach Schaden; die Belegstufe steht je Punkt dabei (1–4, 6–8 bestätigt, 5 teils, 9 Risiko). Einzelheiten und Korrekturrichtung im [Review](ui-rework-architecture-review.md).

1. **Karte ohne Projekt: Auswahl eines Objekts wirft eine Ausnahme.** `IsBusStop` → `BusStopName` → `RequireWorkspace()` (`OmsiMapSessionTrips.cs:98`, `:120`; `OmsiMapSession.cs:896`); der Aufruf in `EditorShellBusStop.cs:20` steht vor dem `try`, der im Kontextmenü (`EditorShellContextMenu.cs:175`) ganz ohne. **Am Player belegt:** `-openMap Grundorf -select 4229` (ein Haus) endet mit `InvalidOperationException` in `RenderInspector`; der Inspector wird nicht fertig gezeichnet, die Befehlskette des Prüflaufs bricht ab. Stammt aus `bbc8304` (per `git log -S` geprüft; auch auf `main`, in keinem Release-Tag; letzter Tag `v1.1.5`). ADR 098 sagt für diesen Fall „nur Properties und ein Hinweis“ zu.
2. **„Paste here“ ist ein eigener Pfad neben Strg+V.** Er ruft `PlaceClipboardAt` ohne die Vorbereitung von `BeginPaste` (`RollPasteOffsets`); `pasteHeadingOffsets` bleibt von einem früheren Einfügen stehen. **Auslöser:** vorher einmal mit Strg+V und eingeschalteter Zufallsdrehung eingefügt (nur dann ist die Liste gesetzt), danach eine andere Zahl von Objekten kopiert und „Paste here“ gewählt: `OmsiClipboard.PlaceAt` wirft `ArgumentException` (`OmsiClipboard.cs:174`), der Aufruf steht außerhalb jedes `try` (`EditorShellClipboard.cs:169`). Bei gleicher Zahl werden die alten Zufallsdrehungen wiederverwendet. Am Code bestätigt, nicht im Lauf.
3. **„Revert tile edits“ im Kontextmenü ohne Rückfrage und ohne Gefahrfarbe**, und `ReloadMap` → `ShowMap` leert die Undo-Historie (`EditorShellBootstrap.cs:7053–7078`, `:1917–1918`, `EditorShellContextMenu.cs:239`). Bestätigt. Die Funktion gab es schon im Inspector; neu ist, dass ein Rechtsklick und ein Klick genügen.
4. **Das Luftbild-Fenster sperrt alle Tastenkürzel.** Es ist nicht modal, steht aber seit dem Rework in `Dialogs()` (`:5982`), und `OnShortcut` kehrt bei `AnyDialogShown()` zurück (`:6045`). Strg+Z, Entf und Werkzeugtasten sind tot, solange es offen ist.
5. **Das Kontextmenü bleibt bei Tasten offen** (bestätigt; die Folgen sind Risiko). Nur Esc und Mausklicks schließen es; Entf, Strg+Z und Werkzeugtasten wirken darunter, die Einträge halten danach veraltete Datensätze. Die OMSI-Schicht weist fremde Datensätze teilweise ab (`OmsiMapSessionLabels.cs:57`); ob überall: unbekannt.
6. **Statuszeile: Hinweise überschreiben Meldungen.** `ShowToolHint` ruft `SetStatus` (`:7788`); jede Zeigerbewegung im Straßen- oder Gelände-Werkzeug ersetzt damit auch Fehlermeldungen wie `roadbuild.status.build_failed`. `ShowToolHint` selbst nachgelesen; die Kette von der Zeigerbewegung dorthin laut Reviewer-Analyse.
7. **Esc im Suchfeld der Objektbibliothek schließt die Bibliothek**, statt die Suche zuzuklappen: `OnShortcut` hängt per TrickleDown an der Wurzel (`:778`) und läuft vor dem Handler in `ToolCatalog.cs:38–44`. Die Aussage in ADR 098 gilt nur für die Straßen-Suche.
8. **Erzwungener Wechsel auf Drag beendet Reihen, Gelände und Linie nicht sauber.** Wird die Ansicht unbearbeitbar (Chrono-Datum, Variante, Karte ohne Projekt), setzt `PresentSummary` `activeTool` direkt auf Drag (`:1992–1994`), ohne `EndRowTool`, `EndTerrainTool`, `EndLineTool`; für fünf andere Werkzeuge ruft sie das jeweilige Ende immer (`:1943–1969`). Bleibt die Ansicht bearbeitbar, bleiben diese drei Werkzeuge über das Neuladen hinweg aktiv – ob das gewollt ist, ist offen. Aufrufer: `ShowMap` (`:1924`) und `PresentChrono` (`:2219`).
9. **Risiko:** Eine Typwahl („Change type…“) überlebt Werkzeug- und Kartenwechsel** (kein gemeinsamer Ausstieg aus Wahl-Modi); der nächste Klick in der Bibliothek ändert das alte Element. Risiko, nicht im Lauf belegt.

## 6. Absicherung

- 697 EditMode-Tests in 76 Dateien; rund 640 prüfen OMSI-Logik, 55 die Lokalisierung, **keiner** die Shell, die Kartenfläche, das Kontextmenü, die Leiste oder `ToolCatalog`. `Editor.Tests` referenziert `Editor.UI` und darf dessen `internal`-Typen sehen (`Scripts/UI/AssemblyInfo.cs:3`).
- Lokalisierung: `LocalizationKeyUsageTests` findet Schlüssel, die der Code nutzt und die in `en-US` fehlen; die zehn gelöschten Schlüssel kommen im Code nicht mehr vor (zwei unabhängige Suchen). Verwaiste Schlüssel werden nur ausgegeben, nie als Fehler gemeldet. Die CI (`.github/workflows/locales.yml`) läuft nicht bei Änderungen an `Scripts/UI`.
- Screenshot-Argumente decken Leiste, alle Popups, Inspector, Objekt-Kontextmenü, Tooltips, alle Werkzeug-Fenster und die meisten Dialoge ab ([CommandLine.md](CommandLine.md)). Nicht ansteuerbar: Kontextmenü für Spline und Kachel, Esc-Folgen, der Settings-Dialog, echte Mausgesten. Es gibt keinen Bildvergleich; jedes Bild sieht ein Mensch an.
- `-inspector`, `-contextMenu`, `-contextChoose`, `-tooltipAt` wirken nur zusammen mit `-screenshot` (`:1699–1714`).
- Pflegelücken: [DevelopmentSetup.md](DevelopmentSetup.md) („Validierungsstand“) hat keinen Eintrag zu ADR 098; [CommandLine.md](CommandLine.md) nennt bei `-projectTab` noch das Project-Fenster; [Architecture.md](Architecture.md) nennt „New road type…“ noch im Straßenmenü.

## 7. Konfiguration der Agenten

Geprüft, wie verlangt: `.claude/agents/` enthält `frontend`, `backend`, `testing`, `reviewer`, `debugger`, `orchestrator`. Die Dateien sind allgemeine Vorlagen für Web-Projekte (Routen, APIs, Migrationen) und nennen Unity, UI Toolkit oder die Projektregeln nicht; jeder Auftrag muss den Kontext deshalb mitliefern. `frontend` und `testing` laufen auf Sonnet 5.5, `backend` und `reviewer` auf Opus 5.5. Der Reviewer hat nur Lesewerkzeuge und braucht Diffs als Datei. Parallele Läufe im Hintergrund funktionieren; in dieser Analyse liefen vier gleichzeitig.
