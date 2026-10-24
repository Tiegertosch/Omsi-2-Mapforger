# Straßenbau und Kreuzungen – Anforderungen aus den Briefings

Der Nutzer hat am 23. und 24. Oktober 2026 drei Entwicklungsbriefings übergeben: Straßenbau mit Gelände (deutsch), Straßenbau mit Knoten und Segmenten (englisch) und ein objektbasiertes Kreuzungssystem (englisch). Diese Seite fasst die verbindlichen Punkte zusammen und hält fest, wie sie im Projekt umgesetzt oder entschieden sind. Entscheidungen stehen als ADR in [Decisions.md](Decisions.md), der Stand in der [Roadmap](Roadmap.md).

## Grundsätze aller drei Briefings

- Erst das vorhandene Projekt untersuchen, dann planen, dann in kleinen, lauffähigen Schritten umsetzen; vorhandene Systeme erweitern statt parallele zu bauen.
- Eine einzige Datenquelle: 2D, 3D, Griffe und Vorschau sind Ansichten derselben Daten. Vorschau bleibt vorläufig, bis sie bestätigt wird; Abbrechen hinterlässt nichts.
- Stabile IDs, keine verwaisten Verweise nach Löschen, Rückgängig über das vorhandene System.
- Straßenhöhe und Geländehöhe sind getrennte Werte. Ungültige Geländeabfragen werden gemeldet, nie stillschweigend als 0 genommen.
- Nur geänderte Teile neu berechnen; reproduzierbare Ergebnisse.
- Keine OMSI-Formate, Felder oder Exportregeln erfinden; Annahmen anhand echter Dateien belegen und, wo möglich, in OMSI prüfen. Nichts als fertig ausgeben, was nicht geprüft ist.
- Befunde strukturiert melden: Schweregrad, betroffenes Element, Meldung, Position zum Markieren.

## Straßen (Briefings 1 und 2)

| Anforderung | Umsetzung |
|---|---|
| Netz aus Knoten und Segmenten, Kurven bearbeitbar | Straßen sind OMSI-Splines, das Netz ergibt sich aus ihren Nachbar-IDs (ADR 021, Wahl des Nutzers statt eigener Bézier-Kurven) |
| Höhenmodi: Gelände folgen, geglättet, manuell, unabhängig vom Gelände | Gelände folgen und manuell in Etappe 1; geglättet und unabhängig in Etappe 2 |
| Steigung, Sprünge, Knicke, ungültiges Gelände melden | `OmsiRoadPlanner` mit `OmsiRoadIssue` (Hinweis, Warnung, Fehler) |
| Straßenkörper mit Breite und Querschnitt | aus dem `.sli`-Profil, texturiert in 2D und 3D |
| Gelände an Straßen anpassen, nicht zerstörend: Original, Straßen, abgeleitete Anpassung und angezeigte Oberfläche getrennt | ADR 022, auf Wunsch des Nutzers vorgezogen |
| Andocken an Knoten; später Andocken an Segmente mit Teilen | Andocken an freie Spline-Enden in Etappe 1; Teilen und Einsetzen von Kreuzungen in Etappe 4a. Befund: Echte Karten zweigen Fahrbahnen über Kreuzungsobjekte ab, nicht über geteilte Splines (Berlin-Spandau: 39 freie Enden auf Gelenken und 68 mitten auf Splines, fast nur Gehwege, Dämme und Gleise); der Nutzer wünscht daher Kreuzungsobjekte, auch selbst erzeugte (Etappe 4b–4d) |
| Kreuzungen später, Anschlüsse vorbereiten | Etappe 3a: Anschluss an `[splinehelper]`-Punkte (ADR 024) |

## Kreuzungen (Briefing 3)

Kernsatz: Eine Kreuzung ist eine bearbeitbare Platzierung eines echten OMSI-Objekts. Das Anschlussmodell im Editor ist getrennt von der Objektplatzierung und den Spline-Daten, die OMSI liest.

- **Ressource und Instanz trennen:** Die Instanz verweist auf die `.sco`, kopiert deren Geometrie nicht. Eine fehlende Ressource lässt die Instanz bestehen, als ungelöst markiert.
- **Anschlussprofil:** Anschlusspunkte in Objektkoordinaten mit Position, Richtung nach außen, Höhe und Bezeichnung; geprüfte und vom Nutzer angelegte, ungeprüfte Profile unterscheiden; Profil mit Versionsnummer, wiederverwendbar je Ressource.
- **Befund im Projekt:** OMSI-Kreuzungsobjekte nennen ihre Anschlüsse als `[splinehelper]` in der `.sco` (Spline-Profil, x, Höhe, y, Richtung); `OmsiSceneryObject.SplineHelpers` liest sie bereits. Sie sind die erste Quelle geprüfter Anschlussprofile. Objekte ohne `[splinehelper]` gelten als „Unknown“, bis ein Profil angelegt ist.
- **Befund an echten Karten (24. Oktober 2026):** Angeschlossene Spline-Enden liegen exakt auf dem gedrehten Punkt, mit seiner Höhe und Richtung; ihre Nachbar-ID bleibt 0, eine ID-Verknüpfung zum Objekt gibt es nicht. Das Profil am Punkt ist nur ein Vorschlag (in Spandau bei 333 von 858 Anschlüssen ein anderes). Kreuzungen dürfen Stirn an Stirn liegen. Umsetzung in ADR 024.
- **Ablauf:** Ressource wählen, platzieren und drehen mit den vorhandenen Werkzeugen, Anschlusspunkt wählen, Straßenende anschließen, prüfen. Nichts wird stillschweigend verschoben, Einrasten nur ausdrücklich.
- **Prüfung:** Status `Valid`, `NeedsReview`, `Invalid`, `Unconnected`, `Unknown`, mit Symbol und Text, nicht nur Farbe; Abweichung von Position, Richtung und Höhe mit einstellbaren Toleranzen, die nicht als OMSI-Regel ausgegeben werden.
- **Export:** Kreuzung als `[object]`, Straßen als `[spline]`; Umrechnung an genau einer Stelle; vor dem Anspruch auf Kompatibilität mit einer bekannten Karte vergleichen und in OMSI laden.
- **Nicht im ersten Schritt:** Kreuzungsgeometrie erzeugen, Anschlüsse aus Meshes erraten, alle Objekte automatisch einordnen, Verkehrslogik.
