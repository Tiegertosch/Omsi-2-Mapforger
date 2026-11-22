# Objekt-Popup in der unteren Leiste: Bestandsaufnahme und Plan

Stand 22. November 2026, Zweig `feature/objects-popup` (von `main` c486e93). **Status: umgesetzt, getestet.** Die Schritte 0 bis 3 sind committet als `3e2de32` (Objekt-Liste), der Projektinhaber hat sie getestet (Öffnen und Scrollen in Berlin-Spandau flüssig). Der Merge nach `main` steht aus. Die Entscheidung steht in ADR 102 (`Decisions.md`). Die Entscheidungen des Projektinhabers stehen in Abschnitt 0, die Befunde von Schritt 0 und der Stand von Schritt 1 in Abschnitt 5; Schritt 1 (`OmsiPlacedObjectIndex`) sowie Schritt 2 (Knopf, Popup-Gerüst) und Schritt 3 (Liste, Suche, Gruppen, Klick; Abschnitt 5.5) sind gebaut und getestet. Offen sind die Nachschritte (angehängte Objekte und Reihen laut 2.5 und Entscheidung 8, „Alle dieser Art wählen“ laut Entscheidung 6, Zählung „In map“ laut 3.5; Roadmap). Auftrag des Projektinhabers: „Neben dem Kacheln-Popup soll noch ein Objekt-Popup dazukommen, damit weiterhin eine Liste existiert, wo man alle auf der Karte platzierten Objekte einsehen kann.“ Die Funktion gehört zu Release 1.2.0 (Roadmap, Abschnitt „UI-Rework: nach dem Rework und Release 1.2.0“, ADR 098 Abschlussvermerk, Entscheidung 3B).

## 0. Entscheidungen des Projektinhabers (22. November 2026)

| Frage (Abschnitt 4) | Entscheidung | Bedeutung für den Bau |
|---|---|---|
| 1 Gliederung | **A** | Nach Objektart (Dateipfad) mit Anzahl, aufklappbar zu den einzelnen Objekten; Umschalter „By tile“ und „Flat“ |
| 2 Klick | **A** | Klick wählt und zentriert, Popup bleibt offen; Strg+Klick ergänzt die Auswahl |
| 3 Splines | **A** | Nur Objekte, keine Splines; eine Liste „Roads“ später |
| 4 Suche | **A** | Suchfeld (Dateiname, Ordner, ID, Kachel) plus Herkunftsfilter Basisspiel/DLC/Fremd |
| 5 Knopf | **A** | Links neben dem Kachelknopf, Popup öffnet links |
| 6 „Alle dieser Art wählen“ | **B** | Nicht in Stufe 1 (später nach Messung) |
| 7 Reiter „In map“ | **A** | Bleibt im Objekte-Fenster |
| 8 Angehängte Objekte und Reihenobjekte | **A** | Nur `[object]`-Datensätze; Anhänge (`[attachObj]`) und Reihenobjekte stehen nicht in der Liste (ein späterer Nachschritt B bleibt möglich) |
| Entf (Zusatz 22. November 2026) | **Entf bleibt** wie überall | Entf mit Fokus in der Objektliste löscht die gewählten Objekte wie auf der Karte (globaler Kurzbefehl, einheitlich, per Strg+Z rücknehmbar); die Liste hat keinen eigenen Löschweg |

Alle Fragen sind entschieden.

## 1. Ist-Stand

### 1.1 Was es vorher gab, und was wegfiel

- Eine Liste der einzelnen platzierten Objekte gab es **nie**. Das Project-Fenster (ADR 078, Commit `2922d2f`) hatte die Reiter Tiles, Map, Entries (Einstiegspunkte), Chrono, Lines, Paths (`git show 1f1751e~1:…/EditorShellProjectTabs.cs`, Zeilen 17–25). Objekte standen dort nur als Zahl je Kachel („N obj / M spl“) und als Gesamtzahl im Kartenkopf (`UpdateCounts`, `EditorShellBootstrap.cs:9138`).
- Der Reiter **Entries** (alle Einstiegspunkte) entfiel am 17. November 2026 (ADR 079, Ergänzung; Funktionsmatrix Zeile 55); Ersatz ist die Suche im Objektbrowser. Auch das ist keine Liste der platzierten Objekte.
- Was am nächsten kommt, ist der Reiter **In map** des Objekte-Fensters (`ObjectLibraryPanel.cs:22`, `:143`, `Refresh` `:432–470`): Er listet die **Objektarten (.sco-Dateien)**, die die Karte benutzt, absteigend nach Anzahl (`Uses`, `:461`), mit Kacheln und Vorschau. Er kennt weder einzelne Objekte (IDs) noch Kacheln, und ein Klick startet das **Platzieren** dieser Art. Es ist ein Katalog, keine Liste zum Einsehen.
- Der Projektinhaber will also etwas Neues, kein Zurückholen. In der Skizze (`UI Research/Skitze.png`) gibt es keinen Objekt-Knopf; der Platz links neben der Pille ist frei.

### 1.2 Kachel-Popup (Vorbild)

- Knopf: `SetUpBottomBar` baut `BarButton(ProjectTab.Tiles, "layout-grid", "bar.tiles.tooltip")` mit Zahl-Abzeichen `tileCountBadge` in die Gruppe `bar-left`, danach die Projektpille (`EditorShellBottomBar.cs:47–54`). Die Knöpfe rechts stehen in `RightBarButtons` (`:30–34`).
- Öffnen: `ToggleBarPopup` (`:170`) → `ShowProjectTab` (`EditorShellProjectTabs.cs:46`) → `ShowBarPopup` (`EditorShellBottomBar.cs:179`) → `RenderBarPopup` (`EditorShellBootstrap.cs:8940`). Ein einziges Popup `bar-popup` (UXML `EditorShell.uxml:27`), der Inhalt hängt an `projectTab` (Enum `ProjectTab`: Tiles, Map, Chrono, Lines, Paths, `EditorShellProjectTabs.cs:17`). `RenderBarPopup` zeichnet nur, wenn das Popup offen ist, und öffnet es nie selbst.
- Seite und Größe: `BarPopupOnLeft(tab)` ist nur für Tiles wahr (`EditorShellBootstrap.cs:8931`), Klasse `bar-popup-tall` (480 px hoch, 320 px breit, `EditorShell.uss:159–161`). **Ein Test hält das fest:** `UiLayoutRulesTests.OnlyTheTilePopupOpensAtTheLeftEdge` (`UiLayoutRulesTests.cs:58`) und muss mit angepasst werden.
- Aufbau `TilesTab` (`EditorShellProjectTabs.cs:140`): Filterfeld (`panel-search-field`, Platzhalter `project.filter`) über einem `ScrollView` mit einem `Button` je Kachel (`browser-tile-entry`, Text „Tile x, y  N obj / M spl • edited“, `project.tile` in `ui2.json:63`). Filter nach Koordinaten („2,-1“) oder Dateiname (`TileMatches`, `:158`). Gewählte Kachel orange umrandet, fehlende rot, bearbeitete mit gelbem Rand.
- Klick: `SelectTile(tile, true)` (`EditorShellBootstrap.cs:5132`) wählt die Kachel ab, zeigt sie orange und zentriert (`MapCanvasElement.CenterOn(tile, null)`, `:1530`). `ApplySelection` rollt die Liste auf die gewählte Kachel (`ScrollBrowserTo`).
- Leistung: **nicht virtualisiert**. `FillTileList` legt bei jedem Neuzeichnen und jedem Tastendruck im Filter einen `Button` je Treffer an (Spandau: 369 Kacheln mit Datei, 766 laut CLAUDE.md). Das trägt bei Kacheln, bei 29.450 Objekten nicht (siehe 1.3). Im Code gibt es **kein** `ListView` (Suche in `Scripts/UI` ergibt keinen Treffer); das Objekt-Popup wäre die erste Verwendung.
- Kommandozeile: `-projectTab tiles|map|chrono|lines|paths` über `Enum.TryParse` (`EditorShellProjectTabs.cs:211–229`), `-tileFilter Text`. Ein neuer Enum-Wert wird von `-projectTab` automatisch erkannt.
- Texte: `locales/<Sprache>/barui.json` (`bar.*.tooltip`), `projecttabs.json` (`project.tab.*`, Filter, Leermeldung); 8 Sprachen, `LocaleCheck --strict`.

