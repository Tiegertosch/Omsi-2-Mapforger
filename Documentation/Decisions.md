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

**Status:** Angenommen, 19. Oktober 2026. Ersetzt den Allowlist-Teil der bisherigen Build-Prüfung. Am 23. Oktober 2026 durch eine Freigabeliste als Datei ergänzt, siehe unten.

**Entscheidung:** Der Windows-Build prüft Player-Assets nicht mehr gegen eine fest im Code hinterlegte Liste mit SHA-256-Prüfsummen. Erhalten bleiben die Prüfung, dass die OMSI-Editorwerkzeuge Editor-only sind, und der bytegenaue Vergleich aller tatsächlichen Player-Abhängigkeiten mit der zuletzt eingelesenen OMSI-Installation.

**Grund:** Jede Änderung an UXML, USS, Szene oder Render-Einstellungen blockierte den Build, bis die Prüfsumme im Code nachgetragen war. Das bremst die frühe UI-Arbeit stark. Der Bytevergleich mit der Installation fängt weiterhin direkt kopierte OMSI-Dateien ab. Nicht erkannt werden konvertierte oder veränderte OMSI-Inhalte; vor einer öffentlichen Auslieferung soll deshalb wieder eine Freigabeliste oder ein vergleichbarer Review-Schritt eingeführt werden (siehe Roadmap).

**Ergänzung vom 23. Oktober 2026:** Die Freigabe steht jetzt in einer Datei statt im Code: `Assets/_Project/Settings/PlayerAssetReview.txt` nennt jede Projektdatei, die in den Player gelangt, mit ihrer SHA-256 (Format von `sha256sum`; Code, Assemblydefinitionen und Paketdateien ausgenommen). Textdateien werden mit LF-Zeilenenden gerechnet, weil Git sie unter Windows je nach `core.autocrlf` mit CRLF auscheckt und die Liste sonst in jedem Klon anders ausfiele. Ein normaler Build meldet neue und geänderte Dateien nur als Warnung, damit die UI-Arbeit nicht stockt; `WindowsPlayerBuild.BuildRelease` (Menü **Build > Windows Player (Release)**) bricht dann ab. Nach einer Prüfung, dass keine der Dateien OMSI-Inhalt ist, auch nicht konvertiert, schreibt **Build > Record Reviewed Player Assets...** die Liste neu; der Befehl zeigt vorher, was neu oder geändert ist. Die Liste liegt außerhalb von `Resources` und gelangt nicht selbst in den Player. Die erste Liste mit 16 Dateien (Szene, Render- und App-Einstellungen, eigene Shader, UXML, USS, Theme, Inter mit Lizenz) wurde am 23. Oktober 2026 Datei für Datei geprüft.

**Ergänzung vom 25. Oktober 2026:** Zu den Player-Dateien zählen auch Logos und Hintergründe des Startbilds sowie die Programm-Icons aus den Player-Einstellungen. Vorher kam das neue Startbild-Logo in den Player, ohne dass die Prüfung es sah.

## ADR 006: Unity Test Framework als direkte Abhängigkeit

**Status:** Angenommen, 19. Oktober 2026.

**Entscheidung:** `com.unity.test-framework` `1.8.0` wird direkt in `Packages/manifest.json` gepinnt. Tests liegen in der Editor-only-Assembly `Editor.Tests`.

**Grund:** Leser und Geometrie sind reine Logik und lassen sich gut automatisch prüfen. Das Paket ist ein eingebautes Unity-Paket und war bereits als indirekte Abhängigkeit aufgelöst; es wird weder zusätzlich heruntergeladen noch in den Player übernommen.

## ADR 007: Gemessene Kachelkante für `[worldcoordinates]`-Karten

**Status:** Angenommen, 20. Oktober 2026. Die gemessene Kante ist seit ADR 019 aus den Kachelnummern abgeleitet.

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

**Ergänzung vom 25. Oktober 2026 (Wunsch des Nutzers):** Die Verweise ziehen jetzt mit, statt den Umzug zu verhindern. Angehängte Objekte (`[attachObj]`), auch in Ketten, ziehen mit ihrem Elternobjekt in die Zielkachel und stehen dort in ihrer Reihenfolge dahinter. Einstiegspunkte, die eines davon nennen, bekommen Zielkachel und neuen Index. Nach dem ersten Spieltest des Nutzers (Einstiegspunkt auf der Wiese, weil das Objekt danach in seiner neuen Kachel weitergezogen und gedreht wurde) folgen Einstiegspunkte ihrem Objekt bei jedem Verschieben und Drehen, auch innerhalb der Kachel: Position samt Versatz, mit dem Objekt gedreht, Höhe und Winkel des Quaternions; ein Einstiegspunkt mehr als 2 m oder 10° neben seinem Objekt gilt als veraltet und wird auf das Objekt gesetzt. Vom Nutzer im Spiel bestätigt; angehängte Objekte nach einem Kachelwechsel am 25. Oktober 2026 ebenfalls (eine Haltestellentafel mit drei Linienschildern, in openOMSI); Chrono-Auswahlen nach einem Kachelwechsel sind im Spiel noch ungeprüft. Chrono-Auswahlen (`[selobject]` mit den Befehlen danach bis zur nächsten Auswahl oder zum nächsten neuen Element) wandern in jeder Variante in die Chrono-Datei der Zielkachel; fehlt sie, wird sie in Kodierung und Version der Quelldatei angelegt. `[varparent]` nennt eine ID, und OMSI löst ihn auch in Nachbarkacheln auf (Berlin-Spandau: 96 von 1.047 Zielen in einer Nachbarkachel); ein Umzug ist deshalb erlaubt, solange jedes solche Paar danach höchstens eine Kachel auseinander liegt, auch über Eck. Weiter verhindern den Umzug: ein `[varparent]`, der danach weiter reichen müsste oder dessen Ziel fehlt, ein von einer Chrono-Variante angehängtes Objekt am umziehenden Objekt, eine Chrono-Auswahl in der Datei einer anderen Kachel und Modifikatoren ohne belegtes Format. Alles ist ein Rückgängig-Schritt über Dateistände; eine neu angelegte Chrono-Datei verschwindet beim Rückgängig wieder.
- Grund: Echte Karten nennen angehängte Objekte nur in derselben Kachel hinter dem Elternobjekt, Einstiegspunkte über Kachel und Index und Chrono-Auswahlen in der Datei der Kachel, die sie ändern; diese Verweise lassen sich also eindeutig nachführen. Die Annahme zu Chrono-Auswahlen in der Datei der Zielkachel folgt aus den Befunden (Auswahlen je Kachel) und ist im Spiel zu prüfen.

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

**Entscheidung:** Die Karte füllt die ganze Fläche zwischen einer schmalen Menüleiste (36 px) und der Statusleiste (24 px). Projektbrowser und Inspector schweben als abgerundete Panels darüber und lassen sich einzeln auf ihre Titelzeile einklappen, auch über das View-Menü. Die Ansichtsknöpfe (Einpassen, Zoom, Zentrieren, Texturen, 3D) liegen als schwebende Leiste oben rechts. Kartentitel steht in der Menüleiste, Zähler, Importwarnungen und die einklappbare Kartenbeschreibung im Projektbrowser, die Legende im View-Menü, die Bedienhinweise in der Hilfe. Die Statusleiste zeigt Position unter dem Mauszeiger bzw. Kameradaten, Anzahl der Auswahl, Meldungen und Speicherstand. Einpassen lässt die Fläche unter den Panels frei. Farben, Abstände, Radien und Schriftgrößen sind USS-Variablen `--mf-*` nach Abschnitt 11 des Corporate Designs; Orange steht nur für Aktives und Hauptaktionen, Rot nur für Löschen und Fehler, „bearbeitet“ ist Gelb. Die untere Werkzeugleiste zeigt nur fertige Werkzeuge mit Tastenkappen. Drag (Q), Move (W) und Rotate (E) sind Modi: Mit Drag wählt ein Klick und Ziehen verschiebt frei. Move zeigt zusätzlich Achsenpfeile – rot Ost, blau Nord, in 3D grün nach oben –, die die Auswahl genau entlang einer Achse verschieben, mit Shift in ganzen Metern; so lässt sich auch die Höhe mit der Maus ändern. Drehgriff und Ringe erscheinen nur mit Rotate, und dort verschiebt Ziehen nichts. Ohne Projekt bleibt nur Drag zum Auswählen. Die Farben der Pfeile folgen den Ringen: Grün ist immer die Senkrechte. Lucide-Icons werden als SVG-Pfaddaten mit UI Toolkits Vektorzeichnung gezeichnet, ohne zusätzliches Paket und scharf bei jeder Skalierung. Das Objekte-Werkzeug öffnet statt des früheren Dialogs ein Panel über der Werkzeugleiste mit den Reitern Library, In map und Favorites und einer Suche; Kategorien sind die Ordner unter `Sceneryobjects`, weil sie verlässlich vorliegen, während `[groups]` je Autor verschieden gepflegt sind. Seit dem 23. Oktober 2026 gibt es die Gruppen zusätzlich als eigenen Reiter: In der Testinstallation nennen 250 Gruppen die Objekte, darunter viele mit nur einem Objekt, deshalb stehen große Gruppen wie „Buildings“ oben. Ein Klick startet das Platzieren, das Panel bleibt für das nächste Objekt offen. Die Oberfläche nutzt die Schrift Inter 4.1 (SIL OFL) in Regular und für Überschriften SemiBold, statt Fettschrift künstlich zu erzeugen; Ansichtsknöpfe, Einklapp-Knöpfe und Panelköpfe tragen Lucide-Icons, Texturen und 3D sind Umschalter, die aktiv orange leuchten. Der sichtbare Name bleibt „OMSI Map Editor“, bis der Projektname final ist.

**Grund:** Vorher bekam die Karte bei 1920 × 1009 nur rund 48 % des Fensters; Kopfbereich, feste Seitenleisten und Ränder nahmen den Rest. Jetzt sind es rund 94 %, mit eingeklappten Panels fast alles. Schwebende Panels entsprechen dem Entwurf des Projektinhabers und lassen die Karte beim Einklappen frei, ohne dass sich ihre Größe ändert. Tokens halten die Farben an einer Stelle, sodass spätere Anpassungen des Corporate Designs nicht jede Regel berühren.

## ADR 016: Herkunft von Objekten aus Steams Dateilisten

**Status:** Angenommen, 22. Oktober 2026.

**Entscheidung:** Für jede Datei der OMSI-Installation bestimmt `OmsiContentOrigins`, ob sie zum Basisspiel, zu einem DLC oder zu keinem Steam-Paket gehört. Steam legt für jedes installierte Depot ein Manifest mit allen Dateinamen in `Steam\depotcache\<Depot>_<Manifest>.manifest` ab; `appmanifest_252530.acf` in der Bibliothek nennt die installierten Depots und kennzeichnet DLC-Depots mit `dlcappid`. Dateien in einem Depot ohne diese Kennung sind Basisspiel, in einem DLC-Depot DLC, in keinem Depot User-Content (Freeware, Eigenbau, außerhalb von Steam gekaufte Add-ons). Steht eine Datei im Basis- und in einem DLC-Depot, zählt sie als Basisspiel. Ohne lesbare Liste des Basisspiels gelten die 19 Ordner des Basisspiels unter `Sceneryobjects` als Basisspiel und alles andere als unbekannt. Die Objekte-Bibliothek kann DLC-Objekte ausblenden („Hide DLC objects“) und kennzeichnet sie sonst; „In map“ und die Kartenprüfung unter **Tools** nennen DLC-Objekte der Karte, auch angehängte und Reihen entlang von Splines.

