using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The AI paths view (ADR 046): "Paths" beside "Routes" shows the paths AI traffic and people use, read on a worker
    /// thread for the whole map as openOMSI builds its lanes. The project browser lists them by kind to show or hide, with
    /// the ends no other path joins; the inspector names the paths of the selected object or spline and their rules.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private static readonly string[] PathKindNames = { "Roads", "Footpaths", "Rails" };

        private Button pathsToggleButton;
        private List<OmsiMapPath> mapPaths;
        private Task<List<OmsiMapPath>> pathsTask;
        private int pathsGeneration;
        private bool pathsShown;
        private readonly bool[] shownPathKinds = { true, true, true };

        // The open end the view went to last, by its place in the list of open ends; -1 before the first.
        private int openEndCursor = -1;

        /// <summary>Puts "Paths" after "Routes" among the view controls, built like it.</summary>
        private void CreatePathsButton()
        {
            var parent = routesToggleButton?.parent;
            if (parent == null)
                return;
            pathsToggleButton = new Button(() => ShowPaths(!pathsShown))
            {
                tooltip = "Show the paths AI traffic and people use in the view from above: roads blue, footpaths green, rails yellow; wide with the " +
                          "right of way, dashed where they give way, red rings where no other path joins. They show closer in than " +
                          MapCanvasElement.PathMetersPerPixel.ToString("0.#", CultureInfo.InvariantCulture) + " m per pixel."
            };
            foreach (var name in routesToggleButton.GetClasses())
                pathsToggleButton.AddToClassList(name);
            pathsToggleButton.RemoveFromClassList("view-control-active");
            DecorateButton(pathsToggleButton, "spline", "Paths");
            parent.Insert(parent.IndexOf(routesToggleButton) + 1, pathsToggleButton);
        }

        /// <summary>Shows or hides the AI paths, reading them first when the map's are not known yet.</summary>
        private void ShowPaths(bool visible)
        {
            if (mapCanvas == null)
                return;
            pathsShown = visible && currentSummary != null;
            mapCanvas.ShowPaths = pathsShown;
            pathsToggleButton?.EnableInClassList("view-control-active", pathsShown);
            if (pathsShown && mapPaths == null && pathsTask == null)
                StartPathsBuild(currentSummary);
            if (pathsShown)
                SetStatus(DescribePaths());
            RenderProjectBrowser();
            RenderInspector();
        }

        private string DescribePaths()
        {
            if (mapPaths == null)
                return "Finding the AI paths of the map...";
            var open = mapPaths.FindAll(path => path.OpenStart || path.OpenEnd).Count;
            return mapPaths.Count + " AI paths shown, " + open + " with an end no other path joins" +
                   (mapCanvas != null && mapCanvas.MetersPerPixel > MapCanvasElement.PathMetersPerPixel ? "; zoom in to see them." : ".");
        }

        /// <summary>Forgets the paths of the map before, as after loading another map or a chrono date, and reads them again while shown.</summary>
        private void ResetPaths(OmsiMapSummary summary)
        {
            mapPaths = null;
            pathsTask = null;
            pathsGeneration++;
            openEndCursor = -1;
            mapCanvas?.SetPaths(null);
            if (pathsShown)
                StartPathsBuild(summary);
        }

        /// <summary>
        /// Reads the paths of the whole map on a worker thread; the paths before stay on screen until then. After an edit
        /// they come in quietly (<paramref name="announce"/> false), so the tool's own message stays in the status line.
        /// </summary>
        private void StartPathsBuild(OmsiMapSummary summary, bool announce = true)
        {
            if (mapCanvas == null || summary == null)
                return;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(summary.RootPath);
            var generation = ++pathsGeneration;
            var task = Task.Run(() => BuildPaths(summary, root));
            pathsTask = task;
            IVisualElementScheduledItem poll = null;
            poll = mapCanvas.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                // Another map or an edit started another build meanwhile.
                if (generation != pathsGeneration)
                    return;
                pathsTask = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatus("The AI paths could not be read: " + task.Exception?.GetBaseException().Message);
                    return;
                }
                mapPaths = task.Result;
                openEndCursor = -1;
                mapCanvas.SetPaths(mapPaths);
                if (pathsShown)
                {
                    if (announce)
                        SetStatus(DescribePaths());
                    RenderProjectBrowser();
                    RenderInspector();
                }
            }).Every(150);
        }

        private static List<OmsiMapPath> BuildPaths(OmsiMapSummary summary, string installationRoot)
        {
            var profiles = new OmsiSplineProfileCatalog(installationRoot);
            var objects = new Dictionary<string, OmsiSceneryObject>(StringComparer.OrdinalIgnoreCase);
            OmsiSceneryObject Read(string assetPath)
            {
                if (objects.TryGetValue(assetPath, out var cached))
                    return cached;
                OmsiSceneryObject sceneryObject = null;
                try
                {
                    var path = OmsiRelativePath.Resolve(installationRoot, assetPath);
                    if (File.Exists(path))
                        sceneryObject = OmsiSceneryObject.Parse(File.ReadAllBytes(path));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException ||
                                                  exception is ArgumentException)
                {
                    // An unreadable object has no paths.
                }
                objects.Add(assetPath, sceneryObject);
                return sceneryObject;
            }

            var paths = new List<OmsiMapPath>();
            foreach (var tile in summary.Tiles)
                paths.AddRange(OmsiMapPaths.OfTile(summary, tile, Read, profiles.Get));
            OmsiMapPaths.FindOpenEnds(paths);
            return paths;
        }

        /// <summary>The project browser's list of paths by kind, each shown or hidden by a click, and a way to the open ends.</summary>
        private VisualElement PathsSection()
        {
            var section = new Foldout { text = "AI paths (" + mapPaths.Count + ")", value = true };
            section.AddToClassList("browser-lines");
            for (var kind = 0; kind < 3; kind++)
            {
                var captured = kind;
                var ofKind = mapPaths.FindAll(path => path.Kind == captured);
                var open = ofKind.FindAll(path => path.OpenStart || path.OpenEnd).Count;
                var entry = new Button(() =>
                {
                    shownPathKinds[captured] = !shownPathKinds[captured];
                    mapCanvas?.SetPathKinds(shownPathKinds[0], shownPathKinds[1], shownPathKinds[2]);
                    openEndCursor = -1;
                    RenderProjectBrowser();
                })
                {
                    text = PathKindNames[kind] + "     " + ofKind.Count + " paths" + (open > 0 ? "  /  " + open + " open" : string.Empty),
                    tooltip = "Show or hide the " + PathKindNames[kind].ToLowerInvariant() + " among the paths."
                };
                entry.AddToClassList("browser-tile-entry");
                entry.EnableInClassList("browser-tile-entry-selected", shownPathKinds[kind]);
                section.Add(entry);
            }
            var ends = OpenEnds();
            var next = new Button(NextOpenEnd)
            {
                text = ends.Count > 0 ? "Next open end     " + (openEndCursor + 1) + " of " + ends.Count : "No open ends",
                tooltip = "Goes to the next end of a shown path that no other path of its kind joins within " +
                          OmsiMapPaths.LinkMeters.ToString("0.#", CultureInfo.InvariantCulture) + " m: AI traffic cannot go on there, or never comes."
            };
            next.AddToClassList("browser-tile-entry");
            next.SetEnabled(ends.Count > 0);
            section.Add(next);
            return section;
        }

        /// <summary>The open ends of the shown kinds of path: each path and whether its first point is open, else its last.</summary>
        private List<KeyValuePair<OmsiMapPath, bool>> OpenEnds()
        {
            var ends = new List<KeyValuePair<OmsiMapPath, bool>>();
            if (mapPaths == null)
                return ends;
            foreach (var path in mapPaths)
            {
                if (path.Kind < 0 || path.Kind > 2 || !shownPathKinds[path.Kind])
                    continue;
                if (path.OpenStart)
                    ends.Add(new KeyValuePair<OmsiMapPath, bool>(path, true));
                if (path.OpenEnd)
                    ends.Add(new KeyValuePair<OmsiMapPath, bool>(path, false));
            }
            return ends;
        }

        /// <summary>Centres the view on the next open end and selects the spline or object it belongs to.</summary>
        private void NextOpenEnd()
        {
            var ends = OpenEnds();
            if (ends.Count == 0 || mapCanvas == null)
                return;
            openEndCursor = (openEndCursor + 1) % ends.Count;
            var path = ends[openEndCursor].Key;
            var point = ends[openEndCursor].Value ? path.Points[0] : path.Points[path.Points.Length - 1];
            var tile = currentSummary.Tiles.Find(candidate => candidate.X == path.TileX && candidate.Y == path.TileY);
            if (tile != null && path.OnSpline && tile.Splines.Find(spline => spline.Id == path.ElementId) is OmsiSplineRecord spline)
                SelectSpline(tile, spline);
            else if (tile != null && tile.Objects.Find(item => item.Id == path.ElementId) is OmsiObjectRecord item)
                SelectObject(tile, item, false);
            mapCanvas.CenterOnWorld(point);
            RenderProjectBrowser();
            SetStatus("Open end " + (openEndCursor + 1) + " of " + ends.Count + ": the " + (ends[openEndCursor].Value ? "start" : "end") + " of path " + path.PathIndex +
                      " of " + (path.OnSpline ? "spline #" : "object #") + path.ElementId + " on tile " + path.TileX + ", " + path.TileY +
                      " - no other " + PathKindNoun(path.Kind) + " joins it.");
        }

        /// <summary>The inspector's list of the paths of the selected object or spline, with their rules.</summary>
        private void AddPathDetails(VisualElement container, OmsiTileSummary tile, int elementId, bool onSpline)
        {
            var paths = mapPaths.FindAll(path => path.TileX == tile.X && path.TileY == tile.Y && path.ElementId == elementId && path.OnSpline == onSpline);
            var heading = new Label("AI PATHS  " + paths.Count);
            heading.AddToClassList("inspector-section-heading");
            container.Add(heading);
            if (paths.Count == 0)
                AddInspectorRecord(container, "It has no paths.", "inspector-record-empty");
            foreach (var path in paths)
            {
                var length = 0.0;
                for (var index = 1; index < path.Points.Length; index++)
                    length += Math.Sqrt(Math.Pow(path.Points[index].X - path.Points[index - 1].X, 2.0) + Math.Pow(path.Points[index].Y - path.Points[index - 1].Y, 2.0));
                var text = "Path " + path.PathIndex + "  " + PathKindNoun(path.Kind) + ", " +
                           (path.Direction == 2 ? "both ways" : path.Direction == 1 ? "against" : "along") + ", " + length.ToString("F1", CultureInfo.InvariantCulture) + " m" +
                           (path.Blinker == 2 ? ", indicates left" : path.Blinker == 3 ? ", indicates right" : string.Empty) +
                           (path.OpenStart || path.OpenEnd ? "\nOpen at its " + (path.OpenStart && path.OpenEnd ? "start and end" : path.OpenStart ? "start" : "end") : string.Empty);
                foreach (var rule in path.Rules)
                    text += "\n" + (rule.Kills ? "takes away " : string.Empty) + DescribeRule(rule);
                AddInspectorRecord(container, text, "inspector-record");
            }
        }

        /// <summary>One path of a kind, as the status and the inspector name it.</summary>
        private static string PathKindNoun(int kind)
        {
            return kind == 1 ? "footpath" : kind == 2 ? "track" : "road lane";
        }

        private static string DescribeRule(OmsiPathRule rule)
        {
            var value = rule.Value.ToString("0.###", CultureInfo.InvariantCulture);
            switch (rule.Name.ToLowerInvariant())
            {
                case "priority":
                    return "priority " + value + (rule.Value > 128.5 ? " (right of way)" : rule.Value < 127.5 ? " (gives way)" : string.Empty);
                case "speedlimit":
                    return "speed limit " + value + " km/h";
                case "trafficdensity":
                    return "traffic density " + value + " for group " + rule.Extra.ToString("0", CultureInfo.InvariantCulture);
                default:
                    return rule.Name + " " + value;
            }
        }
    }
}
