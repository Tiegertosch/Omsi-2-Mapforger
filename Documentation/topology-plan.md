# Plan: Ansicht „Topologie“ (Höhenlinien) für Release 1.2.0

Stand: 23. November 2026, Zweig `feature/topology` (von `main` b295505), per Merge-Commit `8be5aab` in `main` übernommen; der Zweig ist gelöscht. Bestandsaufnahme und Plan; Schritte 1 bis 3 umgesetzt (siehe Abschnitt 7). Auftrag des Projektinhabers: „Am rechten Rand die Ansicht Topologie, damit man Höhenlinien sehen kann bei Terrain, um so besser Straßen planen zu können.“ (Roadmap, Abschnitt „UI-Rework: nach dem Rework und Release 1.2.0“; ADR 098 nannte sie „später“). Nach Abschluss kommt ein ADR 103.

## 1. Ist-Stand mit Belegen

### Wo der Knopf hingehört

- Die Skizze `UI Research/Skitze.png` zeigt ganz rechts in der Leiste sechs runde Knöpfe; der rote Pfeil „Topology View“ zeigt auf den vorletzten (konzentrische Ringe), davor „Lines“/„Chrono“ und danach „Map Settings“. „Rechter Rand“ meint also die **rechte Knopfgruppe der unteren Leiste**, nicht einen Rand der Karte. Der Platz war seit ADR 098 (Nachtrag vom 11. November, `Decisions.md:1699`) bewusst frei gelassen.
- Die rechte Gruppe ist `RightBarButtons` in `EditorShellBottomBar.cs:31-35` (Paths, Lines, Chrono, Map), gebaut in `SetUpBottomBar` (`:81-82`, `BarButton` `:169-177`). **Alle diese Knöpfe öffnen ein Popup** (`ToggleBarPopup` `:180`); `UpdateBarButtons` (`:211-215`) setzt `bar-button-active` aus `barPopupOpen && projectTab == tab`. Ein Umschalter ohne Popup passt dort nicht ohne eigenen Zustand: Der Knopf braucht einen eigenen Klick-Handler und seinen eigenen Aktiv-Zustand (Klasse `bar-button-active` wird heute ausschließlich aus dem Popup-Zustand gesetzt).
- Die Ansichtsschalter links (Routes, Textures, 3D) stehen in `EditorShell.uxml:37-51`; `ToggleThreeDimensional` (`EditorShellBootstrap.cs:898`), `SetTextures` (`:996`), `ShowPaths` (`EditorShellPaths.cs:48`) sind die Vorbilder für „Ansicht an/aus“ (Zustand im Shell, Renderer-Eigenschaft, Aktiv-Klasse, Statuszeile).
- Der Icon-Satz `LucideIcons.cs` kennt `mountain` (Zeile 48), `layers`, `waves`, `map`; für Höhenlinien passt `mountain` (neue Icons müssen sonst dort eingetragen werden, Lizenz in THIRD_PARTY_NOTICES).
- Die CS2-Screenshots (`UI Research/NZYEWvC.jpeg`) zeigen „Topographie“ zusätzlich als Schalter **im Optionsfeld des Straßenwerkzeugs** („Paket / Werkzeugmodus / Höhe / Parallelmodus / Einrasten / Topographie“). Gleiches Vorbild für MapWorks: Schalter in der Leiste und, bequem beim Straßenbau, ein Spiegel im Straßen-Fenster (`RoadToolPanel`).

### Wie das Gelände heute gezeichnet wird

