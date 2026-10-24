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

Ein Rasterpunkt unter irgendeiner Straße nimmt deren Höhe; Böschungen anderer Stücke heben oder senken ihn nie. Unter der Kontur des Profils, also unter seinen Streifen, schneidet die Kartenansicht das Gelände aus, wie OMSI es tut. Es reicht dabei nur 0,3 m unter die Ränder statt 0,75 m wie bei Kreuzungslöchern, damit es am Hang nicht durch den Gehweg ragt. Breiten ohne Streifen, etwa ein Grünstreifen zwischen Fahrbahn und Gehweg oder ein 0,9 m schmaler Mittelstreifen, behalten ihr Gelände. „Edit > Fit terrain to built roads again“ berechnet das Gelände an allen im Editor gebauten Straßen neu, etwa an Straßen, die vor einer Änderung der Anpassung gebaut wurden. Weil das Gelände allein aus Original und Straßen folgt, ist das kein Rückgängig-Schritt.

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

## ADR 024: Straßen schließen an Kreuzungsobjekte über ihre Splinehelper an

**Status:** Angenommen, 24. Oktober 2026. Erste Stufe von Etappe 3 aus dem Kreuzungsbriefing.

**Entscheidung:** Eine Kreuzung bleibt ein gewöhnliches `[object]`; ihre Anschlusspunkte kommen aus den `[splinehelper]` ihrer `.sco`, gedreht mit dem Objekt, und werden nicht in die Karte kopiert. Eine Straße ist angeschlossen, wenn ein Spline-Ende auf dem Punkt liegt, mit seiner Höhe und Richtung; die Nachbar-ID bleibt 0. Der Editor prüft jeden Punkt und meldet Connected, Needs review, Invalid oder Unconnected mit Symbol und Text; Punkte zweier Objekte, die Stirn an Stirn liegen, gelten als verbunden. Die Toleranzen (5 cm, 1°, 5 cm Höhe, 2 m Suchabstand) sind Prüfungen des Editors, keine Regeln von OMSI. Das Straßenwerkzeug rastet an freien Punkten ein; das Profil des Punkts wird nur vorgeschlagen.

**Grund:**
- Befund an echten Karten: In Grundorf liegen alle 36 Punkte exakt an Spline-Enden oder an einem Punkt einer anderen Kreuzung; in Berlin-Spandau 837 von 1.171, alle mit Nachbar-ID 0. OMSI speichert also keine Verknüpfung, die Lage selbst ist der Anschluss.
- In Spandau weicht das Profil an 333 Anschlüssen vom vorgeschlagenen ab; es zu erzwingen widerspräche echten Karten.
- Die 233 Prüffälle in Spandau (Lage, Weichen, Höhe) stammen aus der Originalkarte. Als „Needs review“ mit Erklärung sind sie sichtbar, ohne als Fehler zu gelten.
- Eine Straße mit freiem Start wird vom angeschlossenen Ende aus geplant, damit sie wie in echten Karten ohne Knick ankommt. Liegt die Richtung an beiden Enden fest, etwa beim Weiterbauen an ein freies Spline-Ende oder an einen Anschlusspunkt, plant der Editor seit dem 24. Oktober 2026 zwei Bögen, die knickfrei ineinander übergehen (Biarc mit gleich langen Tangentenschenkeln). Ein einzelner Bogen kam dort mit einem Knick an, und weil OMSI jeden Spline rechtwinklig zu seinem eigenen Ende zeichnet, klaffte außen eine Lücke (Rückmeldung des Nutzers mit Bildern vom 24. Oktober 2026; in seinem Projekt Knicke von 2,8°, 7,2° und 23°).
