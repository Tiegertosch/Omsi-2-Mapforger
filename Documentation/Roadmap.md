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
- [ ] Koordinatentransformation und Kurvenrichtung an weiteren Kachelverbindungen visuell gegen OMSI prüfen
- [ ] Chrono-Varianten und TTData getrennt untersuchen
- [x] Tatsächliche Player-Abhängigkeiten prüfen und OMSI-Spielinhalte vom Ausliefern ausschließen; externes Lesen bleibt erlaubt
- [x] Textdateien unabhängig von der Laufzeit-Codepage dekodieren und Datensätze streng positionell lesen
- [x] Leser gegen Stadtückheim und Berlin-Spandau gegenprüfen
- [x] Automatische EditMode-Tests für Leser, Geometrie und Einstellungen ergänzen

## Später

- [x] Versioniertes Projektformat anlegen und externe OMSI-Karte aus einer gespeicherten Projektdatei wieder öffnen
- Kartenkacheln und Koordinatenverwaltung entwickeln
- 3D-Viewport und navigierbare Weltansicht ergänzen
- Geometrie- und Straßenwerkzeuge entwerfen
- Import-/Exportumfang schrittweise an den OMSI-Bestandsformaten ausrichten
- Validierungsberichte und Integritätsprüfungen ergänzen
- Freigabeschritt für Player-Assets vor einer öffentlichen Auslieferung wieder einführen (siehe ADR 005)
- `[worldcoordinates]`-Karten wie Berlin-Spandau unterstützen
