# UI-Rework: Funktionsmatrix (Stand 22. November 2026, M8 Schritt 5)

Wie jede Funktion von MapWorks in der Oberfläche nach ADR 098 erreichbar ist und wie weit ihr Fenster dem neuen Aufbau folgt. Grundlage: [ui-rework-audit.md](ui-rework-audit.md). Vollständigkeit der Funktionsliste gegen [Roadmap.md](Roadmap.md) und die Dateien in `Assets/_Project/Scripts/UI/`.

Neu belegt am 22. November 2026 (M8 Schritt 5, Zweig `feature/ui-rework`, HEAD `ab5eaea`): jede Zeile gegen den Code geprüft, Fundstellen als Datei:Zeile. Mit „(Vorfassung)“ markierte Zeilennummern stammen aus der Matrix vom 12. November und wurden nicht erneut nachgesehen; dort stimmt der Dateiname. Kein Player-Lauf für jede Zeile. Ergebnisse der Fehlersuche (M1 bis M7) stehen in `Decisions.md` (ADR 098, Nachträge), die Zeilen unten nennen sie.

## Lesehilfe

- **Einstieg:** Wo man die Funktion heute findet, mit Fundstelle. Pfade ohne Ordner liegen in `Assets/_Project/Scripts/UI/`; `Bootstrap` = `EditorShellBootstrap.cs`.
- **Integration**
  - **voll** – über die neue Struktur erreichbar *und* im neuen Aufbau (Katalog mit Kopfzeile, Popup der Leiste, Kontextmenü, neues Fenster). Zeilen, die seit M4 neu gebaut sind (Ampelfenster, Assistent), zählen als voll.
  - **teilweise** – erreichbar, aber Fenster oder Optionen im alten Stil, oder ein Teil fehlt.
  - **alt** – erreichbar, vom Rework nicht angefasst.
  - **Lücke** – etwas ist durch das Rework weggefallen oder fehlerhaft.
- **Nutzertest:** „ja“ heißt, die Etappe, zu der der Weg gehört, ist laut ADR 098 vom Nutzer getestet (Etappe 1, Esc, Tooltips) – nicht, dass jeder einzelne Eintrag geprüft wurde; „offen“ heißt gebaut, aber noch nicht vom Nutzer bestätigt. Ein Test der Funktion selbst (vor dem Rework) zählt hier nicht.

## Einstiegspunkte der neuen Oberfläche

| Ort | Inhalt | Fundstelle |
|---|---|---|
| Menü File | New map, Open project, Save project as, Export, Open OMSI map, Settings | `EditorShell.uxml:68–77` |
| Menü Edit | Place object, Delete selection, Fit terrain to built roads again, Test-/T-/Kreuzung aus Profil, Undo, Redo | `EditorShell.uxml:78–88` |
| Menü Tools | Inspector, Satellite and terrain, Road builder, Check map | `EditorShell.uxml:89–95` |
| Menü Help | Check for updates, Report a bug, Wiki | `EditorShell.uxml:96–100` |
| Leiste links | Routes, Textures, 3D, Einpassen, Zoom, Zentrieren; Kachelknopf; Projektpille (Name und Modus-Abzeichen) | `EditorShell.uxml:36–51`, `EditorShellBottomBar.cs` |
| Leiste Mitte | Drag Q, Move W, Rotate E, Delete Entf, Road R, Rows T, Terrain G, Markings M, Priorities V, Lanes L, Signals A, Line B, Objects 3 | `MapToolbar.cs` |
| Leiste rechts | Popups Pfade, Linien, Chrono, Karte (Einstiegspunkte seit 17. November 2026 ohne Popup, im Objektbrowser) | `EditorShellBottomBar.cs` |
| Kontextmenü | Auswahl, Objekt, Spline, Reihe, Kachel | `EditorShellContextMenu.cs:277–522`, `ContextMenuEntries.cs:131–140` |
| Tasten | Werkzeugtasten, Esc, Entf, Strg+C/V/D/Z/Y | `EditorShellBootstrap.cs:6702` (`OnShortcut`) |

