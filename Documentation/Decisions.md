# Entscheidungen

## ADR 001: Unity 6.6 und URP

**Status:** Angenommen, 19. Oktober 2026.

**Entscheidung:** Unity `6000.6.6f1` verwenden und URP `17.6.0` exakt pinnen.

**Grund:** Unity `6000.6.6f1` ist auf der Entwicklungsmaschine installiert. Die Projektversion und die passende URP-Paketlinie werden an diese vorhandene Installation angepasst. Quellen: [Unity Release Archive](https://unity.com/releases/editor/archive), [URP 17.6 Manual](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.6/manual/index.html).

## ADR 002: UI Toolkit für die Editoroberfläche

**Status:** Angenommen.

**Entscheidung:** UXML und USS mit `UIDocument` im Laufzeitprojekt verwenden.

**Grund:** Der Editor benötigt eine flexible Desktopoberfläche mit Werkzeugleisten, Seitenleisten und Statusbereich. UI Toolkit wird als Unity-Paket-/Engine-Funktion genutzt; ein zusätzliches UI-Framework ist nicht nötig.

## ADR 003: Assemblies nach Zuständigkeit

**Status:** Angenommen.

**Entscheidung:** Core, UI, Geometry, World, Objects, OMSI und Validation erhalten eigene Assembly Definitions. Sie starten ohne leere Platzhalterklassen.

**Grund:** Die geplanten Fachgrenzen werden sichtbar, ohne vorzeitig Anwendungslogik zu erfinden.

## ADR 004: Unity generiert versionsgebundene Projektwerte

**Status:** Angenommen.

**Entscheidung:** Unity-serialisierte Einstellungen, URP-Assets und `packages-lock.json` werden vom installierten Editor erzeugt und danach versioniert.

**Grund:** Diese Dateien enthalten Unity-serialisierte Zustände. Ein Editor-Setup erstellt das URP-Asset beim Import und weist es Graphics- und Quality-Einstellungen zu.

## ADR 005: Freigabeliste für Player-Assets vorerst entfernt

**Status:** Angenommen, 19. Oktober 2026. Ersetzt den Allowlist-Teil der bisherigen Build-Prüfung.

**Entscheidung:** Der Windows-Build prüft Player-Assets nicht mehr gegen eine fest im Code hinterlegte Liste mit SHA-256-Prüfsummen. Erhalten bleiben die Prüfung, dass die OMSI-Editorwerkzeuge Editor-only sind, und der bytegenaue Vergleich aller tatsächlichen Player-Abhängigkeiten mit der zuletzt eingelesenen OMSI-Installation.

**Grund:** Jede Änderung an UXML, USS, Szene oder Render-Einstellungen blockierte den Build, bis die Prüfsumme im Code nachgetragen war. Das bremst die frühe UI-Arbeit stark. Der Bytevergleich mit der Installation fängt weiterhin direkt kopierte OMSI-Dateien ab. Nicht erkannt werden konvertierte oder veränderte OMSI-Inhalte; vor einer öffentlichen Auslieferung soll deshalb wieder eine Freigabeliste oder ein vergleichbarer Review-Schritt eingeführt werden (siehe Roadmap).

## ADR 006: Unity Test Framework als direkte Abhängigkeit

**Status:** Angenommen, 19. Oktober 2026.

**Entscheidung:** `com.unity.test-framework` `1.8.0` wird direkt in `Packages/manifest.json` gepinnt. Tests liegen in der Editor-only-Assembly `Editor.Tests`.

**Grund:** Leser und Geometrie sind reine Logik und lassen sich gut automatisch prüfen. Das Paket ist ein eingebautes Unity-Paket und war bereits als indirekte Abhängigkeit aufgelöst; es wird weder zusätzlich heruntergeladen noch in den Player übernommen.

## ADR 007: Gemessene Kachelkante für `[worldcoordinates]`-Karten

**Status:** Angenommen, 20. Oktober 2026.

**Entscheidung:** Karten mit `[worldcoordinates]` werden mit einer festen Kachelkante von 371,9 m auf beiden Achsen dargestellt. Klassische Karten behalten 300 m. Die Spline-Verbindungsprüfung gilt mit einer Toleranz von 0,5 m als bestanden.

**Grund:** In Berlin-Spandau treffen alle 811 Spline-Verbindungen innerhalb einer Kachel exakt aufeinander, mit 300 m aber keine der 238 Verbindungen über Kachelgrenzen. Aus diesen Übergängen ergibt sich eine Kante von im Mittel 371,9 m. Mit diesem Wert liegen alle 1.049 Folgeverbindungen innerhalb von 0,33 m. Die openOMSI-Formatnotizen nennen denselben Wert. Die Restabweichung ändert sich systematisch mit der Kachelzeile; OMSI leitet diese Kacheln vermutlich aus geografischen Koordinaten ab. Für die Draufsicht und die Prüfung reicht der feste Wert. Eine exakte Formel bleibt offen (siehe Roadmap).

## ADR 008: Verlustfreies Dateimodell, vorerst ohne Schreiben auf Datenträger

**Status:** Angenommen, 20. Oktober 2026.

**Entscheidung:** OMSI-Abschnittsdateien werden über ein verlustfreies Modell gelesen. Es erhält Kodierung, Byte Order Mark, das Zeilenende jeder Zeile, Freitext und die Reihenfolge der Datensätze. Datensätze werden positionell nach belegten Layouts gelesen. Abschnitte und Versionen ohne belegtes Layout bleiben unverändert als Freitext erhalten. Das Modell kann Dateien in Bytes zurückverwandeln, schreibt aber selbst nichts auf den Datenträger.

**Grund:** Ein Editor darf beim Speichern nichts verlieren, was er nicht versteht. Die Rundreise über alle 1.409 Textdateien der drei installierten Karten ist byte-identisch. Wohin und wann gespeichert wird, entscheidet erst der Bearbeitungsablauf. Dateien der OMSI-Installation sollen dabei nie unbemerkt überschrieben werden. Undefinierte Windows-1252-Bytes werden dafür auf die gleichnamigen Steuerzeichen abgebildet statt auf das Ersatzzeichen.

## ADR 009: Arbeitskopie im Projekt, Export als eigene OMSI-Karte

**Status:** Angenommen, 20. Oktober 2026. Konkretisiert das Speicherziel aus ADR 008.

**Entscheidung:** Ein Mapforge-Projekt `Name.omproj` hat eine Arbeitskopie im Ordner `Name.workspace` daneben. Sie enthält nur die geänderten Dateien der Karte. Gelesen wird zuerst aus der Arbeitskopie, sonst aus der Originalkarte. Gespeichert wird nur in die Arbeitskopie, über eine temporäre Datei, damit ein Abbruch keine halbe Datei hinterlässt. In die OMSI-Installation schreibt nur der ausdrückliche Export. Er legt einen eigenen Kartenordner neben der Originalkarte an, Vorschlag `Kartenname (Mapforge)`. Dorthin kopiert er die Originalkarte, legt die geänderten Dateien darüber und setzt `[friendlyname]` neu. `[name]` bleibt unverändert. Der Export baut die Karte in einem temporären Nachbarordner auf und tauscht sie erst am Ende ein. Ein vorhandener Ordner wird nur ersetzt, wenn eine Markierungsdatei `mapforge-export.txt` ihn als früheren Mapforge-Export ausweist.

**Grund:** Die Originalkarte bleibt unberührt, auch mitgelieferte Karten wie Grundorf. Eine Überlagerung statt einer vollständigen Kopie spart Platz, denn Berlin-Spandau ist 145 MB groß. Der Export ist in OMSI direkt neben dem Original spielbar. Die Arbeitskopie darf weder in der Originalkarte liegen noch sie enthalten, und der Exportordner muss von beiden getrennt sein. Ein Probeexport von Grundorf mit einer verschobenen Haltestelle ergab 162 unveränderte Dateien; nur die bearbeitete Kachel und `global.cfg` unterscheiden sich.

## ADR 010: AGPL-3.0 und Übernahme der `.o3d`-Entwürfelung aus openOMSI

**Status:** Angenommen, 20. Oktober 2026.

**Entscheidung:** Mapforge steht unter der GNU Affero General Public License v3.0. Das Entwürfelungsverfahren für `.o3d`-Modelle ist aus openOMSI (`crates/omsi-o3d/src/lib.rs`, Commit `23dd0a9e`) nach C# übertragen. `OmsiO3dMesh.cs` nennt die Herkunft; [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) führt sie auf. Der Lizenztext liegt in `LICENSE`.

**Grund:** 953 der 1.050 Modelle, die die installierten Karten verwenden, speichern ihre Eckpunkte verwürfelt, darunter fast alle Gebäude. Ohne Entwürfelung blieben gerade die Gebäude ohne Umriss. openOMSI steht unter AGPL-3.0; eine Übernahme macht Mapforge zu einem abgeleiteten Werk. Der Projektinhaber hatte ohnehin geplant, Mapforge mit der ersten Veröffentlichung unter AGPL-3.0 zu stellen.

## ADR 011: Rückgängig für strukturelle Änderungen über Dateistände

**Status:** Angenommen, 21. Oktober 2026.

**Entscheidung:** Platzieren und Löschen von Objekten merken sich die Bytes jeder geänderten Datei vor und nach der Änderung, meist die Kachel und `global.cfg`. Rückgängig und Wiederholen schreiben diese Stände über die Arbeitskopie zurück und passen die Kartenübersicht im Speicher an. Sie verweigern das Schreiben, wenn eine Datei inzwischen anders aussieht. Verschieben und Drehen bleiben wertbasiert. Neue Objekte werden am Kachelende angefügt; ihre ID kommt aus `OmsiIdAllocator`, und `[NextIDCode]` wird mitgeschrieben. Eine zurückgenommene ID wird in derselben Sitzung nicht neu vergeben. „Revert tile edits“ setzt außerdem die Objektindizes der Einstiegspunkte dieser Kachel auf das Original zurück.

**Grund:** Beim Löschen verschwinden auch die folgenden `[rule]`- und `[varparent]`-Datensätze, und Einstiegspunkte verschieben sich. Ein exakter Dateistand stellt das ohne eigene Umkehrlogik wieder her. Die Prüfung vor dem Schreiben verhindert, dass ein Rückgängig spätere Änderungen überschreibt. Entspricht eine Datei danach wieder dem Original, entfernt die Arbeitskopie sie wie bisher.

## ADR 012: Kachelwechsel nur ohne kachelgebundene Verweise

**Status:** Angenommen, 21. Oktober 2026.

**Entscheidung:** Ein Objekt, das über eine Kachelgrenze verschoben wird, zieht in die Kachel unter seiner neuen Position um. Sein `[object]`-Datensatz und die folgenden `[rule]`-Datensätze werden am Ende der Zielkachel angefügt, mit derselben ID. Die Objektindizes der Einstiegspunkte in der Quellkachel werden nachgeführt. Der Umzug unterbleibt, wenn ein Verweis das Objekt an seine Kachel bindet: ein Einstiegspunkt, eine Chrono-Auswahl, ein angehängtes Objekt, ein `[varparent]`, der das Objekt nennt, oder ein eigener `[varparent]`. Das Objekt bleibt dann in seiner Kachel mit Koordinaten außerhalb davon; die Statuszeile nennt den Grund. Fahrplan- und Haltestellenverweise nennen nur die ID und hindern den Umzug nicht.

**Grund:** OMSI speichert Objekte mit Koordinaten außerhalb ihrer Kachel und zeigt sie richtig an; Berlin-Spandau enthält 18 solche. Ein unterlassener Umzug ist also sicher. Einstiegspunkte nennen Kachel und Index, Chrono-Patches liegen je Kachel, angehängte Objekte müssen laut Formatbefund in derselben Kachel nach ihrem Elternobjekt stehen. Ob OMSI `[varparent]` über Kachelgrenzen auflöst, ist nicht belegt. Diese Verweise mitzuziehen wäre möglich, aber riskanter als der Verzicht. In Grundorf ließen sich 142 von 149 Objekten der ersten zehn Kacheln umziehen, in Berlin-Spandau 131 von 150.

## ADR 013: Texturierte Draufsicht über eine Kamera unter der Vektorebene

**Status:** Angenommen, 21. Oktober 2026.

**Entscheidung:** Texturen der Draufsicht zeichnet Unity mit echten Meshes und einer orthografischen Kamera, nicht die UI-Toolkit-Ebene. Den Anfang macht der Boden: je Kachel und Bodentextur ein Quadrat auf der XZ-Ebene in Metern relativ zum Kartenursprung, mit einem eigenen URP-Shader für Textur, Detailtextur und Maske. Darüber liegen die Splines, je Kachel und Textur ein Mesh in ihrer echten Höhe; sie schreiben Tiefe, sodass Gehwege über Fahrbahnen und Brücken über Straßen liegen. Objekte zeichnet `MapObjectRenderer` mit GPU-Instancing: je Objektart ein Mesh mit einem Material je Textur und alle Platzierungen in wenigen Aufrufen. Sie stehen auf dem Gelände, sofern ihre Höhe nicht absolut ist. Weil Unity im Build Shader-Varianten streicht, die kein mitgebautes Material nutzt, liegt ein Material mit eingeschaltetem Instancing in `Resources`; der Renderer kopiert es. Der Boden besteht je Kachel aus einem Netz der 61 × 61 Geländehöhen. Dieselbe Szene zeigt die 3D-Ansicht mit einer perspektivischen Kamera; dort schreibt der Boden Tiefe, damit Hügel verdecken, was dahinter liegt. Von oben schreibt er keine, damit Straßen in Einschnitten sichtbar bleiben. Bearbeitet wird nur in der Draufsicht. Die Kamera schaut senkrecht nach unten und rendert genau in das Bildschirmrechteck der Kartenansicht; die Shell gleicht Mittelpunkt und Maßstab in jedem Bild mit `MapCanvasElement` ab. Die Kartenansicht und ihre Eltern sind dafür durchsichtig. Linien, Umrisse, Auswahl und Griffe zeichnet weiter `MapCanvasElement` darüber. Bilder dekodiert `OmsiImageReader` in der OMSI-Baugruppe ohne Unity; die World-Baugruppe lädt sie als Texturen hoch.

**Grund:** Die Grafikkarte wiederholt und mischt die Texturen selbst. Im Speicher liegen nur die wenigen Bodentexturen und die kleinen Masken statt fertiger Bilder je Kachel; bei 766 Kacheln in Berlin-Spandau wären das bei 1.024 Pixeln Kantenlänge über 3 GB. Dieselbe Szene lässt sich später schräg ansehen, und die Geländehöhen liegen bereits vor; der Weg führt also zur 3D-Ansicht der Roadmap. Die Vektorebene bleibt unverändert, Bearbeiten und Auswahl funktionieren wie zuvor.

## ADR 014: Bearbeiten in der 3D-Ansicht mit Drehringen als Kardangestell

**Status:** Angenommen, 22. Oktober 2026. Hebt den Satz „Bearbeitet wird nur in der Draufsicht“ aus ADR 013 auf.

**Entscheidung:** In der 3D-Ansicht bearbeitet die linke Maustaste: Klick wählt ein Objekt über sein Modell, Ziehen verschiebt die Auswahl über die waagrechte Ebene durch den gegriffenen Punkt, Ziehen an einem Ring dreht das einzeln gewählte Objekt. Die Kamera dreht die rechte und verschiebt die mittlere Taste. Die Ringe zeichnet `MapCanvasElement` als projizierte Linien über die 3D-Szene, nicht als Unity-Objekte. Sie folgen OMSIs Drehreihenfolge – Bank, dann Pitch, dann Heading, wie Direct3D-Yaw-Pitch-Roll – als Kardangestell: grün um die Senkrechte für Heading, rot um die Querachse nach dem Heading für Pitch, blau um die Längsachse nach Heading und Pitch für Bank. Jeder Ring ändert so genau einen gespeicherten Winkel. Objekte mit `[splinehelper]`, etwa Kreuzungen, behalten Pitch und Bank; Inspector und Ringe sperren sie.

**Grund:** Ein Kardangestell braucht keine Umrechnung zwischen freier Drehung und den drei gespeicherten Winkeln und hat keine Mehrdeutigkeit außer bei ±90° Pitch, die bei Straßenmöbeln nicht vorkommt. Die Konvention ist an Berlin-Spandau belegt: Bei allen 16 Objekten mit Pitch neigt positiver Pitch die Vorderseite nach unten, im Betrag passend zum Gelände (1,5° gegen 1,4–1,8°). Unity verwendet dasselbe linkshändige System mit derselben Reihenfolge, `Quaternion.Euler(pitch, heading, bank)` bildet OMSI also direkt ab. Für Bank gibt es in den installierten Karten keinen Beleg am Gelände; das Vorzeichen folgt der Direct3D-Konvention (positiv hebt die rechte Seite). Ein Test im Spiel hat beides bestätigt: In einer exportierten Grundorf-Karte standen ein Wartehäuschen mit Pitch 15 und eine Laterne mit Bank 15 in OMSI genauso geneigt wie in der 3D-Ansicht des Editors. Ein Kippen von Kreuzungen risse die angeschlossenen Straßen schon bei 1° sichtbar auf. Die Ringe auf der Vektorebene passen zum übrigen Zeichnen von Auswahl und Griffen und brauchen keine Kollider.

## ADR 015: Oberfläche nach dem Corporate Design, Karte zuerst

**Status:** Angenommen, 22. Oktober 2026. Grundlage ist `Documentation/CorporateDesign.md` mit dem Layout-Entwurf des Projektinhabers.

**Entscheidung:** Die Karte füllt die ganze Fläche zwischen einer schmalen Menüleiste (36 px) und der Statusleiste (24 px). Projektbrowser und Inspector schweben als abgerundete Panels darüber und lassen sich einzeln auf ihre Titelzeile einklappen, auch über das View-Menü. Die Ansichtsknöpfe (Einpassen, Zoom, Zentrieren, Texturen, 3D) liegen als schwebende Leiste oben rechts. Kartentitel steht in der Menüleiste, Zähler, Importwarnungen und die einklappbare Kartenbeschreibung im Projektbrowser, die Legende im View-Menü, die Bedienhinweise in der Hilfe. Die Statusleiste zeigt Position unter dem Mauszeiger bzw. Kameradaten, Anzahl der Auswahl, Meldungen und Speicherstand. Einpassen lässt die Fläche unter den Panels frei. Farben, Abstände, Radien und Schriftgrößen sind USS-Variablen `--mf-*` nach Abschnitt 11 des Corporate Designs; Orange steht nur für Aktives und Hauptaktionen, Rot nur für Löschen und Fehler, „bearbeitet“ ist Gelb. Die untere Werkzeugleiste zeigt nur fertige Werkzeuge mit Tastenkappen. Drag (Q), Move (W) und Rotate (E) sind Modi: Mit Drag wählt ein Klick und Ziehen verschiebt frei. Move zeigt zusätzlich Achsenpfeile – rot Ost, blau Nord, in 3D grün nach oben –, die die Auswahl genau entlang einer Achse verschieben, mit Shift in ganzen Metern; so lässt sich auch die Höhe mit der Maus ändern. Drehgriff und Ringe erscheinen nur mit Rotate, und dort verschiebt Ziehen nichts. Ohne Projekt bleibt nur Drag zum Auswählen. Die Farben der Pfeile folgen den Ringen: Grün ist immer die Senkrechte. Lucide-Icons werden als SVG-Pfaddaten mit UI Toolkits Vektorzeichnung gezeichnet, ohne zusätzliches Paket und scharf bei jeder Skalierung. Das Objekte-Werkzeug öffnet statt des früheren Dialogs ein Panel über der Werkzeugleiste mit den Reitern Library, In map und Favorites und einer Suche; Kategorien sind die Ordner unter `Sceneryobjects`, weil sie verlässlich vorliegen, während `[groups]` je Autor verschieden gepflegt sind. Ein Klick startet das Platzieren, das Panel bleibt für das nächste Objekt offen. Als Nächstes folgen die Schrift Inter (SIL OFL) und Icons für Ansichtsknöpfe und Panels. Der sichtbare Name bleibt „OMSI Map Editor“, bis der Projektname final ist.

**Grund:** Vorher bekam die Karte bei 1920 × 1009 nur rund 48 % des Fensters; Kopfbereich, feste Seitenleisten und Ränder nahmen den Rest. Jetzt sind es rund 94 %, mit eingeklappten Panels fast alles. Schwebende Panels entsprechen dem Entwurf des Projektinhabers und lassen die Karte beim Einklappen frei, ohne dass sich ihre Größe ändert. Tokens halten die Farben an einer Stelle, sodass spätere Anpassungen des Corporate Designs nicht jede Regel berühren.

## ADR 016: Herkunft von Objekten aus Steams Dateilisten

**Status:** Angenommen, 22. Oktober 2026.

**Entscheidung:** Für jede Datei der OMSI-Installation bestimmt `OmsiContentOrigins`, ob sie zum Basisspiel, zu einem DLC oder zu keinem Steam-Paket gehört. Steam legt für jedes installierte Depot ein Manifest mit allen Dateinamen in `Steam\depotcache\<Depot>_<Manifest>.manifest` ab; `appmanifest_252530.acf` in der Bibliothek nennt die installierten Depots und kennzeichnet DLC-Depots mit `dlcappid`. Dateien in einem Depot ohne diese Kennung sind Basisspiel, in einem DLC-Depot DLC, in keinem Depot User-Content (Freeware, Eigenbau, außerhalb von Steam gekaufte Add-ons). Steht eine Datei im Basis- und in einem DLC-Depot, zählt sie als Basisspiel. Ohne lesbare Liste des Basisspiels gelten die 18 Ordner des Basisspiels unter `Sceneryobjects` als Basisspiel und alles andere als unbekannt. Die Objekte-Bibliothek kann DLC-Objekte ausblenden („Hide DLC objects“) und kennzeichnet sie sonst; „In map“ und die Kartenprüfung unter **Tools** nennen DLC-Objekte der Karte, auch angehängte und Reihen entlang von Splines.

**Grund:** Freeware-Mapper dürfen keine DLC-Objekte verwenden, ihre eigenen und fremde Freeware-Objekte aber schon. Eine Ordnerliste allein würde User-Content mit ausblenden und DLC-Dateien in Basisordnern übersehen. Steams Listen sind je Datei genau: Auf der Entwicklungsmaschine stehen im Basisdepot 252531 genau die `.sco`-Dateien der 18 Basisordner, die 27 Objekte von „Pennermasters Objekte“ in keinem Depot. Die Dateinamen stehen unverschlüsselt im Protokollpuffer des Manifests. Außerhalb von Steam gekaufte DLCs erscheinen als User-Content; dafür ist ein Markieren von Ordnern von Hand vorgesehen.
