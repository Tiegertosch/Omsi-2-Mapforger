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

## Später

- [x] Versioniertes Projektformat anlegen und externe OMSI-Karte aus einer gespeicherten Projektdatei wieder öffnen
- Kartenkacheln und Koordinatenverwaltung entwickeln
- [x] 3D-Viewport und navigierbare Weltansicht ergänzen: Gelände aus den Höhen je Kachel, perspektivische Kamera mit Drehen, Verschieben und Zoomen (ADR 013)
- [x] In der 3D-Ansicht Objekte wählen, über das Gelände verschieben und an drei Ringen drehen: grün Heading, rot Pitch, blau Bank (ADR 014)
- [x] Pitch und Bank von Objekten lesen, zeichnen, im Inspector bearbeiten und rückgängig machen; Objekte mit `[splinehelper]` bleiben ungekippt (ADR 014)
- [x] Alle Werkzeuge der Draufsicht auch in 3D: Strg+Rahmen, Entf, Platzieren auf dem Gelände, Marker für Objekte ohne Modell, Kachelrand, Einpassen, Zoomtasten, Zentrieren, Position unter dem Mauszeiger
- [x] Vorzeichen von Pitch und Bank im Spiel prüfen (22. Oktober 2026: Wartehäuschen mit Pitch 15 und Laterne mit Bank 15 in Grundorf wie im Editor)
- [x] Bäume in der 3D-Ansicht als gekreuzte Flächen wie in OMSI zeigen; die Kronenkuppel der Draufsicht wirkt von der Seite wie ein Schirm
- Gelände unter Straßen ausschneiden, wie OMSI es mit `[terrainholeprofile]` tut
- [x] Draufsicht mit Bodentexturen: `[groundtex]` und Masken je Kachel, von einer Kamera unter der Vektorebene gezeichnet (ADR 013)
- [x] Straßen und Gleise als texturierte Bänder aus den `.sli`-Profilen
- Freileitungen, Fahrdraht und Zäune in der Draufsicht als dünne Linien zeigen
- [x] Objekte mit ihren Materialtexturen von oben zeichnen: erste Detailstufe, `[visible]` im Sommer, `.dds` vor `.bmp`, Instancing, auf dem Gelände (ADR 013)
- [x] Text-`.x`-Modelle mit Texturkoordinaten, Normalen und Materialien lesen, auch Materialverweise
- [x] Modelle beim Ziehen und Drehen mitbewegen, auf dem Gelände unter der neuen Position
- [x] Bäume als Kuppel mit dem Kronenausschnitt ihrer Seitentextur zeigen statt als Scheibe in Durchschnittsfarbe
- Speicher und Ladezeit der Bodentexturen großer Karten begrenzen, etwa Masken erst laden, wenn ihre Kachel sichtbar wird
- Geometrie- und Straßenwerkzeuge entwerfen
- Import-/Exportumfang schrittweise an den OMSI-Bestandsformaten ausrichten
- Validierungsberichte und Integritätsprüfungen ergänzen
- Freigabeschritt für Player-Assets vor einer öffentlichen Auslieferung wieder einführen (siehe ADR 005)
- [x] `[worldcoordinates]`-Karten wie Berlin-Spandau mit gemessener Kachelkante darstellen (siehe ADR 007)
- Exakte Kachelgeometrie von `[worldcoordinates]`-Karten aus der Kachelnummer ableiten; die feste Kante weicht bis etwa 0,35 m ab
- [x] Objekte darstellen, die OMSI mit Koordinaten außerhalb ihrer eigenen Kachel speichert (18 in Berlin-Spandau); die durchgehende Draufsicht zeigt sie an ihrer Weltposition
- Spline-Datensätze vor Kachelversion 11 dekodieren
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
- Zeichenzeit großer Karten senken, etwa mit der Hülle statt der Kontur für Objekte, die nur wenige Pixel groß sind; Berlin-Spandau braucht vereinzelt etwas über 50 ms
- [x] Mehrere Objekte gleichzeitig auswählen und verschieben: Strg+Klick, Strg+Rahmen, gemeinsames Ziehen und Löschen als ein Rückgängig-Schritt
- [x] Objekte beim Verschieben über eine Kachelgrenze in die neue Kachel übernehmen, samt Regeln und mit derselben ID (ADR 012)
- Objekte mit kachelgebundenen Verweisen mitsamt diesen Verweisen umziehen: Einstiegspunkte, Chrono-Auswahl, angehängte Objekte und Variablen-Eltern
- Mehrere Objekte gemeinsam drehen
- [x] Objekte in der App anlegen und löschen: Auswahl aus den `.sco`-Dateien der Installation, Platzieren per Klick, Löschen mit Prüfung aller Verweise, Rückgängig über Dateistände (ADR 011)
- Höhe neuer Objekte mit absoluter Höhe, etwa Kreuzungen, aus dem Gelände übernehmen; bisher wird 0 geschrieben und ein Hinweis angezeigt
- Andere Elemente anlegen und löschen: angehängte Objekte, Spline-Anhängungen und Splines
- Fahrplandaten lesend importieren und ihre Querverweise als Validierungsregeln prüfen
- Haltestellen und Linienverläufe in der Kartenübersicht darstellen
- Chrono-Varianten als geordnete Änderungsebenen laden und für ein gewähltes Datum überlagern
- [x] Angehängte Objekte (`[attachObj]`, auch mehrstufig) und Spline-Anhängungen zeichnen; der Startabstand zählt ab dem Kettenanfang
- [x] Rückseiten wie OMSI weglassen, Glas überblenden, `[matl_transmap]` als Deckkraft auswerten, Bäume in 3D als gekreuzte Flächen
- [x] Transparenzkarten am 70er-Wartehäuschen im Player prüfen (22. Oktober 2026: Seitenscheiben halbdurchsichtig)
