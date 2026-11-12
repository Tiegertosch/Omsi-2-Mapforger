# UI-Rework: Funktionsmatrix (12. November 2026)

Wie jede Funktion von MapWorks in der Oberfläche nach ADR 098 erreichbar ist und wie weit ihr Fenster dem neuen Aufbau folgt. Grundlage: [ui-rework-audit.md](ui-rework-audit.md). Vollständigkeit der Funktionsliste gegen [Roadmap.md](Roadmap.md) und die Dateien in `Assets/_Project/Scripts/UI/`.

## Lesehilfe

- **Einstieg:** Wo man die Funktion heute findet, mit Fundstelle. Pfade ohne Ordner liegen in `Assets/_Project/Scripts/UI/`; `Bootstrap` = `EditorShellBootstrap.cs`.
- **Integration**
  - **voll** – über die neue Struktur erreichbar *und* im neuen Aufbau (Katalog mit Kopfzeile bzw. Popup der Leiste, Kontextmenü).
  - **teilweise** – erreichbar, aber Fenster oder Optionen im alten Stil, oder ein Teil fehlt.
  - **alt** – erreichbar, vom Rework nicht angefasst.
  - **Lücke** – etwas ist durch das Rework weggefallen oder fehlerhaft.
- **Nutzertest:** „ja“ heißt, die Etappe, zu der der Weg gehört, ist laut ADR 098 vom Nutzer getestet (Etappe 1, Esc, Tooltips) – nicht, dass jeder einzelne Eintrag geprüft wurde; „offen“ heißt gebaut, aber noch nicht vom Nutzer bestätigt. Ein Test der Funktion selbst (vor dem Rework) zählt hier nicht.

## Einstiegspunkte der neuen Oberfläche

| Ort | Inhalt | Fundstelle |
|---|---|---|
| Menü File | New map, Open project, Save project as, Export, Open OMSI map, Settings, Object and spline folders | `EditorShell.uxml:68–78` |
| Menü Edit | Place object, Delete selection, Fit terrain to built roads again, Test-/T-/Kreuzung aus Profil, Undo, Redo | `EditorShell.uxml:79–89` |
| Menü Tools | Inspector, Satellite and terrain, Road builder, Check map | `EditorShell.uxml:90–96` |
| Menü Help | Bedienhilfe, Check for updates, Report a bug | `EditorShell.uxml:97–111` |
| Leiste links | Routes, Textures, 3D, Einpassen, Zoom, Zentrieren; Kachelknopf; Projektpille (öffnet „Karte“) | `EditorShell.uxml:37–52`, `EditorShellBottomBar.cs:43–56` |
| Leiste Mitte | Drag Q, Move W, Rotate E, Delete Entf, Road R, Rows T, Terrain G, Markings M, Priorities V, Lanes L, Signals A, Line B, Objects 3 | `MapToolbar.cs:27–43` |
| Leiste rechts | Popups Pfade, Einstiegspunkte, Linien, Chrono, Karte | `EditorShellBottomBar.cs:26–30` |
| Kontextmenü | Objekt, Mehrfachauswahl, Spline, Kachel | `EditorShellContextMenu.cs:149–241` |
| Tasten | Werkzeugtasten, Esc, Entf, Strg+C/V/D/Z/Y | `Bootstrap:6019–6280` |

## Matrix