## Matrix

| Funktion | Einstieg heute | Integration | Nutzertest im Rework | Anmerkung |
|---|---|---|---|---|
| **Projekt und Karte** | | | | |
| Karte/Projekt öffnen, speichern, neue Karte, Export | Menü File; Startbildschirm (`EditorShellBootstrap.cs:362–365`, `:408`) | teilweise | – | Export-Dialog (`export-dialog`) kommt seit M7 Schritt 5 aus der Fabrik; Startbild und Menüeinträge von File vom Rework nicht berührt |
| Karte ohne Projekt ansehen | Menü File › Open OMSI map (`EditorShell.uxml:74`) | voll | – | Behoben in M1 (`64e125a` auf `main`): `BusStopName` liest aus der Sitzung, der Inspector fängt Lesefehler ab; Test `OmsiBusStopLookupTests`. Vorher Audit 5.1 |
| Programm-Einstellungen, Sprache | File › Settings (`EditorShell.uxml:76`, `EditorShellSettings.cs:142`) | voll | ja | Prüfargument `-openSettings`; `settings-dialog` aus der Fabrik (M7 Schritt 5); Schalter „Hide DLC content“ mit Hinweis (`EditorShellSettings.cs:107–110`). Zeilen im Dialog als `form-*`-Klassen, siehe Abschnitt „Dialoge: was „alt“ hieß“ |
| Objekt- und Spline-Ordner | Settings › Content › Object and spline folders (`EditorShellContentFolders.cs`) | teilweise | offen | Hinweis „N Ordner ausgelassen“ ist da (Schlüssel `contentfolders.status.applied`, Leermeldung der Objekt- und Straßenmenüs, `EditorShellContentFolders.cs:120`); der Ordner-Knopf im Katalog fehlt (kein Ordner-Knopf in `ObjectLibraryPanel.cs`) |
| Kartenprüfung | Tools › Check map… (`EditorShell.uxml:94`, `EditorShellBootstrap.cs:549`) | voll | ja | `check-dialog` aus der Fabrik (M7 Schritt 5); Menüeintrag neu |
| Updates, Fehlerbericht | Menü Help (`EditorShell.uxml:97–98`) | voll | ja | `update-dialog` und `bug-report-dialog` aus der Fabrik (M7 Schritt 5); die Menüeinträge von Help stehen im gemeinsamen Menüaufbau der Leiste |
| Modus der Karte (nur lesen, Variante, geändert) | – | voll | – | Abzeichen an der Projektpille, Zahl geänderter Dateien im Tooltip (seit 12. November 2026, M2): `EditorShellBottomBar.cs` (`UpdateProjectModeBadge`) |
| **Ansicht** | | | | |
| Draufsicht, 3D, Texturen, Zoom, Einpassen, Zentrieren | Leiste links (`EditorShell.uxml:37–51`) | voll | ja | Zoom-Feld folgt dem Mausrad (M8 Schritt 1: `ViewChanged`, `-wheelZoom`) |
| Linien anzeigen (Routes) | Leiste links (`EditorShell.uxml:39`) | voll | ja | öffnet die Linienliste nicht mehr mit |
| KI-Pfade anzeigen (Paths) | Leiste links (`EditorShellPaths.cs`); Liste im Popup „Pfade“ | voll | ja | Popup nur bei eingeschalteten Pfaden |
| Tooltips | überall (`EditorShellTooltip.cs`) | voll | ja | |
| **Listen der Karte (Popups)** | | | | |
| Kacheln mit Filter | Kachelknopf (`EditorShellProjectTabs.cs`, Vorfassung `:148`) | voll | ja | |
| Kachel hinzufügen / löschen | Plus auf der Karte (`EditorShellBootstrap.cs`, Vorfassung `:527`); Löschen laut ADR 031 im Inspector | teilweise | ja | Einstieg des Löschens nicht einzeln nachverfolgt; kein Eintrag im Kontextmenü (Kachel-Menü `EditorShellContextMenu.cs:522`) |
| Kachel zurücksetzen | Kontextmenü „Revert tile edits“ (`EditorShellContextMenu.cs:520`, `:528`); Inspector (`EditorShellBootstrap.cs:9720`); Rückfrage in `EditorShellTileRevert.cs`, ausgeführt in `RevertSelectedTileNow` (`EditorShellBootstrap.cs:8024`) | voll | offen | Seit M8 Schritt 5 (Rest 1): Rückfrage `revert-tile-dialog` aus der Fabrik (Abbrechen links, roter Knopf „Revert tile“ rechts, Esc ändert nichts). Die Undo-Historie bleibt: der Schritt ist rückgängig machbar (M3 Schritt 3, `PushHistory`; die Angabe „leert die Historie“ der Vorfassung war überholt) |
| Karte: Name, Zahlen, Warnungen | Popup „Karte“ (`EditorShellProjectTabs.cs`, Vorfassung `:63`) | voll | ja | |
| Karteneinstellungen | Popup „Karte“ › Knopf (`EditorShellMapSettings.cs:19`) | voll | ja | `new-map-dialog` (ein Dialog für „New map“ und Karteneinstellungen) aus der Fabrik (M7 Schritt 5), Zeilen seit M4 im Optionsstil (`OptionControls`); nur mit Projekt |
| Parkliste, Tageskurven | Popup „Karte“ › Knöpfe (`EditorShellProjectTabs.cs:91–92`; `EditorShellParkList.cs`, `EditorShellTrafficCurves.cs`) | voll | ja | `park-list-dialog` (M7 Schritt 2, Standard 560) und `traffic-curves-dialog` (M7 Schritt 3, Wide 720) aus der Fabrik |
| Einstiegspunkte | Objektbrowser (Suche „entrypoint“): Platzieren mit Namensdialog, „Change type…“; Kontextmenü „Rename entry point…“ und Inspector-Gruppe „Entry point“ (`EditorShellEntryPoints.cs`). Die Liste aller Einstiegspunkte entfällt; Ersatz ist die Suche im Objektbrowser und die Kartenprüfung | voll | ja (17. November 2026: Platzieren, Duplizieren, Change type, Löschen, openOMSI) | Popup entfernt (ADR 079, Ergänzung 17. November 2026) |
| Chrono-Varianten, Datum | Popup „Chrono“ (`EditorShellChronoDates.cs`, `EditorShellChronoLines.cs`) | voll | ja | welche Variante bearbeitet wird, ist nicht dauerhaft sichtbar |
| Linienliste | Popup „Linien“ (`EditorShellProjectTabs.cs`) | voll | ja | |
| Fahrplan-Editor, Fahrzeuge | Popup „Linien“ › Knöpfe (`EditorShellProjectTabs.cs`, Vorfassung `:120–122`) | voll | ja | `timetable-dialog` (M7 Schritt 4) und `vehicles-dialog` (M7 Schritt 3) aus der Fabrik, Wide 720; Inline-Größen: 0 unmarkiert (M7 Schritt 6, vorher 105) |
| **Objekte** | | | | |
| Objektbibliothek, Platzieren | Leiste „Objects“ 3; Edit › Place object (`ObjectLibraryPanel.cs`, `EditorShell.uxml:79`) | voll | ja | Katalog neu; Optionen über `OptionControls` (`ObjectLibraryPanel.cs`, 7 Aufrufe). Esc im Suchfeld: behoben in M2 (Audit 5.7) |
| Auswählen, Ziehen, Verschieben, Drehen | Q, W, E (`MapToolbar.cs`) | voll | ja | |
| Löschen | Entf, Leiste, Kontextmenü, Edit-Menü (`EditorShell.uxml:80`) | voll | ja | |
| Kopieren, Duplizieren | Strg+C/D, Kontextmenü (`EditorShellClipboard.cs`) | voll | ja | |
| Einfügen | Strg+V; Kontextmenü „Paste here“ (`EditorShellClipboard.cs:161–193`, gemeinsame Vorbereitung `RollPasteOffsets`) | voll | – | Behoben in M1 (ADR 098, Nachtrag M1): „Paste here“ geht über dieselbe Vorbereitung wie Strg+V. Vorher Audit 5.2 |
| Eigenschaften, Typ und Labels, Elternobjekt | Kontextmenü → Inspector (`EditorShellInspector.cs`) | voll | ja | Labels seit 16. November 2026 (M4 Schritt 8, getestet 17. November 2026): volle Zeilen, benannte Felder je Objektart (ADR 100), Ausrichtung ohne „Apply labels“ |
| Typ ändern („Change type…“) | Inspector (Knopf in der Gruppenkopfzeile), Kontextmenü, `EditorShellLabels.cs` | voll | ja (getestet 17. November 2026) | Modus des Objekte-Fensters mit Zeilen „Current“/„Replace“; Ende bei Werkzeug-, Auswahl-, Ansichtswechsel und offenem Fenster (`TypePickRules`) |
| Haltestelle benennen | Kontextmenü „Rename bus stop…“ (`EditorShellBusStop.cs`), Inspector-Gruppe „Bus stop“ | voll | ja (Namen, Liste: getestet 17. November 2026) | Fehler 5.1 ohne Projekt ist behoben (Zeile „Karte ohne Projekt ansehen“) |
| Gelände unter Objekt einebnen | Kontextmenü, Inspector | voll | ja | |
| Zufällige Drehung | Optionen der Bibliothek (`EditorShellRandomHeading.cs`) | voll | ja | Symbolschalter `shuffle` über `OptionControls.Switch` (`ObjectLibraryPanel.cs:120`); Logik in `EditorShellRandomHeading.cs` |
| **Straßen und Splines** | | | | |
| Straße bauen | Leiste R (`RoadToolPanel.cs`) | voll | offen | Meldungen von Hinweisen überschrieben: behoben in M2 (Audit 5.6, `ShowToolHint`) |
| Spline-Eigenschaften, Höhen, Querschnitt, Aussehen | Kontextmenü → Inspector (Spline-Menü `EditorShellContextMenu.cs:497`) | voll | ja | Splines in 3D trifft das Kontextmenü seit M5 Schritt 2 (20. November 2026, `ProjectStatus.md`) |
| Spline löschen, Varianten-Typ | Entf, Kontextmenü (`EditorShellSplineVariants.cs`) | voll | ja | |
| Enden und Gelenke ziehen | Griffe auf der Karte | alt | – | |
| Straßenbauer (eigene Profile) | Tools › Road builder (`EditorShell.uxml:92`, `EditorShellRoadBuilder.cs`; Straßentyp-Fenster nach ADR 101) | teilweise | offen | Straßentyp-Fenster nach ADR 101 (eigenes Fenster, Sonderform). Klärung M8 Schritt 5: „New road type…“ gibt es im Straßenwerkzeug nicht mehr (der Eintrag fiel am 12. November 2026 mit der Kopfzeile des Katalogs weg, `Decisions.md` ADR 098 Etappe 2; der Kommentar `EditorShellRoadBuilder.cs:14` ist veraltet). Der einzige Einstieg ist Tools › „Road builder…“ (`EditorShell.uxml:92`, Klick in `EditorShellBootstrap.cs:759–765`); der Eintrag war ohne geladene Karte wirkungslos und stumm und ist seit dem 22. November 2026 dann ausgegraut (`EditorShellMapMenuState.cs`; Kommentar korrigiert) |
| Kreuzungen aus Profil | Edit-Menü (drei Einträge, `EditorShell.uxml:82–84`) | teilweise | – | Menüeinträge alt; kein Kontextmenü am Kreuzungsobjekt für dieses Profil (siehe Roadmap, nach dem Rework) |
| Gelände an Straßen neu anpassen | Edit-Menü (`EditorShell.uxml:81`) | alt | – | |
| Überweg, Parkreihe | Straßen-Optionen, Zeile „Bauen“ (`RoadToolPanel.cs`, Vorfassung `:141–161`); Überweg auch im Spuren-Werkzeug (`EditorShellLanes.cs`) | voll | offen | Schalter „Parkreihe“ und „Überweg“ über `OptionControls.Switch` (`RoadToolPanel.cs:138`, `:145`); im Spuren-Werkzeug über `LaneToolOptions.cs` |
| Reihen entlang von Straßen | Leiste T (`RowToolOptions.cs`); Kontextmenü „Reihe“ (`EditorShellContextMenu.cs:435`, `ContextMenuKind.Row`) | voll | – | Optionen über `OptionControls` (`RowToolOptions.cs`, 11 Aufrufe); Kontextmenü „Reihe“ ist da (Vorfassung: keins) |
| **Kreuzungen und Verkehr** | | | | |
| Markierungen | Leiste M (`MarkingToolOptions.cs`); am Kreuzungsobjekt Eintrag „Markings…“ (`EditorShellContextMenu.cs:327`) | teilweise | offen | Katalog und Optionen im neuen Stil (`MarkingToolOptions.cs`, 15 `OptionControls`-Aufrufe). Offen bleibt nur: kein eigenes Kontextmenü für die Markierung |
| Vorfahrt (seit 15. November 2026 „Traffic rules“, ADR 099) | Leiste V (`EditorShellPriorities.cs`); Eintrag „Traffic rules…“ am Kreuzungsobjekt (`EditorShellContextMenu.cs`) | voll | – | Fenster mit Optionsstil (`TrafficToolOptions.cs`, `OptionControls`), gebaut in M4, dort vom Projektinhaber getestet (19. November 2026). Vorher „nur Hülle neu“: überholt |
| Spuren, Abbiegespur | Leiste L (`EditorShellLanes.cs`) | voll | – | Fenster neu gebaut (M4 Schritt 6, 15. November 2026), Optionen über `LaneToolOptions.cs` (`OptionControls`) |
| Ampeln, Phasen, Zeitleiste | Freies Fenster `SignalWindow` (`SignalWindow.cs`, `EditorShellSignal*.cs`, `FloatingWindow.cs`) | voll | – | neu (M4 Schritt 7b, getestet 16. November 2026); verschiebbar, einklappbar, größenveränderbar |
| Ampel-Assistent, Vorlagen | Bereich „Assistant“ im Ampel-Fenster (`EditorShellSignalWizard.cs`, `EditorShellSignalTemplates.cs`) | voll | – | neu (M4 Schritt 7b) |
| KI-Pfade und Regeln je Element | Kontextmenü „AI paths and rules…“ → Inspector (`EditorShellPaths.cs:304`) | voll | ja | nur bei eingeschalteten Pfaden; stumm, solange Pfade nicht gelesen sind |
| **Gelände und Unterlagen** | | | | |
| Geländepinsel, Bodentexturen malen | Leiste G (`TerrainToolOptions.cs`, `GroundPaintOptions.cs`) | voll | offen | Katalog neu; Optionen über `OptionControls` (`TerrainToolOptions.cs` 14, `GroundPaintOptions.cs` 17 Aufrufe) |
| Höhenkarte auflegen | Gelände-Fenster, Reiter „Höhenkarte“ | voll | offen | |
| Luftbilder, Verortung, Geländehöhen | Tools › Satellite and terrain (`EditorShell.uxml:91`, `EditorShellAerial.cs:82`, nicht modal) | voll | – | Sonderform nach Entscheidung 6A (M7): nicht modales Werkzeugfenster, bewusst nicht aus der Fabrik (`Architecture.md`); Zeilen seit M4 im Optionsstil (`SatelliteWindow.cs`). Tastenkürzel nur während der Punktwahl gesperrt (M1, Audit 5.4 behoben; `AnyModalDialogShown`, `EditorShellOverlays.cs:46`) |
| Eigene Bilder | im Luftbild-Fenster (`EditorShellPictures.cs`; Einstieg nicht einzeln nachverfolgt) | voll | – | Teil des Luftbild-Fensters, gleiche Sonderform (`SatelliteWindow.ShowPictures`, Einstieg `EditorShellPictures.cs:48`); die Bilderliste nutzt eigene Klassen (`satellite-result`) |
| **Fahrplan** | | | | |
| Linie anlegen (Haltestellen klicken) | Leiste B (`LineToolPanel.cs`, `EditorShellLine.cs`) | voll | – | Fenster neu gebaut (M4 Schritt 5, 15. November 2026), Optionen über `LineToolPanel.cs` (`OptionControls`); Prüfargumente `-lineGroup`, `-lineBackMode` siehe Kommandozeilen-Zeile |
| Fahrten, Zeiten, Depots | Fahrplan- und Fahrzeug-Dialog (`EditorShellTimetable.cs`, `EditorShellVehicles.cs`) | voll | ja | wie die Zeile „Fahrplan-Editor, Fahrzeuge“ (gleiche zwei Dialoge) |
| **Querschnitt** | | | | |
| Undo/Redo | Strg+Z/Y, Edit-Menü (`EditorShell.uxml:86–87`) | voll | ja | gesperrt bei modalen Dialogen; wirkt unter offenem Kontextmenü |
| Tastenkürzel | `OnShortcut` (`EditorShellBootstrap.cs:6702`) | voll | ja | Tests: `ShortcutGuardTests` (M1), `ShortcutMapTests`; Tab-Fokus unsichtbar (Audit 6) |
| Mehrsprachigkeit | alle neuen Texte über `LocBindings`, acht Sprachen | voll | ja | Werkzeug-Hinweise wechseln die Sprache erst bei der nächsten Bewegung |
| Kommandozeile für Prüfläufe | [CommandLine.md](CommandLine.md) | voll | – | Settings (`-openSettings`) und Spline-/Kachel-Menü (`-contextMenu spline:<ID>`, `tile:<X>,<Y>`) sind belegt. Die 17 bisher fehlenden Argumente sind in M8 Schritt 5 nachgetragen; `-lineGroup` und `-lineBackMode` sind im Player geprüft (M8 Schritt 5, Bilder `.omc/handoffs/m8/step5/line-*.png`); neu `-revertTileDialog` |

