# Roadmap

## Schritt 1: Entwicklungsumgebung

- [x] Unity-Projektstruktur und Modulgrenzen vorbereiten
- [x] URP-Paketversion festlegen und Editor-Setup für das Pipeline-Asset anlegen
- [x] UI Toolkit-Startansicht und Startszene vorbereiten
- [x] Git- und C#-Konventionen dokumentieren
- [x] Unity-Import, Kompilierung und Windows-Build auf einer installierten Unity-Umgebung ausführen

## Schritt 2: OMSI-Bestandsformate untersuchen

Grundorf wurde als erste Beispielkarte untersucht. Ein schreibgeschützter Importer liest `global.cfg`, Kachel-`.map`-Dateien und Terrain-Höhen. Die Unity-Editorübersicht zeichnet Kacheln, Objektpunkte und angenäherte Spline-Linien. Der Importumfang und die feste Grenze für OMSI-Spielinhalte stehen in [GrundorfFormatInventory.md](GrundorfFormatInventory.md).

- [x] Grundorf-Datei- und Kachelinventar ermitteln
- [x] Lesenden Import von `global.cfg` und Kachelzählung umsetzen
- [x] Für Grundorf die Datensatzfelder in `[object]` und `[spline]` sowie das 300-m-Koordinatenmodell dokumentieren
- [x] Terrain-Binärheader und 61 × 61 Höhenfelder lesen
- [x] Kachel-Draufsicht mit Objektpunkten und angenäherten Spline-Linien implementieren
- [x] Kacheln in der Laufzeitübersicht auswählen und Objekte, Splines sowie Terrainwerte im Inspektor anzeigen
- [x] Kartenübersicht an den Viewport anpassen, Zoomsteuerung ergänzen und Kartenkopf beim Scrollen sichtbar halten
- [x] Koordinatentransformation und Kurvenrichtung an den Kachelverbindungen prüfen: rechnerisch über die Endpunkte verbundener Splines in allen drei installierten Karten
- [x] Spline-Verbindungsprüfung als erste Kartenvalidierung in App und Unity-Fenster anbieten
- [x] Chrono-Varianten und TTData getrennt untersuchen (siehe [TimetableChronoInventory.md](TimetableChronoInventory.md))
- [x] Tatsächliche Player-Abhängigkeiten prüfen und OMSI-Spielinhalte vom Ausliefern ausschließen; externes Lesen bleibt erlaubt
- [x] Textdateien unabhängig von der Laufzeit-Codepage dekodieren und Datensätze streng positionell lesen
- [x] Leser gegen Stadtückheim und Berlin-Spandau gegenprüfen
- [x] Automatische EditMode-Tests für Leser, Geometrie und Einstellungen ergänzen

## Oberfläche nach dem Corporate Design (ADR 015)

- [x] Etappe 1: Design-Tokens, randlose Karte, schwebende einklappbare Panels, Ansichtsknöpfe, Statusleiste
- [x] Etappe 2: untere Werkzeugleiste mit den fertigen Werkzeugen und Tastenkappen (Drag Q zum freien Ziehen, Move W mit Achsenpfeilen auch für die Höhe, Rotate E, Delete Entf, Objects 3), Lucide-Icons als Vektorpfade
- [x] Etappe 3: Objekte-Panel mit Bibliothek nach Ordnern, Reitern „In map“ und „Favorites“ und Suche statt des Platzierdialogs
- [x] Etappe 4: Schrift Inter, Icons für Ansichtsknöpfe und Panels, Umschalter für Texturen und 3D
- [x] Startbildschirm: Projekt öffnen, Karte öffnen, zuletzt geöffnete Projekte und Karten
- [x] Neue Karte anlegen: Kartenordner mit Hilfsdateien aus einer Basiskarte als Vorlage, eine leere Kachel mit flachem Gelände, dazu ein Projekt (ADR 017); auf dem Startbildschirm und unter **File > New map...**
- [x] Neue Karte im Spiel prüfen: Ohne Einstiegspunkt stürzt OMSI beim Laden ab; neue Karten bekommen den Einstiegspunkt „Start“ auf einem `entrypoint_bus.sco`
- [x] Objekte-Bibliothek: DLC-Objekte nach Steams Dateilisten ausblenden oder kennzeichnen, User-Content bleibt sichtbar; Kartenprüfung nennt DLC-Objekte (ADR 016)
- [x] Ordner von Hand als DLC markieren, für außerhalb von Steam gekaufte Add-ons: Etikett-Knopf an jedem Ordner der Bibliothek, je Benutzer gespeichert; Bibliothek, „Hide DLC objects“, „In map“ und Kartenprüfung zählen markierte Ordner als DLC, Dateien des Basisspiels bleiben Basisspiel
- [x] Objekte-Bibliothek: Vorschaubilder der Objekte aus `Sceneryobjects\nEditorThumbnails` (angelegt vom Drittanbieter-Editor nEditor, nicht von OMSI), Reiter „Groups“ mit den `[groups]` der `.sco`-Dateien, große Gruppen zuerst
- [x] Farben der Vektorebene an das Corporate Design anpassen: Auswahl rot, gewählte Kachel gold, Splines orange, Objekte blau, Bäume und Platzieren grün; die Farben kommen aus den Design-Tokens

