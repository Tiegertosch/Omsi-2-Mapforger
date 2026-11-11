# OMSI MapWorks – Feature Parity

Stand: 9. November 2026, abgeglichen mit dem Code nach Release 1.1.1. Vorhanden 85 · Teilweise 10 · Prüfen 0 · Offen 20 von 115 Punkten. Die Fassung vor dem Abgleich liegt daneben („Original vor Abgleich“). Hinweise in Klammern stammen vom Abgleich. „Kartenübersicht / Roadmap generieren“ wurde am 9. November 2026 auf Wunsch des Nutzers gestrichen (war versehentlich in die Liste geraten).

## A. Kartenverwaltung und Grundeinstellungen
- [x] Neue Karte erstellen — Vorhanden
- [x] Bestehende OMSI-Karte öffnen — Vorhanden
- [x] Karte speichern und exportieren — Vorhanden
- [x] Kartenkacheln hinzufügen — Vorhanden
- [x] Kartenkacheln entfernen — Vorhanden
- [x] Kartenbeschreibung und Grundeinstellungen — Vorhanden (Knopf „Map settings...“ im Projektbrowser; seit 9. November 2026)
- [x] Weltkoordinaten verwenden — Vorhanden
- [x] Einstiegspunkte erstellen und verwalten — Vorhanden (Reiter „Entries“ im Project-Fenster, Gruppe „Entry point“ im Inspector; seit 9. November 2026)
- [x] Kachel als DirectX-X-Terrain exportieren — Vorhanden (Knopf „Export terrain as .x...“ im Kachel-Reiter des Inspectors; seit 9. November 2026)
- [x] Links- und Rechtsverkehr konfigurieren — Vorhanden (beim Anlegen und danach unter „Map settings...“; bestehende Straßen ändern sich nicht)
- [ ] KI-Verkehrsdichte und Fahrgastdichte konfigurieren — Teilweise (Faktor auf die Tageskurve unter „Map settings...“, einzelne Stunden der Kurve nicht)

## B. Objekte und Szenerie
- [x] Objekte aus der OMSI-Bibliothek platzieren — Vorhanden
- [x] Objekte verschieben und löschen — Vorhanden
- [x] Objekte um alle drei Achsen drehen — Vorhanden
- [x] Numerische Position und Rotation bearbeiten — Vorhanden
- [x] Objekte kopieren und duplizieren — Vorhanden (Strg+C, Strg+V mit Vorschau, Strg+D; seit 9. November 2026)
- [x] Mehrfachauswahl und gemeinsame Bearbeitung — Vorhanden
- [x] Objektbeschriftungen und Variablen ändern — Vorhanden
- [x] Objekte an Attachpoints anhängen — Vorhanden
- [x] Objekte entlang Splines platzieren — Vorhanden
- [x] Objektreihen mit Abstand und Wiederholung — Vorhanden
- [x] Objekte an Splineverlauf und Steigung ausrichten — Vorhanden (Reihen folgen dem Verlauf der Straße; die Option „Follow slope“ im Rows-Werkzeug neigt sie mit Steigung und Querneigung; seit 9. November 2026)
- [x] Parent-/Varparent-Zuordnungen bearbeiten — Vorhanden (Variable parent im Inspector, ADR 082, 9. November 2026; Anhängen und Abnehmen gab es schon; den Elternwechsel eines angehängten Objekts gibt es nicht, es wird abgenommen und neu angehängt)
- [x] Objektspezifische Optionen bearbeiten — Vorhanden (mehr als Typ, Beschriftungen und Lage hat ein `[object]`-Eintrag nicht; die Detailstufe bleibt bewusst unberührt, ADR 073; geprüft am 9. November 2026)
- [x] Zufällige Rotation von Objektgruppen — Vorhanden (Option „Random heading“ beim Platzieren und Einfügen, Knopf „Randomize headings“ für die Auswahl; seit 9. November 2026)
- [x] Objektgruppen und Favoriten durchsuchen — Vorhanden
- [ ] Detailstufen und Sichtbarkeitsoptionen bearbeiten — Bewusst nicht gebaut (openOMSI verwirft die Detailstufe, ADR 073; der Wert bleibt erhalten)