## Auswertung

Gezählt sind die 56 Zeilen der Matrix (Zeilen mit einem Integrationswert; Abschnittsköpfe nicht gezählt). „neu“ zählt als voll.

| Integration | Anzahl Zeilen | Bedeutung |
|---|---|---|
| voll | 48 | Kern der täglichen Arbeit, alle 13 modalen Dialoge (Fabrik, M7), alle Werkzeug-Optionen (`OptionControls`, M3/M4), Luftbild-Fenster und Straßentyp-Fenster als Sonderform |
| teilweise | 6 | echte Reste: Start/File-Menü, Ordnerliste, Kachel hinzufügen/löschen, „Kreuzungen aus Profil“ (Straßenbauer-Eintrag ohne Karte: erledigt, ausgegraut), Markierungs-Kontextmenü |
| alt | 2 | Griffe auf der Karte, Menüeintrag „Gelände an Straßen neu anpassen“ |
| Lücke | 0 | „Kachel zurücksetzen“ hat seit M8 Schritt 5 eine Rückfrage |

Vergleich mit der Vorfassung (12. November 2026): Die Vorfassung nannte 25/17/7/7. Nachgezählt enthielt ihre Tabelle 26 voll, 16 teilweise, 6 alt und 5 Lücke (dazu 2 „neu“ und 1 Abzeichen-Zeile); die Summen im Abschnitt stimmten nicht mit der Tabelle überein. Die Lücken sind von 5 auf 1 gesunken: Karte ohne Projekt, Einfügen und Luftbild-Tastenkürzel sind in M1 behoben, die Ordner-Zeile hat einen Hinweis und wird nun als teilweise geführt.

