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

## Darstellung mit Modellen und Texturen

Für die texturierte Draufsicht zählen weitere Angaben der `.sco` und der `.o3d`:

| Quelle | Angabe | Bedeutung |
|---|---|---|
| `.o3d` Abschnitt `0x26` | je Material diffuse RGBA, Glanz RGB, Eigenleuchten RGB, Glanzstärke, dann Texturname mit Längenbyte | Texturname in Windows-1252 |
| `.o3d` Dreiecke | nach den drei Indizes eine 16-Bit-Zahl | Materialindex des Dreiecks |
| `.sco` `[LOD]` | Schwelle | beginnt eine Detailstufe; Stufen zählen in Dateireihenfolge |
| `.sco` `[visible]` | Variable, Wert | das vorherige `[mesh]` ist nur bei diesem Wert sichtbar |
| `.sco` `[matl]` | Texturname, Nummer | wählt ein Material des vorherigen `[mesh]` nach Texturname und Vorkommen |
| `.sco` `[matl_alpha]` | 0, 1 oder 2 | Alpha des vorherigen `[matl]`: aus, Ausschneiden, Überblenden |

Laut openOMSI nimmt OMSI die erste Detailstufe, deren Schwelle die Bildschirmgröße des Objekts erreicht; aus der Nähe ist das die erste. Mapforge zeigt deshalb die Modelle vor dem ersten `[LOD]` und die der ersten Stufe. `[visible]`-Bedingungen werten alle Variablen als 0, nur `sommerhalbjahr` als 1. Das schaltet vor allem Ampellichter (`red`, `green`, `yellow`) aus und zeigt sommerliche Objekte wie Kühe auf der Weide.

**Texturen:** Die Modelle nennen oft `.bmp`, im Ordner `texture` neben der `.sco` liegt aber eine gleichnamige `.dds`. Mapforge sucht wie OMSI zuerst diese `.dds`, dann die genannte Datei, dann beides im globalen Ordner `Texture`. Daneben gibt es `_#low`-Varianten für niedrige Texturqualität, die Mapforge nicht nutzt. In Berlin-Spandau finden sich so 660 Texturen, 431 DDS mit DXT1 oder DXT3, 163 BMP und 66 TGA, zusammen 329 MB. Es fehlen nur vier Schrifttexturen wie `textfeld.bmp`, die OMSI zur Laufzeit erzeugt.

| Karte | Objektarten | Bäume | Dreiecke je Art einmal | Dreiecke aller Platzierungen |
|---|---|---|---|---|
| Grundorf | 108 | 18 | 17.536 | 76.144 |
| Berlin-Spandau | 872 | 33 | 405.645 | 2.401.834 |

Die Modelle aller 872 Objektarten von Berlin-Spandau sind in etwa 1 s gebaut. Text-`.x`-Modelle haben in Mapforge noch keine Texturkoordinaten und erscheinen grau.

## Spline-Profile (`.sli`)

Ein Spline-Datensatz nennt eine `.sli`-Datei. Sie beschreibt den Querschnitt, der entlang des Splines extrudiert wird. Für die Draufsicht zählen diese Abschnitte:

| Abschnitt | Werte | Bedeutung |
|---|---|---|
| `[texture]` | Dateiname | Textur im Unterordner `texture` neben der `.sli`; die Reihenfolge ergibt die Nummer, ab 0 |
| `[matl_alpha]` | Zahl | nach einer `[texture]`: deren Alphakanal gilt, etwa für Zäune als TGA |
| `[profile]` | Texturnummer | beginnt ein Profilstück mit dieser Textur |
| `[profilepnt]` | x, Höhe, u, v | ein Punkt des Stücks; x quer, rechts positiv; u quer zur Fahrtrichtung; v wächst je Meter um diesen Wert |
| `[onlyeditor]` | keine | die ganze Spline-Art ist ein Hilfsspline, den nur der OMSI-Editor zeigt, etwa unsichtbare Wege |

Ein Profilstück hat mindestens zwei Punkte, von links nach rechts. Die Bedeutung von v steht als Kommentar aus dem OMSI-SDK in den Dateien selbst: „0.2 (1/m) bedeutet bspw., dass nach 5 m die V-Koordinate 1 beträgt. V beginnt stets mit 0.“ Kommentare stehen frei zwischen den Abschnitten; nur ganze Zeilen in eckigen Klammern sind Abschlüsse. Ein Spline mit `mirror` am Ende seines Datensatzes dreht den Querschnitt um.

`OmsiSplineSurface` legt jedes Stück zwischen zwei Punkten als Band entlang der Gerade oder des Kreisbogens; Bögen werden höchstens alle 2° unterteilt. Die Höhe ist die Spline-Höhe, linear bis zur Endhöhe, plus die Profilhöhe. Stücke unter 2 cm Breite in der Draufsicht entfallen, etwa Bordsteinkanten.

| Karte | Splines | mit Fläche | nur Editor | Texturen |
|---|---|---|---|---|
| Grundorf | 60 | 53 | 7 | 7 BMP |
| Stadtückheim | 28 | 28 | 0 | 4 BMP |
| Berlin-Spandau | 2.486 | 2.019 | 89 | 41 BMP, 2 TGA |

Die übrigen 378 Splines in Berlin-Spandau sind Freileitungen, Fahrdraht, Quertragwerke und Zäune. Sie bestehen nur aus senkrechten oder sehr schmalen Flächen und sind von oben nicht zu sehen. Gleise wie `rail_concrete_01.sli` zeigen Schotterbett, Schwellen und die Oberseiten der Schienenköpfe, die etwa 5 cm breit sind. Nicht ausgewertet sind `[length]`, `[scaleTexByLength]`, `[patchwork_chain]`, `[heightprofile]` und `[path]`; sie betreffen das Fahrverhalten, die Gelände-Anpassung oder seltene Texturvarianten.

## Grenzen

- **Raster:** Die Kontur ist auf etwa eine Zelle genau, also 0,2 m bei Objekten bis 200 m. Überstehende Dächer zählen zur Fläche, weil die Draufsicht sie zeigt.
- **Gemischte Modelle:** Gekreuzte Flächen, die mit flachen Teilen in einem Modell liegen, etwa Bäume in Kleingärten, bleiben dünne Kreuze.
- **Ausgelassene Angaben:** Animationen, `[visible]`-Varianten und die Matrix `0x79` werden nicht berücksichtigt.
- **Bäume:** OMSI wählt die Baumgröße pro Platzierung zufällig im angegebenen Bereich. Mapforge zeigt den Mittelwert.
