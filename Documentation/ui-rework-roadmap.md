# UI-Rework: Meilensteine bis zum Abschluss (12. November 2026)

Vorschlag auf Grundlage von [ui-rework-audit.md](ui-rework-audit.md), [ui-rework-architecture-review.md](ui-rework-architecture-review.md) und [ui-rework-feature-matrix.md](ui-rework-feature-matrix.md). Er ergänzt den Abschnitt „Aufgeräumte Oberfläche“ in [Roadmap.md](Roadmap.md) und ersetzt ihn nicht; Häkchen werden weiter dort gesetzt. M0 und M1 sind abgeschlossen (siehe Übersicht).

**Prioritäten:** P0 Abstürze, Datenverlust, nicht erreichbare Kernfunktionen · P1 Architektur, unvollständige Migration, zentrale Bedienprobleme · P2 Konsistenz, Komfort, Wiederverwendung · P3 Feinheiten.
**Komplexität:** S bis ein Tag · M wenige Tage · L eine bis zwei Wochen · XL länger. Schätzung ohne Gewähr; sie misst Arbeit der Agenten plus einen Nutzertest je Meilenstein.
**Grundregeln für jeden Meilenstein:** eigener kurzer Zweig oder Abschnitt auf `feature/ui-rework`; Player-Build und Screenshots an Grundorf und Berlin-Spandau; Nutzertest vor dem Commit; geänderte `EditorShell.uss`/`.uxml` vor einem Release mit „Record Reviewed Player Assets“ freigeben; Dokumente mitpflegen.

## Entscheidungen des Nutzers (12. November 2026)

1. **M0 zuerst:** Der `ToolCatalog`-Stand wird geprüft und getestet, bevor Fehler behoben werden. M0 ist freigegeben und läuft.
2. **Fehler ohne Projekt** (Audit 5.1) wird getrennt auf `main` behoben (kurzer Zweig von `main`, danach `main` in `feature/ui-rework` übernehmen); er ist damit Schritt 1.1 von M1 mit eigenem Zweig.
3. **Statuszeile:** Ein eigener Platz für Werkzeug-Hinweise ist genehmigt (M2).
4. **„Revert tile edits“** wird rückgängig machbar und kommt deshalb nach M3; M1 fasst den Eintrag nicht an.
5. **Inspector als Panel am Zeiger** wird weiter verfolgt (M5).
6. **Einspaltige Werkzeuge bekommen Icon-Optionen**, in der Reihenfolge Linie, Vorfahrt, Spuren, Ampeln (M4). Das ändert die Festlegung in ADR 098, wonach sie einspaltig im alten Stil bleiben.
7. **Dokumente:** `Roadmap.md`, `ProjectStatus.md`, `DevelopmentSetup.md` und `CLAUDE.md` dürfen nachgezogen werden.

**Modus-Abzeichen (12. November 2026, nach einem Entwurf als Screenshot):** genehmigt und vor M2 eingebaut. Die Projektpille zeigt in der Pille ein Abzeichen: gelb mit dem Namen der Variante, die bearbeitet wird; grau mit dem Datum, wenn die Karte an einem Chrono-Datum gezeigt wird; grau „read-only“ bei einer Karte ohne Projekt; nichts bei einer bearbeitbaren Karte. Unter 1400 px bleibt nur der Punkt. Die Pille ist seitdem reine Anzeige: Der Klick, der das Fenster „Karte“ öffnete, ist entfernt, weil der Knopf ganz rechts dasselbe tut. Für M2 bleibt vom Abzeichen nur die Zahl geänderter Dateien im Tooltip.

## Übersicht