Stand M8 Schritt 5 (Rest 4, 22. November 2026): Die erste Zählung des Tages (29/20/6/1) führte Dialoge und Werkzeug-Optionen noch als „alt“, obwohl M7 alle modalen Dialoge auf die Fabrik und M3/M4 alle Werkzeug-Optionen auf `OptionControls` umgestellt hatten. Die Zeilen sind gegen den Code neu eingestuft (Begründung je Zeile in der Spalte „Anmerkung“); danach 48/6/2/0 von 56 Zeilen.

## Dialoge: was „alt“ hieß (M8 Schritt 5, Rest 4)

„Alt“ meinte in der Matrix vom 12. November 2026 die **Bauweise**: fünf handgebaute Dialogarten mit eigenem Overlay, eigener Knopfreihenfolge und Inline-Größen. Das gilt nicht mehr:

- **Modale Dialoge (13):** alle aus `DialogFactory.cs` (M7, vom Projektinhaber getestet): Titel, Beschreibung, Knopfzeile (Abbrechen links, Hauptaktion rechts), vier Breitenklassen, Esc nur der oberste, Enter nur bei einem Textfeld, Klick daneben schließt nicht; Inline-Größen 0 unmarkiert. Dazu seit M8 Schritt 5 die Rückfrage „Revert tile“. Sie zählen als voll.
- **Aussehen der Zeilen im Dialog:** Karteneinstellungen/New map und der Kasten „Add texture…“ sind seit M4 im Optionsstil (`OptionControls`). Die Zeilen in Einstellungen, Parkliste, Verkehrskurven, Fahrplan und Fahrzeugen sind `form-*`-Klassen in `EditorShell.uss` mit den `--mf-*`-Farben. M4 hat sie bewusst nicht umgebaut („Dialoge werden nur in den Optionszeilen umgestellt“). Ob ihr Aussehen dem Corporate Design genügt, ist nur im Handtest und nicht durch Code zu belegen und kein eigener Restpunkt, solange der Projektinhaber keinen verlangt.
- **Luftbild-Fenster und eigene Bilder:** nicht modal, bewusst nicht aus der Fabrik (Entscheidung 6A, `Architecture.md`, Sonderform der Werkzeugfenster mit Straßentyp- und Ampel-Fenster); Zeilen seit M4 im Optionsstil. Die Bilderliste nutzt eigene Klassen (`satellite-result`).
- **Werkzeug-Optionen (Gelände, Boden malen, Markierungen, Vorfahrt, Spuren, Reihen, Linie, Zufallsdrehung, Überweg, Parkreihe):** alle über `OptionControls` (Zählung der Aufrufe je Datei in der Spalte „Anmerkung“). Die Roadmap führte sie als offen „nach dem Rework“; das war überholt und ist dort entfernt.

## Entscheidungen für die Restpunkte (Stand 22. November 2026)

- **Klein, in M8 (erledigt in M8 Schritt 5, Rest 1):** Kachel zurücksetzen fragt nach (`EditorShellTileRevert.cs`, Dialog `revert-tile-dialog`). Offen für den Handtest des Projektinhabers.
- **Nach dem Rework (Roadmap, Abschnitt „UI-Rework: nach dem Rework und Release 1.2.0“):** Kontextmenüs für Kreuzungen, Ampeln und Markierungen als eigene Elemente, „Kreuzungen aus Profil“ im Edit-Menü im neuen Stil. Dialoge und Werkzeug-Optionen sind kein Restpunkt mehr (Abschnitt „Dialoge: was „alt“ hieß“).
- **Kein Punkt:** Kontextmenü für Reihen und Splines in 3D (beides vorhanden, siehe oben).