- Daten: `OmsiTerrain` (`OmsiTerrain.cs:12`, 61 × 61 Höhen je Kachel, Spalte 0 = West, Zeile 0 = Süd, `HeightAt` bilinear `:55`); `OmsiTerrainSampler` (`OmsiTerrainSampler.cs:21`, liest je Kachel einmal, interpoliert über Kachelgrenzen, `TryGetHeight` meldet „kein Gelände“ statt 0).
- 2D und 3D teilen dieselbe Geometrie: `MapGroundRenderer.CreateTerrainMesh` (`MapGroundRenderer.cs:1760`) baut je Kachel ein Gitternetz 61 × 61 mit **echten Höhen als Y** und UV 0..1; `BuildTile` (`:1252`) hängt es an. Die Draufsicht ist eine orthografische Kamera auf Höhe 10000 m (`:30`, `SetView` `:727`), 3D dieselbe Kamera perspektivisch (`SetOrbitView` `:699`). Das heißt: **Die Höhe liegt in jedem Fragment bereits als Welt-Y vor**, ohne neue Daten.
- Bodenmaterial: Shader `MapGroundLayer.shader` (eine Ebene je Bodentextur, Maske, Lochmaske, Hangschattierung aus Normalen, Zeilen 76-89); Ebenen laufen in Queue 3000 + Index (`:1177`), Luftbilder in 3090 (`ImageryQueue` `:187`), Splines 3100/3200 (`:33, :37`), Wasser 3210 (`:1376`). In 2D schreibt der Boden keine Tiefe (`_DepthWrite`, `:1183`), in 3D die erste Ebene.
- **Vorbild für einen Überzug:** Luftbilder werden als eigene Fläche je Kachel gezeichnet, die **dieselbe Mesh** wie der Boden teilt (`AddImagerySurface` `MapGroundRenderer.cs:1654-1683`), unter eigenem Wurzelobjekt mit `SetActive` für Sichtbarkeit (`ImageryVisible` `:1610`). Nach Gelände-Änderungen tauscht `RefreshTerrain` (`:522-567`) die Mesh auf Boden, Maskenebenen **und** der Luftbildfläche aus (`:551-559`). Ein Höhenlinien-Überzug nach diesem Muster bekäme Live-Aktualisierung kostenlos, wenn man ihn in `RefreshTerrain` ebenfalls nachführt.
- Weitere Gelände-Berührpunkte, die der Überzug mitbekommen muss: `RefreshGround` (`EditorShellBootstrap.cs:6075`, nach Pinsel/Straßenanpassung/Undo), Aufruf aus `TickTerrain` (`:5726`, Pinsel in Schritten, hier alle ~Schritt sofort sichtbar), Wasser `RefreshWater` (`:1333`, blaue Fläche in 3210 über allem).
- Die Vektorebene (`MapCanvasElement`) hat ein 50-ms-Zeichenbudget (CLAUDE.md; Spandau liegt knapp daran). Jede Linienlösung in der Vektorebene belastet genau dieses Budget.
- Heute zeigt die Oberfläche die Geländehöhe nur beim Terrain-Pinsel (`EditorShellBootstrap.cs:5648`, `terrain.here_height`), sonst nirgends unter dem Zeiger. `MapCanvasElement.GroundHeight = groundRenderer.HeightAt` existiert (`EditorShellBootstrap.cs:847`).

### Gemessene Höhenbereiche (nur gelesen, `.terrain`-Dateien der OMSI-Installation)

| Karte | Kacheln mit Gelände | Min | Max | Spanne | größte Spanne in einer Kachel | mittlere Höhendifferenz je 5 m (Gitterschritt) |
|---|---|---|---|---|---|---|
| Grundorf | 19 | 0,00 m | 14,83 m | 14,8 m | 14,8 m | 0,05 m (rund 1 % Neigung) |
| Berlin-Spandau | 329 (nicht 766; so viele Terrain-Dateien liegen vor) | -12,52 m | 73,89 m | 86,4 m | 46,8 m | 0,11 m (rund 2,2 %); 2.905 Schritte über 2,5 m (Böschungen, Einschnitte), größter Schritt 30,8 m |
| HB_2026_Bremen-Nord | 192 | -14,84 m | 18,00 m | 32,8 m | 24,6 m | 0,09 m; größter Schritt 16,5 m |
| Stadtückheim, Bremenhaven_Cuxland | 164 bzw. 1 | 0 | 0 | 0 | 0 | völlig flach (keine Linien, Hinweis „Gelände flach“ nötig) |

Folgerungen: (1) Grundorf ist sehr flach, bei 1 m Abstand lägen die Linien im Mittel rund 100 m auseinander; brauchbar sind 0,5 m und 1 m, ein fester 5-m-Abstand wäre dort fast leer. (2) Spandau liefert 86 m Spanne, 5 m wären dort angemessen, nahe Böschungen sehr dicht. Der Linienabstand muss also **einstellbar bzw. automatisch** sein. (3) Zwei Flachkarten bestehen aus lauter Höhe 0: ein Shader, der „genau auf einer Linie“ als Linie zeichnet, würde dort **alles** einfärben; Flächen mit Höhenänderung 0 pro Pixel dürfen keine Linie bekommen (Pflicht, Test).
- **Kachelgrenzen:** Von 304 Ost-West-Nachbarpaaren in Spandau haben 18 an der gemeinsamen Kante verschiedene Höhen (bis 7,18 m); in Grundorf stimmen alle 12 überein. Der Boden selbst hat dort schon eine Stufe, die Höhenlinien enden also an diesen Kanten (richtig, nicht verbergen). Sonst sind sie nahtlos, weil beide Kacheln an der Kante gleiche Höhen haben.