| Nr. | Meilenstein | Priorität | Komplexität | Hängt ab von | Führend |
|---|---|---|---|---|---|
| M0 | Arbeitsstand sichern (**erledigt**, 12. November 2026) | P0 | S | – | testing |
| M1 | Fehler der neuen Einstiege beheben (**erledigt**, 12. November 2026) | P0 | M | M0 | backend |
| M2 | Statuszeile und Modusanzeige (**erledigt**, 12. November 2026) | P1 | M | M1 | frontend |
| M3 | Modus- und Überlagerungsgerüst, erste Shell-Tests | P1 | L | M1 | backend |
| M4 | Werkzeug-Fenster fertig migrieren | P1 | L | M2 | frontend |
| M5 | Kontextmenü, zweite Etappe | P1 | M | M3 | frontend + backend |
| M6 | Lesbarkeit und Bedienbarkeit (Design-Review) | P1/P2 | M | M4 | frontend |
| M7 | Dialoge vereinheitlichen | P2 | L | M3 | frontend |
| M8 | Aufräumen und Abschluss | P3 | S | alle | reviewer + testing |

```
M0 → M1 ─┬→ M2 → M4 → M6 ─┐
         └→ M3 ─┬→ M5 ────┼→ M8
                └→ M7 ────┘
```

M2 und M3 berühren verschiedene Stellen (Statuszeile und Pille gegen `SetTool`, `OnShortcut`, `PresentSummary`) und können nacheinander oder mit klarer Dateiaufteilung nebeneinander laufen; `EditorShellBootstrap.cs` gehört dabei immer genau einem Bearbeiter.

---

## M0 – Arbeitsstand sichern (P0, S)

- **Ziel:** Der uncommittete Umbau auf `ToolCatalog` (97 Dateien) ist geprüft und festgehalten, bevor irgendetwas darauf aufbaut.
- **Bereiche:** Objekte, Straße, Gelände, Markierungen; acht Sprachen.
- **Aufgaben:** Arbeitsbaum in einer Projektkopie kompilieren; 697 EditMode-Tests; LocaleCheck `--strict`; Screenshots der vier Kataloge (1100 und 1600 px, en-US und de-DE); Ergebnis in DevelopmentSetup.md („Validierungsstand“) eintragen; Nutzertest der offenen Punkte aus ProjectStatus.md; Commit nach Freigabe.
- **Abhängigkeiten:** keine. Der Unity-Editor des Nutzers darf offen bleiben (Projektkopie).
- **Zuständig:** testing (Läufe), Hauptsitzung (Screenshots ansehen), Nutzer (Test, Freigabe des Commits).
- **Risiken:** Der Umbau kompiliert nicht oder ein Test scheitert – dann zuerst beheben.
- **Tests:** wie Aufgaben.
- **Akzeptanz:** 0 Compilerfehler, 697 Tests grün, LocaleCheck 0 Fehler, vier Kataloge in beiden Breiten ohne abgeschnittene Elemente, Nutzer hat bestätigt, Arbeitsbaum sauber.

## M1 – Fehler der neuen Einstiege beheben (P0, M)

- **Ziel:** Rechtsklick, Kontextmenü und Esc sind auf jeder Karte verlässlich; kein Eintrag kann unbemerkt Daten verwerfen.
- **Bereiche:** Kontextmenü, Inspector, Tastenkürzel, Luftbild-Fenster.
- **Aufgaben:** die neun Punkte aus Audit, Abschnitt 5, ohne 6 und 7 (die gehören zu M2): Ausnahme ohne Projekt; „Paste here“ über den Weg von Strg+V; Kürzel bei offenem Luftbild-Fenster; Kontextmenü schließt bei Tasten, Werkzeug- und Kartenwechsel; `PresentSummary` beendet Reihen, Gelände, Linie, wenn sie auf Drag zwingt; Wahl-Modi enden bei Werkzeug- und Kartenwechsel (nach Sichtung der Aufrufer). Einzelplan: [ui-rework-next-milestone.md](ui-rework-next-milestone.md).
- **Abhängigkeiten:** M0.
- **Zuständig:** backend (Logik), frontend (Rückfrage, Menü), testing, reviewer.
- **Risiken:** Änderungen an `SetTool`/`PresentSummary` wirken auf jedes Werkzeug; deshalb hier nur Ausstiege ergänzen, nichts umbauen.
- **Tests:** neue Unit-Tests in `Editor.OMSI` (Haltestellen-Abfrage ohne Arbeitskopie) und für die Einfüge-Vorbereitung; Prüflauf `-openMap` + `-select` + `-contextMenu` ohne Projekt; Handtest der Tasten.
- **Akzeptanz:** siehe Einzelplan; Kernpunkt: der Prüflauf, der heute mit `InvalidOperationException` hängen bleibt, endet mit Bild und ohne Ausnahme im Log.