| Funktion | Einstieg heute | Integration | Nutzertest im Rework | Anmerkung |
|---|---|---|---|---|
| **Projekt und Karte** | | | | |
| Karte/Projekt öffnen, speichern, neue Karte, Export | Menü File; Startbildschirm (`Bootstrap:351–376`, `:495`) | alt | – | vom Rework nicht berührt |
| Karte ohne Projekt ansehen | Open OMSI map | **Lücke** | – | Objektauswahl wirft eine Ausnahme (Audit 5.1, am Player belegt) |
| Programm-Einstellungen, Sprache | File › Settings (`EditorShellSettings.cs:55`) | teilweise | offen | neuer Schalter „Hide DLC content“; kein Prüfargument für den Dialog |
| Objekt- und Spline-Ordner | File › Object and spline folders (`EditorShellContentFolders.cs:32`) | **Lücke** | offen | Ordner-Knopf im Katalog entfiel; Hinweis „N Ordner ausgelassen“ fehlt |
| Kartenprüfung | Tools › Check map (`Bootstrap:754`) | voll (Menü), Dialog alt | offen | früherer Knopf *Tools* entfiel |
| Updates, Fehlerbericht | Menü Help | alt | – | |
| Modus der Karte (nur lesen, Variante, geändert) | – | **Lücke** | – | Anzeige versteckt (`EditorShell.uss:552`) |
| **Ansicht** | | | | |
| Draufsicht, 3D, Texturen, Zoom, Einpassen, Zentrieren | Leiste links | voll | ja | |
| Linien anzeigen (Routes) | Leiste links | voll | ja | öffnet die Linienliste nicht mehr mit |
| KI-Pfade anzeigen (Paths) | Leiste links (`EditorShellPaths.cs:39–44`); Liste im Popup „Pfade“ | voll | ja | Popup nur bei eingeschalteten Pfaden |
| Tooltips | überall (`EditorShellTooltip.cs`) | voll | ja | |
| **Listen der Karte (Popups)** | | | | |
| Kacheln mit Filter | Kachelknopf (`EditorShellProjectTabs.cs:148`) | voll | ja | |
| Kachel hinzufügen / löschen | Plus auf der Karte (`Bootstrap:527`); Löschen laut ADR 031 im Inspector | voll / teilweise | ja | Einstieg des Löschens nicht einzeln nachverfolgt; kein Eintrag im Kontextmenü |
| Kachel zurücksetzen | Kontextmenü; Inspector (`Bootstrap:7053`) | **Lücke** | – | ohne Rückfrage, leert Undo (Audit 5.3) |
| Karte: Name, Zahlen, Warnungen | Popup „Karte“ (`EditorShellProjectTabs.cs:63`) | voll | ja | |
| Karteneinstellungen | Popup „Karte“ › Knopf (`EditorShellMapSettings.cs:19`) | teilweise | – | Dialog alt; nur mit Projekt |
| Parkliste, Tageskurven | Popup „Karte“ › Knöpfe (`EditorShellProjectTabs.cs:92–93`) | teilweise | – | Dialoge alt |
| Einstiegspunkte | Popup; Kontextmenü „Rename entry point…“ (`EditorShellEntryPoints.cs`) | voll | ja | |
| Chrono-Varianten, Datum | Popup „Chrono“ (`Bootstrap:7991`, `EditorShellChronoDates.cs`) | voll | ja | welche Variante bearbeitet wird, ist nicht dauerhaft sichtbar |
| Linienliste | Popup „Linien“ (`EditorShellProjectTabs.cs:110`) | voll | ja | |
| Fahrplan-Editor, Fahrzeuge | Popup „Linien“ › Knöpfe (`:120–122`) | teilweise | – | Dialoge alt, 105 Inline-Größen |
| **Objekte** | | | | |
| Objektbibliothek, Platzieren | Leiste „Objects“ 3; Edit › Place object (`ObjectLibraryPanel.cs`) | teilweise | offen | Katalog neu; Optionen im alten Stil; Esc im Suchfeld schließt alles (Audit 5.7) |
| Auswählen, Ziehen, Verschieben, Drehen | Q, W, E | voll | ja | |
| Löschen | Entf, Leiste, Kontextmenü, Edit-Menü | voll | ja | |
| Kopieren, Duplizieren | Strg+C/D, Kontextmenü (`EditorShellClipboard.cs`) | voll | ja | |
| Einfügen | Strg+V; Kontextmenü „Paste here“ | **Lücke** | – | eigener Pfad (Audit 5.2) |
| Eigenschaften, Typ und Labels, Elternobjekt | Kontextmenü → Inspector-Gruppen (`EditorShellInspector.cs:121`) | voll | ja | |
| Typ ändern („Change type…“) | Kontextmenü, Inspector (`EditorShellLabels.cs:122`) | **Lücke** | – | Titel der Wahl unsichtbar; Wahl überlebt Werkzeugwechsel (Risiko) |
| Haltestelle benennen | Kontextmenü „Rename bus stop…“ (`EditorShellBusStop.cs`) | voll | ja | Quelle des Fehlers 5.1 ohne Projekt |
| Gelände unter Objekt einebnen | Kontextmenü, Inspector | voll | ja | |
| Zufällige Drehung | Optionen der Bibliothek (`EditorShellRandomHeading.cs`) | teilweise | offen | alte Optionsspalte |
| **Straßen und Splines** | | | | |
| Straße bauen | Leiste R (`RoadToolPanel.cs`) | voll | offen | einziges Fenster mit Icon-Optionen; Meldungen werden von Hinweisen überschrieben (Audit 5.6) |
| Spline-Eigenschaften, Höhen, Querschnitt, Aussehen | Kontextmenü → Inspector (`Bootstrap:8608–8636`) | voll in der Draufsicht | ja | in 3D trifft das Kontextmenü keine Splines |
| Spline löschen, Varianten-Typ | Entf, Kontextmenü (`EditorShellSplineVariants.cs`) | voll | ja | |
| Enden und Gelenke ziehen | Griffe auf der Karte | alt | – | |
| Straßenbauer (eigene Profile) | Tools › Road builder (`Bootstrap:763`) | teilweise | offen | „New road type…“ im Katalog entfiel; Fenster im alten Stil; ohne Karte stumm |
| Kreuzungen aus Profil | Edit-Menü (drei Einträge, `EditorShell.uxml:83–85`); Straßen-Option „Kreuzungen“ laut ADR 098 (nicht nachverfolgt) | teilweise | – | Menüeinträge alt; kein Kontextmenü an Kreuzungen |
| Gelände an Straßen neu anpassen | Edit-Menü | alt | – | |
| Überweg, Parkreihe | Straßen-Optionen, Zeile „Bauen“ (`RoadToolPanel.cs:141–161`); Überweg auch im Spuren-Werkzeug (`EditorShellLanes.cs:68`) | teilweise | offen | |
| Reihen entlang von Straßen | Leiste T (`RowToolOptions.cs`) | teilweise | – | Fenster alt; kein Kontextmenü |
| **Kreuzungen und Verkehr** | | | | |
| Markierungen | Leiste M (`MarkingToolOptions.cs`) | teilweise | offen | Katalog neu (uncommittet), Optionen alt |
| Vorfahrt | Leiste V (`EditorShellPriorities.cs`) | teilweise | – | nur Hülle neu |
| Spuren, Abbiegespur | Leiste L (`EditorShellLanes.cs`) | teilweise | – | nur Hülle neu |
| Ampeln, Phasen, Zeitleiste | Leiste A (`EditorShellSignals*.cs`) | teilweise | – | nur Hülle neu; 380 px breite Sonderspalte |
| Ampel-Assistent, Vorlagen | Knöpfe im Ampel-Fenster (`EditorShellSignals.cs:328–340`) | alt | – | Dialoge alt |
| KI-Pfade und Regeln je Element | Kontextmenü „AI paths and rules…“ → Inspector (`EditorShellPaths.cs:304`) | voll | ja | nur bei eingeschalteten Pfaden; stumm, solange Pfade nicht gelesen sind |
| **Gelände und Unterlagen** | | | | |
| Geländepinsel, Bodentexturen malen | Leiste G (`TerrainToolOptions.cs`, `GroundPaintOptions.cs`) | teilweise | offen | Katalog neu; Optionen alt |
| Höhenkarte auflegen | Gelände-Fenster, Reiter „Höhenkarte“ | voll | offen | |
| Luftbilder, Verortung, Geländehöhen | Tools › Satellite and terrain (`EditorShellAerial.cs:243`) | **Lücke** | – | Fenster alt; sperrt alle Tastenkürzel (Audit 5.4) |
| Eigene Bilder | im Luftbild-Fenster (`EditorShellPictures.cs`; Einstieg nicht einzeln nachverfolgt) | alt | – | |
| **Fahrplan** | | | | |
| Linie anlegen (Haltestellen klicken) | Leiste B (`LineToolPanel.cs`, `EditorShellLine.cs`) | teilweise | – | Fenster alt |
| Fahrten, Zeiten, Depots | Fahrplan- und Fahrzeug-Dialog | alt | – | |
| **Querschnitt** | | | | |
| Undo/Redo | Strg+Z/Y, Edit-Menü | voll | ja | gesperrt bei offenem Luftbild-Fenster; wirkt unter offenem Kontextmenü |
| Tastenkürzel | `OnShortcut` | voll | ja | keine Tests; Tab-Fokus unsichtbar |
| Mehrsprachigkeit | alle neuen Texte über `LocBindings`, acht Sprachen | voll | ja | Werkzeug-Hinweise wechseln die Sprache erst bei der nächsten Bewegung |
| Kommandozeile für Prüfläufe | [CommandLine.md](CommandLine.md) | teilweise | – | kein Argument für Spline-/Kachel-Menü und Settings |

## Auswertung

| Integration | Anzahl Zeilen | Bedeutung |
|---|---|---|
| voll | 25 | Kern der täglichen Arbeit an Objekten, Ansicht und Listen |
| teilweise | 17 | erreichbar; Fenster oder Optionen folgen noch dem alten Aufbau |
| alt | 7 | Dialoge und Menüeinträge, die das Rework nicht anfasst |
| Lücke | 7 | siehe Audit, Abschnitte 3 und 5 |

Gezählt sind die 56 Zeilen der Matrix; „voll / teilweise“ zählt als voll.

- **Keine Kernfunktion ist unerreichbar – mit einer Ausnahme:** Auf einer Karte ohne Projekt lässt sich kein normales Objekt mehr fehlerfrei auswählen. Das ist kein Fehler des Reworks selbst (er kam mit der Haltestellen-Benennung), trifft aber dessen Hauptweg, den Rechtsklick.
- **Das größte Feld ist „teilweise“:** neun Werkzeug-Fenster teilen sich zwei Optionsstile; nur die Straße hat den neuen.
- **Kein Kontextmenü** haben bisher Reihen, Kreuzungen als solche, Ampeln, Markierungen und Splines in 3D.