### Gelände-Pinsel und Straßenbau

- Der Terrain-Pinsel (`OmsiTerrainBrush.cs`, Modi Raise/Smooth/Flatten …; `ApplyTerrainBrush` in `OmsiMapSessionTerrain.cs`) schreibt je Schritt (`TickTerrain`) und ruft `RefreshGround` auf; der Boden ist damit **live** sichtbar. Höhenlinien sollten dabei mitlaufen, sonst widerspricht die Ansicht dem Boden. Mit dem Mesh-Überzug entsteht das ohne Zusatzaufwand (siehe oben).
- Straßenbau: `OmsiRoadHeightMode` (FollowTerrain, Smooth, Manual; `OmsiRoadPlanner.cs:8`, `:144`), Warnschwelle für das Gefälle in `RoadToolPanel` (`gradientField` `:179`, `GradientWarningPercent` `:405`, Standard 10 %), Gefälle-Befunde im `OmsiRoadPlanner` (74 Treffer zu Gradient/Höhe) und `OmsiMapSummary.GradientPercentAt` (`:256`). Synergie: Linien zeigen **vor** dem Zeichnen, wo es steil ist (eng beieinander), der Planer meldet **danach** das Gefälle. Kein Eingriff in den Planer nötig. Optionale Erweiterung (später): Farbe der Linien/des Bandes nach Neigung statt Höhe, mit derselben Schwelle wie die Warnung.

## 2. Technische Optionen

### (a) Shader im Boden-Material oder als Überzug

Isolinien im Fragment-Shader über die interpolierte Welt-Höhe: `h = positionWS.y / Abstand`, Linie bei `abs(frac(h - 0,5) - 0,5) / fwidth(h)` mit Schutz gegen `fwidth(h) ≈ 0` (flach: keine Linie) und Ausblenden, wenn Linien dichter als ~2 px liegen (Böschungen: dann einfarbiger Hang-Ton statt Moiré). Hauptlinie jedes n-ten Abstands dicker/dunkler. Optionaler Farbverlauf nach Höhe als zweiter Term aus `_MinHeight/_MaxHeight`.

- Einbau: **eigener Überzug nach Luftbild-Muster**, nicht in `MapGroundLayer` (der wird je Bodentextur-Ebene wiederholt, hätte Linien mehrfach und müsste mit Masken verrechnet werden). Neuer Shader `MapContourOverlay` in `Assets/_Project/Shaders/Resources/`, **ein** Material für alle Kacheln (Welt-Y reicht, kein Textur-Parameter je Kachel), Queue zwischen Luftbild und Splines (Vorschlag 3095): Straßen decken Linien dann ab, Wasser (3210) tönt sie. Blend SrcAlpha/OneMinusSrcAlpha, ZWrite Off, ZTest LEqual, in 3D ein `Offset -1,-1` gegen Z-Kampf (Luftbild nutzt dieselbe Mesh ohne Offset, funktioniert dort).
- Qualität: Linien sind pixelscharf und im Bildschirmraum gleich dick bei jedem Zoom, in 2D und 3D identisch, nahtlos über Kachelgrenzen (gleiche Höhen), kombinierbar mit Texturen, Luftbild (Überzug liegt darüber) und ohne Texturen (liegt auf dem Hintergrund). Schwäche: Die Höhe ist je Dreieck linear; bei großem Zoom (5-m-Gitter) sind Linien leicht eckig, genau wie bei Marching Squares auf demselben Raster. Verbesserbar später durch bikubische Höhe aus einer R32F-Textur je Kachel (dann mit Nachbarkacheln an den Kanten).
- Beschriftung: nicht im Shader möglich. Ersatz siehe unten.
- Leistung: ein zusätzlicher Zeichenaufruf je sichtbarer Kachel (Spandau 329 × 7.200 Dreiecke = 2,4 Mio. Dreiecke bei voller Sicht, Kamera schneidet ab), kein CPU-Zeichnen, keine Auswirkung auf das 50-ms-Budget der Vektorebene.
- Aufwand: klein bis mittel. Build-Risiko: **gering bis mittel**. Neuer Shader in `Resources` wird mitgebaut (wie die drei vorhandenen); Varianten entstehen nur, wenn man `multi_compile`/Keywords nutzt, also keine verwenden, sondern Float-Uniforms (`_Interval`, `_Major`, `_Mode`). Pflicht: Prüfung im Player-Build per Screenshot (CLAUDE.md „Shader-Varianten im Build prüfen“).

