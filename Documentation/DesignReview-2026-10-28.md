# Design-Review: OMSI MapWorks, Hauptansicht (28. Oktober 2026)

**Status:** offen, noch nichts umgesetzt.\
**Grundlage:** Skill „apple-design“ (Apples Human Interface Guidelines, Stand 28. Oktober 2026), `Documentation/CorporateDesign.md` (ADR 015), `Assets/_Project/UI/Resources/EditorShell.uss` und Screenshots bei 1600 × 900 (Lanes und Priority an Kreuzung #472 im TestMap-Projekt, Draufsicht und 3D in Berlin-Spandau).

**Einordnung:**

- MapWorks ist eine Windows-App mit Unity UI Toolkit. Apples Plattformregeln für macOS gelten nur sinngemäß; übernommen sind die Grundlagen (Kontrast, Schriftgrößen, Größe der Bedienelemente, Farbe, Texte, Bedienung).
- Wo das Corporate Design etwas anders entscheidet, gilt das Corporate Design (zum Beispiel nur dunkel, Satzschreibung statt Title Case).
- Kontraste sind aus den Hex-Werten der Tokens gerechnet (WCAG). Den Panel-Hintergrund `rgba(32,38,48,0.94)` habe ich als deckendes `#202630` gerechnet.

## Zusammenfassung

Bewertung: **Kritische Punkte vorhanden.**

Die Aufgabe ist klar umgesetzt: Die Karte ist die Bühne, Werkzeuge und Einstellungen schweben über ihr. Wiedererkennbar macht MapWorks die randlose Karte mit der unteren Werkzeugleiste mit Tastenkappen. Das ist ein bewusst ruhiges Profi-Werkzeug, und das passt.

Zwei Barrierefreiheitsfehler sind kritisch: Die Oberfläche folgt nicht der Windows-Skalierung, und die gedämpfte Hinweisschrift ist zu kontrastarm.

## Übersicht

| Nr. | Stufe | Befund | Aufwand |
|---|---|---|---|
| 1 | Kritisch | Keine Windows-Skalierung, keine einstellbare Textgröße | mittel (Code, Panelbreiten prüfen) |
| 2 | Kritisch | Gedämpfte Schrift und Fehlerrot unter 4,5:1 | klein (Tokens) |
| 3 | Hoch | Werkzeugpanels verdecken die bearbeitete Kreuzung | mittel |
| 4 | Hoch | Rot und Gelb haben mehrere Bedeutungen | mittel (Entscheidung im Corporate Design) |
| 5 | Hoch | Favoriten- und DLC-Sterne im Aus-Zustand kaum sichtbar | klein |
| 6 | Mittel | Tastaturfokus auf Knöpfen unsichtbar | klein |
| 7 | Mittel | Dieselbe Anleitung dreimal | klein |
| 8 | Mittel | Statusleiste schneidet Texte ab | klein |
| 9 | Niedrig | Orange auf dekorativen Symbolen | klein |

Änderungen an `EditorShell.uss` sind geänderte Player-Dateien: Danach muss der Nutzer die Freigabeliste mit „Record Reviewed Player Assets“ neu schreiben (CLAUDE.md, harte Regel 3).

## Kritisch

### 1. Die Oberfläche ignoriert die Windows-Anzeigeskalierung und hat keine Textgröße zum Einstellen

- **Was:** `EditorShellBootstrap.cs` (`Awake`) setzt `panelSettings.scale` fest aus `Assets/_Project/Settings/Resources/editor-settings.json` (`"scale": 1.0`). Der Skalierungsmodus bleibt auf dem Standard `ConstantPixelSize`. Bei 150 % Windows-Skalierung, üblich auf Laptops, wird die 11-px-Sekundärschrift dadurch physisch so klein wie etwa 7 pt. Der Wert steckt im Build und ist für Nutzer nicht änderbar. Auch das Corporate Design verlangt „Support Windows display scaling“.
- **Warum:** `accessibility.md › Vision`: „Ideally, give people the option to enlarge text by at least 200 percent.“ Und `typography.md › Using custom fonts`: „be sure to let players adjust text size in other ways.“
- **Lösung:**
  - `panelSettings.scaleMode = PanelScaleMode.ConstantPhysicalSize` mit `referenceDpi = 96` und `fallbackDpi = 96`, damit 150 % in Windows auch 1,5-fach ergibt.
  - Dazu „View › Interface size“ mit 90–200 %, gespeichert in den Benutzereinstellungen und nicht in der Resources-Datei. Der Wert wird mit `scale` multipliziert.
  - Danach die festen Breiten der Panels prüfen: 270 px (Project), 300 px (Inspector), 780 px (Objekte-Bibliothek), bei einer Mindestgröße des Fensters von 1100 × 700.

### 2. Gedämpfte Schrift und Fehlerrot unterschreiten 4,5:1

- **Was:** Dieselbe gedämpfte Farbe trägt echten Inhalt und nicht nur deaktivierte Elemente: Objektdetails (`.panel-row-detail`, 10 px), Pfade der zuletzt geöffneten Dateien (`.welcome-recent-path`), Hinweise im Dialog „New map“ (`.new-map-hint`) und Attributionen (`.satellite-attribution`).

  | Farbe | Hintergrund | Kontrast | Mindestwert |
  |---|---|---|---|
  | `#7E8A99` (text-muted) | Panel `#202630` | 4,33:1 | 4,5:1 |
  | `#7E8A99` (text-muted) | Dialog `#29313D` | 3,74:1 | 4,5:1 |
  | `#E06464` (error) | Panel `#202630` | 4,47:1 | 4,5:1 |

  Fehlerrot nutzen fehlende Kacheln (11 px) und der Text des Löschen-Knopfs.
- **Warum:** `accessibility.md › Vision`: „Up to 17 pts … 4.5:1“.
- **Lösung:**
  - `--mf-color-text-muted: #909CAA` (5,45:1 auf dem Panel, 4,70:1 auf `#29313D`). Es bleibt eine klare Stufe unter Sekundär mit 7,57:1.
  - Neues Token `--mf-color-text-disabled: #7E8A99` nur für `:disabled`. Deaktivierter Text ist von der Regel ausgenommen, und der Unterschied zu aktivem Text bleibt sichtbar.
  - `--mf-color-error: #EA7676` (5,32:1 auf dem Panel, 4,59:1 auf `#29313D`).

## Verbesserungen

### 3. Hoch: Werkzeugpanels verdecken die Kreuzung, die man gerade bearbeitet

- **Was:** In den Screenshots von #472 liegt das Panel „Right of way“ über dem südlichen Vorfahrtsschild. „Lane connections“ schneidet die unteren Spurenden an. Beide Panels sitzen mittig direkt über der Leiste (`.tool-panel-host`), also genau dort, wo die gewählte Kreuzung meist liegt.
- **Warum:** `panels.md › HUD-style panels`: „Don’t let a HUD obscure the content it adjusts.“
- **Lösung (einfachste Variante):** Beim Wählen einer Kreuzung in Lanes, Priority oder Markings die Ansicht so verschieben, dass der Umriss der Kreuzung im freien Bereich oberhalb des Panels liegt. Das geht etwa mit `CenterOnWorld` plus einem Versatz um die halbe Panelhöhe. Alternative: Die Optionsspalte `.picker-options` links neben der Leiste andocken statt über ihr.

### 4. Hoch: Eine Farbe hat mehrere Bedeutungen

- **Was:**
  - Rot heißt im Corporate Design „Löschen/Fehler“, markiert in der Karte aber auch die Auswahl (`--map-selection: var(--mf-color-error)`), zum Beispiel den roten Rahmen um #472.
  - Gelb heißt „bearbeitet/Warnung“, ist aber auch der Rahmen der gewählten Kachel (`--map-tile-selected`) und die Farbe der Gleispfade (`PathColor(2)` in `MapCanvasPaths.cs`).
  - Im Spandau-Bild läuft deshalb der gelbe Kachelrahmen mitten durch die gelben Gleise.
- **Warum:** `color.md › Best practices`: „Avoid using the same color to mean different things.“
- **Lösung (Vorschlag, die Entscheidung liegt beim Corporate Design):**
  - Auswahl als helle Kontur `#F2F4F7` mit dunklem Rand.
  - Gewählte Kachel in Orange, also „aktiv“.
  - Gleise mit eigenem Token `--mf-color-path-rail`, zum Beispiel ein Violett wie `#B48EDB` (Kontrast auf Luftbildern noch prüfen).

### 5. Hoch: Die Favoriten- und DLC-Sterne sind im Aus-Zustand kaum sichtbar

- **Was:** `.favorite-button` und `.dlc-mark-button` nutzen `#414B59` auf `#202630`, das sind **1,72:1**.
- **Warum:** `accessibility.md › Vision`: „enough contrast between foreground text and icons and background colors“. Für Symbole gilt mindestens 3:1.
- **Lösung:** Im Aus-Zustand `color: var(--mf-color-text-muted)` mit dem neuen Wert aus Punkt 2 verwenden (5,45:1). Gefüllt in Orange bzw. Gelb bleibt der An-Zustand, damit unterscheidet nicht nur die Farbe.

### 6. Mittel: Der Tastaturfokus auf Knöpfen ist unsichtbar

- **Was:** `EditorShell.uss` hat nur 5 `:focus`-Regeln, alle für Textfelder. `.tool-button`, `.view-control-button`, `.toolbar-menu-button` und `.panel-tab` haben `border-width: 0`. Ein Fokus über Tab ist dort also nicht zu sehen. Das Corporate Design §10 verlangt sichtbare Fokusanzeigen.
- **Warum:** `accessibility.md › Speech`: „Let people use the keyboard alone to navigate and interact with your app.“
- **Abwägung:** Das Bearbeiten der Karte braucht ohnehin die Maus. Für Panels und Dialoge lohnt es sich trotzdem.
- **Lösung:** Eine gemeinsame Regel:

  ```css
  .tool-button:focus, .view-control-button:focus, .toolbar-menu-button:focus, .panel-tab:focus {
      border-width: 2px;
      border-color: var(--mf-color-accent-hover);
  }
  ```

### 7. Mittel: Dieselbe Anleitung steht dreimal da

Meine Einschätzung, keine HIG-Regel.

- **Was:** Bei Lanes steht dieselbe Anleitung in der Legende, in der Planzeile des Panels und in der Statusleiste. Bei Priority stehen „Point at a sign to switch its arm.“ und „Click a sign to switch its arm. Esc: leave.“ direkt untereinander.
- **Lösung:** Im Panel nur die Legende und eine einzige Zeile, die sich mit dem Zustand ändert. Die Statusleiste meldet nur Ergebnisse, etwa „Added the way …“.

### 8. Mittel: Die Statusleiste schneidet wichtige Texte ab

- **Was:** Im Screenshot endet die 3D-Hilfe mit „…red pitch, blu…“ (`.status-message` mit `text-overflow: ellipsis`).
- **Warum:** `layout.md › Desktop (macOS)`: „Avoid placing controls or critical information at the bottom of a window.“ Unter Windows mit maximiertem Fenster ist das weniger heikel, abgeschnitten ist der Text aber trotzdem.
- **Lösung:** Statusmeldungen auf einen Satz begrenzen. Die Bedienhilfe für 3D in das Hilfe-Popup verschieben und den vollen Text der Statuszeile als Tooltip anzeigen.

### 9. Niedrig: Orange auch auf rein dekorativen Symbolen

- **Was:** Die Symbole der Panel-Köpfe (`.floating-panel-icon`, „Project“ und „Inspector“) und die Symbole der zuletzt geöffneten Dateien (`.welcome-recent-icon`) sind orange. Damit verwässert „Orange = aktiv“.
- **Warum:** `branding.md › Best practices`: „Apply your app’s accent color judiciously.“
- **Lösung:** `color: var(--mf-color-text-secondary)`.

## Gestaltung

Die Palette aus dunklem Anthrazit und Orange liegt nahe am verbreiteten Standard dunkler Werkzeuge. Hier ist sie aber begründet: Die farbigen Inhalte sind die Luftbilder, Straßen und Pfade, und die Oberfläche tritt bewusst zurück. So lassen.

Weglassen lässt sich genau ein Zierelement: die orangen Symbole in den Panel-Köpfen (Punkt 9). Sonst ist die Oberfläche schlank.

## Was gut ist (so behalten)

- **Kontraste:** Haupt- und Sekundärtext erreichen 13,8:1 und 7,6:1. Die Beschriftung des aktiven Werkzeugs auf Orange liegt bei 6,84:1.
- **Werkzeugleiste:** Knöpfe von 66 × 70 px, mit Symbol, Text und Tastenkappe. Das ist weit über dem Mindestmaß (macOS: 28 × 28 pt Standard, 20 × 20 pt Minimum).
- **Form statt nur Farbe:** Punkt und Ring für Spur hinein und hinaus, Raute und Dreieck für Vorfahrt und Wartepflicht, gestrichelte Linien für Pfade, die warten müssen. Das ist genau `accessibility.md › Vision`: „Offer visual indicators, like distinct shapes … in addition to color“.
- **Tokens:** Durchgängig `--mf-*`-Tokens, dadurch sind die meisten Korrekturen oben jeweils eine Zeile.
- **Rückgängig:** Strg+Z überall, auch bei Lanes und Priority.

## Hinweise zur Plattform

- **Windows statt macOS:** Apples macOS-Konventionen gelten hier nicht, etwa Einstellungen im App-Menü, Title Case und Fensterknöpfe. Die Satzschreibung des Corporate Designs ist durchgängig, kein Befund.
- **Nur dunkel:** Für einen Karteneditor ist ein reines Dunkelschema vertretbar. Den Windows-Kontrastmodus beachtet die App nicht. Ein eigenes kontrastreiches Token-Set wäre ein späteres, kleines Projekt.

## Vorschlag zur Umsetzung

In einem eigenen Zweig (`feature/design-review-fixes`):

1. Zuerst die kleinen Token-Änderungen: Punkte 2, 5, 9 und die Fokusregel aus Punkt 6.
2. Danach die Skalierung (Punkt 1) mit Prüfung der Panelbreiten bei 150 % und 200 %.
3. Dann Punkt 3 (Panels verdecken die Kreuzung) und Punkt 8 (Statusleiste).
4. Punkt 4 (Farbbedeutungen) erst nach Entscheidung des Nutzers, gegebenenfalls als Ergänzung zu ADR 015.

Nach jedem Schritt Screenshots prüfen und die Freigabeliste der Player-Dateien neu schreiben lassen.