### 1.3 Datenquellen für platzierte Objekte

- `OmsiMapSummary.Tiles[i].Objects` ist `List<OmsiObjectRecord>` (`OmsiMapSummary.cs:49`, Typ `:69`): `AssetPath` (relativ zur Installation, z. B. `Sceneryobjects\Generic\bench.sco`), `Id`, `LocalX/LocalY/Height`, Drehwinkel, `Labels`, `VarParentId`. Kachel: `X`, `Y`, `FileName`, `ObjectCount`, `IsAvailable`. **Es gibt keinen kartenweiten Index** und keinen Anzeigenamen: der Name ist der Dateiname des `AssetPath`.
- Die Summary wird bei Platzieren/Löschen/Verschieben in Kacheln **am Platz** fortgeschrieben (`OmsiMapSession.cs:895–921` zählt `ObjectCount` mit); in einer Chrono-Ansicht/Variante enthalten die Kacheln die Überlagerung (siehe `RefreshAfterChange`, `EditorShellBootstrap.cs:5557–5565`). Die Liste liest also immer den aktuellen Stand von `currentSummary`, ohne eigene Kopie der Daten.
- Zählung heute: `UpdateObjectsCatalog` (`EditorShellBootstrap.cs:7626`) zählt in einem `OrdinalIgnoreCase`-Wörterbuch alle Objekte einmal je Aufruf (Spandau 29.450 Zugriffe; nach CLAUDE.md etwa 1,5 µs je Zugriff auf Mono, also grob 40 ms, **ungemessen**). Aufrufer: Öffnen des Objekte-Fensters, Platzieren, Kopieren, Beschriftungen, Einstiegspunkte, Straßenbauer. **Nicht** aufgerufen bei Löschen und Rückgängig; der Reiter „In map“ kann daher veralten (Befund, nicht Teil dieser Aufgabe; der neue Index sollte die Zählung später mitliefern).
- Herkunft Basisspiel/DLC/Fremd: `OmsiContentOrigins.Of(relativerPfad)` → `OmsiContentOrigin` (`OmsiContentOrigins.cs:9`, `:203`), bereitgestellt als `contentOrigins` (kommt aus dem asynchronen Installations-Scan `StartInstallationScan`, `:7650ff.`; bis dahin `null`). Der Aufruf normalisiert den Pfad (Kleinschreibung, Allokation): je **verschiedenem** Pfad einmal, nie je Objekt. DLC-Ausblenden (Settings) gilt nach `ObjectLibraryPanel.Hidden` für Bibliothekslisten, nicht für die eigenen Objekte der Karte.
- Mengen: Grundorf 1.095 Objekte, 60 Splines, 19 Kacheln; Berlin-Spandau 29.450 Objekte, 2.486 Splines, 369 Kacheln (`PerformanceBaseline.md:12`; dazu 9.747 Objekte in Reihen und 1.447 angehängte, beide **nicht** in `tile.Objects`, siehe 5.1).
- **Die 37.186 aus dem Absturzbericht sind keine platzierten Objekte**, sondern die .sco-Dateien einer fremden Installation im Objektbrowser (Memory „Object menu crash report“, Fehler beim Scrollen in der Bibliothek, offen, ohne Log). Sie zeigen aber den Auslegungsfall: Eine Benutzerkarte kann leicht fünfstellig viele Objekte haben, Spandau liegt schon bei 29.450. Wegen der Meldung sind bei der Liste **keine UI-Elemente je Objekt** erlaubt, nichts darf je Zeile Bilder oder Modelle laden, und kein Stapel an Zeichenketten je Objekt.
- Auswahl und Zentrieren: `SelectObject(tile, item, centerView)` (`EditorShellBootstrap.cs:5081`) wählt genau dieses Objekt, zeigt Kachel und Inspector-Daten und ruft `mapCanvas.CenterOn(tile, item)` (2D: Mitte auf dem Objekt, Maßstab höchstens 0,5 m/Pixel; 3D: Kamera auf das Objekt, Abstand höchstens 60 m; `MapCanvasElement.cs:1530–1548`). `ToggleObject` (Strg+Klick) ergänzt eine Mehrfachauswahl (`:5099`). `-select <ID…>` sucht lineare je Kachel (`:1209`), `TileHolding` prüft `Objects.Contains` (O(n), `:6602`): die Liste kennt die Kachel schon und umgeht beides. Ein offener Inspector zeigt die Auswahl ohne Zutun (`RenderInspector`).
- Aktualisierung nach Änderungen: `RefreshAfterChange` (`:5543`) ist der gemeinsame Weg für Platzieren, Löschen, Verschieben, Rückgängig und Wiederholen; er ruft `UpdateBrowserEntry` je sichtbarer Kachel und `UpdateCounts`, aber **nicht** `RenderBarPopup`. Kartenladen/Neuladen: `LoadMap`/`ReloadMap` (`:5075`); Chrono-Wechsel rufen `RenderBarPopup` (z. B. `:2247`, `:2559`).
- Suche und Auswahl sonst: Der Inspector zeigt Eigenschaften der Auswahl; es gibt keine Suche nach platzierten Objekten (nur `-select` und `-inspectorFind` auf der Kommandozeile).

### 1.4 Prüfargumente heute

`-openMap`, `-openProject`, `-view`, `-screenshot`, `-quit`, `-logFile`, `-language`, `-projectTab`, `-tileFilter`, `-select <ID…>`, `-inspector`, `-uiScale`, `-orbit` (3D) laut `Documentation/CommandLine.md`. Für das Objekt-Popup kommen neue Argumente hinzu (Abschnitt 2.7).

