# Grundorf: erster Formatbefund

## Umfang

Die Untersuchung erfolgte lesend direkt im installierten OMSI-2-Ordner. Die Karte verweist in `global.cfg` auf 19 Basiskacheln. Zusammen enthalten diese laut Abschnittszählung 1.095 `[object]`-Einträge, 60 `[spline]`-Einträge und 37 Spline-/Objekt-Anbindungen. Die Karte beschreibt sich selbst als „Grundorf (Tutorial)“.

## Beobachtete Dateiformate

- `global.cfg` ist eine Textdatei mit wiederholten `[map]`-Abschnitten. Jeder Abschnitt nennt Kachel-X, Kachel-Y und den zugehörigen `.map`-Dateinamen. Name, Anzeigename und Beschreibung stehen ebenfalls in Abschnitten.
- Alle untersuchten Textdateien (`global.cfg`, `.map`) sind UTF-16 LE mit Byte Order Mark und CRLF. Der Leser erkennt das Byte Order Mark und fällt für ältere oder von Hand bearbeitete Dateien auf UTF-8 und danach Windows-1252 zurück, damit Umlaute in Namen und Pfaden unabhängig von der Laufzeit-Codepage erhalten bleiben.
- Datensätze sind positionell: Jede Zeile direkt unter dem Abschnittskopf ist genau ein Feld. Nach dem Datensatz folgen Leerzeilen und freier Text wie `Object Nr. 1`, der bis zum nächsten Abschnitt ignoriert wird. Der Leser überspringt deshalb keine Leerzeilen innerhalb eines Datensatzes; ein leeres Pflichtfeld wird als nicht dekodierbarer Datensatz gemeldet, statt spätere Felder zu verschieben.
- Grundorf verwendet das klassische kartesische Raster mit 300 m Kantenlänge pro Kachel. Die lokale Position wird für die Übersicht als `Welt-X = Kachel-X * 300 + Objekt-X` und `Welt-Y = Kachel-Y * 300 + Objekt-Y` behandelt. [openOMSI-Formatnotizen](https://github.com/openOMSI-org/openOMSI/blob/main/docs/FORMATS.md) beschreiben diese Feldreihenfolge; sie sind Reverse-Engineering-Notizen und keine offizielle OMSI-Spezifikation.
- Die Kachel-`.map`-Dateien sind Text mit wiederholten Abschnittsmarkern, darunter `[object]`, `[spline]`, `[rule]` und `[varparent]`. Im Version-14-Objekteintrag folgen auf Typ, Asset-Pfad und ID die lokale X/Y-Position, Höhe sowie Heading/Pitch/Bank. Ein Splineeinstieg enthält Pfad, ID, Nachbar-IDs, Startposition, Heading, Länge und Kurvenradius. Objekt- und Spline-Einträge verweisen auf externe `.sco`- beziehungsweise `.sli`-Dateien.
- Jede der 19 Basiskacheln besitzt eine 14.888 Byte große `.terrain`-Datei. Der beobachtete Headerwert `60` wird gefolgt von 61 × 61 little-endian `float`-Höhenwerten. Der Importer liest diese Werte nur zur Min-/Max-Höhenangabe in den Arbeitsspeicher; Terrain-Meshes werden nicht erzeugt.
- `TTData` enthält Linien-, Fahrplan-, Tour- und Haltestellendaten. `Chrono` enthält zeitabhängige Varianten, darunter eine alternative Kachel. Beides gehört noch nicht zum ersten Importumfang.

## Umgesetzter erster Import

Im Unity-Editor unter **OMSI Map Editor > Import > Read OMSI Map...** eine Karten-`global.cfg` auswählen. Die Übersicht zeigt das 300-m-Kachelraster, Objektpunkte, aus Heading/Länge/Radius angenäherte Spline-Linien sowie die Terrain-Höhenbereiche. Fehlende oder nicht dekodierbare Datensätze werden gemeldet. Die Quelldateien werden nur gelesen und nicht in das Unity-Projekt kopiert.

Der Batch-Mode kann denselben Parser aufrufen. Setze `OMSI_MAP_CONFIG` auf die `global.cfg` und starte Unity mit `-executeMethod OmsiMapEditor.OMSI.Editor.OmsiMapCli.InspectFromEnvironment`. Für Grundorf ergab der Lauf: 19 Kacheln, 1.095 Objekte, 60 Splines, 37 Anbindungen und 19 gelesene Terrain-Höhenfelder.

## Weitere installierte Karten

Am 19. Oktober 2026 wurde derselbe Leser lesend gegen die beiden anderen Karten der lokalen Installation geprüft. In allen drei Karten kommen innerhalb der gelesenen Objekt- und Splinefelder keine Leerzeilen vor; alle gezählten Objekte und Splines wurden dekodiert.

| Karte | Kacheln | Objekte | Splines | Anbindungen | Terrain | Hinweise |
|---|---|---|---|---|---|---|
| Grundorf | 19 | 1.095 | 60 | 37 | 19 | keine Warnungen |
| Stadtückheim | 164 | 5 | 13 | 0 | 164 | keine Warnungen |
| Berlin-Spandau | 369 | 29.450 | 1.993 | 2.520 | 329 | `[worldcoordinates]`; einige in `global.cfg` genannte Kacheln fehlen |

Warm aus dem Dateicache dauert das Einlesen von Berlin-Spandau rund 0,3 s; der erste Zugriff auf die Festplatte kann deutlich länger dauern.

## Grenze für OMSI-Spielinhalte

Der Editor darf OMSI-Dateien direkt aus der externen Installation lesen und die eingelesenen Daten zur Laufzeit verwenden. Die Quelldateien werden dabei nicht verändert. OMSI-Spielinhalte dürfen jedoch nicht mit dem fertigen Player ausgeliefert werden. Deshalb kopiert der Importer Quelldateien nicht in das Unity-Projekt oder den Build-Ordner.

Die Formatlogik in `Editor.OMSI` ist plattformneutral und darf als Programmcode im Player enthalten sein, damit dieser OMSI-Daten direkt aus einer externen Installation lesen kann. Das Unity-Editor-Fenster, der Batch-Einstieg und die Build-Prüfung liegen separat in `Editor.OMSI.Editor` und bleiben Editor-only. Vor dem Windows-Build ermittelt der Build-Helfer die tatsächlichen Player-Abhängigkeiten aus Szenen, `Resources`, `StreamingAssets` und Render-Einstellungen. Ist eine davon bytegleich mit einer Datei aus der zuletzt eingelesenen OMSI-Installation, blockiert das den Build. Nicht lesbare Ordner der Installation werden übersprungen und als Warnung im Build-Log gemeldet. Die frühere Freigabeliste mit einer SHA-256-Prüfsumme pro Player-Asset ist vorerst entfernt (siehe ADR 005 in [Decisions.md](Decisions.md)). Editor-only Arbeitsdateien außerhalb dieser Player-Abhängigkeiten sind von der Prüfung nicht betroffen.

Diese Build-Prüfung ist eine Schutzmaßnahme für den normalen Buildpfad. Sie garantiert nicht, dass beliebige spätere Codeänderungen keine Inhalte zur Laufzeit exportieren oder kopieren. Der vorgesehene Import bleibt lesend und verwendet OMSI-Inhalte direkt aus der Installation.

## Grenzen und nächste Untersuchung

Die aktuelle Leselogik dekodiert nur die für die Draufsicht erforderlichen Felder. Sie ist keine verlustfreie Import-/Exportimplementierung. Kurven werden aus dem Startpunkt mit konstantem Radius angenähert; Gefälle, Querneigung, Spline-Profile und Objektmodelle sind noch nicht dargestellt. Die 300-m-Annahme gilt für klassische kartesische Karten; `worldcoordinates`-Karten werden ausdrücklich als nicht unterstützt gemeldet.