### (b) Vektor-Isolinien (Marching Squares) in der Vektorebene

Reine Logik in `Editor.OMSI` (testbar ohne Unity), Polylinien je Kachel und Höhenstufe zwischengespeichert, in `MapCanvasElement` per Painter2D gezeichnet; Beschriftung als Labels entlang der Linie.

- Qualität: sauberste Linien, echte Beschriftung und Linienführung unabhängig vom Dreiecksraster möglich (Glättung). Kachelnähte brauchen Verbinden der Enden.
- Leistung: **kritisch**. Spandau liegt schon knapp am 50-ms-Budget; hunderte Polylinien mit Zehntausenden Segmenten je Neuzeichnen, Labels als Elemente. In 3D müssten die Linien projiziert gezeichnet werden (heute nur Auswahlrahmen und Pfade). Gelände-Pinsel erfordert Neuberechnung der Kachel je Schritt.
- Aufwand: hoch. Build-Risiko: gering (kein Shader), Leistungsrisiko hoch.

### (c) Vorab gerenderte Textur je Kachel

Je Kachel eine Textur mit Linien (z. B. 1024 × 1024) aus dem Raster, als Überzug.

- Qualität: bei Zoom unscharf (Linien wachsen mit), Texturgröße und Speicher (329 Kacheln × 4 MB), Abstand ändern oder Pinseln erzwingt Neuberechnung und Upload (8-ms-Upload-Budget, `UploadBudgetMilliseconds` `:40`). Keine Vorteile gegenüber (a).
- Aufwand: mittel. Nicht empfohlen.

### Beschriftung der Höhen

- Im Shader (a) keine Schrift. Empfohlener Ersatz mit kleinem Aufwand: **Höhe unter dem Zeiger in der Statuszeile**, solange Topologie an ist („Höhe 12,4 m, Gefälle zur Nachbarlinie …“ nur Höhe in Schritt 1), plus **Legende** im Popup/Panel (Linienabstand, Hauptlinie, Höhenbereich der Karte). Das ist beim Planen meist das, was man braucht (Höhe an der Stelle, an der man klickt). Echte Zahlen an den Linien wären (b) oder eine Erweiterung: Hauptlinien-Beschriftung als kleine Labels aus einer Marching-Squares-Auswertung **nur der Hauptlinien** und nur im Ausschnitt (reine Logik in `Editor.OMSI`, wenige Labels, Budget unkritisch). Das ist optional und kann nach Test von Schritt 1-3 als eigener Schritt folgen.
- Abstand abhängig vom Zoom: Der Shader blendet Linien aus, die dichter als ~3 px liegen; zusätzlich wählt eine reine Funktion `OmsiContourInterval.Pick(metersPerPixel, rangeMeters)` bei „Automatisch“ aus {0,25 0,5 1 2 5 10 20 50 m} den kleinsten Abstand, dessen Linien im Mittel mindestens ~16 px auseinander liegen, gegeben die mittlere Neigung der Karte; Hauptlinie = jede 5. Stufe.
- 3D: Der Überzug liegt auf der echten 3D-Fläche; Linien bleiben in Bildschirmbreite konstant und werden in der Ferne ausgeblendet (fwidth-Regel). Ergebnis: Berge werden lesbar. Böschungen (Steilwände) fallen in den einfarbigen Hang-Ton.
- Wasser: `RefreshWater` zeichnet blaue Flächen in Queue 3210 darüber; Linien unter Wasser erscheinen getönt, das ist gewollt (zeigt Ufer). Optional später: Wasserstand als eigene Linie hervorheben.
- Kombination: Texturen an/aus, Luftbild und 3D bleiben unabhängig, die Topologie ist ein eigener Schalter. Beim Ausblenden der Texturen liegen die Linien auf dem dunklen Kartenhintergrund (Linienfarbe hell genug, auf hellem Luftbild dunkel nötig: Linie mit dünnem Gegenton/Halo, vom Shader aus zwei Alpha-Bändern, Schritt 2 prüft beide Untergründe).