## Später

- [x] Versioniertes Projektformat anlegen und externe OMSI-Karte aus einer gespeicherten Projektdatei wieder öffnen
- Kartenkacheln und Koordinatenverwaltung entwickeln
- [x] 3D-Viewport und navigierbare Weltansicht ergänzen: Gelände aus den Höhen je Kachel, perspektivische Kamera mit Drehen, Verschieben und Zoomen (ADR 013)
- [x] In der 3D-Ansicht Objekte wählen, über das Gelände verschieben und an drei Ringen drehen: grün Heading, rot Pitch, blau Bank (ADR 014)
- [x] Pitch und Bank von Objekten lesen, zeichnen, im Inspector bearbeiten und rückgängig machen; Objekte mit `[splinehelper]` bleiben ungekippt (ADR 014)
- [x] Alle Werkzeuge der Draufsicht auch in 3D: Strg+Rahmen, Entf, Platzieren auf dem Gelände, Marker für Objekte ohne Modell, Kachelrand, Einpassen, Zoomtasten, Zentrieren, Position unter dem Mauszeiger
- [x] Vorzeichen von Pitch und Bank im Spiel prüfen (22. Oktober 2026: Wartehäuschen mit Pitch 15 und Laterne mit Bank 15 in Grundorf wie im Editor)
- [x] Bäume in der 3D-Ansicht als gekreuzte Flächen wie in OMSI zeigen; die Kronenkuppel der Draufsicht wirkt von der Seite wie ein Schirm
- [x] Gelände unter Straßen ausschneiden, wie OMSI es tut: unter Objekten mit `[terrainhole]`-Modell (Kreuzungen) und entlang von Splines mit `[terrainholeprofile]` (Gleistrog), als Lochmaske je Kachel, 0,75 m kleiner als das Lochmodell (ADR 018); senkrechte Profilstreifen wie Trogwände, Bordsteinkanten und Zäune erscheinen in 3D
- [x] Draufsicht mit Bodentexturen: `[groundtex]` und Masken je Kachel, von einer Kamera unter der Vektorebene gezeichnet (ADR 013)
- [x] Straßen und Gleise als texturierte Bänder aus den `.sli`-Profilen
- [x] Freileitungen, Fahrdraht und Zäune in der Draufsicht als dünne Linien zeigen: schmale Profile (höchstens 0,6 m) ab 2,5 m Höhe gestrichelt als Leitung, ab 1 m Höhe vom Boden als Zaun oder Mauer (ADR 018)
- [x] Objekte mit ihren Materialtexturen von oben zeichnen: erste Detailstufe, `[visible]` im Sommer, `.dds` vor `.bmp`, Instancing, auf dem Gelände (ADR 013)
- [x] Text-`.x`-Modelle mit Texturkoordinaten, Normalen und Materialien lesen, auch Materialverweise
- [x] Modelle beim Ziehen und Drehen mitbewegen, auf dem Gelände unter der neuen Position
- [x] Bäume als Kuppel mit dem Kronenausschnitt ihrer Seitentextur zeigen statt als Scheibe in Durchschnittsfarbe
- [x] Speicher und Ladezeit der Bodentexturen großer Karten begrenzen: Masken werden erst gelesen, wenn ihre Kachel in die Nähe des Ausschnitts kommt (von oben bis 3 km Bildhöhe, in 3D im Umkreis des Kamerablicks), höchstens 96 Kacheln behalten sie, die am längsten unsichtbaren geben sie zuerst wieder ab
- Geometrie- und Straßenwerkzeuge entwerfen
- Import-/Exportumfang schrittweise an den OMSI-Bestandsformaten ausrichten
- [x] Validierungsberichte und Integritätsprüfungen ergänzen: Bericht unter **Tools** mit zehn Themen (Import, Spline-Verbindungen, IDs und `[NextIDCode]`, Einstiegspunkte samt Index und Winkel, Anhängungen und verwaiste Wiederholungen, `[varparent]`-Ziele, fehlende Objekt- und Spline-Dateien, Fahrplan, Chrono-Auswahlen, DLC-Objekte), im Hintergrund berechnet, mit Sprung zur Kachel und „Copy report“
- [x] Freigabeschritt für Player-Assets vor einer öffentlichen Auslieferung wieder einführen (siehe ADR 005): Freigabeliste `PlayerAssetReview.txt` mit SHA-256 je Player-Datei, normaler Build warnt, Release-Build bricht ab, Menübefehl „Record Reviewed Player Assets...“
- [x] `[worldcoordinates]`-Karten wie Berlin-Spandau mit gemessener Kachelkante darstellen (siehe ADR 007)
- [x] Exakte Kachelgeometrie von `[worldcoordinates]`-Karten aus der Kachelnummer ableiten: Die Kacheln sind Web-Mercator-Kacheln der Zoomstufe 16, gezählt von Greenwich und vom Äquator (ADR 019); die Verbindungsprüfung misst darin exakt (Berlin-Spandau 1.464 von 1.467 Verbindungen unter 5 cm), die Kante der ebenen Darstellung kommt aus der mittleren Kachelzeile, der Inspector nennt Breite und Länge der Kachel
- [x] Objekte darstellen, die OMSI mit Koordinaten außerhalb ihrer eigenen Kachel speichert (18 in Berlin-Spandau); die durchgehende Draufsicht zeigt sie an ihrer Weltposition
- Spline-Datensätze vor Kachelversion 11 dekodieren – zurückgestellt: Keine installierte Karte hat solche Kacheln mit Splines (HafenCity Hamburg hat 79 Kacheln der Version 10, alle ohne Splines), und das Format ist nicht dokumentiert; ohne Beispieldatei wäre die Feldfolge geraten (Stand 23. Oktober 2026)
- [x] Vollständige Kachel-Datensätze und `global.cfg` untersuchen (siehe [TileRecordInventory.md](TileRecordInventory.md))
- [x] Kachel-Datensätze verlustfrei lesen und unverändert zurückschreiben können (ADR 008; Rundreise über alle 1.409 Textdateien byte-identisch)
- [x] Bearbeitungsablauf mit Speicherziel festlegen: Arbeitskopie im Projekt, Export als eigene OMSI-Karte (ADR 009)
- [x] Typisierte Zugriffe für Objekte, Splines und Anhängungen auf dem verlustfreien Modell aufbauen
- [x] Neue IDs aus `[NextIDCode]` und höchster vorhandener ID vergeben; ein veralteter `[NextIDCode]` wird beim Lesen gemeldet
- [x] Strukturelle Änderungen ermöglichen: Datensätze einfügen und löschen, Beschriftungsanzahl und `mirror` ändern und abhängige Indizes nachführen
- Splines mit Nachbarn löschen und die Nachbarverweise in anderen Kacheln mitführen; bisher wird das Löschen dann verweigert
- [x] Erste Bearbeitung in der App: Objekte auswählen, Position und Richtung ändern, in der Arbeitskopie speichern, Kachel zurücksetzen und als eigene Karte exportieren
- [x] Exportierte Karte in OMSI laden und die geänderten Objekte im Spiel prüfen (20. Oktober 2026: verschobene Scheune in Grundorf im Spiel sichtbar)
- [x] Durchgehende 2D-Draufsicht mit Zoom und Verschieben; Objekte per Ziehen verschieben, am Griff drehen und Änderungen rückgängig machen
- [x] `.sco`- und `.o3d`-Dateien analysieren und Objekte mit ihren Umrissen darstellen (siehe [SceneryObjectInventory.md](SceneryObjectInventory.md))
- [x] Genauere, nicht konvexe Umrisse für L-förmige Gebäude, große Kreuzungen und Plätze: Dreiecke werden von oben gerastert und zu Konturen mit Löchern verbunden
- [x] Zeichenzeit großer Karten senken: Objekte je Kachel mit aufgelöstem Umriss und Grenzen zwischengespeichert, Kacheln außerhalb des Ausschnitts übersprungen, Objekte unter 12 Pixeln mit ihrer Hülle statt der Kontur; Berlin-Spandau bleibt in allen geprüften Ausschnitten unter 50 ms (vorher bis 94 ms)
- [x] Mehrere Objekte gleichzeitig auswählen und verschieben: Strg+Klick, Strg+Rahmen, gemeinsames Ziehen und Löschen als ein Rückgängig-Schritt
- [x] Objekte beim Verschieben über eine Kachelgrenze in die neue Kachel übernehmen, samt Regeln und mit derselben ID (ADR 012)
- Objekte mit kachelgebundenen Verweisen mitsamt diesen Verweisen umziehen: Einstiegspunkte, Chrono-Auswahl, angehängte Objekte und Variablen-Eltern
- Mehrere Objekte gemeinsam drehen
- [x] Objekte in der App anlegen und löschen: Auswahl aus den `.sco`-Dateien der Installation, Platzieren per Klick, Löschen mit Prüfung aller Verweise, Rückgängig über Dateistände (ADR 011)
- Höhe neuer Objekte mit absoluter Höhe, etwa Kreuzungen, aus dem Gelände übernehmen; bisher wird 0 geschrieben und ein Hinweis angezeigt
- Andere Elemente anlegen und löschen: angehängte Objekte, Spline-Anhängungen und Splines
- [x] Fahrplandaten lesend importieren und ihre Querverweise als Validierungsregeln prüfen (`OmsiTimetable`, `OmsiTimetableCheck`); Verweise auf Elemente, die erst Chrono-Varianten anlegen, werden eigens benannt
- [x] Haltestellen und Linienverläufe in der Kartenübersicht darstellen: „Routes“ in der Ansichtsleiste, Linienliste im Projekt-Panel zum Hervorheben, Linienwege auf der befahrenen Spur von Splines und Kreuzungsobjekten (`OmsiTimetableRoutes`), Haltestellennamen beim Hineinzoomen; nur in der Draufsicht
- [x] Chrono-Varianten als geordnete Änderungsebenen laden und für ein gewähltes Datum überlagern: Bereich „Chrono“ im Projekt-Panel mit Datum, Variantenliste und „Base map“, Überlagerung im Hintergrund, Linienwege aus dem Fahrplan des Datums, Ansicht schreibgeschützt (ADR 020)
- Chrono-Varianten bearbeiten: Änderungen einer Variante zuordnen statt der Kachel
- [x] Angehängte Objekte (`[attachObj]`, auch mehrstufig) und Spline-Anhängungen zeichnen; der Startabstand zählt ab dem Kettenanfang
- [x] Rückseiten wie OMSI weglassen, Glas überblenden, `[matl_transmap]` als Deckkraft auswerten, Bäume in 3D als gekreuzte Flächen
- [x] Transparenzkarten am 70er-Wartehäuschen im Player prüfen (22. Oktober 2026: Seitenscheiben halbdurchsichtig)
