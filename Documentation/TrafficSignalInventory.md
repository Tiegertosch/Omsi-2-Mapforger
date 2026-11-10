# Ampelschaltungen in OMSI 2 – Formatbefund

Stand: 10. November 2026, Meilenstein 1 des Entwicklungsauftrags „Visueller Ampelphasen-Editor“ (ADR 085). Quellen: die
Kreuzungsobjekte der OMSI-2-Installation des Nutzers (Kreuz_MC, Kreuz_RUE, Pennermasters, Buildings_RUE), die Kopfskripte
in `Sceneryobjects\Verkehrszeichen_MC\Script\ampel*.osc` und openOMSIs Quelltext (`crates/omsi-scenery/src/sco.rs`,
`crates/omsi-sim/src/traffic/lights.rs`, `crates/omsi-scene/src/lanes.rs`, `crates/omsi-scene/src/place.rs`,
`crates/omsi-world/src/map_index.rs`). openOMSI entscheidet, wo es von OMSI 2 abweicht (ADR 038).

## Wo die Schaltung steht

- **Im Kreuzungsobjekt (`.sco`), nicht in der Karte.** Das Programm gehört zum Objekttyp; jede platzierte Kreuzung dieses
  Typs in jeder Karte läuft mit demselben Programm. Die Kachel kennt nur das platzierte Objekt.
- **Ampelköpfe sind eigene Objekte in der Kachel.** Ein Kopf ist ein Objekt (oder angehängtes Objekt), dessen `.sco`
  `[trafficlight]` enthält (ohne Unterstrich). Sein `[varparent]` nennt die Kreuzung (ADR 082), seine **erste
  Beschriftung** die Nummer des Lichts (ganze Zahl ab 0). Ohne Zahl folgt er in openOMSI Licht 0.
- **Ein Programm läuft nur mit Köpfen:** openOMSI startet die Uhr einer Kreuzung nur, wenn mindestens ein Kopf mit
  `[trafficlight]` sie per `[varparent]` nennt (oder die Kreuzung selbst `[trafficlight]` hat).
- **Fahrzeuge folgen über ihre Pfade:** `[use_traffic_light] n` unmittelbar nach einem `[path]`/`[path_2]` bindet diesen
  Pfad an Licht n. Pfade ohne Bindung kennen keine Ampel.

## Abschnitte im Kreuzungsobjekt

| Abschnitt | Felder | Bedeutung |
|---|---|---|
| `[traffic_lights_group]` | 1 | Zykluslänge in s; 0 = längstes Licht (mindestens 1 s) |
| `[traffic_light]` | 1 | Name eines Lichts (= Signalgruppe); Nummer = Reihenfolge ab 0 |
| `[phase]` | 2 | Zustand (ganze Zahl), Dauer in s; gehört zum Licht davor |
| `[approachdist]` | 1 | Anforderungsabstand vor der Haltlinie in m; gehört zum Licht davor |
| `[traffic_light_stop]` | 3 | Licht, Zeit, Bedingung: die Uhr hält bei Zeit, solange die Bedingung gilt |
| `[traffic_light_jump]` | 4 | wie oben, springt aber nach dem vierten Feld |
| `[use_traffic_light]` | 1 | Licht, dem der Pfad davor folgt |

Phasen beginnen bei Sekunde 0 und folgen nacheinander. Eine letzte Phase mit 0 s hält bis zum Zyklusende; nach der
letzten Phase gilt deren Zustand weiter. Ein Licht ohne Phasen zeigt Grün (6). Bedingung `1` heißt „niemand fordert das
Licht an“, `0` „jemand fordert an“ (openOMSI `LightStop`). Ohne `[approachdist]` fragt ein Fahrzeug 50 m vorher an,
Schienenfahrzeuge bei 0 oder ohne Wert 600 m; wer an der Linie steht, fragt immer.

## Zustände

Der Wert ist das, was die Skripte der Köpfe als `TrafficLightPhase` lesen; die Bedeutung hängt vom Kopf ab:

| Kopfskript | Bedeutung |
|---|---|
| `ampel1.osc` (Autos) | 0–2 Rot, 3–5 Rot-Gelb, 6–8 Grün, 9–11 Gelb, sonst aus |
| `ampel1_ddr.osc` | wie oben, 8 = Grün-Gelb |
| `ampel2.osc` (Fußgänger) | 6–8 Grün, sonst Rot |
| `ampel3.osc` (Bus) | 6–8 Pfeil, sonst aus |
| `ampel4.osc` (Warnblinker) | 12 aus, sonst gelb blinkend |

openOMSI lässt Fahrzeuge bei Grün (6, 7) und „aus“ (12 und alles Unbekannte) fahren. In den 85 Programmen der Installation
kommen die Werte 0 (662 ×), 6 (430), 9 (204), 3 (198), 12 (16), 8 (13), 1 (4) und 2 (2) vor. Deshalb speichert MapWorks den
Rohwert und deutet ihn nur für die Anzeige.

## Bestand (Installation des Nutzers, 10. November 2026)

- 1.300 Objektdateien, davon 22 Köpfe mit `[trafficlight]` und 85 Kreuzungen mit Programm: 423 Lichter, 987 gebundene
  Pfade, 27 Halte- oder Sprungpunkte in 15 Programmen. 7 Programme nutzen auch `[path_2]`; keines lagert Pfade in eine
  `[paths]`-Datei aus.
- Alle 85 Dateien liest das verlustfreie Dateimodell exakt und schreibt sie unverändert byte-gleich zurück; die Pfadzählung
  stimmt in allen mit der des Footprint-Lesers überein (Probe `SignalProbe`, Scratchpad).
- Grundorf: 28 Köpfe, alle mit `[varparent]` und Lichtnummer, an 3 Kreuzungen. Berlin-Spandau: 1.041 Köpfe, davon 1.034
  mit `[varparent]`, alle mit Lichtnummer, an 52 Kreuzungen.
- Beispiel `Kreuz_MC\Einm_CharlCh_Polizei.sco`: Zyklus 82 s, Lichter Main, Side, Main_Ped, Side_Ped, VacArrow_Main;
  `[traffic_light_stop] 1 34.0 1` hält die Hauptrichtung grün, bis die Nebenrichtung anfordert. openOMSI prüft genau diesen
  Fall in `signal_tests.rs`.

## Was das Format nicht kennt

- **Keinen Versatz zwischen Kreuzungen:** openOMSI startet jede Uhr aus der Tageszeit; Kreuzungen mit gleicher Zykluslänge
  laufen dadurch im Takt („grüne Welle“ nur über gleiche Zykluslängen).
- **Keine Räumzeit als eigenes Feld:** Räumen ist eine Rot-Phase (alle Lichter rot).
- **Keine Konfliktangaben:** welche Pfade sich kreuzen, ergibt sich nur aus der Geometrie.
- **Bindung je Pfad, nicht je Fahrstreifen:** Die Bewegungen eines Mischstreifens können technisch verschiedenen Lichtern
  folgen, ein Kopf zeigt aber nur eines.
- **Keine Vorlage pro Karte:** Eine geänderte Schaltung ist ein geänderter Objekttyp (siehe ADR 085, Export).