**Empfehlung: (a) als Überzug nach Luftbild-Muster**, Beschriftung vorerst über Statuszeile + Legende, echte Hauptlinien-Zahlen als spätere Erweiterung. Begründung: Live beim Pinseln ohne Zusatzaufwand, 2D und 3D aus einer Quelle, keine Belastung des 50-ms-Budgets, geringster Aufwand.

## 3. Entwurf der Bedienung

- **Knopf:** rechte Knopfgruppe der unteren Leiste, Position **zwischen Lines/Chrono und Map-Einstellungen** wie in der Skizze (vorletzter), Icon `mountain`, Tooltip „Topology: show contour lines on the terrain to plan roads“. Er ist ein Umschalter (Orange wenn an, wie `bar-button-active`), kein Popup; der Zustand ist von `barPopupOpen` getrennt zu halten (Schritt 3). Ohne Karte gesperrt; bei einer völlig flachen Karte nicht gesperrt, aber Status „Terrain is flat, no contour lines“.
- **Einstellungen:** Ein Rechtsklick auf den Knopf oder ein kleiner Pfeil öffnet ein kleines Popup über der Leiste (vorhandenes `bar-popup`) mit: Abstand (Automatisch, 0,25, 0,5, 1, 2, 5, 10 m), Darstellung (nur Linien / Linien + Farbband nach Höhe), Deckkraft der Linien, Höhenbereich der Karte als Legende (min/max aus allen geladenen Kacheln). Ein einfacher Klick schaltet nur ein/aus. Alternative: gar kein Popup in 1.2.0, nur „Automatisch“ (siehe Frage 2).
- **Straßen-Fenster:** Schalter „Topology“ als Spiegel des Knopfs (gleicher Zustand), wie in CS2 im Straßenwerkzeug.
- **Statuszeile:** bei aktiver Topologie Höhe unter dem Zeiger (`Height 12.4 m`), vorhandenen Mechanismus der Statuszeile und `HeightAt` verwenden; Übersetzungen in allen 8 Sprachen (`locales/<sprache>/barui.json` und passende Dateien; cs-CZ, de-DE, en-US, es-ES, fr-FR, it-IT, pl-PL, pt-PT).
- **Zustand:** nur in der Sitzung, nicht in der Projektdatei (Voreinstellung aus; optional später in `editor-settings.json`). Kommandozeile für Prüfläufe: `-topology [Abstand]` (Eintrag in `Documentation/CommandLine.md`).

## 4. Arbeitsplan

Jeder Schritt ist für sich baubar und prüfbar. Jeweils nur eigene Dateien stagen; Dokumentation mit jedem Commit (Roadmap-Häkchen, Architecture, ProjectStatus, DevelopmentSetup „Validierungsstand“, ADR 103, CommandLine).

