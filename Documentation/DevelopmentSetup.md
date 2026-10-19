# Entwicklungsumgebung

## Bestand am 19. Oktober 2026

| Werkzeug | Befund |
|---|---|
| Betriebssystem | Windows x64; PowerShell verfuegbar. Die CIM-Abfrage der Windows-Version wurde durch Berechtigungen verweigert. |
| Git | Git 2.56.0.windows.2 installiert; Repository initialisiert, noch ohne Commit. |
| Unity Hub / Unity Editor | Unity Hub installiert; Editor `6000.6.6f1` unter `D:\Unity Hub\Unity 6.6\6000.6.6f1\Editor\Unity.exe`. |
| Visual Studio | Visual Studio Build Tools 2026, Version 18.11.12306.390, installiert. Die vollstaendige IDE und die Unity-Workload wurden nicht nachgewiesen. |
| JetBrains Rider | An den ueblichen Installationsorten und ueber PATH nicht gefunden. |
| .NET SDK | .NET SDK 2.1.202 gefunden. Unity verwendet seinen Editor-Compiler; dieses SDK allein weist keine Unity-Kompatibilitaet nach. |
| Windows-Build-Module | Windows Standalone Player wurde erfolgreich mit Unity gebaut; das Windows-Build-Modul ist damit verfuegbar. |

Die Unity-Installation und der Windows-Build wurden mit der Unity-CLI ausgefuehrt. Generierte Unity-Ordner wie `Library` und `Logs` bleiben durch `.gitignore` ausgeschlossen. Die vorhandene `.gitattributes` wurde um LF-Normalisierung und gezielte Textdateitypen erweitert.

## Festgelegte Versionen

Das Projekt verwendet Unity **6.6, 6000.6.6f1**, entsprechend der installierten Editor-Version. Unity veroeffentlichte `6000.6.6f1` am 15. Oktober 2026. Fuer Unity 6000.6 ist URP **17.6.0** die passende Paketlinie. Quellen: [Unity Release Archive](https://unity.com/releases/editor/archive), [URP 17.6 Manual](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@17.6/manual/index.html).

## Oeffnen und starten

1. Unity Hub starten und das Projekt ueber **Add > Add project from disk** hinzufuegen.
2. `Assets/_Project/Scenes/EditorStartup.unity` oeffnen und Play druecken.
3. Fuer einen Windows-Build **File > Build Profiles** oeffnen, Windows auswaehlen, die Startszene einschliessen und den Build starten. Der CLI-Build-Helfer befindet sich in `Assets/_Project/Scripts/UI/Editor/WindowsPlayerBuild.cs`.

Beim ersten Import erzeugt Unity unter anderem `ProjectSettings/ProjectSettings.asset` und `Packages/packages-lock.json`. Der aufgeloeste Lockfile ist versioniert.

## Validierungsstand

| Pruefung | Status |
|---|---|
| Verzeichnis- und Quellstruktur | Erledigt, statisch geprueft |
| JSON-Syntax der Paketdateien und Assembly Definitions | Erfolgreich: JSON-Parser |
| UXML/XML-Syntax | Erfolgreich: XML-Parser |
| Assembly-Referenzen, Szenenreferenz und Asset-Metas | Erfolgreich: statische Repository-Pruefung; Unity-Metas fuer UXML/USS wurden importiert |
| Git-Ausschluesse und Whitespace | Erfolgreich: `git check-ignore` und `git diff --check`; generierte Ordner sind ausgeschlossen |
| Unity-Paketaufloesung | Erfolgreich: URP 17.6.0 und Abhaengigkeiten aufgeloest |
| Unity-Import und Szene | Erfolgreich: Projekt und Startszene importiert |
| Unity C#-Kompilierung | Erfolgreich: Projekt-Assemblies kompiliert |
| UI Toolkit-Ressource | Erfolgreich: `EditorShell` zur Laufzeit geladen |
| App-Kartenladen | Erfolgreich gebaut: klickbares **File > Open OMSI map...** und zentrale Schaltfläche lesen eine externe `global.cfg` im Windows-Player |
| Windows-Build | Erfolgreich: `Builds/Windows/OMSIMapEditor.exe`; Unity meldete `Build Finished, Result: Success` |
| Player-Inhaltsgrenze | Erfolgreich beim letzten Build: `Editor.OMSI.dll` ist enthalten, `Editor.OMSI.Editor.dll` und OMSI-Quelldateiformate fehlen. Seit ADR 005 ohne Freigabeliste; der Bytevergleich mit der Installation bleibt. Ein neuer Build nach dieser Änderung steht noch aus |
| Grundorf-Import | Erfolgreich: 19 Kacheln, 1.095 Objekte, 60 Splines, 37 Anbindungen und 19 Terrain-Felder per Unity Batch-Mode eingelesen |
| Weitere Karten | Erfolgreich: Stadtückheim und Berlin-Spandau lesend eingelesen, alle gezählten Objekte und Splines dekodiert |
| Portable Unit-Tests | Erfolgreich: 19 Tests für Dekodierung, Leser und Geometrie ausserhalb von Unity mit Roslyn und NUnit ausgefuehrt |
| Kompilierung gegen Unity-DLLs | Erfolgreich: alle Projekt-Assemblies inklusive `Editor.Tests` mit Player- und Editor-Defines gegen die Unity-6000.6.6f1-Referenzen kompiliert |
| Unity Test Runner | Noch nicht durchgefuehrt; die Einstellungs-Tests benoetigen Unity |
| Editor-Playmode / visueller Player-Test | Noch nicht durchgefuehrt |

## Git-Arbeitsweise

- `main` enthaelt integrierbare, stabile Arbeit. Aenderungen in kurzen Zweigen wie `feature/ui-shell` oder `docs/setup` entwickeln.
- Commits buendeln eine nachvollziehbare Aenderung und verwenden kurze imperative Betreffzeilen, zum Beispiel `Add startup shell` oder `Document Unity setup`.
- Unity-Assets gemeinsam mit ihren `.meta`-Dateien committen. `Library`, `Temp`, Build-Ausgaben und IDE-Zwischenstaende bleiben ausgeschlossen.
- Keine privaten absoluten Pfade, Zugangsdaten oder generierten Cachedateien committen.

## Naechste Schritte

1. Das Projekt in Unity Hub oeffnen, **OMSI Map Editor > Import > Read OMSI Map...** waehlen und Grundorfs Kacheldraufsicht visuell pruefen.
2. Die Spline-Kurvenrichtung und Koordinaten an weiteren Kachelverbindungen visuell gegen den OMSI-Editor pruefen.
3. Danach den schreibgeschuetzten Import erweitern, der OMSI-Dateien direkt aus der Installation liest, ohne sie als Player-Inhalte zu übernehmen.
