# Szenerieobjekte und Modelle: Formatbefund

## Umfang

Am 20. Oktober 2026 wurden die Szenerieobjekte (`.sco`) und ihre Modelle (`.o3d`, `.x`) untersucht, soweit sie für die Umrisse in der Draufsicht nötig sind. Die drei installierten Karten verwenden 927 verschiedene Objektdateien mit 34.018 Platzierungen. Eine davon fehlt in der Installation. Die Objekte verweisen auf 1.050 Modelldateien.

| Modellformat | Dateien |
|---|---|
| `.o3d` Version 5, Schlüssel 0, verwürfelt | 953 |
| `.o3d` Version 1, unverwürfelt | 57 |
| `.x` Text | 32 |
| `.o3d` Version 5, Schlüssel `0xFFFFFFFF` | 6 |
| `.o3d` Version 7, Schlüssel 0, verwürfelt | 2 |

Ausgangspunkt waren die [openOMSI-Formatnotizen](https://github.com/openOMSI-org/openOMSI/blob/main/docs/FORMATS.md). Das Entwürfelungsverfahren für `.o3d` ist aus dem openOMSI-Quellcode übernommen (siehe [ADR 010](Decisions.md) und [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)).

## `.sco`-Abschnitte für den Umriss

Eine `.sco`-Datei folgt dem üblichen Abschnittsformat. Für den Umriss zählen diese Abschnitte:

| Abschnitt | Felder | Bedeutung |
|---|---|---|
| `[mesh]` | Dateiname | Modell im Unterordner `model` neben der `.sco` |
| `[onlyeditor]` | keine | Das **folgende** `[mesh]` ist ein Hilfsmodell, das nur der OMSI-Editor zeigt |
| `[LOD]` | Schwellwert | Die folgenden Modelle gehören zu dieser Detailstufe |
| `[collision_mesh]` | Dateiname | Kollisionsmodell |
| `[boundingbox]` | Größe x, y, z, Mitte x, y, z | im Objektrahmen, z nach oben; 76 Objekte haben einen |
| `[tree]` | Textur, kleinste und größte Höhe, kleinster und größter Breitenfaktor | Bäume; ihr Modell `treehelper.x` ist nur ein Hilfsobjekt |
| `[path]`, `[path_2]` | x, y, z, Heading, Radius, Länge, Steigung Anfang und Ende, Art, Breite, Richtung, Blinker | Fahrspuren im Objektrahmen |
| `[splinehelper]` | Spline-Datei, x, Höhe, y, Heading, zwei weitere | Anschlusspunkt für Splines; Höhe vor y wie bei Spline-Datensätzen |
| `[absheight]` | keine | Höhe des Objekts absolut statt relativ zum Gelände |

Die Feldreihenfolge von `[splinehelper]` ergibt sich aus den Daten, zum Beispiel `-3.647`, `-0.2`, `20.681`, `350`. Die Bedeutung der Felder von `[tree]` ist eine Vermutung aus den Werten, zum Beispiel `10`, `18`, `0.4`, `0.5`.

## `.o3d`

Das Format folgt den openOMSI-Notizen: Kennung `84 19`, Versionsbyte, ab Version 3 ein Flag-Byte, ab Version 4 ein 32-Bit-Schlüssel. Danach folgen Abschnitte mit einem Kennbyte: Eckpunkte (`0x17`), Dreiecke (`0x49`), Materialien (`0x26`), eine Matrix (`0x79`) und Knochen (`0x54`). Unbekannte Bytes werden einzeln übersprungen.

**Verwürfelung:** Ab Version 4 sind die Eckpunkte verwürfelt, wenn der Schlüssel nicht `0xFFFFFFFF` ist. Ein 16-Bit-Zustand aus Schlüssel, Version und Flags wird pro Eckpunkt fortgeschrieben. Er hängt auch von einem Wert aus der vorherigen Rohposition ab. Je nach Zustand werden zwei Positionsachsen getauscht, Normalen negiert oder getauscht und Texturkoordinaten verschoben. Bei Schlüssel 0 beginnt der Zustand an jedem Eckpunkt neu.

**Beleg:** Für jedes Dreieck wurde geprüft, ob die gespeicherte Normale zur Fläche passt.

| Modelle | ohne Entwürfelung | mit Entwürfelung |
|---|---|---|
| Version 5, Schlüssel 0 (934) | Median 26 % | Median 100 %, 97,6 % der Modelle über 80 % |
| unverwürfelte Modelle (88) | Median 99,9–100 % | unverändert |

## `.x`

Verwendet werden nur Textdateien (`xof 0303txt`). Mapforge liest Frames mit ihren `FrameTransformMatrix`-Matrizen, verkettet sie von innen nach außen und übernimmt Eckpunkte und Flächen aller Meshes. Mehreckige Flächen werden in Dreiecke zerlegt. Binäre `.x`-Dateien werden mit einer Meldung abgelehnt; in den installierten Karten kommen keine vor.

## Achsen und Drehung

- **Achsen:** Modelle verwenden den Direct3D-Rahmen: X rechts, Y oben, Z vorne. In der Draufsicht wird Modell-X zu Objekt-x und Modell-Z zu Objekt-y. Die Anschlusspunkte von Kreuzungen bestätigen das. Mit dieser Zuordnung liegen 757 von 876 auf oder in der Modellfläche, bei den Alternativen nur 373 bis 551.
- **Drehung:** Ein Objekt wird um sein Heading im Uhrzeigersinn gedreht. Ein lokaler Punkt (x, y) liegt danach bei (x·cos h + y·sin h, −x·sin h + y·cos h). Mit dieser Drehung liegen 34 von 36 Anschlusspunkten in Grundorf und 1.035 von 1.171 in Berlin-Spandau auf einem Spline-Ende. Gespiegelt wären es 11 und 141.

## Umrisse

`OmsiObjectOutlineCatalog` berechnet je Objektart einen Umriss in dieser Reihenfolge:

1. **Baum:** ein Kreis mit mittlerer Höhe mal mittlerem Breitenfaktor als Durchmesser.
2. **Modelle:** die Fläche, die die Dreiecke von oben bedecken. Editor-Hilfsmodelle zählen nur, wenn das Objekt keine anderen hat. Alle Detailstufen werden zusammengefasst.
3. **Bounding Box:** ein Rechteck, wenn kein Modell lesbar ist.
4. **Fahrspuren:** die Fläche der auf ihre Breite erweiterten Spuren.

**Kontur statt Hülle:** `OmsiFootprintTracer` zeichnet die Dreiecke in ein Raster mit 0,2 m Zellen; Objekte über 200 m bekommen gröbere Zellen, höchstens 1.000 je Seite. Eine Zelle gilt als bedeckt, wenn ihr Mittelpunkt in einem Dreieck liegt. Dreiecke, die schmaler als eine Zelle sind, etwa Wände von oben gesehen, bedecken stattdessen die Zellen entlang ihrer Kanten. Die Ränder der bedeckten Fläche werden zu geschlossenen Konturen verbunden: außen gegen den Uhrzeigersinn, Löcher wie Innenhöfe im Uhrzeigersinn. Nur über eine Ecke verbundene Zellen bleiben getrennte Teile. Danach glättet Douglas-Peucker mit einer Zelle Toleranz die Rasterstufen. Teile unter 0,5 m² entfallen, solange es größere gibt. Die konvexe Hülle bleibt zusätzlich erhalten; die Kartenansicht nutzt sie für Sichtbarkeit und Abstand.

**Senkrechte Flächen:** Pflanzen und Tiere sind oft gekreuzte senkrechte Flächen, die von oben keine Fläche haben. Ein Modell bis 20 m, dessen Dreiecke weniger als 5 % seiner Hülle bedecken, wird deshalb durch seine Hülle ersetzt. Zäune behalten ihre Linie, weil ihre Hülle selbst keine Fläche hat.

| Karte | Objektarten verglichen | Fläche/Hülle im Median | unter 80 % der Hülle | unter 50 % |
|---|---|---|---|---|
| Grundorf | 85 | 0,98 | 4 | 1 |
| Berlin-Spandau | 814 | 0,97 | 154 | 41 |

Am stärksten weichen lange Wohnzeilen in L-, U- und Mäanderform, große Kreuzungen, Brücken und Gelände mit verstreuten Teilen ab; dort deckt die Kontur oft nur ein Fünftel bis die Hälfte der Hülle. Bäume ohne Modell und Rechtecke bleiben unverändert. Bei kleinen Objekten kann die Kontur durch das Raster um bis zu eine Zelle größer sein als die Hülle.

| Karte | Objektarten | mit Umriss | aus Modellen | als Baum | Platzierungen mit Umriss |
|---|---|---|---|---|---|
| Grundorf | 108 | 107 | 89 | 18 | 1.094 von 1.095 |
| Stadtückheim | 5 | 5 | 5 | 0 | 5 von 5 |
| Berlin-Spandau | 872 | 872 | 839 | 33 | 29.450 von 29.450 |

Der Katalog für Berlin-Spandau braucht ohne das Lesen der Karte etwa 2,8 s, wenn die Modelldateien im Dateicache liegen. Beim ersten Lesen von der Festplatte dauerte er 36 s. Die App berechnet ihn im Hintergrund. Über alle Platzierungen von Berlin-Spandau haben die Konturen 281.000 Eckpunkte, die Hüllen 235.000.

## Grenzen

- **Raster:** Die Kontur ist auf etwa eine Zelle genau, also 0,2 m bei Objekten bis 200 m. Überstehende Dächer zählen zur Fläche, weil die Draufsicht sie zeigt.
- **Gemischte Modelle:** Gekreuzte Flächen, die mit flachen Teilen in einem Modell liegen, etwa Bäume in Kleingärten, bleiben dünne Kreuze.
- **Ausgelassene Angaben:** Animationen, `[visible]`-Varianten und die Matrix `0x79` werden nicht berücksichtigt.
- **Bäume:** OMSI wählt die Baumgröße pro Platzierung zufällig im angegebenen Bereich. Mapforge zeigt den Mittelwert.
