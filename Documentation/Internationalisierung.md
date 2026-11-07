# Mehrsprachige Oberfläche – Abschluss (Entwicklungsauftrag 1.2)

Stand: 7. November 2026, Zweig `feature/i18n`. Grundsatzentscheidung: ADR 068 in `Decisions.md`. Anleitung für Übersetzer: [`locales/README.md`](../locales/README.md).

## Verwendete Technik und Begründung

Ein eigenes, kleines System in `Editor.Core/Localization/`, ohne neue Abhängigkeit: JSON-Sprachdateien, eine Teilmenge des ICU-MessageFormat (`{name}`, `number`, `date`, `plural`, `select`) und CLDR-Pluralregeln für Englisch, Deutsch, Polnisch, Tschechisch, Französisch und Spanisch.

Warum nicht Unitys Localization-Paket: Es hält Texte als Assets (schlecht für Beiträge per Pull Request, `.meta`-Dateien), ist schwer ohne Unity zu testen und bringt mehr mit, als hier gebraucht wird. Das eigene System lässt sich mit dem schnellen Testlauf ohne Unity prüfen und liest Sprachen als gewöhnliche Textdateien neben dem Programm.

## Architektur

- `TranslationManager` hält die gewählte Sprache und Englisch als Rückfall. Ein fehlender Text fällt auf Englisch zurück, fehlt auch der, steht `[schluessel]` da; jeder Mangel wird einmal protokolliert (nur im Editor und in Entwicklungsbuilds). Alle Texte werden beim Laden übersetzt und vorbereitet, `T` darf aus jedem Thread gerufen werden.
- `LanguageChoice` bestimmt die Sprache beim Start: gespeicherte Wahl, sonst Systemsprache, sonst Englisch. Der erste Start speichert, was er fand; eine von Hand gewählte Sprache wird nie überschrieben. Gespeichert wird in den PlayerPrefs (`Mapforge.Language`).
- `LocBindings` (Oberfläche) bindet Elemente an Schlüssel; ein Sprachwechsel schreibt die Texte eines Baums neu. Fenster mit Daten haben `RefreshTexts()`, Statusmeldungen mit `SetStatusT` werden neu geschrieben.
- `LocaleValidator` prüft alle Dateien (JSON-Syntax, doppelte und ungültige Schlüssel, ICU-Syntax, fehlende und unbekannte Platzhalter, Pluralformen der Sprache). Er läuft im Build (Fehler stoppen einen Release-Build), als Werkzeug `Tools/LocaleCheck` und im GitHub-Workflow `locales.yml`.
- Projektdateien, Karten und Exporte hängen nie von der Oberflächensprache ab (Test `TheLanguageOfAMapNeverReachesItsFiles`).

## Speicherort der Sprachdateien

`locales/<Code>/` im Repository (`locale.json` und beliebig viele `*.json` mit flachen Schlüsseln). Der Build kopiert sie nach `OMSIMapWorks_Data/StreamingAssets/locales`; eine Sprache erscheint in der Auswahl, wenn ihre Dateien dort liegen und `status` auf `approved` steht (Entwürfe nur in Editor- und Entwicklungsbuilds). Auswahl: **Einstellungen > Allgemein > Sprache**; Befehlszeile: `-language <Code>` (nur dieser Lauf), `-chooseLanguage <Code>` (speichert).

## Vollständig migrierte Bereiche

Menüleiste, Statusleiste und Kartenhinweise, Willkommensbild, Werkzeugleiste, Projektbrowser (Linien, Chrono, Kacheln), Inspector (Kachel, Objekt, Spline, Auswahl, angehängte Objekte, Wasser, Verkehrsregeln, Typ und Beschriftung), Einstellungen, Neue Karte, Export, Updates, Fehlerbericht (Fenster), Straßenwerkzeug, Straßenbauer, Reihen, Gelände und Höhenkarte, Markierungen, Vorfahrt, Spuren, Pfade, Objekt-Bibliothek, Satellit und Gelände, Chrono-Varianten, Kartenprüfung (Rahmen, Knöpfe, Meldungen), Dateidialoge, alle Statusmeldungen, Rückgängig-Beschreibungen und die Meldungen der Renderer. Rund 1.700 Schlüssel, Englisch und Deutsch vollständig.

## Noch offene Übersetzungen

- `pl-PL`, `cs-CZ`, `fr-FR`, `es-ES` sind Entwürfe ohne Texte (nur `locale.json`); sie erscheinen nicht in der Auswahl der Release-Builds.
- Texte aus `Editor.OMSI` bleiben englisch (siehe Ausnahmen); die Kartenprüfung soll später auf Codes mit Parametern umgestellt werden.

## Testergebnisse