## 2. Entwurf

### 2.1 Knopf

- Neuer Wert `ProjectTab.Objects`. Der Knopf steht in der Gruppe `bar-left` **direkt rechts vom Kachelknopf, vor der Projektpille**, kein neues Layout nötig (`SetUpBottomBar`, `:47–54`). Form wie der Kachelknopf (`bar-tile-button`) mit Zahl-Abzeichen: Zahl aller Objekte, ab 10.000 gekürzt („29k“), damit das Abzeichen nicht wächst.
- Icon: Lucide `boxes` (neu in `LucideIcons.cs`, Pfade aus dem Lucide-Quelltext übernehmen, Lizenz steht schon in `THIRD_PARTY_NOTICES`). Vorhanden und bereits belegt sind `box` (Objekte-Werkzeug), `building-2` (Kategorie), `map-pin` (Reiter „In map“) und `layout-grid` (Kacheln); `boxes` ist von `box` gut zu unterscheiden. Fällt `boxes` weg, ist `list-ordered` der Ersatz.
- Tooltip `bar.objects.tooltip`: „Objects: every object placed on the map, with a search; click one to select it and show it.“ Ohne Karte gesperrt (`UpdateBarButtons`: `SetEnabled(loaded && ProjectTabAvailable(...))`, neuer Fall in `ProjectTabAvailable`: Karte offen). Fällt die Karte weg, schließt `RenderBarPopup` das Popup schon (`:8949`).
- Kompakte Leiste (< 1400 px): der Knopf nimmt den Stil der kleinen runden Knöpfe an wie der Kachelknopf; Prüfung bei 1100 × 700.

### 2.2 Popup-Inhalt

Gleiches Popup-Gerüst (`bar-popup`, links, `bar-popup-tall`, Breite bei Bedarf 360 px statt 320, weil Name, Kachel und ID in eine Zeile müssen). Von oben nach unten:

1. **Kopfzeile des Popups:** Titel „Objects“ (`project.tab.objects`), X zum Schließen (vorhanden).
2. **Suchfeld** (`panel-search-field`, gleiches Muster wie Kacheln): sucht in Dateiname, Ordnerteil des Pfads, ID (reine Zahl, ganz oder Anfang), Kachel („2,-1“). Mehrere Wörter = UND.
3. **Kleine Zeile mit Schaltern:** Gruppierung „By type | By tile | Flat“ (Reiter-Stil wie `panel-tab`), und die Herkunftsfilter Basisspiel/DLC/Fremd (nur wenn `contentOrigins` bekannt ist; sonst ausgeblendet). Zusammenfassung „1.095 objects in 214 types“ bzw. „37 of 1.095 match“.
4. **Liste** (`ListView`, virtualisiert, feste Zeilenhöhe 28 px wie `browser-tile-entry`). Die Daten sind eine **abgeflachte Zeilenliste** aus Gruppenzeilen und Objektzeilen:
   - *Gruppenzeile* (nach Objektart = `AssetPath`): Pfeil (auf/zu), Dateiname ohne Endung, Anzahl, Herkunftspunkt; Tooltip mit dem ganzen Pfad. Standard: zu; sortiert nach Anzahl absteigend, dann Name (wie „In map“).
   - *Objektzeile:* `#ID`, Kachel „x, y“, bei Bedarf der Ordner grau; gewählte Objekte orange umrandet (wie `browser-tile-entry-selected`), Objekt in nicht vorhandener Datei rot (nur wenn der Katalog es sicher weiß), bearbeitete Kachel gelber Rand links.
   - Bei „By tile“: Gruppe je Kachel („Tile 2, -1  ·  38 objects“), darunter die Objekte mit Namen. „Flat“: nur Objektzeilen nach Name, dann ID.
   - Mit Suchtext sind Gruppen mit Treffern automatisch offen (Zustand „von Hand zu“ geht dabei nicht verloren: eigene Menge `objectListCollapsedByFilter` nicht nötig, der Filter überstimmt nur die Anzeige).
5. **Leermeldungen:** „This map has no objects yet.“ / „No object matches.“ (`muted`, wie `project.no_match`).

### 2.3 Klickverhalten

- **Klick auf eine Objektzeile:** `SelectObject(tile, record, true)`: wählt, zentriert (2D und 3D), Inspector aktualisiert sich, Statuszeile nennt das Objekt. Das Popup **bleibt offen**, damit man die Liste durchgehen kann (wie das Kachel-Popup); Zeile bleibt markiert.
- **Strg+Klick:** `ToggleObject`, Mehrfachauswahl ohne Zentrieren.
- **Klick auf Gruppenzeile:** auf-/zuklappen. Zusätzlich ein kleiner Knopf „Select all“ an der Gruppe (Lucide `mouse-pointer-2` oder `check`), der alle Objekte der Art wählt (`AddToSelection`/`ApplySelection`); Obergrenze (z. B. 2.000, danach Meldung), denn eine Auswahl von 29.450 Objekten würde das Zeichnen und den Inspector belasten (Messung: 360 Objekte im Inspector 0,1 ms, 118 ok; 5.000 ungemessen). In Schritt 4 gemessen; fällt es durch, entfällt der Knopf.
- **Tastatur:** `ListView` bringt Pfeiltasten, Pos1/Ende und Bild auf/ab mit; Enter wählt, Rechts/Links klappt die Gruppe. Fokusring und Reihenfolge nach `EditorShellFocusRing.cs` prüfen. Esc: schließt zuerst das Popup, Reihenfolge in `EscapeOrderTests` und `EscapeOrder` nachlesen und nur ergänzen, wenn das Popup dort bisher nicht vorkommt.
- **Mit Rechtsklick auf eine Zeile:** nichts in dieser Stufe (das Kontextmenü gehört der Karte, `EditorShellContextMenu.cs`); später möglich.
- Verhalten in der 3D-Ansicht: `CenterOn` dreht die Kamera auf das Objekt (vorhanden).
- Nach Löschen eines gewählten Objekts (Entf auf der Karte) verschwindet seine Zeile mit der nächsten Aktualisierung.

### 2.4 Datenmodell und Leistung

