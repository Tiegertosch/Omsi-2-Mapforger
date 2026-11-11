# OMSI MapWorks – Feature Parity

Stand: 11. November 2026, abgeglichen mit dem Code nach dem Abschluss von F (Fahrpläne, Fahrzeuggruppen, Fahrwege). Vorhanden 102 · Teilweise 6 · Prüfen 0 · Offen 7 von 115 Punkten. Die Fassung vor dem Abgleich liegt daneben („Original vor Abgleich“). Hinweise in Klammern stammen vom Abgleich. „Kartenübersicht / Roadmap generieren“ wurde am 9. November 2026 auf Wunsch des Nutzers gestrichen (war versehentlich in die Liste geraten).

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
- [x] KI-Verkehrsdichte und Fahrgastdichte konfigurieren — Vorhanden (Faktor auf die Tageskurve unter „Map settings...“, einzelne Stunden im Dialog „Traffic curves...“, ADR 088)

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
- [x] Spline-Export und OMSI-Kompatibilität prüfen — Vorhanden (Datensätze mit denen des Original-Editors verglichen, Profile vollständig gelesen und gezeichnet, ADR 090); ein Test einer exportierten Karte in OMSI 2 selbst steht aus

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
- [x] Geländetexturen auswählen und aufmalen — Vorhanden (Modus „Malen“, neue Texturen aus dem Texture-Ordner, ADR 091); Test in openOMSI steht aus
- [x] Texturmasken und Übergänge bearbeiten — Vorhanden (Pinselhärte = weicher Übergang, Radieren, Weichzeichnen, Kachel füllen/leeren; kein Import eigener Masken)
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
- [x] KI-Fahrpfade manuell erstellen und bearbeiten — Vorhanden (Zeichnen mit `invis_street` im Pfadmodus samt Einrasten, Breite und Richtung als eigener Pfadtyp, Start und Ende mit der Fahrtrichtung der Spur, ADR 086 und 089; Punkte eines vorhandenen Pfades nur wie bei einem Spline)
- [x] Fahrstreifenverbindungen definieren — Vorhanden
- [x] Fußgängerpfade erstellen und verbinden — Vorhanden (Überwege an Kreuzungen mit Zebra, Ampeln und Köpfen, Überweg-Stück mit abgesenktem Bordstein in jede Straße, Pfadmodus mit `invis_sidewalk` und Einrasten auf Pfadlinien; ADR 086, seit 11. November 2026; offen: Ampel am Überweg-Stück)
- [x] Ampeln und Signalphasen bearbeiten — Vorhanden für im Editor erzeugte Kreuzungen (Werkzeug „Signals“: Zeitleiste, Phasen, Assistent, Vorlagen, Prüfung, ADR 085; Fußgängerampeln ADR 086); an OMSIs eigenen Kreuzungen nur Gruppen der Köpfe
- [x] Ampelgruppen und Kreuzungszuordnung — Vorhanden (Bewegungen und Köpfe den Gruppen zuordnen, ADR 085)
- [ ] Parkplätze / KI-Parkpfade konfigurieren — Teilweise: Es gibt keine KI-Parkpfade; ein Parkplatz ist ein Objekt (`car_park.sco`, ADR 087). Parkreihen entlang von Straßen und die Parkliste der Karte (Dialog) sind da, dazu Listennummern und die Prüfung unbrauchbarer Plätze in der Kartenprüfung
- [x] Verkehrsregeln auf vorhandenen OMSI-Pfaden ändern — Vorhanden (Tempo, Dichte, Pkw, Busse, Lkw)

## F. Tracks, Trips und Fahrpläne
- [x] Vorhandene Linien und Haltestellen anzeigen — Vorhanden
- [x] Vorhandene Trips und Routen anzeigen — Vorhanden
- [x] Tracks erstellen und bearbeiten — Vorhanden (Schalter „Rail“ im Linien-Werkzeug schreibt Fahrweg und Fahrt; Bearbeiten über neu anlegen und löschen, ADR 097; vom Nutzer in openOMSI getestet)
- [x] Haltestellenreihenfolge definieren — Vorhanden (Dialog „New trip…“, ADR 093)
- [x] Trips erstellen und bearbeiten — Vorhanden (Linien-Werkzeug für neue Fahrten, auch auf Schienen; Halte vorhandener Fahrten ändern, Löschen und Umbenennen, ADR 093/096/097; vom Nutzer in openOMSI getestet) (früher: Teilweise)
- [x] Fahrzeiten zwischen Haltestellen festlegen — Vorhanden (Fahrzeit je Profil, dazu Ankunft, Abfahrt und Haltemodus je Halt, ADR 092/096) (früher: Teilweise)
- [x] Tours und Umläufe erstellen — Vorhanden (anlegen, umbenennen, verschieben, Fahrten im Takt, ADR 092; vom Nutzer in openOMSI getestet)
- [x] Abfahrtszeiten und Fahrplantabellen bearbeiten — Vorhanden (Dialog im Reiter Lines, ADR 092)
- [x] Betriebstage und Kalenderzuordnung bearbeiten — Vorhanden (Tagesmaske je Umlauf: Wochentage, Feiertage, Schulzeit/Schulferien, ADR 092; vom Nutzer in openOMSI getestet)
- [x] Depots und Fahrzeugzuordnung bearbeiten — Vorhanden (Dialog „Vehicles…“: Depots mit Typen und Bussen, einfache Gruppen, Umbenennen mit den Umläufen, ADR 095; vom Nutzer in openOMSI getestet)
- [x] TTData vollständig schreiben und validieren — Vorhanden (Busstops.cfg, StnLinks.cfg, .ttp, .ttl werden geschrieben; die Kartenprüfung prüft sie, und nach dem Speichern einer Linie läuft dieselbe Prüfung und meldet Befunde sofort)

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