1. **Reine Logik in `Editor.OMSI` (ohne Unity):** `OmsiContourInterval` (automatische Wahl aus Zoom und Höhenspanne, feste Stufenliste, Hauptlinie jede 5.), `OmsiTerrainRange` (min/max über Kacheln, „flach“ erkennen). Tests in `Assets/_Project/Tests/Editor/` (Grenzen: flache Karte, 0,05 m Neigung, 86-m-Spanne, NaN/negative Höhen). Prüfung: schneller csc-Lauf ohne Unity, dann EditMode im Scratchpad-Projektkopie-Verfahren.
2. **Shader + Renderer:** `MapContourOverlay.shader` (Float-Uniforms, keine Keywords), in `MapGroundRenderer`: Wurzelobjekt `Map Contours`, eine Fläche je Kachel mit gemeinsamer Mesh (wie `AddImagerySurface`), `ContourVisible`, `ContourInterval`, `ContourMode`; Nachführen in `RefreshTerrain` (Mesh tauschen) und Neuaufbau in `Clear`/Laden. Prüfung: Screenshots Grundorf (`-openMap … -view 0 -1 150 150 0.25 -topology`) und Spandau (`2402 11279 200 200 0.4`), in 2D mit und ohne Texturen, mit Luftbild, in 3D; Prüfpunkte: flache Fläche ohne Linie, Böschung ohne Moiré, Kachelnaht Grundorf glatt, Nahtstellen mit Höhenstufe in Spandau enden dort.
3. **Bedienung:** Knopf in `RightBarButtons`-Gruppe mit eigenem Handler (neue Datei `EditorShellTopology.cs`, `partial`), Aktivzustand getrennt vom Popup, Popup für Abstand/Darstellung/Legende, Spiegel im Straßen-Fenster, Statuszeile mit Höhe unter dem Zeiger, Übersetzungen in 8 Sprachen (Test auf fehlende Schlüssel/Tooltips läuft im Unity-Testlauf), Tastatur/Fokus des Knopfs wie die Nachbarn, schmale Fenster (`bar-compact`). Prüfung: Screenshots bei 1600 × 900 und unter 1400 px, `-language en-US` und eine weitere Sprache.
4. **Live-Pinsel und Undo:** Prüflauf mit Terrain-Werkzeug (Argumente laut `CommandLine.md`): Linien folgen dem Pinsel in `TickTerrain`, Undo/Redo und Straßenanpassung (`RefreshGround`) laufen mit. Messung: Bildzeit in Spandau vorher/nachher (Player-Log, 50-ms-Meldung der Vektorebene darf nicht neu auftreten), Messverfahren aus `PerformanceBaseline.md`.
5. **Build-Prüfung:** Player-Build im Hintergrund (ca. 3 Min, vorher ansagen) über Kopie des Projekts; Shader im Player (kein Rosa/kein Fehlen durch gestrichene Variante), `OmsiContentBoundary` und Player-Freigabeliste unverändert (nur eigener Shader). Danach alle bisherigen Screenshots aus dem Player wiederholen.
6. **Optional, nach Test durch den Projektinhaber:** Beschriftung der Hauptlinien (Marching Squares nur für Hauptlinien im Ausschnitt in `Editor.OMSI`, Labels in der Vektorebene), Farbe nach Neigung mit der Straßenbau-Warnschwelle, bikubisch geglättete Linien.
7. **Doku und Abschluss:** ADR 103, Roadmap-Häkchen, README „Current status“, Architecture, ProjectStatus, DevelopmentSetup, `CommandLine.md`; Test durch den Projektinhaber, danach Commit auf Verlangen; Wiki erst nach Release 1.2.0.

Risiken: Z-Kampf in 3D (Offset im Shader, Prüfpunkt in Schritt 2); Moiré an Steilkanten (Ausblendregel); Kontrast auf hellen Luftbildern (Halo); Shader fehlt im Build (Prüfpunkt Schritt 5); Knopf-Zustand kollidiert mit der Popup-Logik (`UpdateBarButtons`, Schritt 3).

## 5. Entscheidungsvorlage

1. **Darstellungsart.** (A) Nur Linien (dünn, Hauptlinie kräftiger). (B) Linien plus Farbband nach Höhe, per Einstellung zuschaltbar. (C) Nur Farbband. **Empfehlung: B**, Standard „nur Linien“, Farbband im Popup zuschaltbar; es kostet im Shader nur wenige Zeilen und hilft in flachen Karten wie Grundorf (0 bis 15 m).
2. **Linienabstand.** (A) Fest (z. B. 1 m). (B) Automatisch nach Zoom und Gelände. (C) Automatisch mit Einstellung im Popup (0,25 m bis 10 m). **Empfehlung: C**; Grundorf braucht 0,5 m, Spandau 5 m, ein fester Wert passt zu keiner von beiden.
3. **Beschriftung.** (A) Keine Zahlen an den Linien, nur Höhe unter dem Zeiger in der Statuszeile und Legende. (B) Zahlen an den Hauptlinien (eigener Aufwand, Schritt 6). **Empfehlung: A für 1.2.0**, B als Folgeschritt nach deinem Test; die Statuszeile deckt das Planen ab.
4. **In 3D.** (A) Linien auch in 3D. (B) Nur von oben. **Empfehlung: A**; der Überzug liegt auf derselben Fläche, der Mehraufwand ist null, und Hänge sind in 3D besonders gut zu beurteilen.
5. **Platz des Knopfs.** (A) Nur in der rechten Knopfgruppe (vorletzter Knopf laut Skizze). (B) Zusätzlich als Schalter im Straßen-Fenster (wie CS2). **Empfehlung: B**, gleicher Zustand; im Straßenbau ist er am nützlichsten.
6. **Live beim Geländepinsel.** (A) Linien laufen mit dem Pinsel mit. (B) Erst nach Loslassen. **Empfehlung: A**; der Überzug teilt die Mesh des Bodens und aktualisiert sich ohnehin bei jedem Schritt; B wäre extra Code, nur um das Verhalten zu verschlechtern.
7. **Merken der Einstellung.** (A) Nur in der Sitzung (beim Start aus). (B) In `editor-settings.json` merken. **Empfehlung: A**; mit der Zeit nachrüstbar.