## C. Splines und Straßen
- [x] Spline-Typen laden und auswählen — Vorhanden
- [x] Gerade Splines erstellen — Vorhanden
- [x] Gebogene Splines erstellen — Vorhanden
- [x] Spline-Länge und Radius einstellen — Vorhanden
- [x] Spline-Anfangs- und Endhöhe bearbeiten — Vorhanden
- [x] Steigung am Anfang und Ende bearbeiten — Vorhanden
- [x] Querneigung / Cant bearbeiten — Vorhanden (Inspector eines Splines, wirkt auf den verbundenen Nachbarn; seit 9. November 2026)
- [x] Spline-Skew bearbeiten — Vorhanden (Inspector eines Splines, ab Kachelversion 14; seit 9. November 2026)
- [x] Spline-Mirroring verwenden — Vorhanden (Schalter „Mirrored“ im Inspector; seit 9. November 2026)
- [x] Spline-Texturversatz bearbeiten — Vorhanden (Feld „Tex. offset“ im Inspector; bleibt bis sich die Kette ändert; seit 9. November 2026)
- [x] Splines automatisch verbinden — Vorhanden
- [x] Spline-Endpunkte nachträglich verschieben — Vorhanden
- [x] Splines teilen und löschen — Vorhanden
- [x] Gelände an Straßen anpassen — Vorhanden
- [x] Kreuzungsobjekte mit Splines verbinden — Vorhanden
- [x] Straßenprofile selbst erstellen — Vorhanden
- [x] Schienen und Straßenbahnsplines verlegen — Vorhanden
- [ ] Spline-Export und OMSI-Kompatibilität prüfen — Teilweise

## D. Gelände und Kacheln
- [x] Gelände anheben und absenken — Vorhanden
- [x] Gelände glätten — Vorhanden
- [x] Gelände auf Höhe abflachen — Vorhanden
- [x] Gelände aufrauen — Vorhanden
- [x] Pinselgröße und Stärke verändern — Vorhanden
- [x] Geländerampen erstellen — Vorhanden
- [x] Terrain-Höhenkarten importieren — Vorhanden
- [x] Terrain-Höhenkarten exportieren — Vorhanden
- [x] DEM-Höhendaten importieren — Vorhanden
- [ ] Geländetexturen auswählen und aufmalen — Teilweise
- [ ] Texturmasken und Übergänge bearbeiten — Offen
- [x] Terrainlöcher durch Objekte und Splines — Vorhanden
- [x] Wasserflächen und Wasserhöhe bearbeiten — Vorhanden
- [x] Gelände unter Objekten anpassen — Vorhanden
- [x] Satellitenbilder auf Kacheln legen — Vorhanden
- [x] Luftbildauflösung und Ausschnitt einstellen — Vorhanden

## E. Verkehr und KI-Regeln
- [x] KI-Fahrpfade anzeigen — Vorhanden
- [x] Geschwindigkeitsbegrenzungen setzen — Vorhanden
- [x] Überholverbote definieren — Vorhanden (Kontrollkästchen „No overtaking“ bei den Verkehrsregeln, `[rule] overtaking_prohib`; seit 9. November 2026)
- [x] Busspuren und Busfreigaben konfigurieren — Vorhanden
- [x] Lkw-Verbote konfigurieren — Vorhanden
- [x] Pkw-Verbote konfigurieren — Vorhanden
- [x] Verkehrsdichte pro Pfad festlegen — Vorhanden
- [x] Vorfahrtsregeln an Kreuzungen — Vorhanden
- [ ] KI-Fahrpfade manuell erstellen und bearbeiten — Teilweise (Zeichnen mit `invis_street` im Pfadmodus samt Einrasten, seit 11. November 2026; offen: Breite und Richtung wählen, Richtung beim Einrasten übernehmen, Punkte vorhandener Pfade ändern)
- [x] Fahrstreifenverbindungen definieren — Vorhanden
- [x] Fußgängerpfade erstellen und verbinden — Vorhanden (Überwege an Kreuzungen mit Zebra, Ampeln und Köpfen, Überweg-Stück mit abgesenktem Bordstein in jede Straße, Pfadmodus mit `invis_sidewalk` und Einrasten auf Pfadlinien; ADR 086, seit 11. November 2026; offen: Ampel am Überweg-Stück)
- [ ] Ampeln und Signalphasen bearbeiten — Offen (Ampeln nur als Objekte platzierbar)
- [ ] Ampelgruppen und Kreuzungszuordnung — Offen
- [ ] Parkplätze / KI-Parkpfade konfigurieren — Teilweise: Es gibt keine KI-Parkpfade; ein Parkplatz ist ein Objekt (`car_park.sco`, ADR 087). Parkreihen entlang von Straßen sind da, offen: Parkliste bearbeiten, Listennummer am Platz, Prüfung
- [x] Verkehrsregeln auf vorhandenen OMSI-Pfaden ändern — Vorhanden (Tempo, Dichte, Pkw, Busse, Lkw)