## M2 – Statuszeile und Modusanzeige (P1, M)

- **Ziel:** Meldungen bleiben lesbar, Hinweise gehören dem offenen Werkzeug, und man sieht jederzeit, ob man eine Variante bearbeitet oder nur liest.
- **Bereiche:** Statusstreifen, Projektpille, alle Kataloge, Objekt- und Straßenmenü.
- **Aufgaben:** eigener Platz für den Werkzeug-Hinweis, der beim Schließen leer wird; `ToolCatalog` merkt sich den letzten Hinweis und zeigt ihn beim Einblenden; Hinweise mit Schlüssel, damit sie die Sprache wechseln; Modus als Abzeichen an der Pille (Variante gelb, nur lesen grau, Zahl geänderter Dateien im Tooltip); Titel der Typwahl sichtbar; Hinweis „N Ordner ausgelassen“ im Leer-Text oder `ShowFoldersLeftOut` entfernen; Esc im aufgeklappten Suchfeld klappt zuerst die Suche zu; voller Statustext im Tooltip (Design-Review 8).
- **Abhängigkeiten:** M1. Der Hinweis-Platz ist genehmigt; das Abzeichen an der Pille ist genehmigt und schon eingebaut (siehe oben).
- **Zuständig:** frontend; reviewer.
- **Risiken:** Platz bei 1100 px; die Statuszeile ist 28 px hoch und einzeilig.
- **Tests:** Screenshots Straße mit Fehlermeldung nach Zeigerbewegung (`-road …`), Variante, Karte ohne Projekt, 1100 px, de-DE; Handtest Esc in beiden Suchfeldern.
- **Akzeptanz:** Eine Fehlermeldung des Straßenbaus steht nach einer Zeigerbewegung noch da; nach dem Schließen eines Fensters ist kein Hinweis mehr zu sehen; in Variante und Nur-lesen zeigt die Pille den Modus; Esc im Suchfeld der Objekte klappt die Suche zu und lässt die Bibliothek offen.

## M3 – Modus- und Überlagerungsgerüst, erste Shell-Tests (P1, L)

- **Ziel:** Ein neues Werkzeug, ein neuer Dialog oder ein neuer Menüeintrag wird an einer Stelle eingetragen; die Regeln dahinter sind getestet.
- **Bereiche:** `SetTool`, `PresentSummary`, `OnShortcut`, `Dialogs()`, Kontextmenü, Tooltip.
- **Aufgaben:** Werkzeug-Tabelle (`Begin`, `End`, Esc, Nur-lesen-Schlüssel), zuerst nur für `End` und Esc genutzt; `RoadClicked` ohne Straßenbau als Rückfall; Überlagerungen melden sich an (modal ja/nein) statt in `Dialogs()` zu stehen; „Revert tile edits“ als rückgängig machbarer Schritt, der die Historie behält (Entscheidung des Nutzers); reine Funktionen herauslösen und testen: Einträge des Kontextmenüs, Zerlegung der Tooltips, Taste → Werkzeug.
- **Abhängigkeiten:** M1 (dessen Ausstiege gehen in der Tabelle auf).
- **Zuständig:** backend; testing (Tests), reviewer.
- **Risiken:** größter Eingriff in die 8.700-Zeilen-Datei; Verhalten muss gleich bleiben. Deshalb in drei Schritten mit je vollem Testlauf und Screenshot-Reihe; kein gleichzeitiger zweiter Bearbeiter der Datei.
- **Tests:** `ContextMenuEntriesTests`, `TooltipTextTests` (alle Tooltips aller Sprachen), `ShortcutMapTests`; Screenshot-Reihe aller elf Werkzeuge vor und nach; Handtest Esc-Folgen.
- **Akzeptanz:** `SetTool`, `PresentSummary` und die Esc-Zweige lesen dieselbe Tabelle; kein `was*`-Flag mehr; ein Dialog schließt mit Esc, ohne in einer Liste zu stehen; mindestens 30 neue Tests grün; Screenshots vor/nach ohne Unterschied.