**Grund:** Freeware-Mapper dürfen keine DLC-Objekte verwenden, ihre eigenen und fremde Freeware-Objekte aber schon. Eine Ordnerliste allein würde User-Content mit ausblenden und DLC-Dateien in Basisordnern übersehen. Steams Listen sind je Datei genau: Auf der Entwicklungsmaschine stehen in den Basisdepots 252531 und 252533 genau die `.sco`-Dateien der 19 Basisordner (einschließlich `Streetobjects_RW1HH` mit der Werbeuhr), die 1.742 Objekte des DLC „HafenCity Hamburg“ (630010) im DLC-Depot und die 27 Objekte von „Pennermasters Objekte“ in keinem Depot. Die Dateinamen stehen unverschlüsselt im Protokollpuffer des Manifests. Außerhalb von Steam gekaufte DLCs erscheinen als User-Content; seit dem 23. Oktober 2026 lassen sich ihre Ordner in der Bibliothek von Hand als DLC markieren. Die Markierung gilt je Benutzer für alle Karten, weil sie die Installation beschreibt und nicht eine Karte; eine Datei des Basisspiels bleibt auch in einem markierten Ordner Basisspiel.

## ADR 017: Neue Karten aus einer Vorlagenkarte

**Status:** Angenommen, 23. Oktober 2026.

**Entscheidung:** Eine neue Karte entsteht im `maps`-Ordner neben einer Karte der Installation, die als Vorlage dient. Übernommen werden ihre Hilfsdateien im Kartenordner (KI-Listen, Fahrer, Fahrgäste, geparkte Autos, Kennzeichen, Ferien und Feiertage, Zeitzone) und die Wassertexturen aus `texture`, nicht aber Kacheln, Kachelbilder in `texture\map`, `TTData`, `Chrono`, Signalrouten, die letzte Situation, das Kartenbild und die `.dsc`-Beschreibungen. Die `global.cfg` der Vorlage wird über das verlustfreie Modell übernommen: ohne `[map]`, `[entrypoints]`, `[backgroundimage]` und `[worldcoordinates]`, mit neuem `[name]`, `[friendlyname]` und `[description]`, `[NextIDCode]` 1, der Kartenkamera über Kachel 0, 0 und genau einer Kachel `tile_0_0.map` mit flachem Gelände in Höhe 0. Alle übrigen Abschnitte wie Geldsystem, Fahrscheine, Jahre, Bodentexturen, Jahreszeiten und Verkehrsdichte bleiben, auch unbekannte. Die Karte wird in einem temporären Nachbarordner gebaut und erst am Ende umbenannt; ein vorhandener Ordner wird nie überschrieben. Dazu legt die App ein Projekt an, dessen Ort der Benutzer wählt.

**Grund:** Eine Karte braucht neben `global.cfg` und Kacheln Hilfsdateien, ohne die OMSI keinen Verkehr und keine Fahrgäste kennt; sie aus einer vorhandenen Karte zu nehmen, liefert zueinander passende Werte, ohne OMSI-Inhalte im Player mitzuliefern (ADR 005). Die mit nEditor angelegte Karte Stadtückheim zeigt, dass leere Kacheln nur `.map` und `.terrain` brauchen; Lichtkarten und `.prt`-Dateien legt OMSI selbst an. Ortsgebundene Daten der Vorlage würden auf Objekte und Kacheln zeigen, die es nicht gibt. Neue Karten nutzen das klassische 300-m-Raster, weil die Geometrie von `[worldcoordinates]` nicht genau bekannt ist (ADR 007). Im Spiel geprüft (23. Oktober 2026): Ohne Einstiegspunkt hängt sich OMSI beim Laden der Karte auf und stürzt ab; mit einem `entrypoint_bus.sco` auf der Kachel lädt sie. Neue Karten bekommen deshalb in der Kachelmitte dieses Objekt aus dem Basisspiel (ID 1, `[NextIDCode]` 2) und in der `global.cfg` den Einstiegspunkt „Start“ darauf.

## ADR 018: Geländelöcher als Maske, schmale Profile als Linien

**Status:** Angenommen, 23. Oktober 2026.

**Entscheidung:** Wo OMSI das Gelände ausschneidet, unter Objekten mit `[terrainhole]` und entlang von Splines mit `[terrainholeprofile]`, verwirft der Boden-Shader die Pixel einer Lochmaske je Kachel. Die Maske entsteht von oben aus dem Lochmodell bzw. aus den Bändern des Lochprofils, mit 1.024 Pixeln Kantenlänge, und wird um 0,75 m verkleinert. Spline-Profile werden mit ihren senkrechten Streifen gebaut, damit die Wände eines Trogs das Loch säumen. Profile, deren Punkte höchstens 0,6 m auseinander liegen, zeigt die Vektorebene als Linie statt als Spline: ab 2,5 m Höhe als gestrichelte Leitung, mit mindestens 1 m Höhe vom Boden als Zaun oder Mauer.

**Grund:** Das Gelände ist ein Raster mit 5 m Abstand; ein Ausschnitt im Netz würde ganze Rasterfelder kosten oder neue Dreiecke brauchen, die Maske dagegen schneidet pixelgenau und kostet nur Kacheln mit Loch Speicher (in Berlin-Spandau 45 Kreuzungen und 9 Gleistrog-Splines). Ohne Verkleinerung schien an den Rändern der Kreuzungen der Himmel durch, weil die Lochmodelle etwas größer sind als ihre Fahrbahn. Ohne die Trogwände sah man durch das Loch ins Leere. Leitungen und Zäune stehen senkrecht und haben von oben keine Fläche; als orange Spline-Linie sahen sie aus wie Straßen. In Berlin-Spandau sind es 353 Leitungen und 196 Zäune und Mauern, Bordsteine und Fahrbahnmarkierungen bleiben wegen ihrer geringen Höhe Flächen.

## ADR 019: `[worldcoordinates]`-Kacheln sind Web-Mercator-Kacheln

**Status:** Angenommen, 23. Oktober 2026. Ergänzt ADR 007.

**Entscheidung:** Die Kachelnummern einer `[worldcoordinates]`-Karte sind die Kacheln der Web-Mercator-Projektion der Zoomstufe 16, wie Webkarten sie zählen, aber von Greenwich nach Osten und vom Äquator nach Norden. Eine Kachel ist am Äquator 611,5 m breit und schrumpft mit dem Kosinus der Breite; Positionen in einer Kachel sind Meter im Maßstab ihrer eigenen Breite. Die Spline-Verbindungsprüfung rechnet Endpunkte so in Kachelspalten und -zeilen um und misst ihren Abstand dort. Die Karte wird weiter als ebenes Raster gezeichnet, mit der Kante der mittleren Kachelzeile statt eines festen Werts. Der Inspector nennt Breite und Länge der Südwestecke und die Kante der gewählten Kachel.

**Grund:** Die in ADR 007 gemessenen 371,9 m sind genau die Kante einer Zoom-16-Kachel auf 52,5° Breite, und die Kachel 2402, 11279 liegt bei 52,53° N und 13,19° E, in Falkensee bei Spandau. Mit diesem Modell treffen 1.464 von 1.467 Verbindungen in Berlin-Spandau auf unter 5 cm zusammen, im Median auf 0,1 mm; mit der festen Kante waren es 34 von 354 Verbindungen über Kachelgrenzen. Ein ebenes Raster kann den Maßstabswechsel zwischen den Zeilen (2,7 cm je Zeile) nicht abbilden: Kanten je Zeile verbessern den Median, schieben aber weit von der Mitte entfernte Spalten bis 0,8 m auseinander. Exakt wäre nur eine Darstellung mit eigener Drehung und Streckung je Kachel; das beträfe Zeichnen, Auswählen und Bearbeiten überall und lohnt für wenige Zentimeter nicht. Die mittlere Zeile ergibt für Berlin-Spandau 371,93 m und passt sich bei Karten auf anderen Breiten von selbst an.

## ADR 020: Chrono-Datum als schreibgeschützte Ansicht

**Status:** Angenommen, 23. Oktober 2026.

**Entscheidung:** Die App zeigt eine Karte auf Wunsch so, wie OMSI sie an einem Datum lädt: Die an diesem Tag gültigen Chrono-Varianten werden in Namensreihenfolge über eine Kopie der Basiskarte gelegt (`OmsiChrono.Overlay`), Fahrplan und Linienwege kommen aus den Fahrplänen dieser Varianten (`OmsiTimetable.ReadForVariants`). Die Chrono-Ansicht ist schreibgeschützt: Ziehen, Werkzeuge, Platzieren, Löschen, Inspector-Felder, Zurücksetzen von Kacheln sowie Rückgängig und Wiederholen sind gesperrt (`CanEdit`), bis **Base map** zur Basiskarte zurückkehrt. Die Basiskarte und ihre Bearbeitungen bleiben unterdessen unverändert im Speicher; Export, Speichern und die Kartenprüfung betreffen immer die Basiskarte.

**Grund:** Chrono-Änderungslisten wählen Elemente der Basiskarte über ihre ID aus und löschen oder tauschen sie; ein Objekt, das nur eine Variante anlegt, gehört in deren Änderungsliste, nicht in die Kachel. Bearbeiten in der überlagerten Ansicht müsste daher bei jeder Änderung entscheiden, ob sie die Kachel oder eine Variante betrifft, und Verschiebungen von Elementen, die eine Variante löscht oder tauscht, hätten keinen eindeutigen Ort. Eine reine Ansicht beantwortet die häufigste Frage – wie sieht die Karte an diesem Tag aus, welche Linien fahren – ohne dieses Risiko für die verlustfreie Arbeitskopie (ADR 008). Bearbeiten von Varianten bleibt eine eigene, spätere Aufgabe.

## ADR 021: Straßen sind OMSI-Splines

**Status:** Angenommen, 23. Oktober 2026, nach Wahl des Nutzers.

**Entscheidung:** Das Straßenwerkzeug baut Straßen direkt als `[spline]`-Datensätze. Ein eigenes Netz aus Knoten und Bézier-Segmenten gibt es nicht. Segmente sind die Splines, Knoten ihre Enden, und die Verbindungen sind die Vorgänger- und Nachfolger-IDs, die OMSI selbst speichert. Eine gezogene Straße ist ein Kreisbogen, der tangential aus seinem Start hervorgeht und den geklickten Punkt erreicht, oder eine Gerade, wenn sie frei beginnt. Sie wird in gleich lange Stücke von höchstens 25 m geteilt (einstellbar). Höhen und Geländehöhen sind getrennte Werte:
- **Gelände folgen:** Jede Stückgrenze liegt auf dem Gelände.
- **Manuell:** Die Höhe läuft gleichmäßig vom Start zur eingegebenen Endhöhe.

In beiden Modi hat jedes Stück eine gleichbleibende Steigung (Feld 11 und 12). Das Planen verändert das Gelände nie; angepasst wird es erst beim Bauen nach ADR 022. Punkte außerhalb des Geländes sind Fehler, keine Höhe 0. Steigungen über einer Warnschwelle (10 %), Knicke an angeschlossenen Enden und Straßen unter oder hoch über dem Gelände werden als strukturierte Befunde mit Position gemeldet.

Neue Splines kommen ans Ende der Kachel, in der ihr Start liegt, mit neuen kartenweiten IDs. Beim Andocken an ein freies Spline-Ende werden die IDs gegenseitig gesetzt. Ein gelöschter Spline öffnet die Enden seiner Nachbarn, auch in anderen Kacheln. Anhängungen auf ihm sowie Fahrplan- und Chrono-Verweise verhindern das Löschen. Rückgängig und Wiederholen laufen über die Dateistände. Gebaut wird in Etappe 1 nur in der Draufsicht.