## F. Tracks, Trips und Fahrpläne
- [x] Vorhandene Linien und Haltestellen anzeigen — Vorhanden
- [x] Vorhandene Trips und Routen anzeigen — Vorhanden
- [ ] Tracks erstellen und bearbeiten — Offen
- [ ] Haltestellenreihenfolge definieren — Offen
- [ ] Trips erstellen und bearbeiten — Offen
- [ ] Fahrzeiten zwischen Haltestellen festlegen — Offen
- [ ] Tours und Umläufe erstellen — Offen
- [ ] Abfahrtszeiten und Fahrplantabellen bearbeiten — Offen
- [ ] Betriebstage und Kalenderzuordnung bearbeiten — Offen
- [ ] Depots und Fahrzeugzuordnung bearbeiten — Offen
- [ ] TTData vollständig schreiben und validieren — Offen

## G. Signalrouten, Prioritäten und Bahnverkehr
- [ ] Signalrouten erstellen — Offen
- [ ] Signalrouten bearbeiten und löschen — Offen
- [ ] Fahrstraßen und Weichen zuordnen — Offen
- [ ] Signalabhängigkeiten konfigurieren — Offen
- [x] Prioritätsregeln für KI-Pfade bearbeiten — Vorhanden (Vorfahrt-Werkzeug (V), auch in Chrono-Varianten)
- [ ] Schienenpfade und Weichenverbindungen prüfen — Teilweise
- [ ] Bahn- und Straßenbahnsignale konfigurieren — Offen

## H. Chrono und zeitabhängige Varianten
- [x] Chrono-Varianten laden und anzeigen — Vorhanden
- [x] Chrono-Varianten anlegen — Vorhanden (Name, erster und letzter Tag)
- [x] Start- und Enddatum bearbeiten — Vorhanden (Felder im Projektbrowser bei einer bearbeiteten Variante; seit 9. November 2026)
- [x] Objekte innerhalb von Chrono bearbeiten — Vorhanden
- [x] Objektbeschriftungen in Chrono ändern — Vorhanden
- [x] Straßen in Chrono ändern oder entfernen — Vorhanden (Typ tauschen und löschen (seit 1.1.1); die Form lässt sich nicht ändern)
- [x] Neue Umleitungsstraßen in Chrono erstellen — Vorhanden (seit 1.1.1, ohne Kreuzungen, Parallelstraßen und Geländeanpassung)
- [x] Verkehrsregeln in Chrono ändern — Vorhanden (seit 1.1.1, Regeln und Vorfahrt)
- [x] Linien zeitabhängig deaktivieren — Vorhanden (seit 1.1.1)
- [ ] Chrono-Fahrpläne bearbeiten — Offen (bewusst nicht: Stationsverbindungen bräuchten eine Wegsuche)
- [ ] Chrono-Varianten vollständig exportieren — Teilweise

## I. Umgebung, Werkzeuge und Diagnose
- [x] 2D- und 3D-Kartenansicht — Vorhanden
- [x] Objekte und Splines selektieren — Vorhanden
- [x] Rückgängig und Wiederholen — Vorhanden
- [x] Kartenfehler und fehlende Dateien prüfen — Vorhanden
- [x] Kachel- und Objektinformationen anzeigen — Vorhanden
- [ ] Umgebungs-/Jahreszeitenoptionen bearbeiten — Teilweise (Jahre, Jahresversatz und Jahreszeiten-Vorlagen unter „Map settings...“, einzelne Jahreszeiten nicht)
- [x] Debug-Informationen und Pfadverbindungen anzeigen — Vorhanden
- [x] Originale OMSI-Datensätze verlustfrei erhalten — Vorhanden (ADR 008; unbekannte Datensätze bleiben als Text erhalten)
- [ ] Karte im originalen OMSI 2 testen — Teilweise (im Oktober geprüft, seither nur in openOMSI)
- [ ] Exportierte Karte mit OMSI 2 vollständig validieren — Teilweise