- **Reine Logik in `Editor.OMSI`** (ohne Unity, schnell testbar): neue Klasse `OmsiPlacedObjectIndex` (Dateiname eigene `.cs`, Namespace `OmsiMapEditor.OMSI`). Baut aus `OmsiMapSummary` eine Liste `Entry { OmsiTileSummary Tile; OmsiObjectRecord Record; int TypeIndex; }` (Struct-Array, keine UI-Objekte) und eine Typentabelle `ObjectType { string AssetPath; string FileName; string LowerSearchText; int Count; OmsiContentOrigin Origin; }`.
- **Mono-Falle:** Die Typzuordnung je Objekt nicht über `OrdinalIgnoreCase`-Wörterbuch (gemessen 48 ms bei Spandau, 5.2), sondern über ein `Ordinal`-Wörterbuch mit dem Pfad, wie er in der Datei steht, und einen Treffer-Zwischenspeicher mit `ReferenceEquals` auf den zuletzt gesehenen Pfad. *Befund Schritt 1:* Der Leser gibt jedem Datensatz einen eigenen String, der Zwischenspeicher trifft beim Lesen nie; `Ordinal` kostet auf Mono etwa 0,6 µs je Zugriff. Schreibvarianten desselben Pfads (Groß-/Kleinschreibung) werden danach je Typ **einmal** zusammengeführt. Pfadnormalisierung und `contentOrigins.Of` je verschiedenem Pfad einmal.
- **Filtern** ohne Allokation je Objekt: erst die (wenigen hundert) Typen gegen den Text prüfen, dann ihre Objekte durchgehen; ID- und Kachelsuche vergleichen Zahlen statt Text. Ergebnis = Liste von Zeilenverweisen (`int`-Indizes), die `ListView` als `itemsSource` bekommt; Zeilentext wird erst in `bindItem` für die ~20 sichtbaren Zeilen erzeugt.
- **Neuaufbau-Zeitpunkt:** nur wenn das Popup offen ist (`ProjectTab.Objects` aktiv) und der Index veraltet ist. Ein „Veraltet“-Zähler wird in `RefreshAfterChange`, nach `LoadMap`/`ReloadMap`, nach Chrono-Wechseln und nach Undo/Redo erhöht (Änderungszähler der Sitzung wie bei `OmsiReferenceIndex`, ADR 057, wenn passend, sonst eigener). Beim Verschieben (Ziehen) ändert sich die Liste nicht, nur bei Platzieren, Löschen, Kachelwechsel und Typänderung; `RefreshAfterChange` kann beim Ziehen ohne Wirkung bleiben, sonst wird in 100 ms zusammengefasst (`schedule.Execute(...).ExecuteLater(100)`), nicht je Mausbewegung.
- **Aktualisieren ohne Neuaufbau der Liste:** gewählte Zeilen werden aus `selection` neu markiert (`RefreshObjects`), vergleichbar mit `SetTileHighlight`; Scrollen zur Auswahl (`ScrollToItem`) nur nach Klick auf die Karte, nicht nach Klick in der Liste.
- Veraltete Antworten: kein Hintergrundthread nötig, solange der Aufbau unter einem Bild bleibt (Ziel < 16 ms bei Spandau; Messung Schritt 4). Sonst Aufbau in Scheiben von je ~4 ms über `schedule` mit einer **Aufbau-Nummer**, die bei jedem neuen Auftrag steigt (Ergebnisse alter Nummern verwerfen), wie bei `installationScan`.
- Speicher: ein `Entry` je Objekt (24 Byte, 29.450 → < 1 MB); keine Kopie der Records; Index beim Schließen des Popups und beim Kartenwechsel verwerfen.

### 2.5 Splines, Straßen, Anhänge, Gruppen

- **Splines und Straßen: nein** in dieser Stufe. Die Aufgabe heißt „platzierte Objekte“; 2.486 Splines in Spandau haben keine sprechenden Namen, sie wählt man per Klick auf der Karte oder über Reihen/Kreuzung. Eine eigene Liste „Roads“ ist später gut möglich (Rolle: nach Straßentyp gruppieren) und wäre ein weiterer Knopf.
- **Angehängte Objekte** (`[attachObj]`, 1.447) und Reihenobjekte stehen **nicht** in `tile.Objects` (Befund 5.1). Stufe 1 listet nur `[object]`-Datensätze; ob sie dazukommen, ist Frage 8 an den Projektinhaber.
- **Einstiegspunkte, Haltestellen, Ampeln** sind gewöhnliche Objekte und stehen mit Dateinamen in der Liste. Eine Suche „entrypoint“ greift erst über das .sco (`IsEntryPointAsset` liest die Datei, nicht je Objekt, nur je Art, `EditorShellEntryPoints.cs:41`): optional, als eigener Schalter „Only entry points“ später.

### 2.6 Zustände

- **Ohne Karte:** Knopf gesperrt (wie die anderen Knöpfe), kein Popup.
- **Karte ohne Objekte:** Leermeldung, Suchfeld gesperrt.
- **Karte nur lesbar, Chrono-Datum, Variante bearbeiten:** Liste funktioniert (zeigt, was `currentSummary` enthält); Klick wählt, zentriert, ändert nichts. In der Variante zeigen die Kacheln die Überlagerung, die Liste folgt ihr durch das Neuzeichnen beim Wechsel (`RenderBarPopup`-Aufrufe in `EditorShellChronoDates.cs`, `ShowChrono`).
- **Laden/Scan:** Solange `contentOrigins` noch `null` ist, fehlen die Herkunftsschalter und -punkte; die Liste kommt trotzdem sofort. Wenn der Scan fertig ist, ruft er (`:7678`) einmal das Neuzeichnen, das den Index mit Herkunft ergänzt.
- **Ladefehler/Datei fehlt:** Kacheln ohne Datei (`IsAvailable` falsch) haben keine Objekte; keine Sonderbehandlung.
- **Doppelklick und Schnellklicks:** Klick auf Zeilen ist synchron, kein Zustand außer der Auswahl; zwei Klicks hintereinander wählen zweimal dasselbe Objekt. Navigation während eines Neuaufbaus: Aufbau-Nummer (2.4).
- **Sprache:** Neue Schlüssel in allen acht Sprachen (`locales/*/barui.json`, `projecttabs.json`), Zahlen mit `CultureInfo`/`Loc` formatieren; `LocaleCheck --strict` und `LocalizationKeyUsageTests`.

### 2.7 Kommandozeile für Prüfläufe (in `CommandLine.md` eintragen)

- `-projectTab objects` (ohne Zutun durch das Enum).
- `-objectFilter Text` (wie `-tileFilter`).
- `-objectGroup by-type|by-tile|flat`, `-objectOpenGroup <Dateiname|x,y>` (klappt eine Gruppe auf), `-objectClick <ID>` (wie ein Klick auf die Zeile: wählt und zentriert, nur für Prüfläufe).

### 2.8 Barrierefreiheit und Layout

- Echte Schaltflächen/`ListView` mit Tastatur, sichtbarer Fokus über die vorhandenen Fokus-Tokens, Tooltip an Knopf und Gruppen (voller Pfad).
- Schmale Fenster: Breite 94 % begrenzt (`.bar-popup max-width`), Zeile kürzt den Ordnerteil mit Auslassung (`text-overflow: ellipsis` in USS), die ID bleibt immer sichtbar.
- Farben nur über `--mf-*`-Tokens, neue Klassen `objlist-*` in `EditorShell.uss` (Reste `InlineSizeCeiling` 0 beachten: keine Inline-Größen ohne `// computed`; `UiLayoutRulesTests`, `UiTokenContrastTests`).

## 3. Arbeitsplan

Jeder Schritt in kleinem Commit, wenn der Projektinhaber es verlangt; Docs wandern mit (siehe 3.9). Zweig `feature/objects-popup`.