- Unity-EditMode: 439 Tests grün (davon 29 in `LocalizationTests`, 5 in `LocalizationKeyUsageTests`, 5 in `LocalizationInterfaceTests`), Windows-Player-Build erfolgreich.
- Schneller Lauf ohne Unity (Core, OMSI, Tests): 421 grün.
- `LocaleCheck`: 0 Fehler, 4 Warnungen (die leeren Entwurfssprachen).
- `LocalizationKeyUsageTests` liest den Quelltext und prüft jeden Schlüssel (`Loc.T`, `SetStatusT`, `LocBindings.*`, `UpperLabel`) gegen `locales/en-US`, samt Argumentnamen; Schlüssel müssen als ganzer Text geschrieben sein.
- Player-Prüfung an Grundorf auf Deutsch und im Live-Wechsel Englisch nach Deutsch (Hauptfenster, Straßenwerkzeug, Straßenbauer, Objekte, Satellit, Gelände, Einstellungen, Neue Karte, Fehlerbericht).
- Nicht ausführbar auf diesem Rechner: der GitHub-Workflow (nur das .NET SDK 2.1 vorhanden); seine Quellen sind mit `csc` geprüft.

## Bekannte Einschränkungen

- Rückgängig-Beschreibungen und Statusmeldungen, in die schon übersetzte Teile eingebaut sind (`SetStatus` statt `SetStatusT`), behalten nach einem Sprachwechsel ihre damalige Sprache.
- Deutsche Texte sind länger; die Fenster wurden darauf geprüft und einzelne Beschriftungen gekürzt, in sehr schmalen Fenstern kann trotzdem etwas abgeschnitten werden.
- Sprachdateien, die jemand in den Programmordner legt, überschreibt ein Update (Beiträge gehören ins Repository).
- Der Installer (`Tools/Installer`) ist nicht übersetzt.
- `interface.defaultLocale` in `editor-settings.json` wird nicht ausgewertet.

## Neue Sprache hinzufügen

1. Ordner `locales/<Code>/` anlegen (z. B. `pl-PL`) mit `locale.json` (`code`, `name` in der eigenen Sprache, `englishName`, `status`: `draft`).
2. Die Dateien von `locales/en-US` kopieren und die Werte übersetzen; Platzhalter in `{…}` und die Pluralformen der Sprache (`one`, `few`, `many`, `other`) beachten; Hinweise für Übersetzer stehen als `"@schluessel"`.
3. `LocaleCheck.exe locales` (oder der Workflow) prüft die Dateien.
4. Ist die Übersetzung vollständig, `status` auf `approved` setzen. Am Programm ändert sich nichts.

## Abschließende Suche nach fest codierten Texten

Gesucht wurde in allen Dateien unter `Scripts/UI` und `Scripts/World` nach Texten in Zeichenfolgen, die Beschriftungen, Tooltips, Meldungen oder Platzhalter ergeben (Werkzeug im Scratchpad, `units.py`, jeweils `list` und `rest`). Übrig sind nur diese Ausnahmen, die nicht übersetzt werden müssen:

| Ausnahme | Grund |
|---|---|
| `Debug.Log`, `Debug.LogWarning`, Präfixe wie „Rows:“ im Log | Diagnose für Entwickler und Prüfskripte, soll in Berichten gleich lesbar bleiben |
| Texte aus `Editor.OMSI` (`throw new`, Kartenprüfung mit „Made objects“, „Chain offsets“, „DLC objects“, `OmsiRoadIssue`, `DescribeReferences`) | Modellschicht ohne Oberflächenabhängigkeit; die Texte erscheinen als `{reason}` in übersetzten Rahmentexten; die Prüfung folgt in einem eigenen Schritt |
| Inhalt des Fehlerberichts an GitHub (`BugReport`) | Wird von den Entwicklern auf Englisch gelesen |
| Namen und Pfade aus den Daten (Kartennamen, Dateinamen, Straßentypen, Quellen der Luftbilder und ihre Quellenangaben) | Daten, keine Oberflächentexte |
| Namen erzeugter Objekte („Mapforge …“, „T junction …“, „Crossroads …“, „Transition …“) und der Vorgabename „Road works“, „New map“ | werden als Dateinamen und Daten in OMSI geschrieben |
| Einheiten und Zahlenformate wie „m/px“, „px/m“ | technische Schreibweise |
| Namen der englischen Vorlagen und Kennungen, an denen die Befehlszeile hängt (`-roadTab "My roads"`, Kategorienamen aus `Editor.OMSI`) | stabile Kennungen, die Oberfläche zeigt übersetzte Fassungen |
| Namen von Kameras, Texturen und Materialien in Unity | interne Bezeichner |
| `EditorShellBenchmark` | Entwicklerwerkzeug, kein Teil der Oberfläche |
| Installer (`Tools/Installer`) | eigenes Programm, getrennt von MapWorks |