## 6. Entscheidungen des Inhabers (22. November 2026)

| Nr. | Frage | Entscheidung | Folge |
|---|---|---|---|
| 1 | Darstellungsart | **B**: Linien, Hauptlinie kräftiger; Farbband nach Höhe im Popup zuschaltbar, Standard nur Linien | Shader: Uniform `_Band`, Hauptlinie `_MajorFactor` |
| 2 | Linienabstand | **C**: automatisch nach Zoom und Gelände, im Popup einstellbar 0,25 bis 10 m | `OmsiContourRules.PickInterval`, `ClampInterval` |
| 3 | Beschriftung | **A** (am 23. November 2026 bestätigt; der Inhaber hatte „4A“ doppelt geschrieben, Frage 3 wurde als A gewertet): keine Zahlen an den Linien, Höhe unter dem Zeiger in der Statuszeile und Legende | Schritt 3 |
| 4 | In 3D | **A**: Linien auch in 3D | gleiche Fläche, `Offset -1,-1` |
| 5 | Platz des Knopfs | **B**: rechte Leistengruppe, vorletzter Knopf laut Skizze, zusätzlich Spiegel-Schalter im Straßenwerkzeug, gleicher Zustand | Schritt 3 |
| 6 | Live beim Geländepinsel | **A** | Überzug in `RefreshTerrain` nachgeführt |
| 7 | Merken | **A**: nur Sitzung, beim Start aus | `OmsiContourSettings` nicht in der Projektdatei |

## 7. Stand der Umsetzung