0. **Klärungen am Bestand (kein Code):** Stehen Reihen- und angehängte Objekte in `tile.Objects`? Wie lange braucht heute `UpdateObjectsCatalog` bei Spandau (Stoppuhr im Scratchpad-Programm mit `Editor.OMSI.dll`, Messkarte Spandau und Grundorf)? Liest `ListView` in Unity 6.6 im Player mit dem Projekt-Theme richtig (Scrollbar, Zeilenhöhe)? Kleiner Wegwerf-Versuch in der Projektkopie.
1. **Logik `OmsiPlacedObjectIndex` (Editor.OMSI, ohne Unity)** mit Tests (`Editor.Tests`): Gruppieren nach Art und nach Kachel, Filter (Name, Ordner, ID, Kachel, mehrere Wörter), Sortierung, Herkunftsfilter, Schreibvarianten desselben Pfads, leere Karte, Index nach Platzieren/Löschen (zwei Sitzungen mit Beispieldateien aus den vorhandenen Testhelfern) und Verwerfen. Schnell mit Roslyn csc und Reflection-Runner (siehe CLAUDE.md).
2. **Knopf und Popup-Gerüst:** `ProjectTab.Objects`, Knopf + Zahl-Abzeichen + Icon `boxes`, `ProjectTabAvailable`, `ProjectTabTitleKey`, `BarPopupOnLeft`/`bar-popup-tall` (Test `OnlyTheTilePopupOpensAtTheLeftEdge` anpassen: Tiles und Objects), Texte in 8 Sprachen. Neue Partial-Datei `EditorShellObjectList.cs` (nicht in `EditorShellBootstrap.cs`).
3. **Liste, Suche, Gruppen, Klick:** `ListView`, Zeilen, Gruppenzeilen, Suchfeld, Schalter Gruppierung/Herkunft, Klick/Strg+Klick/Tastatur, Markierung der Auswahl, `-objectFilter`, `-objectGroup`, `-objectOpenGroup`, `-objectClick`.
4. **Aktualisierung und Leistung:** Veraltet-Zähler in `RefreshAfterChange`, `LoadMap`, Chrono, Undo/Redo; Aufbau-Nummer; Messung Öffnen, Filtern, Gruppieren und Neuaufbau bei Spandau; „Select all“ messen oder weglassen.
5. **Prüfungen:** siehe 3.1 bis 3.6.
6. **Docs:** 3.9.

### 3.1 Tests

- EditMode (Projekt direkt, Editor geschlossen, sonst Projektkopie im Scratchpad): neue Tests für den Index; `UiLayoutRulesTests` (Popup links, Zeilenhöhe, keine Inline-Größen); `LocalizationKeyUsageTests`; `LocaleCheck --strict` (8 Sprachen); `BottomBarLayoutTests` (Leiste mit zusätzlichem Knopf nicht breiter als vorgesehen). Erwartet: alle bisherigen 1.400+ Tests grün.
- Build im **Hintergrund** starten und vorher ansagen (~3 Min), danach `RecordReviewedPlayerAssets`, falls die Prüfliste meldet.

### 3.2 Screenshots (Player-Build, `-language en-US`, 1600 × 900 und 1100 × 700, danach `-uiScale 1.5`)

- Grundorf `-openMap …/Grundorf/global.cfg -view 0 -1 150 150 0.25 -projectTab objects` (Gruppen zu), mit `-objectOpenGroup` (aufgeklappt), mit `-objectFilter bench`, mit `-objectClick <ID>` (gewähltes Objekt orange, Mitte der Ansicht), mit `-objectGroup by-tile`, `de-DE` (längere Texte).
- Berlin-Spandau `-view 2402 11279 200 200 0.4 -projectTab objects` (Gruppen zu, Anzahl, Abzeichen „29k“) und ein Filterbild.
- Testprojekt im Scratchpad (`.omproj`, Arbeitskopie neben der Datei, Original bleibt unberührt): ein Objekt platzieren und löschen, das Popup bleibt offen; Bild vorher/nachher (per `-place` und `-deleteSelection`, falls vorhanden; sonst im Handtest).
- Kartenansicht 3D (`-orbit`) mit Klick auf ein Objekt.
- Nicht per Standbild prüfbar, Handtest des Projektinhabers: alle sieben Handtest-Punkte vom Projektinhaber am 22. November 2026 bestätigt (Öffnen und Scrollen in Spandau, Klick, Strg+Klick, Tastatur inkl. Esc, Suche/Herkunft/Gliederung, Karte zur Liste, Änderungen bei offener Liste).

### 3.3 Messung Spandau (Player-Log, Zeilen „Object list: …“, Stoppuhr wie bei „Inspector placement:“)

Index bauen, erstes Öffnen des Popups bis zum ersten Bild, Filtern (Tippen einer Buchstabenfolge, je Tastendruck), Gruppe auf- und zuklappen, Neuaufbau nach Platzieren/Löschen. Grenzwerte: Öffnen und Filtern je < 50 ms (Grenze der Vektorebene für Neuzeichnungen, CLAUDE.md), Neuaufbau im Hintergrund < 16 ms je Scheibe. Dazu Speicher vor/nach (GC-Zähler) und längstes Bild beim Öffnen. Vergleich Grundorf.

### 3.4 Rückwirkungen prüfen

Kachel-Popup unverändert (Screenshot `-projectTab tiles`, Test grün), Objekte-Fenster/Reiter „In map“ unverändert, Esc-Reihenfolge (`EscapeOrderTests`), Inspector-Platzierung bei offenem Popup (`KeepSelectionClearOfObjectsWindow`: das Objekte-Fenster sitzt rechts, das neue Popup links).

### 3.5 Offene Punkte außerhalb der Aufgabe

Reiter „In map“ veraltet nach Löschen/Rückgängig (`UpdateObjectsCatalog` wird dort nicht gerufen); nach der Funktion sollte die Zählung aus dem neuen Index kommen (eigener Schritt, kleiner Eingriff, vorher mit dem Projektinhaber klären).

### 3.9 Dokumente (mit jedem Commit)

`README.md` (Abschnitt „Current status“), `Architecture.md` (Index, Popup), `Roadmap.md` (Häkchen „Objekt-Popup“), `DevelopmentSetup.md` (Validierungsstand), `CommandLine.md` (neue Argumente), `ui-rework-feature-matrix.md` (neue Zeile unter „Objekte“), `Decisions.md` **ADR 102** „Objekt-Popup in der unteren Leiste“ (Entscheidungen aus Abschnitt 4), `ProjectStatus.md`. Wiki erst nach Release 1.2.0.

## 4. Entscheidungsvorlage für den Projektinhaber