## M4 – Werkzeug-Fenster fertig migrieren (P1, L)

- **Ziel:** Alle Werkzeug-Fenster haben denselben Aufbau und einen Optionsstil.
- **Bereiche:** Objekte, Gelände, Reihen, Markierungen, Linie, Vorfahrt, Spuren, Ampeln, Labels, Straßenbauer.
- **Aufgaben:** `ToolCatalog.Options(scrolling)`, benannter Platz über den Kacheln, `AddTile`; Icon-Optionen (`OptionControls`) für Gelände, Objekte, Reihen, Markierungen, dann Linie, Vorfahrt, Spuren, Ampeln; Kopf des Straßenbauers; Abstände zur Leiste aus ihrer gemessenen Höhe statt vier fester Werte; danach `picker-option*`, `picker-segmented` und `tool-panel-header*` aus der USS.
- **Abhängigkeiten:** M2 (Hinweis-Platz). Entschieden: Die einspaltigen Werkzeuge bekommen Icons, Reihenfolge Linie, Vorfahrt, Spuren, Ampeln; davor Gelände, Objekte, Reihen, Markierungen.
- **Zuständig:** frontend; je Fenster ein Schritt mit Nutzertest.
- **Risiken:** Jedes Fenster ist ein eigenes Gestaltungsthema (Icons, Zeilen); neue Lucide-Icons; Ampel-Fenster mit Zeitleiste passt schlecht in Icon-Zeilen.
- **Tests:** je Fenster Screenshots bei 1100 und 1600 px, en-US und de-DE; Tooltip-Stichproben (`-tooltipAt`).
- **Akzeptanz:** `OptionControls` wird von jedem Werkzeug mit Optionen genutzt; keine von Hand gebaute Optionsspalte mehr (Suche nach `picker-options` in C# außerhalb von `ToolCatalog` ergibt nichts); eine Zahl für die Leistenhöhe; Nutzer hat jedes Fenster bestätigt.

## M5 – Kontextmenü, zweite Etappe (P1, M)

- **Ziel:** Der Rechtsklick erreicht alles, was man auf der Karte anfassen kann, auch in 3D.
- **Bereiche:** Kontextmenü, Kartenfläche, Kommandozeile, Inspector.
- **Aufgaben:** Einträge für Reihen, Kreuzungen (Markierungen, Vorfahrt, Spuren, Ampeln öffnen das jeweilige Werkzeug an dieser Kreuzung) und Ampeln; Spline-Treffer in 3D; Pfeiltasten und Enter im Menü; `-contextMenu` für Spline und Kachel; Inspector am Zeiger (Nutzer: weiter verfolgen): zuerst Entwurf mit Screenshot, dann Umsetzung.
- **Abhängigkeiten:** M3 (Einträge als getestete Funktion, Werkzeug-Tabelle zum Öffnen eines Werkzeugs an einem Ziel).
- **Zuständig:** frontend (Menü), backend (Treffer in 3D, Befehle), testing.
- **Risiken:** Einträge dürfen nur vorhandene Befehle rufen (Lehre aus „Paste here“); `-inspectorTo`/`-inspectorFind` hängen am Namen `inspector-records` und brächen bei einem Zeiger-Panel.
- **Tests:** erweiterte `ContextMenuEntriesTests`; Prüfläufe je Menüart; Handtest in 3D.
- **Akzeptanz:** Roadmap.md:56 ist abgehakt oder die offenen Teile sind mit Entscheidung gestrichen; jede Menüart per Argument ansteuerbar.

## M6 – Lesbarkeit und Bedienbarkeit (P1/P2, M)

- **Ziel:** Die neun Befunde aus [DesignReview-2026-10-28.md](DesignReview-2026-10-28.md) sind erledigt oder entschieden.
- **Bereiche:** Tokens, alle Knöpfe, Einstellungen, schmale Fenster.
- **Aufgaben:** Windows-Skalierung und „Interface size“ (Befund 1, P1); Kontrast-Tokens für gedämpfte Schrift, Fehlerrot, Sterne (2, 5); Fokus- und Drück-Zustand für alle Knopfklassen (6); Alpha-Tokens statt 22 Literalen; Schriftgrößen-Tokens für 10 und 9 px; Prüfung aller Fenster bei 1100 px und bei 150 %; Farbbedeutungen (4) nur nach Entscheidung.
- **Abhängigkeiten:** M4 (sonst werden Fenster zweimal vermessen).
- **Zuständig:** frontend; reviewer.
- **Risiken:** Skalierung verändert jede feste Breite (780, 300, 320, 380 px); halbtransparente Flächen über hellen Luftbildern.
- **Tests:** Screenshot-Reihe bei 100, 150, 200 % und 1100/1600 px über hellem und dunklem Kartengrund; gerechnete Kontraste.
- **Akzeptanz:** Bei 150 % Windows-Skalierung ist die Oberfläche 1,5-fach; alle Textfarben erreichen 4,5:1 auf ihrer Fläche; Tab-Fokus ist auf jedem Knopf sichtbar; bei 1100 × 700 überlappt nichts.

## M7 – Dialoge vereinheitlichen (P2, L)

- **Ziel:** Ein Dialog-Baustein statt fünf Bauweisen.
- **Bereiche:** Fahrplan, Fahrzeuge, Parkliste, Tageskurven, Ordnerwahl, Karteneinstellungen, Ampel-Assistent und -Vorlagen, Luftbild-Fenster.
- **Aufgaben:** Fabrik für modale Dialoge (Titel, Beschreibung, Knopfzeile, Breite als Klasse, Anmeldung für Esc); die fünf handgebauten Dialoge umstellen; Inline-Größen in Fahrplan und Fahrzeugen in USS-Klassen; Luftbild-Fenster als erklärte Sonderform.
- **Abhängigkeiten:** M3 (Anmeldung der Überlagerungen).
- **Zuständig:** frontend.
- **Risiken:** viel Fläche, wenig sichtbarer Gewinn; Fahrplan-Dialog ist komplex.
- **Tests:** Screenshots jedes Dialogs vor/nach (`-timetable`, `-vehicles`, `-parkList`, `-trafficCurves`, `-mapSettings`, `-contentFolders`, `-satellite`).
- **Akzeptanz:** keine von Hand gebaute `dialog-overlay` außerhalb der Fabrik; unter 20 Inline-Größen in `Scripts/UI`.

## M8 – Aufräumen und Abschluss (P3, S)

- **Ziel:** Keine Reste; Rework dokumentiert und freigabefähig.
- **Aufgaben:** tote USS-Klassen und versteckte Labels entfernen (`MapToolbar` baut dann keine Labels und Tastenkappen mehr); verwaiste Textschlüssel; UXML-Namen (`view-menu-button`, `project-collapse-button`) und `RenderProjectBrowser` umbenennen; verwaiste Schlüssel als Testfehler; Architecture.md, CommandLine.md, DevelopmentSetup.md, CLAUDE.md nachziehen; ADR 098 abschließen; „Record Reviewed Player Assets“; Zusammenführen nach `main`; Wiki nach dem Release.
- **Abhängigkeiten:** alle.
- **Zuständig:** reviewer (Befunde), frontend (Änderungen), testing (voller Lauf).
- **Risiken:** Umbenennungen brechen Prüfargumente; deshalb zuletzt.
- **Akzeptanz:** Suche nach den 16 Klassen aus dem Review ergibt nichts; `BuildRelease` läuft durch; alle Zeilen der Funktionsmatrix stehen auf „voll“ oder tragen eine Entscheidung.

## Nicht Teil dieses Plans

- **Topographie (Höhenlinien):** laut ADR 098 „später“; eigene Funktion, kein Rework.
- **Neues Designkonzept:** ausdrücklich nicht verlangt; alle Meilensteine setzen ADR 098 und das Corporate Design um.
- **Leistung des Neuzeichnens** (Inspector je Auswahl, Dateilesungen): erst messen (Review, Abschnitt 7); wenn Spandau auffällig ist, wird daraus ein eigener Meilenstein vor M5.