- **Schritt 1 (22. November 2026):** Reine Logik `OmsiContourRules` (Höhenbereich, „flach“ unter 0,1 m Spanne, automatischer Abstand aus {0,25 0,5 1 2 2,5 5 10 m}, Hauptlinie jede 5., Klemmung) und `OmsiContourSettings` (Sitzungszustand) in `Scripts/OMSI`; `OmsiTerrain.TryGetRange` liest den Bereich je Kachel einmal. Tests `OmsiContourRulesTests`.
- **Schritt 2 (22. November 2026):** Shader `MapContourOverlay` und Überzug in `MapGroundRendererContours.cs` (partial), Prüfargument `-topology [auto|<Meter>] [band]`. Prüfwerte und Bilder: `.omc/handoffs/topology/step2/`, Ergebnisse in `DevelopmentSetup.md` („Validierungsstand Topologie Schritt 2“). Noch kein Knopf (Schritt 3).
- **Handtest Schritte 1 bis 3 (23. November 2026):** Der Projektinhaber hat alle 9 Punkte bestanden gemeldet: Knopf, Höhe, Popup, Listen-Popups, Straßenwerkzeug, Pinsel mit Undo, 3D, Stadtückheim flach, schmales Fenster. Noch nicht committet.
- **Schritt 3 (22. November 2026):** Bedienung in `EditorShellTopology.cs` (partial). Knopf `mountain` als vorletzter der rechten Gruppe, **nicht** in `barButtons` und damit unabhängig von `UpdateBarButtons`/`barPopupOpen` (die Aktiv-Klasse kommt aus `OmsiContourSettings.Enabled`); ohne Karte gesperrt, auf flacher Karte aktiv, das Einschalten sagt in der Statuszeile „This map is flat, so there are no contour lines to show.“, sobald das Gelände gelesen ist.
  - **Bedienung:** Linksklick schaltet um; **Rechtsklick** (oder Menütaste/Umschalt+F10 bei Tastaturfokus) öffnet das Einstellungs-Popup. Begründung: Im Projekt öffnen Rechtsklicks die Kontextmenüs (M5), und ein kleiner Pfeil am runden Knopf hätte die Leiste verbreitert (jetzt sind es fünf Knöpfe rechts, die Kompaktleiste ist bei 1100 px knapp). Der Tooltip nennt den Rechtsklick, die Statusmeldung beim Einschalten ebenfalls.
  - **Popup** (eigenes `floating-panel` rechts über der Leiste, schließt die Listen-Popups und umgekehrt, Esc schließt es): Abstand = „Auto“ plus feste Stufen 0,25 / 0,5 / 1 / 2 / 5 / 10 m (Auto ist der Schalter „Automatisch an/aus“: feste Stufe wählen = aus), Farbband-Schalter, Legende (aktueller Abstand und ob automatisch, Hauptlinie alle 5 Linien = n m, Höhenbereich der Karte, bei Farbband eine Skala mit Min/Max). Wer einen Abstand oder das Band wählt, schaltet die Linien damit ein. Die Legende liest sich alle 250 ms neu (der automatische Abstand folgt dem Zoom).
  - **Straßenwerkzeug:** Zeile „Topography“ mit Schalter `mountain` in `RoadToolPanel` (`TopographyChanged`/`ShowTopography`), gleicher Zustand wie der Knopf (5B).
  - **Statuszeile:** eigenes Feld links vom Werkzeug-Hinweis mit „Height 12.4 m“, solange die Linien an sind, in 2D und 3D; die Leinwand meldet den Zeiger nur auf Anforderung (`ReportGroundPointer`/`GroundPointerMoved`), der Text wird nur bei einer geänderten Zehntelmeter-Stufe neu gebildet, kein `Loc.T` je Mausbewegung. Außerhalb von Kacheln mit Gelände verschwindet das Feld (`MapGroundRenderer.TryHeightAt`).
  - **Sitzungszustand:** beim Start aus (7A), **bleibt über Kartenwechsel** in der Sitzung (`OmsiContourSettings` hängt am Renderer, `ClearContours` ändert die Einstellung nicht); nicht in der Projektdatei. Auf einer flachen Folgekarte bleibt der Knopf orange, das Popup sagt dort „flat“.
  - **Kopplung zur Leinwand:** `TickTopology` (jede Frame, billig) setzt `MapCanvasElement.ContourLinesBelow` aus `ContourShown` (also auch nach Kartenwechsel, flach oder nicht) und schaltet die Zeigermeldung nur bei eingeschalteten Linien ein.
  - Texte in `locales/*/topology.json` (22 Schlüssel, 8 Sprachen, Englisch maßgeblich, Deutsch selbst, die übrigen eigene Entwürfe); Prüfargumente `-topology` (nutzt jetzt den Knopfzustand), `-topologyPopup`, `-topologyHere` (siehe `CommandLine.md`). Ergebnisse und Bilder: `.omc/handoffs/topology/step3/`, `DevelopmentSetup.md` („Validierungsstand Topologie Schritt 3“).

## 8. Nachbesserung vom 23. November 2026

- **Befund Spandau:** Am Steilhang eines Pinselhügels (46 m auf 40 m Radius, 0,25 m Abstand, 0,4 m je Pixel) lagen alle Linien dichter als 3 px und wurden ausgeblendet; der Halo (4 px breit) färbte die Fläche trotzdem dunkel.
- **Variante 1 (Shader `MapContourOverlay`):** Linienstufen ×5 hoch (Grundabstand, ×5, ×25, ×125). Ist eine Stufe ausgeblendet (Bildschirmabstand unter 3 px, weich bis 6 px), wird die nächste Stufe zur Nebenlinie und die darüber zur Hauptlinie. Eine Stufe, die zwei Schritte gröber ist als eine voll sichtbare, wird nicht gezeichnet. Der Halo hängt je Stufe am eigenen Einblendfaktor, so entstehen keine dunklen Flächen. Flache Bereiche, Farbband und Variantenzahl (keine Keywords) unverändert; die Legende nennt weiter den Grundabstand.
- **Höhenanzeige:** Die Statuszeile las die Höhe nur bei Mausbewegung. `TickTopology` merkt jetzt den letzten Punkt (auch den aus `-topologyHere`) und liest ihn neu, sobald sich `TerrainVersion` ändert (Undo, Redo, Pinsel). Mit `-topologyHere` schreibt jede Neulesung die Zeile „Topology height: ...“ erneut ins Log.