**Grund:**
- Vorschau und Datei sind dasselbe: Was der Editor zeigt, schreibt er genau so, ohne Näherung von Bézier-Kurven durch Bögen beim Export.
- Vorhandene Straßen jeder Karte gehören sofort zum Netz, weil ihre Nachbar-IDs es schon beschreiben.
- Profil, Spuren und Darstellung kommen aus den vorhandenen `.sli`-Dateien und Renderern.
- Geteilte Stücke mit eigener Steigung sind der einfachste Weg, mit OMSIs Splines (eine Steigung am Anfang und eine am Ende) dem Gelände zu folgen; Steigungsknicke an den Grenzen erlaubt OMSI (Formatbefund: 1.324 von 1.438 Verbindungen in Berlin-Spandau ohne Knick).
- Ein glatter Höhenverlauf, Böschungen und Kreuzungsanschlüsse bauen auf denselben Datensätzen auf (Etappen 2 und 3).

## ADR 022: Gelände an gebaute Straßen anpassen, nicht zerstörend

**Status:** Angenommen, 23. Oktober 2026, auf Wunsch des Nutzers vor Etappe 2 gezogen. Ergänzt ADR 021.

**Entscheidung:** Neue Straßen passen das Gelände standardmäßig an („Fit terrain to the road“ im Straßen-Panel). Das Gelände bekommt drei Zonen:
- Unter der Fahrbahn, über die ganze Breite des `.sli`-Querschnitts, liegt es 5 cm unter dem Profilrand (seit 24. Oktober 2026; zuvor 0,3 m unter dem Spline).
- Daneben folgt ein Randstreifen in derselben Höhe, etwa 5 cm unter der Bordsteinoberkante. Er ist 2 m breit, mindestens aber eine Diagonale des Geländerasters (7,1 m bei 300-m-Kacheln, 8,8 m in Berlin-Spandau). So liegt jedes Rasterdreieck, das über den Gehweg reicht, flach; mit nur 2 m stieg es am Hang durch den Gehweg (Spieltest des Nutzers mit Bild vom 24. Oktober 2026). Die 5 cm verhindern Flimmern, wo das Gelände unter den Gehweg reicht; die Stufe am Schnitt ist im Spiel nicht zu sehen.
- Dahinter hält eine Böschung von 1:1,5 das Gelände in einem Kegel um diese Randhöhe: Höheres Gelände wird eingeschnitten, tieferes aufgeschüttet, bis höchstens 40 m.

An einem Ende, das auf einem Anschlusspunkt eines Objekts liegt (ADR 024), geht das Objekt weiter: Dahinter bleibt das Gelände, wie es ist, statt Randstreifen und Böschung um das Ende herumzuführen, und unter der Straße liegt es innerhalb einer Rasterdiagonale vor dem Ende 5 cm unter dem Spline statt unter dem Bordstein. Sonst ragte das Gelände in die Fahrbahn der Kreuzung, die auf Höhe des Splines anschließt (Rückmeldung des Nutzers mit Bild vom 24. Oktober 2026). Das gilt für Kreuzungen, die beim Bauen oder bei „Fit terrain to built roads again“ schon stehen.

Eine im Editor platzierte Kreuzung (ID ab dem ersten freien ID des Originals), an der mindestens eine solche Straße hängt, bekommt selbst angepasstes Gelände (Entscheidung des Nutzers vom 24. Oktober 2026): Ihr Umriss von oben gilt wie die Fahrbahn einer Straße, darunter liegt das Gelände 5 cm unter der Ebene des Objekts, daneben folgen Randstreifen auf Höhe des höchsten Bordsteins der Profile, die ihre Anschlusspunkte vorschlagen, und Böschung. Weil OMSI für Objekte kein Kennzeichen wie `[spline_terrain_align_2]` hat, folgt das allein aus den Kartendaten; nichts wird zusätzlich gespeichert. Nach jeder Objektänderung vergleicht die Shell die Lage der im Editor platzierten Kreuzungen (`OmsiMapSession.JunctionTerrainState`) und passt bei einer Änderung neu an, so auch nach Verschieben, Platzieren, Löschen und Rückgängig. Kreuzungen der Originalkarte behalten ihr Gelände.

Ein Rasterpunkt unter irgendeiner Straße nimmt deren Höhe; Böschungen anderer Stücke heben oder senken ihn nie. Daneben muss er in den Böschungskegeln aller nahen Straßen liegen. Lassen Straßen auf verschiedener Höhe keine gemeinsame Höhe zu, etwa zwischen den Ästen einer Kehre am Hang, läuft das Gelände gerade von Bordstein zu Bordstein, gewichtet nach dem Abstand zu jedem, sodass es jede Kante trifft. Flache Randstreifen hätten dort an beiden Bordsteinen eine halbe Stufe gelassen (Bildprüfung an einer Kehre mit 11 m zwischen den Bordsteinen). Zuvor setzte sich die zuletzt berechnete Straße durch, und am Bordstein der anderen stand im Spiel eine senkrechte Wand (Spieltest des Nutzers mit Bildern vom 24. Oktober 2026). Unter der Kontur des Profils, also unter seinen Streifen, schneidet die Kartenansicht das Gelände aus, wie OMSI es tut. Es reicht dabei nur 0,3 m unter die Ränder statt 0,75 m wie bei Kreuzungslöchern, damit es am Hang nicht durch den Gehweg ragt. Breiten ohne Streifen, etwa ein Grünstreifen zwischen Fahrbahn und Gehweg oder ein 0,9 m schmaler Mittelstreifen, behalten ihr Gelände. „Edit > Fit terrain to built roads again“ berechnet das Gelände an allen im Editor gebauten Straßen neu, etwa an Straßen, die vor einer Änderung der Anpassung gebaut wurden. Weil das Gelände allein aus Original und Straßen folgt, ist das kein Rückgängig-Schritt.

Das ursprüngliche Gelände bleibt in der Originalkarte. Nach jedem Bauen, Löschen, Rückgängig, Wiederholen und Kachel-Zurücksetzen wird das Gelände der betroffenen Kacheln und ihrer Nachbarn neu berechnet, und zwar aus dem Original plus allen im Editor gebauten Straßen mit Anpassung. Gleicht es dem Original, verschwindet die Kopie in der Arbeitskopie wieder.

Angepasste Straßen tragen wie bei OMSIs eigenem Editor `[spline_terrain_align_2]` mit dem Wert 1. Im Editor gebaute Straßen erkennt der Editor an IDs ab dem ersten freien ID der Originalkarte; nur ihr Gelände berechnet er. Straßen der Originalkarte behalten ihr Gelände. Die Anpassung lässt sich im Inspector für eine ganze im Editor gebaute Straße nachträglich ein- und ausschalten.

**Grund:**
- Ein Gelände mit 5 m Rasterabstand kann eine 25-m-Stückelung und Querhänge nicht nachzeichnen; ohne Anpassung stößt es durch die Fahrbahn (Rückmeldung des Nutzers mit Bild vom 23. Oktober 2026).
- Das Briefing verlangt die Trennung von Original-Gelände, Straßendaten, abgeleiteter Anpassung und angezeigter Oberfläche: Straßen können verschoben und gelöscht werden, ohne Gelände zu verlieren.
- Das Kennzeichen und die Form folgen dem Befund in Berlin-Spandau: An den 203 so gekennzeichneten Splines liegt das gespeicherte Gelände bis etwa 10 m neben der Mitte 0,15 bis 0,25 m über dem Spline, auf Höhe der Gehwege dieser Profile (Fahrbahn +0,10 m, Gehweg +0,25 m), und läuft über 20 bis 30 m aus. OMSIs Editor formt das Gelände also an solche Splines an und kennzeichnet sie.
- Im Spiel schneidet OMSI das Gelände unter gekennzeichneten Splines aus und schließt den Schnitt senkrecht bis zum Profilrand. Mit 0,3 m Absenkung zeigte sich dort eine senkrechte Wand aus gestrecktem Gras (Spieltest des Nutzers mit Bild vom 24. Oktober 2026). Auf Höhe des Profilrands, wie OMSIs Editor es hinterlässt, hat diese Wand keine Höhe.
- Ein erster Versuch, im Editor über die ganze Profilbreite auszuschneiden, legte in Berlin-Spandau Himmel frei: `str_6spur_falkenseer2_2.sli` lässt zwischen Gehweg und Fahrbahn 9 m ohne Streifen. Darum wird nur die Kontur ausgeschnitten, wie openOMSI das Element beschreibt.

## ADR 023: Spline-Griffe wie MoveIt

**Status:** Angenommen, 24. Oktober 2026, auf Wunsch des Nutzers. Ändert Etappe 2b von ADR 021.

**Entscheidung:** Spline-Enden und Gelenke werden gezogen wie mit MoveIt in Cities: Skylines.
- Beim Ziehen eines Gelenks behalten der gezogene Spline und der Nachbar ihr anderes Ende und ihre Gesamtdrehung; nur ihre Sehne folgt dem Gelenk. Knicke am Gelenk und an den anderen Enden werden gemeldet.
- Mit gedrückter Alt-Taste bleibt das Gelenk knickfrei: das bisherige Verfahren, bei dem der Nachbar tangential anschließt.
- Ein runder Griff in der Mitte jedes Splines biegt ihn als Bogen durch den Zeiger; beide Enden und ihre Höhen bleiben. Mit Alt wird er gerade.

**Grund:**
- Der Nutzer fand das tangentiale Mitbiegen des Nachbarn ungewohnt und nannte MoveIt als Vorbild (Rückmeldung vom 24. Oktober 2026). Dort behält jedes Segment seinen Kurvenpunkt, wenn ein Knoten wandert; Alt rastet ein.
- OMSI speichert Splines als Kreisbögen, nicht als Bézierkurven. Ein Bogen ist durch Sehne und Drehung festgelegt; die Drehung zu erhalten bewahrt seine Form, auch wenn das Gelenk weit wandert. Ein fester Kurvenpunkt dagegen ergäbe beim Zurückziehen Schleifen.
- Ein Bogen hat nur einen freien Wert, daher ein Mittelgriff je Spline; S-Kurven entstehen aus zwei Splines.

**Ergänzung vom 24. Oktober 2026 (knickfrei mit zwei Bögen):** Alt hält jetzt die ganze Straße knickfrei statt nur das Gelenk. Das Gelenk behält die Richtung, mit der die Straße es durchläuft, wie ein Knoten in MoveIt. Jeder der beiden Splines läuft von seinem anderen Ende knickfrei zum Gelenk: von einem freien Ende als ein Bogen, von einem angeschlossenen Ende, dessen Richtung ebenfalls bleibt, als zwei Bögen (Biarc wie in ADR 024), wenn einer nicht beide Richtungen trifft. Der zweite Bogen wird ein neuer Spline: `OmsiMapSession.ApplyJointMove` teilt den Spline mit `SplitSpline` (ADR 025, Nachbarverweise werden nachgeführt) und schreibt dann alle Teile; alles ist ein Rückgängig-Schritt. Liegen beide Bögen auf einem Kreis, bleibt es ein Spline; ist der zweite kürzer als 1 m, ebenfalls, und der kleine Knick wird gemeldet. Fahren Objekte auf dem Spline oder nennen ihn Fahrplan oder Chrono, wird nicht geteilt und nichts geändert, mit Hinweis, ohne Alt zu ziehen. Ein freies Gelenk (Straßenende) verhält sich wie bisher. Grund: Das frühere Alt-Verfahren verschob den Knick nur ans andere Ende des Nachbarn; mit zwei Bögen je Spline lassen sich Lage und beide Richtungen zugleich treffen.

## ADR 024: Straßen schließen an Kreuzungsobjekte über ihre Splinehelper an