**Frage 1: Wie ist die Liste gegliedert?**
- A) Nach Objektart (Dateiname) mit Anzahl, aufklappbar zu den einzelnen Objekten, dazu Umschalter „By tile“ und „Flat“.
- B) Nur eine flache Liste aller Objekte, sortiert nach Name.
- C) Nach Kachel gegliedert (wie das Kachel-Popup, nur mit Objekten darunter).
- *Empfehlung:* A. Bei einer Karte mit zehntausenden Objekten ist die flache Liste unbrauchbar, und die Art ist die Frage, die man stellt („wo steht überall die Bank?“). B und C bleiben über den Umschalter erreichbar.

**Frage 2: Was tut ein Klick auf ein Objekt?**
- A) Auswählen **und** auf die Karte zentrieren (wie beim Klick auf eine Kachel); Popup bleibt offen. Strg+Klick ergänzt die Auswahl.
- B) Nur auswählen, nicht zentrieren.
- C) Auswählen, zentrieren und das Popup schließen.
- *Empfehlung:* A. Der Zweck der Liste ist, ein Objekt zu finden; Zentrieren macht es sichtbar, und die offene Liste erlaubt das Durchgehen.

**Frage 3: Kommen Straßen (Splines) in dieselbe Liste?**
- A) Nein, nur Objekte; eine spätere eigene Liste „Roads“.
- B) Ja, als zweite Gruppierung oder Abschnitt „Roads“ nach Straßentyp.
- *Empfehlung:* A. Der Auftrag nennt Objekte; Splines haben keine Namen, sind mit 2.486 Stück in Spandau besser über die Karte zu greifen, und ein Knopf je Liste bleibt übersichtlich.

**Frage 4: Wie wird gesucht und gefiltert?**
- A) Ein Suchfeld (Dateiname, Ordner, ID, Kachel) plus Schalter nach Herkunft (Basisspiel/DLC/Fremd).
- B) Nur das Suchfeld.
- C) Suchfeld plus Auswahl nach Objektkategorie (Gebäude, Bäume, …) wie in der Bibliothek.
- *Empfehlung:* A. Die Herkunft kostet fast nichts (liegt je Art vor) und beantwortet „welche Objekte brauchen DLC?“; Kategorien gibt es schon im Katalog, doch nur für Dateien der Installation, nicht für fehlende.

**Frage 5: Wohin kommt der Knopf?**
- A) Links, direkt neben dem Kachelknopf (Popup öffnet links, wie die Kacheln).
- B) Rechts bei den Listen Pfade/Linien/Chrono/Karte (Popup rechts).
- *Empfehlung:* A. Der Auftrag sagt „neben dem Kacheln-Popup“; Kacheln und Objekte sind beide Fragen nach dem Inhalt der Karte, und die rechte Gruppe gehört den Fahrplan- und Karten-Listen (Skizze).

**Frage 6: „Alle dieser Art wählen“ an der Gruppe?**
- A) Ja, mit Obergrenze (2.000), sofern die Messung in Spandau es trägt.
- B) Nein, erst später.
- *Empfehlung:* B für die erste Stufe, dann nach der Messung nachziehen. Es ist ein neues Verhalten (Massenauswahl, auch Entf trifft sie), das der Auftrag nicht verlangt.

**Frage 7 (klein): Was wird aus dem Reiter „In map“ im Objekte-Fenster?**
- A) Bleibt (Katalog zum Platzieren).
- B) Entfällt, weil das Popup die Karte zeigt.
- *Empfehlung:* A. Er dient dem Platzieren der schon benutzten Arten, das Popup dem Finden; nur die veraltete Zählung wird später behoben.

**Frage 8 (aus Schritt 0): Gehören angehängte Objekte und Reihenobjekte in die Liste?** Beide stehen nicht in `tile.Objects` (5.1).
- A) Nein, Stufe 1 listet nur `[object]`-Datensätze; angehängte Objekte und Reihen greift man wie bisher über die Karte bzw. das Objekt, an dem sie hängen.
- B) Angehängte Objekte (`[attachObj]`, eigene ID und eigener Pfad, Spandau 1.447) kommen als eigene Einträge dazu; ein Klick wählt sie wie auf der Karte (eigener Auswahlweg für `OmsiAttachedObjectRecord`, Zentrieren auf den Aufhängepunkt). Reihen bleiben draußen.
- C) Wie B, dazu je Reihe (`[splineAttachement]`, Spandau 1.073 Datensätze für 9.747 Objekte) ein Eintrag „Row“, der die Reihe wählt (`SelectRow`); die einzelnen Reihenobjekte haben keine ID und werden nicht gelistet.
- *Empfehlung:* A für Stufe 1, B als kleiner Nachschritt. Die Liste ist dann genau „was als `[object]` auf der Karte steht“, die Zahl stimmt mit dem Kachel-Popup und `ObjectCount` überein. Angehängte Objekte brauchen einen zweiten Auswahl- und Zentrierweg; Reihen sind Spline-Zubehör (Frage 3 schließt Splines aus), und ihre Objekte haben keine IDs. B ließe sich als zweite Eintragsart ergänzen, ohne die Zeilenlogik umzubauen.

## 5. Befunde Schritt 0 und Stand Schritt 1

### 5.1 Reihenobjekte und angehängte Objekte (Schritt 0)

- **Code:** `OmsiMapReader` legt nur `[object]` in `tile.Objects` ab. `[attachObj]` geht nach `tile.AttachedObjects` (`OmsiAttachedObjectRecord`: eigene ID, eigener `AssetPath`, `ParentId`, Aufhängepunkt; erst ab `[version]` 12), `[splineAttachement]` und `[splineAttachement_repeater]` nach `tile.SplineAttachments` (`OmsiSplineAttachmentRecord`: ein Datensatz je Reihe mit Abstand und Reichweite). Die einzelnen Reihenobjekte entstehen erst beim Zeichnen über `OmsiSplineAttachmentLayout.Place` und haben keine eigene ID. Gezählt wird getrennt (`ObjectCount` bzw. `AttachmentCount`).
- **Lauf gegen die echten Karten** (Sonde gegen `Editor.OMSI.dll`, nur gelesen, Original unverändert):

| Karte | `tile.Objects` (= `ObjectCount`) | `AttachedObjects` | `SplineAttachments` (Datensätze) | Reihenobjekte nach `Place` | IDs von Anhängen/Reihen auch in `tile.Objects` |
|---|---|---|---|---|---|
| Berlin-Spandau | 29.450 | 1.447 | 1.073 | 9.747 | 0 |
| Grundorf | 1.095 | 22 | 15 | 79 | 0 |

  `AttachmentCount` ist bei Spandau 2.520 = 1.447 + 1.073. Die 29.450 enthalten also weder Anhänge noch Reihenobjekte.

### 5.2 Laufzeit `UpdateObjectsCatalog` (Schritt 0)

