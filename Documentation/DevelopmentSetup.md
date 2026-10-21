# Entwicklungsumgebung

## Bestand am 19. Oktober 2026

| Werkzeug | Befund |
|---|---|
| Betriebssystem | Windows x64; PowerShell verfuegbar. Die CIM-Abfrage der Windows-Version wurde durch Berechtigungen verweigert. |
| Git | Git 2.56.0.windows.2 installiert; Repository mit erstem Commit auf `main`. |
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
| Player-Inhaltsgrenze | Erfolgreich: Build vom 20. Oktober 2026 nach ADR 005 ohne Freigabeliste; `Editor.OMSI.dll` ist enthalten, `Editor.OMSI.Editor.dll` und OMSI-Quelldateiformate fehlen, der Bytevergleich mit der Installation meldete keinen Treffer |
| Grundorf-Import | Erfolgreich: 19 Kacheln, 1.095 Objekte, 60 Splines, 37 Anbindungen und 19 Terrain-Felder per Unity Batch-Mode eingelesen |
| Weitere Karten | Erfolgreich: Stadtückheim und Berlin-Spandau lesend eingelesen, alle gezählten Objekte und Splines dekodiert |
| Portable Unit-Tests | Erfolgreich: 86 Tests für Kodierung, verlustfreies Dateimodell, typisierte Zugriffe, ID-Vergabe, Leser, Geometrie und Spline-Verbindungen ausserhalb von Unity mit Roslyn und NUnit ausgefuehrt |
| Typisierte Zugriffe | Erfolgreich: 36.093 Datensätze der drei Karten ohne Lesefehler; 0 Abweichungen zum Leser bei 32.639 Objekten und Splines; Neusetzen der eigenen Werte ändert nur 2 Datensätze mit fremdem Zahlenformat |
| Objektumrisse | Erfolgreich am 20. Oktober 2026: Umrisse für 107 von 108 Objektarten in Grundorf und alle 872 in Berlin-Spandau; Entwürfelung über Normalen geprüft, Achsen und Drehung über Anschlusspunkte von Kreuzungen. Im gebauten Player in Berlin-Spandau bei 1,3 m/px keine Neuzeichnung über 50 ms und keine Ausnahmen |
| 2D-Draufsicht | Erfolgreich am 20. Oktober 2026 im gebauten Player: Grundorf und Berlin-Spandau vollständig eingepasst, Zoom mit dem Mausrad und Verschieben mit der rechten Maustaste. Ein Objekt per Ziehen verschoben und am Griff gedreht; zweimal Strg+Z stellte die Originalwerte her und entfernte die Kachel wieder aus der Arbeitskopie. In Berlin-Spandau dauerte keine Neuzeichnung länger als 50 ms; keine Ausnahmen im Player-Log |
| Bearbeiten in der App | Erfolgreich am 20. Oktober 2026 im gebauten Player: Grundorf geöffnet, Projekt gespeichert und wieder geöffnet, Objekt per Liste und per Klick in die Kachel gewählt, X und Richtung geändert. Die Arbeitskopie unterschied sich vom Original in genau diesen zwei Zeilen; das Original blieb unverändert. Der Exportdialog verweigerte den Originalordner als Ziel; "Revert tile edits" stellte das Original wieder her. Keine Ausnahmen im Player-Log |
| Export im Spiel | Erfolgreich am 20. Oktober 2026: eine in der App verschobene Scheune erscheint in der exportierten Grundorf-Karte in OMSI an der neuen Position auf der Wendeschleife |
| Strukturelle Änderungen | Erfolgreich im Arbeitsspeicher: nach dem Entfernen eines Objekts vor Einstiegspunkten zeigen in Grundorf 7/7 und in Berlin-Spandau 51/51 Einstiegspunkte weiter auf ihr Objekt; nach dem Entfernen eines Splines bleiben 57 Anhängungen in Berlin-Spandau auf ihrem Spline. Der Verweisindex findet 17.890 Verweise in Berlin-Spandau in etwa 0,5 s |
| ID-Vergabe | Erfolgreich: Grundorf und Berlin-Spandau setzen bei `[NextIDCode]` fort, Stadtückheim bei 93 statt beim bereits vergebenen 92 |
| Verlustfreie Rundreise | Erfolgreich: alle 1.409 Textdateien der drei installierten Karten byte-identisch, darunter 766 Kacheln mit 57.180 Datensätzen, alle mit belegtem Layout; Unity-Batch-Prüfung für Berlin-Spandau 1.172 von 1.172 |
| Kompilierung gegen Unity-DLLs | Erfolgreich: alle Projekt-Assemblies inklusive `Editor.Tests` mit Player- und Editor-Defines gegen die Unity-6000.6.6f1-Referenzen kompiliert |
| Unity Test Runner | Erfolgreich: alle 91 EditMode-Tests am 21. Oktober 2026 per Unity Batch-Mode bestanden |
| Platzieren und Löschen | Erfolgreich am 21. Oktober 2026 über eine Projekt-Arbeitskopie: In Grundorf und Berlin-Spandau je ein Objekt platziert, `[NextIDCode]` fortgeschrieben, je 48 Objekte gelöscht oder abgewiesen und alles zurückgenommen. Abgewiesen wurden Objekte mit Verweisen aus Fahrplan, Haltestellen, Einstiegspunkten, Chrono und Elternbeziehungen. Danach war die Arbeitskopie leer und die Originalkarte byte-gleich. Löschen dauert in Berlin-Spandau bis 0,7 s, Platzieren 0,45 s beim ersten Mal |
| Platzieren im Spiel | Erfolgreich am 21. Oktober 2026: in Grundorf im gebauten Player Heuballen und Kühe auf die Wendeschleife gesetzt; sie erscheinen in der exportierten Karte in OMSI an ihrer Stelle. Löschen im Spiel noch nicht eigens geprüft |
| Spline-Verbindungen | Erfolgreich: Grundorf 36/36, Stadtückheim 17/17, Berlin-Spandau 1.464/1.467 innerhalb 0,5 m; die drei Lücken sind einseitige Verweise in den Kartendaten |
| Visueller Player-Test | Erfolgreich am 20. Oktober 2026: Grundorf und Berlin-Spandau im gebauten Player geladen, Kacheln und Liste angeklickt, Kartenprüfung über **Tools** ausgeführt; keine Ausnahmen im Player-Log |
| Editor-Playmode | Noch nicht durchgefuehrt |

