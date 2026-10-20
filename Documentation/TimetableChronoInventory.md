# TTData und Chrono: Formatbefund

## Umfang

Am 20. Oktober 2026 wurden die Fahrplandaten (`TTData`) und die zeitabhängigen Kartenvarianten (`Chrono`) der lokalen OMSI-2-Installation lesend untersucht. Grundorf diente als kleines Beispiel, Berlin-Spandau als Gegenprobe mit realem Umfang. Stadtückheim hat einen leeren `TTData`-Ordner und keine Chrono-Varianten.

| Karte | TTData-Dateien | Chrono-Ordner | Chrono-Dateien | Chrono-Kacheln |
|---|---|---|---|---|
| Grundorf | 10 | 1 | 10 | 1 |
| Stadtückheim | 0 | 0 | 0 | 0 |
| Berlin-Spandau | 168 | 24 | 640 | 253 |

**Methode:** Ein Analyseskript las alle Dateien und prüfte jeden Querverweis gegen die Kartenkacheln. Es prüfte zum Beispiel, ob eine Haltestelle auf ein vorhandenes Objekt zeigt und ob eine Verbindungslänge zur Summe ihrer Wegstücke passt. Die Feldbedeutungen unten gelten erst als belegt, wenn sie diese Prüfungen in beiden Karten bestehen. Ausgangspunkt waren die [openOMSI-Formatnotizen](https://github.com/openOMSI-org/openOMSI/blob/main/docs/FORMATS.md). Sie sind Reverse-Engineering-Notizen und keine offizielle Spezifikation. Abweichungen davon stehen in einem eigenen Abschnitt.

## Gemeinsame Grundlagen

- **Abschnitte:** Alle Dateien verwenden dasselbe Muster wie die Kachel-`.map`-Dateien. Ein Abschnittskopf in eckigen Klammern wird von genau einem Feld pro Zeile gefolgt. Freitext wie `Dep.: 4:7:0` oder `0:` zwischen den Abschnitten ist nur Kommentar.
- **Kodierung:** TTData-Dateien sind UTF-8 ohne Byte Order Mark, einzelne `.cfg`-Dateien Windows-1252. Chrono-Kacheln und Markierungsdateien sind UTF-16 LE mit Byte Order Mark. Die Sprachdateien `.dsc` kommen in allen drei Kodierungen vor. Der vorhandene `OmsiTextFile`-Dekodierer deckt alle Fälle ab.
- **Kachelindex:** Viele Felder verweisen nicht über X/Y auf eine Kachel, sondern über die Position des Eintrags in der `[map]`-Liste von `global.cfg`, beginnend bei 0. In Grundorf ist Index 4 zum Beispiel `tile_1_2.map`.
- **Element-IDs:** Eine ID bezeichnet ein Objekt oder einen Spline in der angegebenen Kachel. Objekte schließen die angehängten Varianten `[attachObj]` und `[splineAttachement]` ein. Bei `[splineAttachement_repeater]` steht die ID im fünften Feld, nach einer Kennung, dem Kachelindex, der Spline-ID und dem Pfad.
- **Rundung:** Längen und Positionen schreibt OMSI mit drei gültigen Ziffern. Unter 100 m bleibt also eine Nachkommastelle, darüber werden ganze Meter geschrieben, zum Beispiel 51,4 und 272. Vergleiche müssen diese Rundung berücksichtigen.

## TTData

### Haltestellen: `Busstops.cfg`

Jeder `[busstop]`-Abschnitt beschreibt einen Haltepunkt.

| Feld | Bedeutung |
|---|---|
| 1 | Name, zum Beispiel `Nordspitze` |
| 2 | Kachelindex |
| 3 | ID des Haltestellenobjekts in dieser Kachel |
| 4 | Versatz; die Bedeutung ist noch nicht bestätigt |
| 5, 6 | immer `0` |

Ein Name kommt meist zweimal vor, einmal je Fahrtrichtung. Die Fahrten verweisen über die Objekt-ID auf die Haltestelle, nicht über den Namen.

### Stationsverbindungen: `StnLinks.cfg`

Ein `[StnLink]` beschreibt den Fahrweg zwischen zwei Haltestellen. Darauf folgen nummerierte `[StnLink_entry]`-Abschnitte, ein Abschnitt pro befahrenem Pfad.

| Feld | Bedeutung |
|---|---|
| 1 | Länge von Haltestelle zu Haltestelle in Metern |
| 2, 3 | Objekt-ID der Start- und der Zielhaltestelle |
| 4, 5 | Abstand der Start- und der Zielhaltestelle vom Pfad, vermutlich seitlich |
| 6 | Position der Starthaltestelle entlang ihres Pfads |
| 7 | Position der Zielhaltestelle entlang ihres Pfads |
| 8 | Index des Eintrags, auf dem die Starthaltestelle liegt |
| 9 | Index des Eintrags, auf dem die Zielhaltestelle liegt |

Ein `[StnLink_entry]` hat die Felder Element-ID, Pfadnummer im Element, Kachelindex, Pfadlänge sowie immer `-1`, `0`, `0`.

**Belegt durch die Länge:** Feld 1 entspricht der Strecke vom Start bis zum Ziel. Sie besteht aus dem Rest des Starteintrags ab Feld 6, den vollen Längen der Einträge dazwischen und dem Anfang des Zieleintrags bis Feld 7. Die Abweichung beträgt in Grundorf höchstens 0,40 m und in Berlin-Spandau im Median 0,23 m. Die Einträge reichen oft über die Haltestellen hinaus. Deshalb sind die Felder 8 und 9 nicht immer 0 und der letzte Index.

**Abstand vom Pfad:** Die Werte gehören zur Haltestelle und zum befahrenen Pfad. In Grundorf hat jede Haltestelle in allen Verbindungen denselben Wert, in Berlin-Spandau gilt das für 159 von 237 Haltestellen. Die Deutung als seitlicher Abstand ist plausibel, aber nicht belegt.

### Fahrwege: `.ttr`

Eine `.ttr`-Datei ist eine Folge von `[track_entry]`-Abschnitten. Jeder Abschnitt ist ein befahrener Pfad.

| Feld | Bedeutung |
|---|---|
| 1 | Element-ID, also Objekt oder Spline |
| 2 | Pfadnummer im Element, also der `[path]` in der `.sco` oder `.sli` |
| 3 | Kachelindex |
| 4 | Kachelweite Pfadnummer |
| 5 | Pfadlänge in Metern |
| 6 | immer `0` |

**Pfadnummern:** Eine Kreuzung wird über mehrere ihrer Pfade befahren, zum Beispiel Objekt 19 in Grundorf über die Pfade 17, 18, 19 und 7. Feld 4 ist dann 93, 94, 95 und 83. Die Differenz zwischen Feld 4 und Feld 2 ist je Element konstant. Feld 4 ist also eine kachelweite Nummerierung aller Pfade, die OMSI selbst ableitet.

**Längen:** Bei Spline-Pfaden entspricht Feld 5 der Länge der Spline-Mittellinie auf drei gültige Ziffern, auch bei versetzten Fahrspuren in Kurven.

### Fahrten: `.ttp`

Eine Fahrt beginnt mit `[trip]` und drei Zeilen. Die erste nennt den Fahrweg, die zweite das Fahrtziel, die dritte die Linie. Ist die erste Zeile leer, fährt die Fahrt über die Stationsverbindungen. Danach folgen die Halte in einer von zwei Formen.

- **`[station_typ2]`:** Diese Form nutzen Busfahrten ohne eigenen Fahrweg. Jeder Abschnitt enthält nur die Objekt-ID einer Haltestelle. Zwei aufeinanderfolgende Halte sind immer durch eine Stationsverbindung verbunden: 13 von 13 in Grundorf, 1.474 von 1.474 in Berlin-Spandau.
- **`[station]`:** Diese ältere Form nutzen Fahrten mit eigenem Fahrweg, vor allem Schienenverkehr. Sie hat acht Felder: Objekt-ID, Index des Fahrwegeintrags, Name, Kachelindex, seitlicher Abstand, Strecke ab Fahrwegbeginn, Position im Eintrag und eine weitere Zahl. Der Eintragsindex zählt ab 1. Der seitliche Abstand stimmt mit dem Versatz angehängter Haltestellenobjekte überein, zum Beispiel 0,036 gegen 0,0359 m. Die Strecke ab Fahrwegbeginn ergibt sich aus den Einträgen vor dem Haltepunkt plus der Position im Eintrag. Das passt für 15 von 19 Einträgen. Die vier Ausnahmen sind U-Bahn-Fahrten der Linie U1 in Berlin-Spandau mit negativen Positionen.

Am Ende stehen ein oder mehrere `[profile]`-Abschnitte mit Name und Fahrzeit in Minuten. Optional folgen Ankunfts- und Abfahrtszeiten je Halt sowie `[profile_otherstopping]`.

### Linien und Umläufe: `.ttl`

Eine Liniendatei enthält `[userallowed]` als Flag ohne Wert und `[priority]`. Danach folgen Umläufe als `[newtour]` mit Nummer, KI-Fahrzeuggruppe und einer weiteren Zahl. Jeder Umlauf reiht `[addtrip]`-Abschnitte mit Fahrtname, Profilindex und Abfahrt in Minuten nach Mitternacht. Die Minuten stimmen in allen 130 Grundorfer und 4.418 Spandauer Fahrten mit dem Kommentar `Dep.: h:m:s` davor überein. Jede Fahrt verweist auf eine vorhandene `.ttp`-Datei.

### Prüfergebnisse

| Prüfung | Grundorf | Berlin-Spandau |
|---|---|---|
| Haltestellenobjekt in seiner Kachel gefunden | 13 von 13 | 237 von 238 |
| Stationsverbindungen | 14 | 326 |
| Länge passt zur Strecke zwischen den Haltestellen | 14 von 14 | 320 von 322 |
| Verbindungseinträge, Element in der Kachel gefunden | 97 von 97 | 4.055 von 4.055 |
| Fahrwege und ihre Einträge | 3 mit 91 | 18 mit 726 |
| Fahrwegeinträge, Element in der Kachel gefunden | 91 von 91 | 726 von 726 |
| Fahrwegeinträge auf Splines, Länge passt | 50 von 50 | 571 von 571 |
| Fahrten über Stationsverbindungen und über Fahrwege | 2 und 1 | 107 und 18 |
| Umläufe und eingeplante Fahrten | 3 und 130 | 137 und 4.418 |

**Auffälligkeiten in Berlin-Spandau:**
- **Haltestelle ohne Objekt:** Die Haltestelle „Fernbahnhof Spandau“ verweist auf Objekt 2548688. Dieses Objekt kommt in keiner Datei der Karte vor.
- **Verbindungen zu Chrono-Haltestellen:** Drei Stationsverbindungen der Basisdaten beginnen oder enden an Haltestellen, die erst Chrono-Varianten hinzufügen oder die gar nicht existieren.
- **Doppelte Verbindungen:** Vier Haltestellenpaare haben zwei Stationsverbindungen.
- **Leere Einträge:** 328 Verbindungseinträge haben die Länge 0. Sie liegen auf dem Eintrag der Zielhaltestelle oder dahinter, wo ihre Länge nicht in die Verbindungslänge eingeht. Grundorf hat 2 solche Einträge.

## Chrono

### Ordner und Aktivierung

Jeder Unterordner von `Chrono` ist eine Variante. Der Ordnername beginnt mit einer Nummer, zum Beispiel `0100_BaustelleNordspitze`. Ein Ordner kann enthalten:

- **`Chrono.cfg`:** Gültigkeit und globale Änderungen, siehe unten.
- **`Chrono_<Sprache>.dsc`:** Name und Beschreibung je Sprache, zum Beispiel `DEU` und `ENG`.
- **`tile_X_Y.map`:** Änderungslisten für einzelne Kacheln.
- **`TTData/`:** Ergänzende und ersetzende Fahrplandateien.
- **`ailists_#upd.cfg`:** Aktualisierte KI-Fahrzeuglisten.
- **Weitere Dateien:** Markierungsdateien `tile_X_Y.map.chronoterrrelevant` und Parklisten `parklist_p*.txt`.

Die Ordner bauen in Namensreihenfolge aufeinander auf. Das belegen die Kachel-Änderungslisten. 46 Objektauswahlen und 5 Spline-Auswahlen in Berlin-Spandau beziehen sich auf Elemente, die erst ein früherer Ordner hinzufügt. Namens- und Datumsreihenfolge können voneinander abweichen. `5001_Bauarbeiten_1` gilt zum Beispiel 1989, steht aber am Ende.

### `Chrono.cfg`

| Schlüssel | Inhalt | Vorkommen in Berlin-Spandau |
|---|---|---|
| `[startdate]` | Beginn als `JJJJMMTT`; fehlt nur bei `3001_Tarif 1986`, die damit vermutlich von Anfang an gilt | 23 von 24 |
| `[enddate]` | Ende als `JJJJMMTT` | 9 |
| `[deactivate_lines]` | Anzahl, dann je Zeile ein Linienname wie `5 & 5N`; der Name entspricht einer `.ttl`-Datei | 6, alle 23 Namen gefunden |
| `[ticketpack]` | Pfad zu einem Fahrscheinpaket, zum Beispiel `TicketPacks\Berlin_91\Berlin_91.otp` | 5 |
| `[moneysystem]` | Pfad zu einem Währungssystem, zum Beispiel `Money\DM\DM_91.cfg` | 3 |

Die Grundorfer Variante gilt vom 1. bis 14. April 1992.

### Kachel-Änderungslisten

Eine Chrono-Kachel ersetzt die Basiskachel nicht. Sie enthält eine Änderungsliste. Von 272 IDs in den Spandauer Chrono-Kacheln kommt nur eine auch in der zugehörigen Basiskachel vor. Neue Elemente erhalten neue IDs und stehen im normalen Datensatzformat in der Datei. Bestehende Elemente werden ausgewählt und dann verändert.

| Befehl | Felder | Wirkung |
|---|---|---|
| `[selobject]` | ID | wählt ein Objekt der Basiskachel oder eines früheren Ordners aus |
| `[selspline]` | ID | wählt einen Spline aus |
| `[delete]` | keine | löscht das ausgewählte Element |
| `[typ]` | Pfad | ersetzt die `.sco`-Datei des ausgewählten Objekts |
| `[relabel]` | Index, Text | ersetzt einen Beschriftungstext, zum Beispiel Index 1 mit `13N` |
| `[rule]` | Pfad, Name, Wert, Zusatz | setzt eine Regel wie `trafficdensity` oder `priority` für einen Pfad |
| `[kill_rule]` | wie `[rule]` | entfernt diese Regel |

`[rule]` und `[kill_rule]` beziehen sich auf das zuletzt ausgewählte oder zuletzt neu angelegte Element.

| Zählung in Berlin-Spandau | Anzahl |
|---|---|
| Objektauswahlen, davon in der Basiskachel, aus früheren Ordnern, als Wiederholungsobjekt einer Nachbarkachel | 882, davon 825, 46, 11 |
| Spline-Auswahlen, davon in der Basiskachel und aus früheren Ordnern | 350, davon 345 und 5 |
| Löschungen, Typwechsel, Neubeschriftungen | 104, 404, 666 |
| Regeln gesetzt und entfernt | 2.370 und 39 |
| Neue Objekte, angehängte Objekte, Splines | 152, 115, 10 |

In Grundorf ändert `tile_1_1.map` die Kachel für eine Baustelle. Die Datei fügt 135 Objekte hinzu, darunter die verlegte Haltestelle 8840. Sie wählt 3 Objekte und 4 Splines aus, löscht ein Objekt und setzt 19 Regeln.

### Fahrpläne in Chrono-Ordnern

- **Haltestellen und Verbindungen:** `Busstops.cfg` und `StnLinks.cfg` ergänzen die Basisdaten und ersetzen sie nicht. Grundorf fügt eine einzige Haltestelle mit zwei Verbindungen hinzu. Alle 24 Spandauer Ordner haben beide Dateien, oft leer. Einige Einträge wiederholen Haltestellen der Basis.
- **Fahrten und Linien:** Eine `.ttp`- oder `.ttl`-Datei mit dem Namen einer Basisdatei ersetzt diese. Die Grundorfer Baustelle ersetzt `76_BH-Kk.ttp` und tauscht darin nur Halt 129 gegen Halt 8840. Neue Namen fügen Fahrten und Linien hinzu.

### KI-Listen: `ailists_#upd.cfg`

Die Basisdatei `ailists.cfg` enthält Fahrzeuggruppen (`[aigroup_2]`) und Depotlisten (`[aigroup_depot]` mit `[aigroup_depot_typgroup_2]`). Die Depotlisten nennen je Fahrzeug Wagennummer, Werbung und einen Gültigkeitszeitraum, getrennt durch Tabulatoren. Die 11 Spandauer `#upd`-Dateien enthalten Gruppen und Depots mit gleichem oder neuem Namen. Die Endung `#upd` deutet darauf hin, dass gleichnamige Einträge die Basis aktualisieren und neue hinzukommen. Das ist aus den Daten abgeleitet und nicht im Spiel überprüft.

### Markierungsdateien `.chronoterrrelevant`

Alle 18 Dateien enthalten nur das Wort `Dummy`. Sie liegen in zwei Spandauer Ordnern und markieren dort jeweils denselben Block von 3 × 3 Kacheln um `tile_2393_11285`. Nur 8 davon haben im selben Ordner eine Chrono-Kachel. Vermutlich kennzeichnen sie Kacheln, deren Terrain OMSI für diese Variante neu berechnen muss. Das ist nicht belegt.

## Abweichungen von den openOMSI-Notizen

- **Eintragsindex:** Beim älteren `[station]`-Format zählt der Index des Fahrwegeintrags ab 1. Die Notizen nennen keinen Startwert.
- **Stillgelegte Linien:** `[deactivate_lines]` beginnt mit der Anzahl der folgenden Liniennamen.
- **Stationsverbindungen:** Die Notizen lassen die Felder nach Start und Ziel offen. Ihre Bedeutung ist oben belegt.
- **Wiederholungsobjekte:** Bei `[splineAttachement_repeater]` steht die Objekt-ID an anderer Stelle als bei den übrigen Objekten. Chrono-Auswahlen verweisen auch auf diese IDs, und zwar in jeder Kachel, die das Objekt berührt.

## Offene Fragen

- Bedeutung des Versatzfelds in `Busstops.cfg` und der vierten Zahl im älteren `[station]`-Format.
- Die vier U1-Halte mit negativen Positionen und Objekten außerhalb ihrer Kachel.
- Genaue Wirkung von `#upd` und `.chronoterrrelevant`; beides lässt sich nur im Spiel sicher prüfen.
- Inhalt und Zweck der Parklisten `parklist_p*.txt`.

## Folgerungen für Mapforge

1. **Lesender Fahrplan-Import:** Haltestellen, Stationsverbindungen, Fahrwege, Fahrten und Linien lassen sich lesend in `Editor.OMSI` übernehmen. Die belegten Querverweise eignen sich direkt als Validierungsregeln, ähnlich der Spline-Verbindungsprüfung.
2. **Chrono als Ebenen:** Eine Variante ist eine geordnete Änderungsliste über der Basiskarte. Ein späterer Editor sollte die Basiskarte nie mit einer Variante zusammenführen. Er sollte Varianten als eigene Ebenen halten und beim Speichern wieder als Änderungslisten schreiben.
3. **Darstellung:** Die Kartenübersicht kann Haltestellen und Linienverläufe einzeichnen und für ein gewähltes Datum die aktiven Varianten überlagern.