Gemessen wurde dieselbe Zählschleife (`OrdinalIgnoreCase`-Wörterbuch über alle `tile.Objects`, ohne `SetCatalog`) in einer Sonde gegen `Editor.OMSI.dll`, ausgeführt mit Unitys Mono (`Editor/Data/MonoBleedingEdge/bin/mono.exe`, dieselbe Laufzeit wie der Player mit Mono-Backend), sechs Läufe nach dem Lesen der Karte: **Berlin-Spandau 47–52 ms** (872 Arten), Grundorf 0,3 ms (108 Arten). Unter .NET Framework läuft dieselbe Schleife in 8 ms; der Unterschied ist der Mono-Vergleicher. Die Schätzung aus 1.3 ist bestätigt; die Zählung allein liegt bei Spandau an der 50-ms-Grenze. Kein Wegwerf-Code im Projekt, keine Player-Messung (`SetCatalog` und das Neuzeichnen des Fensters sind nicht enthalten). Spandau hat mit `Ordinal` und `OrdinalIgnoreCase` gleich viele Pfade (872), also keine Schreibvarianten.

### 5.3 `ListView` (nur notiert, Bau in Schritt 3)

- Unity 6.6 bringt `ListView` mit `virtualizationMethod = FixedHeight` und `fixedItemHeight`; Zeilen werden über `makeItem`/`bindItem` wiederverwendet (nur die sichtbaren ~20), `itemsSource` kann die `List<OmsiPlacedObjectRow>` des Index sein, `RefreshItems()` nach neuem Filter, `ScrollToItem(index)` für die Auswahl.
- Theme: `EditorShell.uss` gestaltet die Scroller global (`.unity-scroller--vertical`/`--horizontal`, Zeilen 830–839, 8 px, Tokens); `ListView` benutzt intern einen `ScrollView` mit denselben Klassen, die Scrollbar sollte also ohne neue Regeln wie in den übrigen Listen aussehen. Zu prüfen in Schritt 3: Zeilenhöhe 28 px als `fixedItemHeight` mit der UI-Skalierung (`-uiScale`), Auswahlfarbe von `ListView` (`.unity-collection-view__item--selected`) über Tokens überschreiben oder `selectionType = None` mit eigener Markierung, Fokusring (`EditorShellFocusRing.cs`), `InlineSizeCeiling` (Zeilenhöhe als `// computed` oder in USS).
- Bisher gibt es im Projekt kein `ListView`; ein Wegwerf-Versuch im Player wurde nicht gemacht.

### 5.4 Stand Schritt 1: `OmsiPlacedObjectIndex`

Neue Datei `Assets/_Project/Scripts/OMSI/OmsiPlacedObjectIndex.cs` (Namespace `OmsiMapEditor.OMSI`, ohne Unity), Tests `Assets/_Project/Tests/Editor/OmsiPlacedObjectIndexTests.cs` (11). Unkommittiert.