## Git-Arbeitsweise

- `main` enthaelt integrierbare, stabile Arbeit. Aenderungen in kurzen Zweigen wie `feature/ui-shell` oder `docs/setup` entwickeln.
- Commits buendeln eine nachvollziehbare Aenderung und verwenden kurze imperative Betreffzeilen, zum Beispiel `Add startup shell` oder `Document Unity setup`.
- Unity-Assets gemeinsam mit ihren `.meta`-Dateien committen. `Library`, `Temp`, Build-Ausgaben und IDE-Zwischenstaende bleiben ausgeschlossen.
- Keine privaten absoluten Pfade, Zugangsdaten oder generierten Cachedateien committen.

## Kommandozeile

Alle Befehle laufen in Git Bash vom Projektordner aus. Unity braucht dafür einen geschlossenen Editor, sonst ist das Projekt gesperrt.

```bash
UNITY="/d/Unity Hub/Unity 6.6/6000.6.6f1/Editor/Unity.exe"

# EditMode-Tests; das Ergebnis steht in der XML-Datei
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults /tmp/editmode.xml -logFile /tmp/editmode.log

# Windows-Player nach Builds/Windows/OMSIMapEditor.exe, inklusive Prüfung der Inhaltsgrenze
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD" -executeMethod OmsiMapEditor.Editor.WindowsPlayerBuild.Build -logFile /tmp/build.log

# Verlustfreie Rundreise einer Karte im Arbeitsspeicher, ohne etwas zu schreiben
OMSI_MAP_CONFIG="D:/SteamLibrary/steamapps/common/OMSI 2/maps/Grundorf/global.cfg" \
  "$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod OmsiMapEditor.OMSI.Editor.OmsiMapCli.VerifyRoundTripFromEnvironment -logFile /tmp/roundtrip.log
```

**Bei offenem Editor:** Ordner `Assets`, `Packages`, `ProjectSettings` und `Library` in ein temporäres Verzeichnis kopieren, etwa 1,1 GB, und die Befehle dort mit `-projectPath` auf die Kopie ausführen. Die reine Logik in `Editor.Core` und `Editor.OMSI` lässt sich zusätzlich ohne Unity prüfen. Dazu kompiliert man sie mit dem Roslyn-Compiler der Visual Studio Build Tools und NUnit aus `Library/PackageCache/com.unity.ext.nunit@*/net472/unity-custom/`. Die dafür benutzten Hilfsskripte lagen nur im Arbeitsverzeichnis der Sitzung und sind nicht Teil des Repositorys.

**Player-Log:** Der gebaute Player schreibt nach `%USERPROFILE%\AppData\LocalLow\OMSI Map Editor\OMSI Map Editor\Player.log`. Die Kartenansicht meldet dort Neuzeichnungen über 50 ms.

## Naechste Schritte

Die offenen Punkte stehen in der [Roadmap](Roadmap.md) unter „Später“. Zuletzt abgeschlossen sind das Platzieren und Löschen von Objekten, davor die 2D-Draufsicht mit Bearbeiten und die Objektumrisse. Naheliegende nächste Schritte sind:

1. Ein gelöschtes Objekt in einer exportierten Karte in OMSI prüfen.
2. Genauere, nicht konvexe Umrisse für L-förmige Gebäude, Kreuzungen und Plätze.
3. Mehrfachauswahl und Übernahme von Objekten in die neue Kachel beim Verschieben über Kachelgrenzen; das Platzieren und Löschen liefert dafür die Bausteine.
4. Fahrplandaten lesend importieren, Haltestellen und Linien anzeigen, Chrono-Varianten als Ebenen.
5. Terrain und 3D-Ansicht.
