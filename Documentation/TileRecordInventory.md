# Kachel-Datensätze und global.cfg: Formatbefund

## Umfang

Am 20. Oktober 2026 wurden alle Abschnittsarten der Kachel-`.map`-Dateien und von `global.cfg` lesend untersucht. Grundlage waren die drei installierten Karten Grundorf, Stadtückheim und Berlin-Spandau. Ziel ist die Grundlage für verlustfreies Lesen und späteres Schreiben. Terrain-Inhalte und die Asset-Formate `.sco` und `.sli` gehören nicht dazu; sie folgen mit der 3D-Ansicht.

**Methode:** Ausgangspunkt waren die [openOMSI-Formatnotizen](https://github.com/openOMSI-org/openOMSI/blob/main/docs/FORMATS.md). Jede Feldbedeutung wurde an den Daten geprüft, wo eine Prüfung möglich war: über Querverweise, über die Stetigkeit verbundener Splines oder über den Vergleich mit referenzierten Objekten. Nicht prüfbare Bedeutungen sind als Vermutung gekennzeichnet.

| Abschnitt | Grundorf | Stadtückheim | Berlin-Spandau |
|---|---|---|---|
| `[object]` | 1.095 | 5 | 29.450 |
| `[attachObj]` | 22 | 0 | 1.447 |
| `[splineAttachement]` | 11 | 0 | 860 |
| `[splineAttachement_repeater]` | 4 | 0 | 213 |
| `[spline]` | 60 | 13 | 1.993 |
| `[spline_h]` | 0 | 15 | 493 |
| `[rule]` / `[kill_rule]` | 418 / 0 | 0 / 0 | 12.101 / 120 |
| `[varparent]` | 31 | 0 | 1.047 |
| `[spline_terrain_align]` / `_2` | 0 / 0 | 0 / 0 | 33 / 203 |
| `[water]` | 0 | 0 | 34 |

## Allgemeine Regeln

- **Positionell lesen:** Jede Zeile unter dem Abschnittskopf ist genau ein Feld, auch wenn sie leer ist. Danach folgen Leerzeilen und Freitext wie `Object Nr. 5`. In keinem der geprüften Datensätze stand nach dem letzten Feld etwas anderes.
- **Erstes Feld:** Fast jeder Datensatz beginnt mit einer kleinen Zahl, meist `0`, bei Anhängungen auch `1` oder `2`. Laut openOMSI ist das die Detailstufe.
- **IDs:** Objekte, angehängte Objekte, Splines und Anhängungen teilen sich einen Nummernraum. In allen drei Karten ist jede ID kartenweit eindeutig, in Berlin-Spandau 34.243 IDs.
- **Bezug auf das Vorgängerelement:** `[rule]`, `[kill_rule]`, `[varparent]` und `[spline_terrain_align]` gehören zum unmittelbar davor geschriebenen Objekt oder Spline.
- **Versionen:** Alle Kacheln haben `[version]` 11 bis 14. Die Unterschiede betreffen in den vorhandenen Daten nur die Splines.

## Objekte

### `[object]`

| Feld | Bedeutung | Prüfung |
|---|---|---|
| 0 | Detailstufe | |
| 1 | Pfad der `.sco`-Datei | |
| 2 | ID | eindeutig |
| 3, 4 | x und y in der Kachel, in Metern | |
| 5 | Höhe z | siehe unten |
| 6, 7, 8 | Heading, Pitch und Bank in Grad | alle numerisch |
| 9 | Anzahl der Beschriftungen | in allen 30.550 Objekten eine Ganzzahl |
| 10 … | genau so viele Beschriftungszeilen, auch leere | danach nur Freitext |

**Höhe:** Sie ist meist relativ zum Gelände. Bei Objekten, deren `.sco`-Datei einen `[splinehelper]` hat, ist sie absolut. Das sind vor allem Kreuzungen. In Berlin-Spandau liegt das Gelände bei etwa 30 bis 37 m. Dort haben 362 von 363 solcher Objekte eine Höhe über 5 m, bei den übrigen Objekten sind es 204 von 29.087. Diese 204 sind vermutlich Objekte mit absoluter Höhe aus anderen Gründen.

### `[attachObj]`

| Feld | Bedeutung |
|---|---|
| 0, 1, 2 | Detailstufe, Pfad, ID |
| 3 | ID des Elternobjekts |
| 4 | Instanz des Elternobjekts, immer `0` |
| 5 | Index des Anhängepunkts im Elternobjekt |
| 6, 7, 8 | Heading, Pitch, Bank |
| 9 … | Anzahl der Beschriftungen und die Beschriftungen |

Das Elternobjekt steht in allen 1.469 Fällen weiter oben in derselben Kachel. Ein Editor muss diese Reihenfolge beim Schreiben erhalten.

### `[splineAttachement]`

| Feld | Bedeutung |
|---|---|
| 0, 1, 2 | Detailstufe, Pfad, ID |
| 3 | Index des Splines in der Spline-Reihenfolge dieser Kachel; `[spline]` und `[spline_h]` zählen gemeinsam |
| 4 | seitlicher Versatz |
| 5 | Höhe |
| 6 | Startabstand entlang des Splines |
| 7, 8, 9 | Heading, Pitch, Bank |
| 10 | Abstand zwischen Wiederholungen |
| 11 | Reichweite |
| 12 | Kippflag |
| 13 … | Anzahl der Texte und die Texte |

Der Spline-Index liegt in allen 871 Fällen innerhalb der Spline-Anzahl der Kachel. Haltestellen in Fahrplänen können solche Objekte sein. Ihr seitlicher Versatz stimmt mit dem Fahrplanwert überein (siehe [TimetableChronoInventory.md](TimetableChronoInventory.md)).

### `[splineAttachement_repeater]`

Eine Wiederholung führt eine Anhängung über Kachelgrenzen fort.

| Feld | Bedeutung |
|---|---|
| 0 | Detailstufe |
| 1 | Kachelindex der Kachel mit dem Hauptdatensatz |
| 2 | Nummer des ersten Objekts dieser Fortsetzung |
| 3, 4 | Pfad und ID, gleich wie im Hauptdatensatz |
| 5 | Spline-Index in **dieser** Kachel |
| 6 … | Versatz, Höhe, Startabstand, Winkel, Abstand und Reichweite, gleich wie im Hauptdatensatz |

**Abgleich mit dem Hauptdatensatz:** Bei 185 von 217 Wiederholungen findet sich der Hauptdatensatz als `[splineAttachement]`. Die Felder ab 6 stimmen dort bis auf Rundung im Bereich von 10⁻⁵ überein. Die übrigen 32 Wiederholungen in Berlin-Spandau sind verwaist. Ihr Hauptdatensatz fehlt, oder an seiner Stelle stehen nur weitere Wiederholungen.

**Mitzählung:** Wiederholungen zählen bei Objektindizes mit, zum Beispiel bei den Einstiegspunkten.

## Splines

### `[spline]`

| Feld | Bedeutung | ab Version |
|---|---|---|
| 0, 1, 2 | Detailstufe, Pfad der `.sli`-Datei, ID | |
| 3, 4 | ID des Vorgängers und des Nachfolgers, 0 für offen | 11 |
| 5, 6, 7 | x, Höhe, y; Achtung: andere Reihenfolge als bei Objekten | |
| 8 | Heading in Grad | |
| 9 | Länge in Metern | |
| 10 | Radius; 0 gerade, positiv rechts | |
| 11, 12 | Steigung am Anfang und am Ende in Prozent | |
| 13, 14 | Querneigung am Anfang und am Ende | 5 |
| 15, 16 | Querverschiebung (Skew) am Anfang und am Ende | 14 |
| 17 | Ausrundungslänge | |
| 18 | optional `mirror` | 7 |

In Version 14 hat ein Spline 18 Felder oder 19 mit `mirror`. In den Versionen 11 und 12 sind es 16 Felder, weil die Querverschiebung fehlt.

### `[spline_h]`

`[spline_h]` hat ein zusätzliches Feld an Position 13: den **Höhenunterschied** zwischen Anfang und Ende in Metern. Querneigung, Querverschiebung, Ausrundungslänge und `mirror` rücken um eins nach hinten. Ein `[spline_h]` hat also 19 Felder, mit `mirror` 20. Das „h“ steht vermutlich für diese Höhenangabe.

**Beleg:** Bei normalen Splines ergibt sich die Endhöhe aus der linear verlaufenden Steigung. Sie ist die Anfangshöhe plus Länge mal Mittelwert der beiden Steigungen. Bei `[spline_h]` ist sie die Anfangshöhe plus Feld 13. Mit diesen beiden Regeln ist die Höhe an jeder Verbindung stetig. Mit der Steigungsregel allein passten bei `[spline_h]` nur 100 von 270 Verbindungen.

### Stetigkeit verbundener Splines

| Prüfung über Nachfolger-Verweise | Grundorf | Stadtückheim | Berlin-Spandau |
|---|---|---|---|
| Höhe stetig auf 5 cm | 36 von 36 | 17 von 17 | 1.442 von 1.442 |
| Querneigung stetig | 36 von 36 | 17 von 17 | 1.436 von 1.438 |
| Steigung stetig | 36 von 36 | 17 von 17 | 1.324 von 1.438 |

Steigungsknicke sind in OMSI also erlaubt; die Höhe bleibt trotzdem stetig. Die Lage in der Ebene ist in [GrundorfFormatInventory.md](GrundorfFormatInventory.md) geprüft.

## Regeln, Variablen und Terrain-Anpassung

- **`[rule]`:** Die Felder sind Pfadindex im Element, Regelname, Wert und eine weitere Zahl. Vorkommende Namen sind `trafficdensity`, `speedlimit`, `priority`, `trucks`, `no_cars` und `bus`. Regeln folgen Objekten und Splines.
- **`[kill_rule]`:** Gleiches Layout. In Berlin-Spandau kommt nur `priority` vor. Vermutlich wird damit eine Regel entfernt, die das Objekt selbst mitbringt.
- **`[varparent]`:** Ein Feld mit der ID des Elements, das die Variablen liefert, zum Beispiel die Kreuzung einer Ampel. In Berlin-Spandau liegt das Ziel 910-mal in derselben Kachel und 96-mal in einer Nachbarkachel. Die 41 Ziele, die hier zunächst als fehlend galten, sind Objekte entlang von Splines (`[splineAttachement]`); alle 1.047 Ziele existieren (Kartenprüfung vom 23. Oktober 2026).
- **`[spline_terrain_align]`:** Ohne Feld. Es folgt Splines und auch Kreuzungsobjekten, in 33 Fällen direkt einem Objekt. Laut openOMSI schneidet das Element dann seine Kontur aus dem Gelände.
- **`[spline_terrain_align_2]`:** Ein Feld mit den Werten 1 bis 4. Es steuert laut openOMSI, wie die Enden der Kontur behandelt werden. In Berlin-Spandau folgt es 203 Splines, 195-mal mit dem Wert 1; ein schlichtes `[spline_terrain_align]` folgt dort nur Objekten. Unter diesen Splines liegt das gespeicherte Gelände bis etwa 10 m neben der Mitte 0,15 bis 0,25 m über der Fahrbahnmitte (Median 0,18 m auf der Mitte, 0,15 m bei 6 bis 10 m), bei 20 m noch 0,06 m, bei 30 m wie sonst. Bei anderen Splines streut der Abstand weit (90 % innerhalb 5,2 m). Die Profile dieser Splines legen die Fahrbahn auf +0,10 m und die Gehwege auf +0,25 m; das Gelände liegt also auf Gehweghöhe. OMSIs Editor formt das Gelände an solche Splines an. Was OMSI im Spiel mit dem Kennzeichen tut, ist offen (Messung vom 23. Oktober 2026).
- **`[water]`:** Verweist auf die Binärdatei `tile_X_Y.map.water` mit einer 32-Bit-Anzahl und je vier Höhenwerten. Alle 34 Dateien haben die Anzahl 1 und sind 20 Byte groß.
- **`[terrain]`, `[variable_terrain]`, `[variable_terrainlightmap]`:** Flags ohne Felder. Ihre Inhalte gehören zur Terrain-Analyse.

## global.cfg

| Abschnitt | Felder | Status |
|---|---|---|
| `[name]`, `[friendlyname]` | ein Text | belegt |
| `[description]` | Freitext bis `[end]` | belegt |
| `[version]` | 14 | |
| `[NextIDCode]` | nächste freie ID | siehe unten |
| `[worldcoordinates]` | ohne Feld; Web-Mercator-Kacheln der Zoomstufe 16, in Berlin-Spandau rund 371,9 m | belegt, ADR 007 und ADR 019 |
| `[dynhelperactive]`, `[realrail]` | Flags ohne Feld | Bedeutung offen |
| `[backgroundimage]` | Flag, Bildpfad, Breite, Höhe, zwei Versätze | Vermutung; in beiden Karten ohne Bild |
| `[mapcam]` | Kachel-X, Kachel-Y, x, Höhe, y, drei Kamerawerte | Kachelkoordinaten belegt (2402, 11280 in Berlin-Spandau); Kamerawerte vermutlich Richtung, Neigung, Abstand |
| `[moneysystem]`, `[ticketpack]` | Pfad | Standard, Chrono kann ersetzen |
| `[repair_time_min]` | Minuten | |
| `[years]` | erstes und letztes Jahr | |
| `[realyearoffset]` | Jahresversatz, Berlin-Spandau −25 | Bedeutung vermutet |
| `[standarddepot]` | Depotname aus `ailists.cfg` | belegt über Namensgleichheit |
| `[groundtex]` | Textur, Detailtextur, Maskengröße als Zweierpotenz, Wiederholungen der Textur und der Detailtextur je Kachel | Masken unter `texture/map/tile_X_Y.map.<n>.dds` vorhanden, zum Beispiel 106 für Textur 2 in Berlin-Spandau |
| `[addseason]` | Art, erster Tag, letzter Tag im Jahr | Grundorf: Winter mit Schnee 0–38, Winter 38–75, Frühling 76–134, Herbst 260–323, Winter 324–370 |
| `[trafficdensity_road]`, `[trafficdensity_passenger]` | Stunde, Faktor; je Abschnitt ein Stützpunkt | belegt |
| `[entrypoints]` | Anzahl, dann je 12 Felder | siehe unten |
| `[map]` | Kachel-X, Kachel-Y, Dateiname | belegt |

### `[entrypoints]`

Nach der Anzahl folgen je Einstiegspunkt zwölf Felder.

| Feld | Bedeutung |
|---|---|
| 1 | Index des Objekts unter allen platzierten Objekten seiner Kachel in Dateireihenfolge, einschließlich angehängter Objekte und Wiederholungen |
| 2 | ID des Objekts, immer ein `entrypoint_bus.sco` |
| 3 | immer `0` |
| 4, 5, 6 | x, Höhe, y |
| 7–10 | Drehung als Quaternion (x, y, z, w) |
| 11 | Kachelindex |
| 12 | Name |

Die 58 Einstiegspunkte von Grundorf und Berlin-Spandau liegen höchstens 1 m neben ihrem Objekt. Der Winkel aus dem Quaternion, 2·atan2(y, w), entspricht dem Heading des Objekts. Der Index in Feld 1 stimmt in 58 von 58 Fällen, wenn Wiederholungen mitzählen, und nur in 53, wenn nicht. Der einzige Einstiegspunkt von Stadtückheim liegt ebenfalls auf seinem Objekt, aber sein Winkel weicht um 173° ab und sein Index passt nicht. Vermutlich ist er veraltet, wie der `[NextIDCode]` dieser Karte.

### `[NextIDCode]`

In Grundorf und Berlin-Spandau liegt der Wert genau eins über der höchsten ID, auch über den IDs der Chrono-Kacheln. In Stadtückheim ist er 92, und die ID 92 ist bereits vergeben. Ein Editor muss neue IDs deshalb oberhalb des größeren der beiden Werte vergeben: `[NextIDCode]` und höchste vorhandene ID plus eins.

### `[groundtex]` und Bodenmasken

Jeder `[groundtex]`-Abschnitt beschreibt eine Bodentextur: Pfad der Textur, Pfad der Detailtextur, dann drei Zahlen. Die erste Zahl ist die Kantenlänge der Maske als Zweierpotenz, die zweite die Wiederholungen der Textur je Kachel, die dritte die Wiederholungen der Detailtextur. Die Pfade sind relativ zur OMSI-Installation, nicht zur Karte, zum Beispiel `texture\gras.bmp` für `OMSI 2\Texture\gras.bmp`. Die Feldbedeutungen stammen aus den [openOMSI-Formatnotizen](https://github.com/openOMSI-org/openOMSI/blob/main/docs/FORMATS.md) und passen zu den Daten.

| Karte | Bodentexturen | Masken | Beispiel |
|---|---|---|---|
| Grundorf | 2 | 7 | Gras mit `gras_det` 60-mal, Kopfstein 16-mal mit Masken 512 × 512 |
| Berlin-Spandau | 8 | 230 | zusätzlich Schotter, Asphalt, Sand, zwei Feldarten, Betonplatten |

Die erste Bodentextur bedeckt die ganze Karte und hat keine Maske. Jede weitere hat je Kachel höchstens eine Maske `texture\map\tile_x_y.map.<Index>.dds`. Fehlt die Datei, ist die Textur auf dieser Kachel nicht gemalt. Eine Maske ist ein 8-Bit-Alpha-DDS der angegebenen Größe. Ihre erste Zeile liegt an der Nordkante, ihre erste Spalte an der Westkante. Das wurde an Grundorf geprüft: Die Kopfsteinflächen der Masken liegen unter der Wendeschleife, dem Haltestellenplatz und der Dorfmitte, gespiegelt passen sie nicht. Alle Bodentexturen der drei Karten sind 24-Bit-BMP. Die Detailtextur wird einfach aufmultipliziert; die mitgelieferten Detailtexturen sind hell, `noise_low` hat im Mittel 242.

### Geländehöhen

`tile_x_y.map.terrain` beginnt mit der 32-Bit-Zahl 60, dann folgen 61 × 61 Höhen als 32-Bit-Gleitkommazahlen, je eine alle 1/60 der Kachelkante. Der erste Wert liegt an der Südwestecke, x läuft am schnellsten nach Osten, die Zeilen laufen nach Norden. Geprüft an den absolut gespeicherten Spline-Höhen: In Berlin-Spandau liegen 2.466 Spline-Anfänge im Median 1 mm und zu 80 % höchstens 0,25 m über dem bilinear gelesenen Gelände. Bei gespiegelter oder vertauschter Anordnung sind es im Median 0,4 bis 0,6 m und zu 80 % bis 2,9 m.

Weitere Dateien je Kachel neben der Karte: `tile_x_y.map.LM.bmp` ist eine 256 × 256 große Nacht-Lightmap mit dem Licht der Laternen. `texture\map\tile_x_y.map.roadmap.bmp` ist OMSIs schematische Karte für die Kartenanzeige im Spiel. Mapforge verwendet beide nicht.

## Zahlenformat

Der OMSI-Editor schreibt Kommazahlen mit höchstens 15 gültigen Ziffern und invarianter Kultur. Sehr kleine Werte schreibt er wissenschaftlich mit kurzem Exponenten, zum Beispiel `1.33514401335476E-5` oder `-3.59590604939723E-6`. Ganze Werte stehen ohne Nachkommastellen, etwa `300`. Diese Regel reproduziert 243.482 von 243.487 Kommazahlen der drei Karten zeichengenau. Die fünf Ausnahmen haben einen zweistelligen Exponenten wie `1.024455E-06` und stammen von einem anderen Werkzeug. `OmsiNumberFormat` setzt diese Regel um.

## Folgerungen für Mapforge

1. **Verlustfrei lesen:** Der Leser sollte jeden Datensatz vollständig und positionell lesen. Unbekannte Felder müssen mitsamt Freitext und Reihenfolge erhalten bleiben. Nur so lässt sich eine Kachel unverändert zurückschreiben. Umgesetzt mit `OmsiRecordFile` und `OmsiTileRecordLayouts` (ADR 008).
2. **Reihenfolge ist Teil des Formats:** Spline-Indizes, Objektindizes der Einstiegspunkte, Elternverweise und Regeln hängen an der Dateireihenfolge. Ein Editor muss diese Indizes beim Einfügen und Löschen nachführen.
3. **IDs:** Neue IDs müssen kartenweit eindeutig sein und oberhalb des größeren Werts aus `[NextIDCode]` und höchster ID plus eins liegen. Chrono-Kacheln zählen mit. Umgesetzt mit `OmsiIdAllocator`; der Leser meldet einen veralteten `[NextIDCode]` als Warnung.
4. **Validierung:** Neue Prüfungen bieten sich an, etwa für verwaiste Wiederholungen, `[varparent]`-Ziele ohne Element, einen veralteten `[NextIDCode]` und Einstiegspunkte, deren Index oder Winkel nicht zum Objekt passt.

## Offene Fragen

- Bedeutung von `[dynhelperactive]`, `[realrail]`, `[realyearoffset]` und der Kamerawerte in `[mapcam]`.
- Zweck der zusätzlichen Zahl in `[rule]` und der Kippflag in `[splineAttachement]`.
- Ältere Kachelversionen unter 11 sind in der Installation nicht vorhanden und deshalb ungeprüft.
