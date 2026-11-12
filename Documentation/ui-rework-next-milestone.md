# UI-Rework: nächster Schritt – M0 und M1 (12. November 2026)

Empfehlung aus [ui-rework-roadmap.md](ui-rework-roadmap.md). **Nichts hiervon ist begonnen; die Umsetzung startet erst nach ausdrücklicher Freigabe des Nutzers.**

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