**Status:** Angenommen, 24. Oktober 2026. Erste Stufe von Etappe 3 aus dem Kreuzungsbriefing.

**Entscheidung:** Eine Kreuzung bleibt ein gewöhnliches `[object]`; ihre Anschlusspunkte kommen aus den `[splinehelper]` ihrer `.sco`, gedreht mit dem Objekt, und werden nicht in die Karte kopiert. Eine Straße ist angeschlossen, wenn ein Spline-Ende auf dem Punkt liegt, mit seiner Höhe und Richtung; die Nachbar-ID bleibt 0. Der Editor prüft jeden Punkt und meldet Connected, Needs review, Invalid oder Unconnected mit Symbol und Text; Punkte zweier Objekte, die Stirn an Stirn liegen, gelten als verbunden. Die Toleranzen (5 cm, 1°, 5 cm Höhe, 2 m Suchabstand) sind Prüfungen des Editors, keine Regeln von OMSI. Das Straßenwerkzeug rastet an freien Punkten ein; das Profil des Punkts wird nur vorgeschlagen.

**Grund:**
- Befund an echten Karten: In Grundorf liegen alle 36 Punkte exakt an Spline-Enden oder an einem Punkt einer anderen Kreuzung; in Berlin-Spandau 837 von 1.171, alle mit Nachbar-ID 0. OMSI speichert also keine Verknüpfung, die Lage selbst ist der Anschluss.
- In Spandau weicht das Profil an 333 Anschlüssen vom vorgeschlagenen ab; es zu erzwingen widerspräche echten Karten.
- Die 233 Prüffälle in Spandau (Lage, Weichen, Höhe) stammen aus der Originalkarte. Als „Needs review“ mit Erklärung sind sie sichtbar, ohne als Fehler zu gelten.
- Eine Straße mit freiem Start wird vom angeschlossenen Ende aus geplant, damit sie wie in echten Karten ohne Knick ankommt. Liegt die Richtung an beiden Enden fest, etwa beim Weiterbauen an ein freies Spline-Ende oder an einen Anschlusspunkt, plant der Editor seit dem 24. Oktober 2026 zwei Bögen, die knickfrei ineinander übergehen (Biarc mit gleich langen Tangentenschenkeln). Ein einzelner Bogen kam dort mit einem Knick an, und weil OMSI jeden Spline rechtwinklig zu seinem eigenen Ende zeichnet, klaffte außen eine Lücke (Rückmeldung des Nutzers mit Bildern vom 24. Oktober 2026; in seinem Projekt Knicke von 2,8°, 7,2° und 23°).

## ADR 025: Kreuzungen werden in Straßen eingesetzt, nicht per geteiltem Spline angedockt

**Status:** Angenommen, 24. Oktober 2026, auf Wunsch des Nutzers. Etappe 4a; 4b bis 4d folgen mit eigenen Entscheidungen.

**Entscheidung:** „An Segmente andocken“ heißt im Editor, ein Kreuzungsobjekt in eine Straße einzusetzen. Beim Platzieren eines Objekts mit mindestens zwei Anschlusspunkten rastet die Vorschau auf einer Straße ein, deren Mittellinie höchstens 4 m vom Zeiger entfernt ist: Zwei gegenüberliegende Punkte liegen auf der Mittellinie, das Profil der Straße wird bevorzugt, die übrigen Arme zeigen zur Seite des Zeigers, die Höhe ist die der Straße. Alt platziert frei. Ein Klick teilt die Straße 5 m hinter jedem Punkt (`OmsiMapSession.SplitSpline`), löscht das Stück dazwischen, setzt das Objekt und führt von jedem Schnitt ein kurzes Stück im Profil der Straße an den Punkt, mit zwei Bögen geplant (ADR 024), also knickfrei auch in Kurven und mit der Höhe des Punkts. Alles ist ein Rückgängig-Schritt; scheitert ein Schritt, werden die vorigen zurückgenommen. Teilen behält ID, Start und alle Felder des Splines und hängt den zweiten Teil ans Kachelende (Regel 4); Lage, Richtung, Höhe, Steigung und Überhöhung bleiben stetig, der Nachbar am alten Ende wird mit dem neuen Teil verknüpft. Hängen Objekte am Spline oder nennen ihn Fahrplan oder Chrono, wird nicht geteilt; Splines der Originalkarte mit Geländeanpassung auch nicht, weil der neue Teil sonst das Gelände wie eine hier gebaute Straße formen würde (ADR 022).

**Grund:**
- Ein OMSI-Spline hat nur einen Vorgänger und einen Nachfolger. Ein dritter Ast an einem geteilten Spline hängt ohne Verknüpfung, die Fahrbahnen überlappen, die KI biegt nicht ab. Echte Karten zweigen Fahrbahnen über Kreuzungsobjekte ab: In Berlin-Spandau liegen nur 39 freie Enden auf Gelenken und 68 mitten auf Splines, fast alle Gehwege, Dämme und Gleise; in Grundorf keines.
- Das kurze Stück mit zwei Bögen macht das Einsetzen unabhängig von Krümmung und Gefälle der Straße; ein gerader Kreuzungsarm träfe eine Kurve sonst daneben.

## ADR 026: Erzeugte Objekte liegen unter Sceneryobjects\Mapforge in der Installation

**Status:** Angenommen, 24. Oktober 2026, Wahl des Nutzers. Etappe 4b; im Spiel bestätigt (Nutzer, 24. Oktober 2026): OMSI zeichnet das erzeugte Stück texturiert, ein Fußgänger nutzte seine Pfade, ein Bus fuhr ohne Probleme darüber.