- `OmsiPlacedObjectIndex.Build(IReadOnlyList<OmsiTileSummary> tiles, Func<string, OmsiContentOrigin> originOf = null)`: ein `OmsiPlacedObjectEntry` (Struct: `Tile`, `Record`, `TypeIndex`, `TileIndex`) je `[object]`, keine Kopie der Records. `Types` (`OmsiPlacedObjectType`: `AssetPath` in der ersten Schreibweise, `Key` klein mit `\`, `FileName`, `Folder`, `Count`, `Origin?`), absteigend nach Anzahl, dann Name; `Tiles` (nur mit Objekten, nach X, dann Y). Schreibvarianten (Groß/Klein, `/` gegen `\`) werden eine Art.
- Herkunft: `originOf` je Art einmal, darf fehlen (Scan noch nicht fertig); `ResolveOrigins(originOf)` ergänzt sie später ohne Neuaufbau, `HasOrigins`. Ein Herkunftsfilter lässt Arten ohne bekannte Herkunft nicht durch.
- `BuildRows(OmsiPlacedObjectFilter filter, OmsiPlacedObjectGrouping grouping, ICollection<string> expandedGroups)` → `OmsiPlacedObjectRows` (`Rows` als `List<OmsiPlacedObjectRow>` mit `Kind` Group/Object, `Group`, `Entry`, `Count`, `Expanded`; `MatchCount`, `GroupCount`). Gruppen ohne Treffer fallen weg; mit Suchwörtern sind alle Gruppen mit Treffern offen, sonst die in `expandedGroups` (Schlüssel `GroupKey`: `Key` der Art bzw. „x,y“, überdauert einen Neuaufbau).
- Filter `OmsiPlacedObjectFilter(text, origins)`: Wörter mit UND; ein Wort trifft einen Teil des Pfads (Name oder Ordner, ohne Groß/Klein, `/` = `\`), den Anfang der ID (Zahlvergleich ohne String) oder die Kachel „x,y“ (auch „x, y“). Pfadtreffer werden je Art geprüft, nicht je Objekt.
- Sortierung stabil: Art → Kachel → ID; Kachel → Name → ID; flach Name → ID → Kachel; gleiche Schlüssel in Dateireihenfolge. Je Gruppierung einmal sortiert (64-Bit-Schlüssel mit `Array.Sort`, auf Mono deutlich schneller als ein Vergleicher: erste Zeilen bei 40.000 Objekten 9 statt 24 ms) und gemerkt.
- `EntryOf(record)`: Eintrag eines Records (für „zur Auswahl rollen“), −1 nach Löschen.
- Fortschreibung: Der Index wird nach Änderungen neu gebaut (er kopiert nichts und merkt sich keinen Stand); der Test platziert und löscht über `OmsiMapSession` und baut neu.
- **Messung** (Unitys Mono): Berlin-Spandau Aufbau 21–28 ms, erste Zeilen „By type“ 6–10 ms, Filter „baum“ 1,5–2,8 ms. Synthetische Karte mit 40.000 Objekten, 500 Arten, 400 Kacheln: Aufbau 24–26 ms, erste Zeilen 9 ms, Filter je 8 ms (einschließlich der ersten flachen Sortierung), „Flat“ und „By tile“ zum ersten Mal zusammen 11–13 ms; unter .NET Framework Aufbau 5 ms. Der Test prüft grobe Obergrenzen (Aufbau < 500 ms, Filter < 200 ms, erste andere Gruppierungen < 1 s). Der Aufbau liegt bei Spandau über dem Ziel von 16 ms aus 2.4 (ein `Ordinal`-Zugriff je Objekt, etwa 0,6 µs auf Mono). Für Schritt 4: Aufbau nur beim Öffnen und nach Änderungen, nicht je Tastendruck (Filtern braucht keinen Neuaufbau); messen, ob ein Bild mit ~25 ms beim Öffnen stört, sonst in Scheiben bauen.
- Grenze: Der Sortierschlüssel nimmt Arten × Kacheln bis 2^31 auf (heute Spandau 872 × 369).
- Prüfung: schneller csc-Lauf der neuen Tests 11 von 11 grün (.NET Framework und Unitys Mono); EditMode 1448 von 1448 grün (1437 + 11, Projekt direkt, Editor geschlossen).

### 5.5 Stand Schritte 2 und 3: Knopf, Popup und Liste (22. November 2026, unkommittiert)

- **Knopf:** `ProjectTab.Objects`; runder Knopf (`bar-tile-button`, Lucide `boxes` aus lucide-static 0.454.0) in `bar-left` direkt rechts vom Kachelknopf, Abzeichen mit der Zahl aller `tile.Objects` (ab 10.000 „29k“, `ObjectBadgeText`), ohne Karte gesperrt (`UpdateBarButtons`). Das Popup öffnet links (`BarPopupOnLeft`: Tiles und Objects), 480 px hoch, 360 px breit (`bar-popup-wide`); in der kompakten Leiste (< 1400 px) ist der Knopf 36 px groß (`.bar-compact .bar-tile-button`), damit die Werkzeugleiste bei 1100 px nicht schmaler wird als vorher. Test umbenannt: `OnlyTheTileAndObjectPopupsOpenAtTheLeftEdge`.
- **Inhalt** (neue Datei `EditorShellObjectList.cs`): Suchfeld, Schalter „By type | By tile | Flat“ (`panel-tab`), Herkunftsschalter „Base game | DLC | Other“ (erst sichtbar, wenn der Installations-Scan fertig ist; „Other“ = Fremdinhalt und Unbekannt), Summenzeile („1,095 objects in 108 types“ bzw. „Showing 6 of 1,095 objects“), virtualisierte `ListView` (`fixedItemHeight` 28, `selectionType` Single als Tastaturmarke, Zeilen ohne Bilder). Gruppenzeile: Pfeil, Name, Anzahl, Herkunftspunkt; Objektzeile je nach Gliederung `#ID` und Kachel, Dateiname und `#ID`, bzw. flach Dateiname und `#ID · Kachel`; Tooltip der Zeile = voller Pfad. Das Objekt der Auswahl hat orange Rand (`objlist-row-selected`), die Tastaturmarke der Liste die Hover-Farbe; keine Unity-Standardfarben.
- **Klick:** Objektzeile = `SelectObject(tile, record, true)` (wählt und zentriert, Popup bleibt offen), Strg+Klick = `ToggleObject`, Gruppenzeile klappt auf/zu (mit Suchtext sind Gruppen mit Treffern immer offen, ein Klick tut dann nichts). Tastatur: Pfeile, Pos1/Ende, Bild auf/ab sind die der `ListView`; Enter wählt (Strg+Enter ergänzt), Rechts öffnet, Links schließt die Gruppe bzw. springt zur Gruppenzeile; Pfeil ab im Suchfeld geht in die Liste; Esc schließt das Popup (vorhandener Weg `CloseTopOverlay`, auch aus dem Suchfeld). Nach einer Auswahl in der Karte rollt die Liste zur Zeile, wenn sie offen und in der Liste ist (nur dann, nicht nach Klick in der Liste).
- **Lebensdauer:** Der Index wird beim Öffnen (`ObjectsTab`) und 100 ms nach der letzten Änderung (`ScheduleObjectListRebuild` in `RefreshAfterChange`, nur bei offener Liste, zusammengefasst) gebaut und beim Schließen oder Wechsel des Reiters verworfen (`ReleaseObjectList`). Aufklapp-Zustand, Suchtext, Gliederung, Herkunftsschalter und Scrollposition bleiben über Neuzeichnen und Schließen erhalten und beginnen mit einer anderen Karte neu. Die Herkunft kommt nach dem Scan über `OnObjectOriginsKnown` ohne Neuaufbau.
- **Befund `ListView` in Unity 6.6:** Die Liste hat keine öffentlichen `scrollView`/`scrollOffset` (der `ScrollView` kommt über `view.Q<ScrollView>()`), und die Wurzel von `makeItem` ist **selbst** das Listenelement (Klassen `unity-collection-view__item`, `unity-list-view__item`): Eigene Regeln mit weniger Spezifität als `.objlist-list .unity-collection-view__item` greifen dort nicht. Darum ist die Zeile nur der Klickfänger (`objlist-row`), die „Karte“ darin (`objlist-card`, 1 px Rand oben und unten) trägt das Aussehen. Eine frisch gebaute Liste hat noch keine Größe (`layout.height` ist NaN, Inhalt 0): Scroll-Positionen werden darum sofort, nach 1/40/120 ms und bei jedem Layout innerhalb von 0,3 s gesetzt (`ScrollObjectList`).
- **Texte:** neue Datei `objectlist.json` (22 Schlüssel) sowie `bar.objects.tooltip` und `project.tab.objects`, in allen acht Sprachen; `LocaleCheck --strict` 8 Sprachen, 2849 Schlüssel, 0/0. Französisch, Italienisch, Spanisch, Portugiesisch, Polnisch und Tschechisch sind eigene Entwürfe in der Art der vorhandenen Dateien („tile“ bleibt in Listen stehen).
- **Prüfargumente** (`CommandLine.md`): `-projectTab objects`, `-objectFilter`, `-objectGroup type|tile|flat`, `-objectOpenGroup`, `-objectClick`, dazu `-objectType` (Buchstabe für Buchstabe tippen) und `-objectOrigins`.
- **Messung** (Player, Mono, Berlin-Spandau, 1600 × 900, Log „Object list: …“): Öffnen kalt 67–85 ms insgesamt (Index 26–38 ms, Zeilen 16–19 ms, der Rest sind das erste Erzeugen von Feld, Tabs und `ListView`); ein zweites Zeichnen im selben Lauf (warm) 28 ms (Index 17–21 ms, Zeilen 6–9 ms). Grundorf 26 ms kalt, 1 ms warm. Jeder Buchstabe im Suchfeld („baum“, „shrubbery“): 3–8 ms einschließlich der Neuzuweisung an die `ListView`. Gruppe aufklappen 2 ms (Zeilen) plus Neuaufbau der `ListView`. Das erste Öffnen liegt damit über dem Ziel von 50 ms (ein Bild mit rund 70–85 ms, vor allem der Index); der Aufbau in Scheiben (2.4) ist als Nachbesserung offen, falls der Handtest es als Ruckeln meldet. Die Vektorebene meldete bei diesen Läufen keine Neuzeichnung über 50 ms.
- **Prüfung:** EditMode 1456 von 1456 grün (1448 + 8: 7 `TheObjectBadgeShortensFromTenThousand`, 1 `ObjectListRowHeightMatchesTheRowRuleAndTheRowsAreSetInTokens`; `UnusedStyleClassTests`, `LocalizationKeyUsageTests`, `ElementNameLookupTests`, `DialogCodeRulesTests` grün); Player-Build ok; Bilder `.omc/handoffs/objects-popup/step3/`.
- **Handtest:** alle sieben Handtest-Punkte vom Projektinhaber am 22. November 2026 bestätigt (Öffnen und Scrollen in Spandau ohne Ruckeln, Klick, Strg+Klick, Tastatur inkl. Esc, Suche/Herkunft/Gliederung, Karte zur Liste, Änderungen bei offener Liste). Entf mit dem Fokus in der Liste löscht wie bei jedem Element des Popups die gewählten Objekte (globaler Kurzbefehl).