**Entscheidung:** Objekte, die der Editor erzeugt, zuerst Kreuzungen, schreibt er sofort nach `Sceneryobjects\Mapforge\<Projekt>\<Name>\` der OMSI-Installation: `<Name>.sco` in Windows-1252, das Modell als Text-`.x` in `model\model.x` und Kopien der benötigten Texturen samt gleichnamiger `.dds` in `texture`. Vorhandene Dateien werden nie überschrieben; ein belegter Name bekommt eine Nummer. Die Karte nennt das Objekt wie jedes andere mit diesem Pfad. Die Originalkarte bleibt unberührt (Regel 1); wer die Karte weitergibt, gibt den Ordner `Sceneryobjects\Mapforge\<Projekt>` mit, wie Kartenpakete ihre Objekte unter `Sceneryobjects\<Karte>` mitbringen. Für den ersten Test erzeugt **Edit > Create test junction from road profile** ein gerades 12-m-Stück im Profil des Straßenwerkzeugs: Flächen aus der Extrusion des Profils (dieselbe wie für Splines), zwei `[splinehelper]` an den Enden mit diesem Profil, die `[path]` des Profils als Fahr- und Gehwege (gegenläufige Spuren vom anderen Ende aus, beidseitige einmal mit Richtung 2), dazu wie bei `Einm_Altonaer` `[rendertype] surface`, `[fixed]`, `[surface]` und das Modell als `[collision_mesh]`.

**Grund:**
- Alle installierten Karten nennen Objekte relativ zum OMSI-Hauptordner, belegt nur unter `Sceneryobjects` (71.076) und `Vehicles` (216); kein Objekt liegt in einem Kartenordner. Ein Pfad in den Kartenordner enthielte dessen Namen, den der Export ändert, und ist in OMSI unbelegt.
- Kartenpakete legen ihre eigenen Objekte unter `Sceneryobjects\<Karte>` ab (in der Installation etwa `HafenCityHamburg`); `Mapforge\<Projekt>` folgt dem und trennt erzeugte Objekte von allem anderen.
- Sofort geschrieben finden Editor und OMSI das Objekt wie jedes andere, ohne eigenen Suchweg.
- Format belegt an echten Dateien: Die Flächen der Kreuzungsmodelle drehen wie die Profil-Extrusion, (b − a) × (c − a) zeigt aus der Fläche (78 von 78 nach oben gerichteten Dreiecken von `Einm_altonaer.o3d`); `[path]` hat zwölf Felder: x, y, Höhe, Kurs, Radius, Länge, zwei Steigungen, Art, Breite, Richtung, Blinker.

## ADR 027: Geometrie erzeugter Kreuzungen

**Status:** Angenommen, 24. Oktober 2026. Etappe 4c; im Spiel bestätigt (Nutzer, 24. Oktober 2026): Kreuzungen werden gezeichnet, mit der richtigen Textur, und der KI-Verkehr fährt ohne Probleme hindurch.

**Entscheidung:** Eine erzeugte Kreuzung entsteht aus ihren Armen, je Richtung und Profil (`OmsiJunctionLayout`). Aus dem Profil liest der Editor die Fahrbahn (der ebene Streifenzug durch die Spline-Mitte), die Gehwege (die höheren ebenen Streifen dahinter) und den Bordstein (ein senkrechter Streifen) samt Texturen und Texturkoordinaten (`OmsiProfileSections`). Zwischen zwei benachbarten Armen werden die Bordsteinlinien mit 6 m ausgerundet, die äußeren Gehwegkanten mit 6 m weniger der Gehwegbreite (mindestens 0,5 m); liegen zwei Arme gegenüber (175° bis 185°), läuft der Bordstein gerade durch, bei mehr als 185° treffen sich die Linien im Schnittpunkt. Jeder Arm reicht 2 m über das Ende seiner Ausrundungen hinaus; dort liegt sein Anschlusspunkt, und der Querschnitt ist der des Profils. Die Fahrbahn ist eine Fläche (Ohrenschnitt-Triangulierung) mit `Splines\Marcel\texture\str_asphdrk.bmp`, dem markierungsfreien Asphalt des Basisspiels, flach aufgelegt mit 8 m quer und 15 m längs je Wiederholung, wenn die Fahrbahn des ersten Arms Asphalt ist (Dateiname mit „asph“); Pflaster und andere Beläge behalten ihre Textur, flach aufgelegt im Maßstab des Profils (nach dem ersten Nutzertest vom 24. Oktober 2026, der bei `str_2spur_8m_sedan1` Asphalt statt Kopfsteinpflaster zeigte). Fehlt der Asphalt in der Installation, gilt ebenfalls die Textur des Arms. Beim Erzeugen verlässt der Editor das Straßenwerkzeug, damit der Klick die Kreuzung platziert, und liest die Installation neu ein, damit die Bibliothek das neue Objekt zeigt. Gehwege sind Bänder um jede Ecke mit der Textur und den u-Werten des Profils, Bordsteine senkrechte Bänder zur Fahrbahn; jedes Teil bekommt ein eigenes Teilmodell, auch bei gleicher Textur. Für jede Spur in die Kreuzung (Richtung 1 oder 2) und jede Spur hinaus auf einem anderen Arm (Richtung 0 oder 2) entsteht ein Pfad aus einem oder zwei Bögen (ADR 024), mit Blinker rechts ab 30° Rechtskurve, links ab 30° Linkskurve; Gehwegpfade führen um jede Ecke und gelten in beide Richtungen. Zum Testen erzeugen **Edit > Create T junction from road profile** und **Create crossroads from road profile** eine T-Kreuzung und eine mit vier Armen im Profil des Straßenwerkzeugs.

**Grund:**
- Die Profile tragen alles, was der Querschnitt braucht; aus ihnen gebaut passen die Armenden ohne Stufe an die Straßen.
- Die Fahrbahntexturen der Profile enthalten Markierungen, die auf einer Fläche wiederholt quer lägen; `str_asphdrk.bmp` ist schlichter Asphalt mit `.cfg` für Nässe und Pfützen, die mitkopiert wird (ebenso `.surf`).
- Ein Pfad in einer `.sco` ist ein Bogen oder eine Gerade; zwei Bögen verbinden beliebige Spuren knickfrei, wie bei den Straßen.
- Im ersten Bild teilten Gehweg und Bordstein ihre Textur und damit ein Teilmodell; ihre Bänder vermischten sich zu Fächern. Eigene Teilmodelle je Teil beheben das.

## ADR 028: Kreuzungen entstehen, wo neue Straßen auf Straßen treffen

**Status:** Angenommen, 24. Oktober 2026, Etappe 4d nach dem Plan mit dem Nutzer; gemischte Profile auf seinen Wunsch hier statt im Menü. Vom Nutzer im Editor geprüft: funktioniert bei verschiedenen Winkeln und Profilen; sehr breite Straßen mit Mittelstreifen fehlen noch (siehe unten).

**Entscheidung:** Mit „Make junctions where roads meet“ im Straßen-Panel (an) erzeugt das Straßenwerkzeug Kreuzungen selbst. Endet die neue Straße auf der Fahrbahn einer bestehenden Straße (nicht an einem freien Ende oder Anschlusspunkt), entsteht dort eine T-Kreuzung; ihr Seitenarm zeigt zum Start der neuen Straße, zwischen 35° und 145° gegen die Straße. Kreuzt die neue Straße eine bestehende, entsteht am ersten Schnittpunkt eine Kreuzung mit vier Armen in den tatsächlichen Richtungen, auch schiefwinklig, ab 30° gegeneinander; flacher wird nicht gebaut, mit Hinweis. Als Straße zählt ein Profil mit mindestens 3 m Fahrbahn und einer Fahrspur (`OmsiRoadJunctions.IsRoad`); Gleise, Leitungen, Zäune und Gehwege nicht. Die Kreuzung wird mit den Profilen beider Straßen erzeugt (ADR 026, ADR 027): die Arme der bestehenden Straße bei 0° und 180° als Hauptstraße, deren Belag durchläuft, die Arme der neuen Straße mit ihrem eigenen Belag ab der Bordsteinlinie der Hauptstraße; Gehweg- und Bordsteinhöhen gehen um die Ecken ineinander über. Sie wird wie in ADR 025 in die bestehende Straße eingesetzt, das durchgehende Armpaar vorgegeben; die neue Straße wird bis zum Anschlusspunkt gebaut, bei einer Kreuzung in zwei Teilen, und geht danach weiter wie gewohnt. Alles ist ein Rückgängig-Schritt; scheitert ein Teil, werden die vorigen zurückgenommen. Die Vorschau sagt, was ein Klick erzeugt.

**Grund:**
- Echte Karten zweigen Fahrbahnen über Kreuzungsobjekte ab (ADR 025); erzeugte Kreuzungen passen sich Winkel und Profilen an, die vorhandenen Objekte nicht.
- Treffen zwei Straßen verschiedener Art aufeinander, gehört jeder Belag zu seiner Straße; wie in der Wirklichkeit läuft die Hauptstraße durch.
- Der Schnitt zweier Polylinien (neue Straße alle 0,5 m, bestehende alle 1 m) findet die Kreuzung genau und schnell genug für die Vorschau bei jeder Zeigerbewegung.
- Unter 30° würden die Ecken spitz und die Arme sehr lang.

**Ergänzung nach dem Nutzertest vom 24. Oktober 2026:**
- *Breite Straßen:* Bei einer Straße mit Mittelstreifen nahm die Kreuzung nur den Mittelstreifen als Fahrbahn, weil die Profilanalyse den ebenen Streifen durch die Spline-Mitte suchte. Die Fahrbahn reicht jetzt über alle Fahrspuren für Fahrzeuge, von der äußersten linken bis zur äußersten rechten, und weiter über ebene Streifen auf ihrer Höhe wie Rinnen; ein Anstieg über 3 cm gilt als Bordstein. Ein Mittelstreifen zwischen den Spuren wird in der Kreuzung zur Fahrbahn, der Belag kommt vom Streifen unter der Spur nächst der Spline-Mitte, ein Gehweg ist der höhere Streifen direkt neben der Fahrbahn. Liegt die Fahrbahn ganz auf einer Seite, wie bei einer Richtungsfahrbahn mit dem Spline am Mittelstreifen, liegen beide Ränder auf dieser Seite. Von 40 Straßenprofilen der Installation, die zuvor falsch gelesen wurden (darunter `str_6spur_falkenseer1`, `str_10spur_altstaedterring5`, `Hstr_6spur_Ruhlebener1`, `RQ10.5_Landstraße`), deckt die Fahrbahn jetzt bei allen ihre Spuren.
- *Fahrtrichtung:* Ein Arm, auf dem die Straße zur Kreuzung hin läuft, sieht ihr Profil gespiegelt (`OmsiJunctionArm.Mirrored`, `OmsiSplineProfile.Mirrored`): Seiten und Spurrichtungen tauschen. Das kurze Stück hinter einer eingesetzten Kreuzung läuft jetzt in der Richtung der Straße (zuvor entgegen), so stimmen Profile, die rechts und links verschieden sind.
- *Übergänge:* Schließt eine neue Straße an ein freies Ende einer Straße anderer Breite an, am Anfang oder am Ende, setzt der Editor ein Übergangsstück auf dieses Ende (Wunsch des Nutzers): zwei Arme gegenüber, dazwischen gehen Fahrbahn, Bordsteine und Gehwege in einem S ohne Knick von einem Querschnitt in den anderen über, viermal so lang wie die größte Kantenänderung, 8 bis 40 m; jede Hälfte der Fahrbahn trägt den Belag ihrer Straße, Spuren und Gehwege führen zur nächstgelegenen auf der anderen Seite, eine wegfallende Spur fädelt ein. Ab 25 cm Kantenänderung; gleich breite Profile mit anderem Belag bekommen keins.
- *Mittelstreifen bei T-Kreuzungen (Wunsch des Nutzers):* Hat die Hauptstraße einen Mittelstreifen (die breiteste Lücke von mindestens 0,5 m zwischen Spuren entgegengesetzter Richtung, über Rinnen bis zu den Bordsteinen, mindestens 0,3 m breit) und liegen alle anderen Arme auf einer Seite, nimmt die Kreuzung nur die Fahrbahn dieser Seite bis zum Mittelstreifen (`OmsiProfileSections.NearHalf`). Mittelstreifen und ferne Fahrbahn samt Gehweg laufen als gerades Stück des Profils durch (`OmsiSplineProfile.Clipped`), mit ihren Spuren geradeaus; abgebogen wird nur auf der nahen Seite, also rechts hinein und rechts hinaus, wie an Straßen mit Mittelstreifen üblich. Kreuzungen mit Armen auf beiden Seiten trennen den Mittelstreifen weiter durch.
- *Gehwege mit Radweg (Nutzertest):* Zuvor wurde der Gehweg um eine Ecke als ein Band mit der Textur eines Arms gelegt; zeigte sie einen Radweg, bog er in die Seitenstraße ab und brach an Ecken im anderen Profil ab. Jetzt gehört das gerade Stück neben jedem Arm dessen Gehweg, sodass ein Radweg gerade bis an die Ecke läuft, und die Ausrundung trägt den Gehweg der Seitenstraße, wo eine Hauptstraße auf sie trifft, sonst den des ersten Arms mit Gehweg. Höhen gehen weiter um die Ecke ineinander über.

## ADR 029: Straßenmenü nach dem Vorbild von Cities: Skylines

**Status:** Angenommen, 24. Oktober 2026, auf Wunsch des Nutzers (Bild aus Cities: Skylines mit Optionen und Straßenauswahl).

**Entscheidung:** Das Straßenwerkzeug zeigt zwei Panels über der unteren Werkzeugleiste: links „Options“ mit einer Zeile je Einstellung (Höhenmodus als Umschalter Terrain, Smooth, Manual, Endhöhe, Stücklänge, Steigungswarnung, Fit terrain, Make junctions), rechts die Straßenauswahl mit gewähltem Profil und Vorschau des Plans in der Kopfzeile, Suche über alle Profile, Reitern und Kacheln. Die Reiter sind „In map“ (mit Anzahl), „Favorites“ (Stern, je Benutzer in `PlayerPrefs`) und die Arten aus `OmsiSplineCategories`, automatisch aus dem Profil gelesen: One-way (alle Fahrspuren in eine Richtung, auch Richtungsfahrbahnen), 2 lanes, 3-4 lanes, 5+ lanes, Median, Rail (Pfad-Typ 2, auch Tram), Paths (nur Fußgängerpfade), Other (Leitungen, Zäune, Helfer nur für den OMSI-Editor). Jede Kachel zeigt ein Vorschaubild, das `OmsiSplineThumbnail` aus den Texturen des Profils zeichnet: ein gerades Stück von oben, die oberste Fläche je Breite, weiße Pfeile für die Fahrtrichtung der Spuren; berechnet nacheinander auf einem Hintergrund-Thread, Texturen einmal gelesen und auf 256 Pixel verkleinert. Nur fertige Einstellungen stehen im Optionen-Panel; neue Bauarten bekommen dort später ihre Zeile.

**Grund:**
- Mit 185 Profilen in der Installation war die Liste mit Dateinamen schwer zu durchsuchen; Art und Bild erkennt man schneller als Namen wie `str_2spur_6+6m_Flankenschanze`.
- Die Profile tragen ihre Spuren, Richtungen und Mittelstreifen selbst; eine Einteilung daraus braucht keine Pflege und passt auch für User-Content.
- Beim Einteilen fiel auf, dass manche Gleisprofile einen auskommentierten Erklärblock mit `[path]` beginnen; er wurde als Straßenspur gelesen. Unlesbare Pfade haben jetzt die Art −1 (`OmsiSplineProfile.UnreadableLane`), behalten aber ihren Index; Gleise und Tramstrecken gelten nicht als Straße für Kreuzungen. Ebenso gilt eine Lücke zwischen Spuren, die auf Fahrbahnhöhe mit Fahrbahn belegt ist, nicht mehr als Mittelstreifen.

## ADR 030: Objekt-Menü wie das Straßenmenü, mit festen Arten und gerenderten Vorschaubildern

**Status:** Angenommen, 24. Oktober 2026, auf Wunsch des Nutzers („baue bitte auch das Objekt Menü so um“), feste Arten und gerenderte Vorschauen nach seiner Wahl.

**Entscheidung:** Das Objekt-Menü hat den Aufbau von ADR 029: links „Options“ (Hide DLC objects, Heading für das nächste Objekt, mit R und Shift+R gekoppelt, kurzer Hinweis zu Shift, R, Alt und Esc), rechts die Auswahl mit Kopfzeile (gewähltes Objekt, Hinweis, Suche über Pfade und Gruppen aller Objekte), Reitern und Kacheln. Die Reiter sind „In map“ (meistgenutzte zuerst, mit Anzahl), „Favorites“, „Library“ (Ordner als Kacheln mit Anzahl und DLC-Markierung, wie bisher nur für Ordner mit Inhalten jenseits des Basisspiels) und feste Arten aus `OmsiObjectCategories`: Buildings, Trees, Junctions, Signs, Stops, Lights, Rail, Other. Ein `[tree]` ist ein Baum, ein Objekt mit `[splinehelper]` eine Kreuzung; sonst entscheiden die `[groups]`, die letzte zuerst, über Schlüsselwörter in Deutsch und Englisch (englische nur am Wortanfang, deutsche Wortteile auch in Zusammensetzungen), dann der Dateiname, dann der Ordner. Der Scan der Installation trägt die Art je Objekt ein. Je Seite werden 120 Kacheln gebaut, eine letzte Kachel zeigt mehr. Alle Vorschaubilder zeichnet `MapObjectRenderer` aus dem Modell; die Bilder in `Sceneryobjects\nEditorThumbnails` stammen von einem fremden Editor und werden auf Wunsch des Nutzers nicht berücksichtigt (die frühere Anbindung ist entfernt): Art lesen wie für die Platziervorschau, mit den Materialien des Renderers per Command Buffer in eine Render-Textur 128 × 96 mit Kantenglättung, isometrisch (orthografisch) von vorn links und 30° von oben, eingepasst an die Ecken der Hülle (zuerst versehentlich von unten, nach Rückmeldung des Nutzers korrigiert), durchsichtiger Hintergrund, Bäume als gekreuzte Flächen, zwei je Bild; gespeichert als PNG unter `persistentDataPath\ObjectPreviews`, benannt nach Installation, Objekt und Dateizeit. Bis ein Bild fertig ist, dreht sich auf der Kachel ein Lade-Spinner in Akzentfarbe (`SpinnerElement`, Wunsch des Nutzers); ein Kasten steht nur bei Objekten ohne Modell. Die zuletzt angeforderten Bilder, etwa die des gerade geöffneten Reiters, werden zuerst gezeichnet.

**Grund:**
- Mit 2.862 Objekten in 251 uneinheitlich benannten Gruppen (etwa „Buildings“ und „Gebaeude109“, „Crossings“ und „Kreuzungen“, viele Straßennamen) war die Gruppenliste wenig hilfreich; feste Arten wie in Cities: Skylines findet man schneller. In der Installation: 1.435 Gebäude, 60 Bäume, 549 Kreuzungen, 349 Schilder und Ampeln, 152 Haltestellen, 102 Laternen und Masten, 35 Bahn, 180 Sonstiges.
- Am Wortanfang statt überall: Zuerst landeten 323 Objekte bei den Bäumen, weil „German Street Side“ das Wort „tree“ enthält.
- Selbst gezeichnete Bilder gibt es für jedes Objekt mit Modell und sie sehen aus wie im Editor; der Shader der Objekte braucht außer der Kameramatrix nichts aus der Render-Pipeline. Die nEditor-Bilder gab es nur für 1.069 Objekte, einige davon leer und weiß, und sie gehören zu einem anderen Editor.

## ADR 031: Kacheln hinzufügen und löschen

**Status:** Angenommen, 24. Oktober 2026, auf Wunsch des Nutzers („Mir geht langsam der Platz zum Testen aus“; Plus-Symbol halbtransparent, deckend erst unter dem Mauszeiger).

**Entscheidung:** Mit Projekt zeigt die Karte im Werkzeug Drag, solange nichts platziert oder gebaut wird, auf jedem freien Platz links, rechts, unter oder über einer Kachel ein rundes Plus, von oben und in 3D (auf der Geländehöhe der Nachbarkachel). Es ist blass und wird unter dem Mauszeiger deckend mit Ring in der Platzierfarbe; ab einer Kachelgröße unter 90 Pixeln entfällt es. Ein Klick legt `tile_X_Y.map` an (UTF-16, Version 14, leer wie die erste Kachel einer neuen Karte) und trägt sie als letzten `[map]`-Eintrag ein, sodass sich kein Kachelindex verschiebt. Das Gelände setzt das der Nachbarn fort: Die Ränder übernehmen die angrenzenden Ränder der vier Nachbarn, innen wird nach dem Kehrwert des Abstands zu jedem Rand gewichtet; ohne Nachbarn ist es flach auf 0 m. **Delete tile** im Inspector (rot, zweiter Klick innerhalb von 4 s bestätigt) nimmt die Kachel aus der Liste. Weil der Kachelindex die Stelle in `[map]` ist, zählt der Editor alle Verweise auf spätere Kacheln um eins herunter: Einstiegspunkte in `global.cfg`, `[splineAttachement_repeater]` in Kacheln und Chrono, `TTData\Busstops.cfg`, `StnLinks.cfg`, `.ttr`-Strecken, `.ttp`-Haltestellen, `signalroutes.cfg` und die Chrono-Kopien des Fahrplans. Splines anderer Kacheln, die an die gelöschte anschließen, bekommen ein offenes Ende. Gelöscht wird nicht, wenn es die einzige Kachel ist oder etwas außerhalb die Kachel braucht: ein Verweis auf ihre IDs aus anderen Kacheln (außer Spline-Nachbarn), Chrono-Dateien zur Kachel oder ein Index auf sie in Einstiegspunkt, Fahrplan, Haltestelle, Signalroute oder Wiederholung; die Statusleiste nennt die Gründe. Die Dateien der Kachel bleiben liegen, sind aber nicht mehr eingetragen. Hinzufügen und Löschen sind ein Rückgängig-Schritt über Dateistände vorher und nachher; danach wird die Karte neu gelesen, und der Schritt bleibt allein in der Historie. **Revert tile edits** gibt es für hinzugefügte Kacheln nicht, da ohne Datei eine eingetragene Kachel fehlen würde; der Weg zurück ist Löschen.

**Grund:**
- Ans Ende angefügt bleibt jeder bestehende Index gültig, wie bei Objekten (ADR 011); beim Löschen ist das Verschieben unvermeidbar, deshalb muss jeder bekannte Indexverweis mitgehen.
- Ein fortgesetztes Gelände lässt neue Kacheln ohne Stufe an die Karte anschließen; ein Höhensprung am Rand wäre im Spiel eine Wand.
- Die Zusammenfassungen der Karte hängen an Kachelobjekten, die nach dem Neulesen nicht mehr gelten; ältere Schritte lassen sich danach nicht sicher zurücknehmen, wie schon nach **Revert tile edits**.

## ADR 032: Dialog für neue Karten mit allen Grundeinstellungen

**Status:** Angenommen, 24. Oktober 2026, auf Wunsch des Nutzers („ziemlich rudimentär und viele Optionen fehlen noch“, Vorbild: der Dialog „New map“ von openMapper, nicht eins zu eins nachgebaut).

**Entscheidung:** Der Dialog `NewMapDialog` fragt in zwei Spalten: **Map** (Vorlage, Name in OMSI, Ordnername, der dem Namen folgt, bis er von Hand geändert wird, Beschreibung über mehrere Zeilen), **Land** (Größe 1 × 1 bis 9 × 9 Kacheln um Kachel 0, 0, Geländehöhe), **Traffic** (Rechts- oder Linksverkehr, Autos und Fahrgäste als Faktor 0 bis 2 auf die Tageskurven der Vorlage, „Real rail“), **Era and fares** (Jahre, Jahresversatz, Währung und Fahrscheinpaket aus den Ordnern `Money` und `TicketPacks` der Installation) und **Seasons** (wie die Vorlage, vier Jahreszeiten mit Schneewinter, milder Winter, nur Sommer); darunter über die ganze Breite die Bodentextur aller Kacheln als Kacheln mit Vorschaubild, zuerst die Bodenebenen der Vorlage, dann Texturen im Ordner `Texture`, die nach Boden aussehen (Gras, Wiese, Feld, Erde, Sand, Kies; ohne Detail- und Rauschtexturen). Jede Einstellung beginnt mit dem Wert der Vorlage (`OmsiNewMapOptions.FromTemplate`), und `OmsiNewMap.Create` schreibt nur, was davon abweicht, sodass unveränderte Zeilen der Vorlage bleiben. Linksverkehr ist `[LHT]` (in `Omsi.exe` belegt, openOMSI liest es ebenso), echte Schienen `[realrail]`. Jahreszeiten ersetzen die `[addseason]`-Liste samt den Kommentarzeilen, die OMSIs Editor darüber schreibt; der Schneewinter folgt Grundorf (Schnee bis Tag 38, Winter bis 75, Frühling 76 bis 134, Herbst 260 bis 323, Winter ab 324), der milde Winter ersetzt Schnee durch Winter. Der Verkehrsfaktor multipliziert die Dichten von `[trafficdensity_road]` und `[trafficdensity_passenger]`; fehlt der Vorlage eine Kurve, bekommt die Karte bei einem Faktor ungleich 1 den Tagesverlauf von Grundorf, mal dem Faktor. Die Bodentextur ersetzt die Textur der ersten `[groundtex]`, die Detailtextur bleibt. Alle Kacheln bekommen die Geländehöhe, der Einstiegspunkt steht in Höhe des Geländes, die Kartenkamera 50 m darüber; Kachel 0, 0 mit dem Einstiegspunktobjekt steht als erste in der Liste, weil der Einstiegspunkt sie über den Index 0 nennt.

**Grund:**
- Der alte Dialog konnte nur Namen vergeben; alles andere musste man nach dem Anlegen in `global.cfg` von Hand ändern. Für Testkarten (Größe, Höhe, Boden) und neue Projekte (Zeit, Währung, Linksverkehr) gehört es an den Anfang.
- Werte der Vorlage als Ausgangspunkt halten die Vorlage als Quelle für alles, was der Dialog nicht kennt, und eine nicht angefasste Einstellung ändert nichts.
- „Nur Sommer“ ist in OMSI eine Karte ohne `[addseason]`; alle installierten Karten haben einen Schneewinter mit dieser Abfolge, die Tage unterscheiden sich wenig; übernommen sind die von Grundorf.

## ADR 033: Mehrere Kreuzungen auf einer neuen Straße

**Status:** Angenommen, 24. Oktober 2026, auf Wunsch des Nutzers (offener Punkt aus ADR 028); vom Nutzer im Editor geprüft.

**Entscheidung:** Ein Klick mit dem Straßenwerkzeug baut an jeder Straße, die die neue Straße auf dem Weg kreuzt, eine Kreuzung mit vier Armen, und endet sie auf einer Straße, dort zusätzlich eine T-Kreuzung (bisher nur eine Kreuzung, und eine T-Kreuzung am Ende verdrängte jede Kreuzung davor). Gebaut wird der Reihe nach: Die erste gekreuzte Straße bekommt ihre Kreuzung, die neue Straße wird bis zu deren hinterem Arm gebaut, dann wird vom vorderen Arm aus neu geplant und die nächste Kreuzung gesucht. `OmsiRoadJunctions.Crossings` liefert dafür alle Schnittpunkte in Fahrtrichtung (`FirstCrossing` ist der erste davon). Die Mittellinie der Straße, auf der die neue endet, zählt in den letzten 20 m nicht als Kreuzung. Kreuzt die Straße eine davon flacher als 30°, wird nichts gebaut, mit Hinweis; mehr als zwölf Kreuzungen in einem Klick ebenso. Alles bleibt ein Rückgängig-Schritt; scheitert ein Teil, werden die vorigen zurückgenommen. Die Vorschau nennt alle Kreuzungen mit ihrem Abstand.

**Grund:**
- Lange Straßen über mehrere Querstraßen in einem Zug zu ziehen ist der übliche Fall; bisher entstand nur an der ersten eine Kreuzung, die übrigen wurden ohne Verbindung überbaut.
- Neu planen nach jeder Kreuzung statt alle Schnittpunkte der ersten Planung zu nutzen: Jede Kreuzung verschiebt den Weg um ihre Arme, und die Straße verlässt sie in Richtung ihres vorderen Arms; die übrigen Schnittpunkte liegen danach woanders.

## ADR 034: Produktname OMSI MapWorks

**Status:** Angenommen, 25. Oktober 2026, Entscheidung des Nutzers.

**Entscheidung:** Das fertige Programm heißt **OMSI MapWorks**. „OMSI Map Editor“ war ein Platzhalter, „Mapforge“ ist der Entwicklungsname. Umbenannt wird alles Sichtbare: Fenstertitel sowie Firmen- und Produktname in den Player-Einstellungen, Startbildschirm, Kopfzeile, Hilfedialog, `editor-settings.json`, Meldungen („MapWorks project“), Kommentare in neuen Karten und erzeugten Modellen, der vorgeschlagene Exportname „Karte (MapWorks)“, das Unity-Menü **OMSI MapWorks** und die Exe `Builds/Windows/OMSIMapWorks.exe`. Das Startbild zeigt das Logo des Nutzers statt des Unity-Logos. Technische Namen behalten „Mapforge“: `Sceneryobjects\Mapforge\<Projekt>` samt `[groups]` und Anzeigenamen erzeugter Kreuzungen, die Markierungsdatei `mapforge-export.txt`, die Endung `.omproj`, Schlüssel der PlayerPrefs, Namespaces `OmsiMapEditor.*` und der Repository-Name. Weil Unity PlayerPrefs unter Firmen- und Produktnamen ablegt, kopiert `PreferencesMigration` beim ersten Start den Registry-Schlüssel von „OMSI Map Editor“ einmal herüber (zuletzt geöffnet, Favoriten, DLC-Markierungen); das Zwischenlager der Vorschaubilder unter `persistentDataPath` wird neu gerendert.

**Grund:**
- Bestehende Karten nennen erzeugte Kreuzungen über ihren Pfad unter `Sceneryobjects\Mapforge`, frühere Exporte werden an `mapforge-export.txt` erkannt; ein Umbenennen würde beides brechen oder Ordner doppeln (Entscheidung des Nutzers).
- Die Vorschaubilder sind ein reiner Cache; die Einstellungen dagegen hat der Nutzer von Hand gepflegt.
- `CorporateDesign.md` schlägt „OMSI MapForge“ als vorläufige Wortmarke vor; dieser ADR legt den Namen fest.

## ADR 035: Arme erzeugter Kreuzungen drehen

**Status:** Angenommen, 25. Oktober 2026; Bedienung nach Wahl des Nutzers (Griff am Arm statt automatischem Nachführen). Offener Punkt aus Etappe 4. Vom Nutzer im Editor und im Spiel bestätigt.

**Entscheidung:** Ist genau eine im Editor erzeugte Kreuzung gewählt (Datei unter `Sceneryobjects\Mapforge`, Kopfzeile „a junction of“), zeigt jeder Arm in der Draufsicht einen runden Griff, 3 m innerhalb seines Anschlusspunkts, damit die Raute mit dem Zustand sichtbar bleibt. Ziehen dreht den Arm um die Kreuzungsmitte, in 5°-Schritten, mit Alt frei; er bleibt zwischen seinen Nachbararmen und mindestens 30° von ihnen entfernt. Die Vorschau zeigt alle Arme und die nachgeführten Straßen. Beim Loslassen wird die Kreuzung mit denselben Profilen, Haupt- und gespiegelten Armen neu erzeugt (neuer Ordner mit Nummer, ADR 026) und ersetzt die Datei des Objekts; ID, Lage, Drehung und folgende Datensätze bleiben (neue Objektänderung `Replacement`, Rückgängig über Dateistände). Weil die Länge jedes Arms aus den Ausrundungen neben ihm folgt (ADR 027), können alle Anschlusspunkte wandern. Jede angeschlossene Straße, deren Punkt wandert, wird wie beim Einsetzen einer Kreuzung (ADR 025) angeschlossen: Sie wird an der ersten Stelle geschnitten, die mindestens 5 m vor dem neuen Punkt liegt (in Richtung des Arms gemessen) und an der ein Stück mit zwei Bögen von mindestens 25 m Radius den Punkt erreicht; das Stück bis zur Kreuzung geht, ein neues Stück im Profil der Straße schließt die Lücke. Findet sich bis 120 m keine solche Stelle, gilt die mit dem größten Radius; hinter dem Schnitt bleiben mindestens 5 m Straße. Alles ist ein Rückgängig-Schritt. Ein Punkt, der Stirn an Stirn an einem anderen Objekt liegt, verhindert jede Änderung, die ihn verschiebt. Haupt- und gespiegelte Arme schreibt der Generator seit diesem Tag als Freitextzeile „MapWorks arms:“ in die `.sco`; für ältere Kreuzungen gelten die Arme bei 0° und 180° als Hauptstraße, gespiegelt ist ein Arm, dessen Straße auf die Kreuzung zuläuft. Der Gelände-Zustandstext der Kreuzungen enthält jetzt die Objektdatei, damit das Gelände nach dem Neu-Erzeugen neu angepasst wird (ADR 022).

**Grund:**
- Ersetzen statt Löschen und neu Platzieren: ID und Verweise bleiben, und nichts verschiebt Indizes in der Kachel.
- Die alte Datei bleibt liegen, weil Rückgängig sie wieder braucht; erzeugte Dateien werden nie überschrieben (ADR 026). Unbenutzte erzeugte Objekte aufzuräumen ist ein eigener Schritt (Roadmap).
- Schneiden statt den nächsten Spline umzuformen: Im ersten Versuch wurde der Spline hinter einem gewachsenen Arm ganz neu geplant; in Grundorf war das ein gekrümmter Spline der Originalkarte von 150 m, der zu zwei Bögen von je 77 m geworden wäre. Geschnitten bleibt die Straße hinter dem Schnitt, wie sie ist.
- 25 m Radius statt fester 5 m: Ein gedrehter Arm verschiebt seinen Punkt auch seitlich (10° bei 12 m Armlänge etwa 2 m), und eine gekrümmte Straße entfernt sich von einem längeren Arm; mit festen 5 m entstanden in Grundorf S-Kurven mit 4 bis 5 m Radius.
- OMSI liest Text außerhalb von Abschnitten nicht; die Zeile „MapWorks arms:“ stört das Spiel also nicht und macht das Neu-Erzeugen verlustfrei.

## ADR 036: Unbenutzte erzeugte Objekte aufräumen

**Status:** Angenommen, 25. Oktober 2026, auf Wunsch des Nutzers (offener Punkt aus ADR 035). Vom Nutzer im Editor bestätigt.

**Entscheidung:** Die Kartenprüfung (Tools) hat in einem Projekt das Thema „Made objects“. Es sucht im Hintergrund die Objektordner unter `Sceneryobjects\Mapforge\<Projekt>` der Installation (ADR 026), die nichts mehr nennt. Als benutzt gilt ein Ordner, solange eine Kacheldatei (`*.map`) irgendeiner Karte unter `maps` der Installation seinen Pfad enthält, also auch Exporte und Chrono-Varianten, oder eine Kacheldatei der Arbeitskopie des Projekts, oder etwas, das das offene Projekt zurückholen könnte: Objekte in Schritten der Rückgängig- und Wiederherstellen-Liste, auch die Dateien vor und nach einem Austausch und die Kacheln einer gelöschten Kachel, sowie das Objekt, das gerade platziert wird. Groß- und Kleinschreibung und Schrägstriche zählen nicht. Kann eine Kacheldatei nicht gelesen werden, gilt nichts als unbenutzt. Das Thema nennt die unbenutzten Ordner mit ihrer Größe; „Delete unused“ löscht sie nach einem zweiten Klick innerhalb von vier Sekunden, nur innerhalb des Projektordners. Das lässt sich nicht rückgängig machen.

**Grund:**
- Beim Drehen eines Arms bleibt die alte Kreuzung liegen, weil Rückgängig sie braucht (ADR 035); ohne Aufräumen sammeln sich Ordner an.
- Exporte liegen als eigene Karten unter `maps` und nennen dieselben Pfade; alle Karten zu lesen schützt sie, auch wenn das Projekt die Objekte nicht mehr nutzt.
- Projekte desselben Namens teilen sich einen Ordner; deren Arbeitskopien kennt der Editor nicht. Das Thema sagt das, und gelöscht wird erst nach Bestätigung.
- In der Kartenprüfung statt als eigener Menüpunkt: Sie läuft ohnehin im Hintergrund, zeigt Befunde mit Knöpfen und braucht keine neue Oberfläche.

## ADR 037: Reihen von Objekten entlang von Straßen anlegen

**Status:** Angenommen, 25. Oktober 2026; eigenes Werkzeug und Reichweite bis zur nächsten Kreuzung nach Wahl des Nutzers. Vom Nutzer im Editor bestätigt; im Spiel noch ungeprüft.

**Entscheidung:** Das Werkzeug „Rows“ (T) legt `[splineAttachement]`-Reihen an, etwa Laternen oder Pfosten. Es zeigt die Objekt-Bibliothek mit eigener Optionsspalte: Abstand zwischen den Objekten (25 m), Abstand von der Mitte (leer: 0,5 m innerhalb des Profilrands), Höhe, Drehung gegen die Fahrtrichtung, beide Seiten, ganze Straße. Zeigt der Zeiger auf eine Straße, zeigt die Karte, wo die Objekte stehen würden, auf der Seite des Zeigers; ein Klick legt die Reihe, mit „Whole road“ entlang der ganzen Kette verbundener Splines von Kreuzung oder freiem Ende zu Kreuzung oder freiem Ende, gleichmäßig verteilt mit gleichen Rändern, sonst zwischen zwei Klicks. Auf der linken Seite steht jedes Objekt um 180° gedreht, so zeigen beide Reihen zur Straße. Die Vorschau zeigt die Objekte selbst als Modelle; R dreht sie vorher um 15°, Shift+R zurück; nach dem Legen erscheint erst wieder eine Vorschau, wenn sich der Zeiger bewegt, damit ein zweiter Klick die Reihe nicht doppelt legt (Wünsche des Nutzers nach dem ersten Test). Ein Klick auf ein Objekt einer vorhandenen Reihe wählt die Reihe, Entf löscht sie; Verweise wie Haltestellen im Fahrplan verhindern das. Alles ist rückgängig zu machen (`OmsiAttachmentChange`, Dateistände).

Eine Reihe wird als ein Datensatz je Kachel geschrieben, in der ihre Objekte stehen, verankert an einem Spline dieser Kachel. Der Startabstand zählt wie in OMSI ab dem Anfang der ganzen Kette des Ankersplines; läuft die Kette gegen den Ankerspline, zählt der Datensatz vom anderen Ende, mit umgekehrtem Versatz und um 180° gedrehten Objekten. Jeder geplante Datensatz wird vor dem Schreiben mit der Zeichenlogik (`OmsiSplineAttachmentLayout`) nachgerechnet und muss genau die geplanten Positionen ergeben.

**Grund:**
- `[splineAttachement_repeater]` wird nicht geschrieben: In Berlin-Spandau passt die Nummer des ersten Objekts nur in 29 von 210 Fällen zum ersten Objekt der Kachel, der genannte Spline trägt es nur in 40 Fällen, und manche Wiederholungen stehen doppelt. Ein Datensatz je Kachel erreicht dasselbe Bild ohne unbelegte Felder.
- Die Bibliothek wiederzuverwenden spart eine zweite Objektauswahl mit Reitern, Suche und Vorschaubildern.
- Weil der Startabstand ab dem Kettenanfang zählt, verrutschten in OMSI alle Reihen einer Kette, wenn vor ihrem Anfang eine Straße angebaut wurde, auch Reihen der Originalkarten.

**Ergänzung vom 25. Oktober 2026:** Jede Spline-Änderung (Straße bauen, Spline löschen, teilen, verformen, und damit auch Kreuzungen einsetzen, Gelenke ziehen und Kreuzungen neu erzeugen) merkt sich vorher, wo der Spline jeder Reihe in seiner Kette beginnt, und rechnet es danach neu. Wo sich das geändert hat, ändert sich der Startabstand der Reihe um dieselbe Strecke; die Kacheldateien kommen in dieselbe Änderung, Rückgängig nimmt beides zurück. So bleiben die Objekte stehen, wo sie standen; ihr Abstand zum eigenen Spline bleibt gleich. Änderungen nur am oder hinter dem Spline einer Reihe verschieben sie wie bisher mit der Straße.

**Dritte Ergänzung vom 25. Oktober 2026:** Reihen werden nach ADR 038 ausgelegt und geplant: ein Datensatz für die Splines einer Kachel in einer Laufrichtung, Startabstand ab dem gespeicherten Kettenabstand des Ankers. Das Mitführen beim Anbauen rechnet mit dem gespeicherten Kettenabstand statt mit der Kette.

**Zweite Ergänzung vom 25. Oktober 2026 (Reihen bearbeiten):** Ein Klick auf ein Objekt wählt die ganze Reihe: den Datensatz und die Datensätze desselben Objekts mit gleichem Abstand, gleicher Höhe, gleichem Versatz und gleicher Drehung entlang derselben Kette, die ihn im selben Abstand fortsetzen, so wie das Werkzeug eine Reihe je Kachel aufteilt (`OmsiAttachmentRows.RowOf`). Die Felder zeigen dann ihre Werte wie beim Legen (Abstand von der Mitte ohne Vorzeichen, Drehung wie auf der rechten Seite); während des Tippens zeigt die Karte die geänderte Reihe, Enter oder das Verlassen des Feldes ändert sie, ein Objekt aus der Bibliothek ersetzt ihre Objekte, R dreht sie um 15°. Esc gibt die Reihe frei, die Felder zeigen wieder die Werte zum Legen. Geändert wird über dieselbe Strecke: vom ersten Objekt an so viele, wie bis zum letzten passen. Die neu geplanten Teile kommen der Reihe nach in die vorhandenen Datensätze ihrer Kachel und behalten deren IDs, Detailstufe, Neigung und Texte; weitere Teile kommen ans Kachelende, übrige Datensätze werden gelöscht, außer Verweise nennen sie. Eine Reihe mit `[splineAttachement_repeater]` wird nicht neu geplant, weil deren Bedeutung ungeklärt ist: Hauptdatensatz und alle Wiederholungen mit derselben ID bekommen dieselben neuen Werte, Start und Reichweite bleiben (die Wiederholungen wiederholen laut Inventar die Werte des Hauptdatensatzes). Werte gelten als unverändert, solange sie auf die angezeigten drei Nachkommastellen gleich sind, damit bloßes Verlassen eines Feldes keine Originalreihe rundet. Entf löscht alle Datensätze der Reihe in einem Schritt.

**Grund:** Eine Reihe über mehrere Kacheln ist für den Nutzer eine Reihe; sie Kachel für Kachel zu bearbeiten, ließe an jeder Grenze einen falschen Abstand. Neu zu planen statt Werte einzeln zu ändern hält jedes Objekt in der Kachel seines Datensatzes und prüft das Ergebnis wieder mit der Zeichenlogik.

## ADR 038: openOMSI als Ziel; Kettenabstände pflegen und Reihen wie openOMSI auslegen

**Status:** Angenommen, 25. Oktober 2026, nach Spieltests des Nutzers in openOMSI und OMSI 2; vom Nutzer in openOMSI bestätigt. Ziel ist laut Nutzer die Verträglichkeit mit openOMSI (Omsi-Hub); in OMSI 2 sind etwa erzeugte Kreuzungen unsichtbar, das ist hingenommen.

**Entscheidung:** Wo die Kartendaten eine Formatfrage offen lassen, gilt openOMSIs Auslegung (Quelltext unter github.com/openOMSI-org/openOMSI; seine Kommentare halten Abgleiche mit Omsi.exe fest).

- Die letzte Zahl eines `[spline]` (Feld 17, ab Version 11) ist der Kettenabstand: die Länge der Kette vor dem Spline (TileRecordInventory). Nach jeder Spline-Änderung bekommen neue Splines und Splines, deren Kette jetzt anders vor ihnen beginnt, diesen Wert, im selben Rückgängig-Schritt (`KeepChains`). Werte von Ketten, die die Änderung nicht berührt, bleiben, auch veraltete der Originalkarten.
- `OmsiSplineAttachmentLayout.Place` legt eine Reihe wie openOMSI aus: ab dem gespeicherten Kettenabstand des eigenen Splines, nur auf den Splines der eigenen Kachel, Wiederholungen ebenso; Datensätze, die vor ihrem Spline beginnen, zeichnet es nicht.
- Das Werkzeug „Rows“ teilt eine Reihe in Datensätze je zusammenhängendem Stück der Kette in einer Kachel und einer Laufrichtung; läuft die Straße gegen die Splines, beginnt der Datensatz beim letzten Objekt und legt die anderen zurück, mit umgekehrtem Versatz und um 180° gedreht.
- Rückt der gespeicherte Kettenabstand eines Ankers, rückt der Startabstand seiner Reihen um dasselbe, so bleiben die Objekte stehen.
- Das Thema „Chain offsets“ der Kartenprüfung listet Splines, deren gespeicherter Wert mehr als 1 cm von ihrer Kette abweicht; „Store offsets“ schreibt die richtigen Werte und verschiebt die Reihen auf diesen Splines mit, als ein Rückgängig-Schritt (`StoreChainOffsets`).

**Grund:**
- Der Nutzer spielt und testet in openOMSI.
- Unser Straßenwerkzeug schrieb 0 als Kettenabstand. Eine Reihe in der zweiten Kachel einer Straße war deshalb in openOMSI unsichtbar, und die Texturen liefen nicht über die Gelenke.
- Die Abgleiche in Grundorf, Stadtückheim und Berlin-Spandau bestätigen die Bedeutung des Felds.

**Offen:** Kacheln löschen führt Kettenabstände und Reihen benachbarter Kacheln noch nicht nach. Reihen, die mit früheren Versionen über eine Kachelgrenze gelegt wurden, bleiben auf den falschen Wert gerechnet. „Store offsets“ hält sie dort, wo openOMSI sie zeigt; solche Reihen löscht man am besten und legt sie neu.

## ADR 039: Angehängte Objekte beim Platzieren einrasten

**Status:** Angenommen, 25. Oktober 2026; Bedienung nach Wahl des Nutzers („Beim Platzieren einrasten“), vom Nutzer im Editor und in openOMSI bestätigt.

**Entscheidung:** Ein Objekt aus der Bibliothek wird platziert wie bisher. Kommt die Vorschau einem freien Anhängepunkt (`[new_attachment]`) eines Objekts oder angehängten Objekts der Karte nahe (16 Pixel, mindestens 1 m, von oben gesehen), rastet sie dort ein und steht in der Lage des Punkts; R dreht sie am Punkt, Alt platziert frei. Ein Klick schreibt ein `[attachObj]` ans Ende der Kachel des Elternobjekts, also hinter das Elternobjekt (TileRecordInventory), mit neuer ID, Instanz 0, Punktindex und der Drehung gegen den Punkt (`OmsiTileEditing.AppendAttachedObject`, `OmsiMapSession.AttachObject`). Frei ist ein Punkt, an dem noch kein angehängtes Objekt hängt. Kreuzungen rasten weiter an Straßen ein, nicht an Punkten.

Der Inspector des gewählten Objekts listet unter „Attached objects“ alles, was daran hängt, auch über mehrere Stufen, je mit „Delete“ (`DeleteAttachedObject`, nimmt auch ab, was daran hängt). Ein Objekt zu löschen, nimmt seine angehängten Objekte mit (`OmsiTileEditing.RemoveWithFamily`); bisher verweigerte der Editor das, weil die Kinder auf ihr Elternobjekt verweisen. Andere Verweise, etwa aus Fahrplänen, Chrono-Dateien oder Einstiegspunkten, verhindern das Löschen weiter. Alles ist rückgängig zu machen (`OmsiAttachedObjectChange`, Dateistände).

**Grund:**
- Der Nutzer wählte das Einrasten: Es braucht keinen eigenen Arbeitsschritt, und man sieht vorher, wo das Objekt hängt.
- Die Lage der Punkte rechnet der Renderer ohnehin für das Zeichnen; er merkt sich dafür die Lage jedes Objekts mit Anhängepunkten.
- Ohne Löschen samt Kindern ließe sich ein Mast mit angehängtem Objekt gar nicht mehr löschen.

**Offen:** Reihen entlang von Straßen als Eltern (openOMSI hängt `[attachObj]` an das erste Objekt einer Reihe) und Kacheln vor Version 12.

## ADR 040: Gelände-Werkzeuge wie openMapper, mit einer Grundlage für die Straßenanpassung

**Status:** Angenommen, 25. Oktober 2026; Vorbild und Etappen nach Wahl des Nutzers (openMapper übernehmen, damit es sicher zu openOMSI passt; zuerst Pinsel und Rampe). Etappe 1 vom Nutzer in openOMSI bestätigt, auch über Kachelkanten.

**Entscheidung:** Das Werkzeug „Terrain“ (G) arbeitet wie openMappers Terrain-Werkzeug (`src/editor.rs`, siehe THIRD_PARTY_NOTICES):

- Ein runder Pinsel wirkt, solange die linke Taste gedrückt ist, alle 1/15 s mit der seither vergangenen Zeit. Jeder Rasterpunkt im Radius ändert sich mit dem Gewicht (1 − (d/r)²)²:
  - Raise und Lower um Stärke · 12 m/s,
  - Smooth zum Mittel der acht Nachbarn mit Faktor Stärke · 6/s,
  - Flatten zur Höhe beim Ansetzen oder einer eingetippten Höhe mit Stärke · 8/s,
  - Roughen um Stärke · 3 m/s mal ein festes Wellenmuster aus Wertrauschen von etwa 40 m und 12 m.
- Radius 3 bis 150 m (logarithmischer Regler, Standard 25 m), Stärke 0,05 bis 1 (Standard 0,5).
- Die Rampe macht das Gelände zwischen zwei Klicks zu einer gleichmäßigen Steigung von der Höhe am ersten zur Höhe am zweiten Punkt, voll bis 60 % des Radius, weich bis zum Rand. Die Steigung wird angezeigt.
- Das Gelände ist ein kartenweites Raster mit 60 Zellen je Kachel. Ein Punkt auf einer Kachelkante wird in beiden Kacheln gesetzt.
- Neben einer Straße (jedes Profil am Boden, keine Leitungen, Zäune oder Hilfssplines) steigt das Gelände nie höher als 12 cm unter die Fahrbahn, außer es lag schon höher.

Unsere Straßenanpassung (ADR 022) rechnet nach jeder Straßenänderung neu. Ihre Grundlage ist jetzt das von Hand geformte Gelände der Kachel, abgelegt in der Arbeitskopie unter `Mapforge\Terrain\<Kachel>.map.terrain`; fehlt es, gilt wie bisher das Original. Ein Pinselstrich ändert das gezeigte Gelände und diese Grundlage um denselben Betrag. Am Ende des Strichs wird die Grundlage gespeichert und das gezeigte Gelände der berührten Kacheln und ihrer Nachbarn daraus neu an die Straßen angepasst; das ist ein Rückgängig-Schritt (`OmsiTerrainChange` mit den Dateiständen der Grundlage). Der Export lässt den Ordner `Mapforge` aus (`OmsiMapExporter.ProjectDataFolder`). Im Editor angelegte Kacheln ohne Original werden erst angepasst, wenn auf ihnen modelliert wurde.

**Grund:**
- Der Nutzer will openMappers System übernehmen, damit Karten sicher zu openOMSI passen.
- Ohne eigene Grundlage hätte der nächste Straßenbau alles überschrieben, was man modelliert hat, weil die Anpassung vom Original ausgeht.
- Gemeinsame Kachelkanten verhindern Stufen an Kachelgrenzen.

**Offen:** Gelände unter Objekten einebnen, Höhenkarte und See/Fluss (Roadmap, Etappen 2 bis 4).
