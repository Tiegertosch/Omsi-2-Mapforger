using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The road builder in the road tool (ADR 049): New road type... in the road picker opens it in place of the picker;
    /// saving writes the road type under Splines\Mapforge in the installation, adds it to the picker's My roads and
    /// chooses it, ready to build. The pencil on a road type of My roads opens it again (stage 3): saving then writes the
    /// same file anew, so every spline of it changes, and makes the junctions made with it again, as one undo step.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private RoadBuilderPanel roadBuilder;

        // Road types made in this session, offered before the installation scan lists them.
        private readonly HashSet<string> madeRoadTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A road type being deleted once the search for maps and junctions naming it is done.
        private Task<string> roadTypeUsers;

        // While true, the object library stands in for the road builder to choose the object of a lane's row.
        private bool pickingRowObject;
        private VisualElement rowPickOptions;

        private void CreateRoadBuilder()
        {
            roadBuilder = new RoadBuilderPanel();
            roadBuilder.style.display = DisplayStyle.None;
            roadBuilder.SaveRequested += SaveRoadType;
            roadBuilder.ReplaceRequested += ReplaceRoadType;
            roadBuilder.DeleteRequested += DeleteRoadType;
            roadBuilder.FromChosenRequested += TakeChosenRoadApart;
            roadBuilder.RowObjectRequested += PickRowObject;
            roadBuilder.CloseRequested += () => ShowRoadBuilder(false);
            objectsPanelHost.Add(roadBuilder);
            roadPanel.EditRoadTypeRequested += EditRoadType;

            rowPickOptions = ToolCatalog.Options();
            var hint = LocBindings.NewLabel("roadbuilder.row.hint");
            hint.AddToClassList("panel-info");
            rowPickOptions.Add(hint);
            var back = LocBindings.WithTooltip(LocBindings.NewButton("roadbuilder.row.back", () => EndRowObjectPick(null)), "roadbuilder.row.back.tooltip");
            back.AddToClassList("map-control-button");
            rowPickOptions.Add(back);
        }

        /// <summary>Shows the object library in place of the road builder, to choose the object of the picked lane's row.</summary>
        private void PickRowObject()
        {
            if (objectsPanel == null || roadBuilder == null || roadBuilder.SelectedLane < 0 || currentSummary == null)
                return;
            UpdateObjectsCatalog();
            StartInstallationScan();
            pickingRowObject = true;
            objectsPanel.UseOptions(rowPickOptions, Loc.T("roadbuilder.row.panel", ("lane", roadBuilder.SelectedLane + 1)));
            objectsPanel.ShowState(OmsiObjectCategories.Name(OmsiObjectCategory.Trees), null, null);
            roadBuilder.style.display = DisplayStyle.None;
            objectsPanel.style.display = DisplayStyle.Flex;
            SetStatusT("roadbuilder.status.pick_row", ("lane", roadBuilder.SelectedLane + 1));
        }

        /// <summary>Back from the object library to the road builder, with the object chosen for the lane's row, or none.</summary>
        private void EndRowObjectPick(string assetPath)
        {
            if (!pickingRowObject)
                return;
            pickingRowObject = false;
            objectsPanel.style.display = DisplayStyle.None;
            objectsPanel.UseOptions(null, null);
            roadBuilder.style.display = DisplayStyle.Flex;
            if (assetPath != null)
            {
                var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
                var turn = OmsiRoadLane.SuggestedRowTurn(new OmsiObjectOutlineCatalog(root).Get(assetPath));
                roadBuilder.SetRowObject(assetPath, turn);
                SetStatusT("roadbuilder.status.row_set", ("name", Path.GetFileNameWithoutExtension(assetPath)), ("lane", roadBuilder.SelectedLane + 1),
                    ("turned", turn != 0.0 ? "yes" : "no"), ("degrees", turn));
            }
            else
                SetStatusT("roadbuilder.status.no_object");
        }

        /// <summary>
        /// After a click of the road tool built or changed roads (ADR 049, stage 4): the rows of objects their road types
        /// ask for, along each stretch the click touched, from junction or free end to junction or free end, evenly with half
        /// a gap at each end. Rows of the road type's objects already there are taken away first, so a junction set into a road later
        /// leaves no objects in it and the gaps stay even. All goes into the click's undo step; a row that cannot be laid
        /// or taken away is left as it is and named.
        /// </summary>
        private void LayRoadTypeRows(ObjectEditStep step)
        {
            var touched = new HashSet<int>();
            CollectChangedSplines(step, touched);
            if (touched.Count == 0 || currentSummary == null)
                return;
            var failed = new List<string>();
            // A road in a chrono variant gets no rows: they would go into the base map's tile files. Its bus lanes keep their rules,
            // written into the variant's change list (ADR 066).
            var objects = 0;
            var changes = EditingVariant ? new List<OmsiAttachmentChange>() : RelayRows(touched, null, null, failed, out objects);
            var busRules = KeepBusLanes(touched, null, null);
            if (changes.Count > 0 || busRules != null)
            {
                var parts = new List<ObjectEditStep> { step };
                foreach (var change in changes)
                    parts.Add(new ObjectEditStep { AttachmentChange = change });
                if (busRules != null)
                    parts.Add(new ObjectEditStep { SplineChange = busRules });
                undoSteps[undoSteps.Count - 1] = new ObjectEditStep
                {
                    Group = parts, Description = Loc.T(changes.Count > 0 ? "roadbuilder.step.with_rows" : "roadbuilder.step.with_bus", ("step", DescribeStep(step))) // i18n:keys
                };
                rowLayout = null;
                rowObjects = null;
                RefreshAfterChange(null);
            }
            if (objects > 0 || failed.Count > 0)
                SetStatus((statusLabel?.text ?? string.Empty) + (objects > 0 ? " " + Loc.T("roadbuilder.rows_laid", ("count", objects)) : string.Empty) +
                          (failed.Count > 0 ? " " + Loc.T("roadbuilder.rows_failed", ("list", string.Join(", ", failed))) : string.Empty));
        }

        /// <summary>
        /// Lays the rows of the road types along the stretches through the given splines anew (<see cref="TakeRows"/>, then
        /// <see cref="LayRows"/>). Returns the changes; rows not laid are named in <paramref name="failed"/>.
        /// </summary>
        private List<OmsiAttachmentChange> RelayRows(HashSet<int> touched, string previousPath, OmsiRoadType previous, List<string> failed, out int objects)
        {
            var kept = new HashSet<string>();
            var changes = TakeRows(touched, previousPath, previous, kept);
            changes.AddRange(LayRows(touched, kept, failed, out objects));
            return changes;
        }

        /// <summary>
        /// Takes away the rows along the stretches through the given splines whose object their road type uses, or used as
        /// <paramref name="previous"/>, the type <paramref name="previousPath"/> was before it was changed, so rows laid by
        /// older versions go too. Rows that must stay, as something names them, are put in <paramref name="kept"/>.
        /// </summary>
        private List<OmsiAttachmentChange> TakeRows(HashSet<int> touched, string previousPath, OmsiRoadType previous, HashSet<string> kept)
        {
            var changes = new List<OmsiAttachmentChange>();
            foreach (var stretch in Stretches(touched, previousPath, previous))
                foreach (var segment in stretch.Segments)
                    foreach (var record in new List<OmsiSplineAttachmentRecord>(segment.Tile.SplineAttachments))
                    {
                        if (record.IsRepeater || record.SplineIndex < 0 || record.SplineIndex >= segment.Tile.Splines.Count ||
                            !ReferenceEquals(segment.Tile.Splines[record.SplineIndex], segment.Spline))
                            continue;
                        var row = stretch.Type.Rows().Find(each => SameAsset(each.AssetPath, record.AssetPath)) ??
                                  stretch.Old.Find(each => SameAsset(each.AssetPath, record.AssetPath));
                        if (row == null)
                            continue;
                        try
                        {
                            var result = session.DeleteAttachmentRow(currentSummary, segment.Tile, record);
                            if (result.Succeeded)
                                changes.Add(result.Change);
                            else
                                kept.Add(row.AssetPath + "|" + row.X);
                        }
                        catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
                        {
                            Debug.LogException(exception);
                            kept.Add(row.AssetPath + "|" + row.X);
                        }
                    }
            return changes;
        }

        /// <summary>Lays the rows the road types ask for along the stretches through the given splines, but none that had to stay.</summary>
        private List<OmsiAttachmentChange> LayRows(HashSet<int> touched, HashSet<string> kept, List<string> failed, out int objects)
        {
            objects = 0;
            var changes = new List<OmsiAttachmentChange>();
            var layout = new OmsiSplineAttachmentLayout(currentSummary);
            foreach (var stretch in Stretches(touched, null, null))
                foreach (var row in stretch.Type.Rows())
                {
                    if (kept.Contains(row.AssetPath + "|" + row.X))
                    {
                        failed.Add(Loc.T("roadbuilder.rows_kept", ("name", Path.GetFileNameWithoutExtension(row.AssetPath))));
                        continue;
                    }
                    var length = stretch.To - stretch.From;
                    var count = length >= row.Interval ? (int)Math.Floor((length - row.Interval) / row.Interval + 1e-9) + 1 : 1;
                    var first = stretch.From + (length - (count - 1) * row.Interval) / 2.0;
                    var request = new OmsiAttachmentRowRequest
                    {
                        AssetPath = row.AssetPath, LateralOffset = row.X, Height = row.Height, HeadingDegrees = row.HeadingDegrees, Interval = row.Interval
                    };
                    var plan = OmsiAttachmentRows.Plan(currentSummary, layout, stretch.Tile, stretch.Spline, request, first, first + (count - 1) * row.Interval);
                    if (plan.Problem != null)
                    {
                        failed.Add(Path.GetFileNameWithoutExtension(row.AssetPath) + " (" + plan.Problem + ")");
                        continue;
                    }
                    try
                    {
                        changes.Add(session.AddAttachmentRow(currentSummary, plan));
                        objects += plan.Objects.Count;
                    }
                    catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
                    {
                        Debug.LogException(exception);
                        failed.Add(Path.GetFileNameWithoutExtension(row.AssetPath) + " (" + exception.Message + ")");
                    }
                }
            return changes;
        }

        /// <summary>
        /// The stretches through the given splines whose road type has rows, or had as <paramref name="previous"/>: each chain
        /// through such a spline over its splines of that type, from junction or free end to junction or free end.
        /// </summary>
        private List<RowStretch> Stretches(HashSet<int> touched, string previousPath, OmsiRoadType previous)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var types = new Dictionary<string, OmsiRoadType>(StringComparer.OrdinalIgnoreCase);
            OmsiRoadType TypeOf(string path)
            {
                path = path.Trim();
                if (types.TryGetValue(path, out var known))
                    return known;
                OmsiRoadType type = null;
                if (OmsiRoadTypeStore.IsMade(path))
                    try
                    {
                        var file = OmsiRelativePath.Resolve(root, path);
                        type = File.Exists(file) ? OmsiRoadTypeStore.Read(File.ReadAllBytes(file)) : null;
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        type = null;
                    }
                types[path] = type;
                return type;
            }

            var layout = new OmsiSplineAttachmentLayout(currentSummary);
            var stretches = new List<RowStretch>();
            var covered = new HashSet<OmsiSplineRecord>();
            foreach (var tile in currentSummary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    if (!touched.Contains(spline.Id) || covered.Contains(spline))
                        continue;
                    var before = previous != null && SameAsset(spline.AssetPath, previousPath) ? previous.Rows() : new List<OmsiRoadRow>();
                    if (!(TypeOf(spline.AssetPath)?.Rows().Count > 0) && before.Count == 0)
                        continue;
                    var stretch = new RowStretch { Tile = tile, Spline = spline, Type = TypeOf(spline.AssetPath) ?? new OmsiRoadType(), From = double.MaxValue, To = double.MinValue };
                    stretch.Old.AddRange(before);
                    foreach (var segment in layout.Chain(tile, spline))
                        if (SameAsset(segment.Spline.AssetPath, spline.AssetPath))
                        {
                            covered.Add(segment.Spline);
                            stretch.Segments.Add(segment);
                            stretch.From = Math.Min(stretch.From, segment.Begin);
                            stretch.To = Math.Max(stretch.To, segment.End);
                        }
                    stretches.Add(stretch);
                }
            return stretches;
        }

        /// <summary>
        /// Keeps the bus lanes of the road types of the given splines for buses (stage 4 of the AI paths): on each spline of
        /// a road type with bus lanes, or that had them as <paramref name="previous"/>, the rules no_cars and bus of all
        /// paths go and the paths of its bus lanes get them, as AI cars then keep off them and AI buses may use them. Null
        /// when nothing changes.
        /// </summary>
        private OmsiSplineChange KeepBusLanes(HashSet<int> splineIds, string previousPath, OmsiRoadType previous)
        {
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var types = new Dictionary<string, OmsiRoadType>(StringComparer.OrdinalIgnoreCase);
            var edits = new List<OmsiSplinePathRules>();
            foreach (var tile in currentSummary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    if (!splineIds.Contains(spline.Id) || !OmsiRoadTypeStore.IsMade(spline.AssetPath))
                        continue;
                    var path = spline.AssetPath.Trim();
                    if (!types.TryGetValue(path, out var type))
                    {
                        try
                        {
                            var file = OmsiRelativePath.Resolve(root, path);
                            type = File.Exists(file) ? OmsiRoadTypeStore.Read(File.ReadAllBytes(file)) : null;
                        }
                        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                        {
                            type = null;
                        }
                        types[path] = type;
                    }
                    var buses = type?.BusPaths() ?? new List<int>();
                    var had = previous != null && SameAsset(path, previousPath) && previous.BusPaths().Count > 0;
                    if (buses.Count == 0 && !had)
                        continue;
                    var edit = new OmsiSplinePathRules { Tile = tile, Spline = spline, Names = new[] { "no_cars", "bus" } };
                    foreach (var bus in buses)
                    {
                        edit.Rules.Add(new OmsiPathRule(bus, "no_cars", 0));
                        edit.Rules.Add(new OmsiPathRule(bus, "bus", 0));
                    }
                    edits.Add(edit);
                }
            if (edits.Count == 0)
                return null;
            try
            {
                var change = session.SetSplinePathRules(currentSummary, edits);
                if (change != null)
                    RefreshSplineViews(change);
                return change;
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatus((statusLabel?.text ?? string.Empty) + " " + Loc.T("roadbuilder.bus_failed", ("reason", exception.Message)));
                return null;
            }
        }

        /// <summary>A stretch of one road type along a chain, which its rows are laid along.</summary>
        private sealed class RowStretch
        {
            public OmsiTileSummary Tile;
            public OmsiSplineRecord Spline;
            public OmsiRoadType Type;

            /// <summary>The rows the road type had before it was changed, taken away with its rows now.</summary>
            public readonly List<OmsiRoadRow> Old = new List<OmsiRoadRow>();

            public readonly List<OmsiChainSegment> Segments = new List<OmsiChainSegment>();
            public double From;
            public double To;
        }

        /// <summary>The splines a step built or changed, by their IDs; deleted ones no longer exist and are passed over.</summary>
        private static void CollectChangedSplines(ObjectEditStep step, HashSet<int> ids)
        {
            if (step.Group != null)
                foreach (var part in step.Group)
                    CollectChangedSplines(part, ids);
            if (step.SplineChange != null)
                foreach (var id in step.SplineChange.SplineIds)
                    ids.Add(id);
        }

        private bool RoadBuilderShown => roadBuilder != null && roadBuilder.style.display == DisplayStyle.Flex;

        /// <summary>Shows the road builder in place of the road picker, starting a new road type, or the picker again.</summary>
        private void ShowRoadBuilder(bool show)
        {
            if (roadBuilder == null || roadPanel == null)
                return;
            if (show && activeTool != EditorTool.Road)
                SetTool(EditorTool.Road);
            // A read-only map keeps the road tool off, and the builder with it.
            if (show && activeTool != EditorTool.Road)
                return;
            if (show && currentSummary != null)
            {
                var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
                roadBuilder.SetInstallation(installation);
                roadBuilder.SetExcludedFolders(ExcludedContentFolders(installation));
            }
            if (show && !RoadBuilderShown)
                roadBuilder.Reset();
            if (show)
                roadBuilder.SetChosenProfile(roadPanel.Profile);
            roadBuilder.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            roadPanel.style.display = !show && activeTool == EditorTool.Road ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
                SetStatusT("roadbuilder.status.start");
        }

        /// <summary>The spline profiles to offer: the installation's and the road types made since it was listed, without those deleted since.</summary>
        private IEnumerable<string> RoadProfilePaths()
        {
            var paths = new List<string>();
            var root = currentSummary != null ? OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath) : null;
            bool Exists(string path) => root == null || !OmsiRoadTypeStore.IsMade(path) || File.Exists(OmsiRelativePath.Resolve(root, path));
            if (installationCatalog?.SplineProfiles != null)
                paths.AddRange(installationCatalog.SplineProfiles.FindAll(path => Exists(path)));
            foreach (var made in madeRoadTypes)
                if (Exists(made) && !paths.Exists(path => string.Equals(path, made, StringComparison.OrdinalIgnoreCase)))
                    paths.Add(made);
            return paths;
        }

        private void SaveRoadType(string name, OmsiRoadType type)
        {
            if (currentSummary == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.make_road_types"));
                return;
            }
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            string path;
            try
            {
                path = OmsiRoadTypeStore.Write(root, currentProjectName, name, type);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuilder.status.make_failed", ("reason", exception.Message));
                return;
            }
            madeRoadTypes.Add(path);
            ShowRoadBuilder(false);
            UpdateRoadProfiles();
            roadPanel.ChooseProfile(path);
            roadPanel.ShowTab("My roads");
            SetStatusT("roadbuilder.status.made", ("name", Path.GetFileNameWithoutExtension(path)), ("width", type.Width), ("path", path));
        }

        /// <summary>Opens a road type the builder made, as the pencil on its tile in My roads does.</summary>
        private void EditRoadType(string path)
        {
            if (currentSummary == null || string.IsNullOrWhiteSpace(path))
                return;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var name = Path.GetFileNameWithoutExtension(path);
            OmsiRoadType type;
            try
            {
                type = OmsiRoadTypeStore.Read(File.ReadAllBytes(OmsiRelativePath.Resolve(root, path)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                SetStatusT("roadbuilder.status.unreadable", ("name", name), ("reason", exception.Message));
                return;
            }
            if (type == null)
            {
                SetStatusT("roadbuilder.status.no_lanes", ("name", name));
                return;
            }
            ShowRoadBuilder(true);
            var uses = SplinesUsing(path);
            roadBuilder.Edit(path, type, uses);
            SetStatusT("roadbuilder.status.edit", ("name", name), ("uses", uses));
        }

        /// <summary>How many splines of the map use a profile.</summary>
        private int SplinesUsing(string path)
        {
            var count = 0;
            if (currentSummary != null)
                foreach (var tile in currentSummary.Tiles)
                    foreach (var spline in tile.Splines)
                        if (SameAsset(spline.AssetPath, path))
                            count++;
            return count;
        }

        private static bool SameAsset(string a, string b)
        {
            return a != null && b != null && string.Equals(a.Trim().Replace('/', '\\'), b.Trim().Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes the road type being edited anew (ADR 049, stage 3): every spline of it changes, and the junctions of the
        /// map made with it are made again for its new cross-section, keeping their markings; their lane connections stay
        /// while the road type keeps its traffic lanes each way. All of it is one undo step.
        /// </summary>
        private void ReplaceRoadType(OmsiRoadType type)
        {
            var path = roadBuilder.EditingPath;
            if (currentSummary == null || path == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.change_road_types"));
                return;
            }
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var name = Path.GetFileNameWithoutExtension(path);
            var junctions = JunctionsMadeWith(root, path);
            // The roads whose rows go before the junctions are made again: those of the road type and those joined to the
            // junctions, as rows on a road that is cut back to a moved connection point would keep it from moving.
            var rowRoads = new HashSet<int>();
            foreach (var tile in currentSummary.Tiles)
                foreach (var spline in tile.Splines)
                    if (SameAsset(spline.AssetPath, path))
                        rowRoads.Add(spline.Id);
            foreach (var junction in junctions)
            {
                var made = OmsiGeneratedJunction.Read(root, junction.Value.AssetPath, RoadProfiles().Get);
                var rebuild = made != null ? OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, junction.Key, junction.Value) : null;
                if (rebuild != null)
                    for (var arm = 0; arm < made.Arms.Count; arm++)
                        if (rebuild.JoinedAt(arm) != null)
                            rowRoads.Add(rebuild.JoinedAt(arm).Spline.Id);
            }
            OmsiRoadTypeChange change;
            try
            {
                change = OmsiRoadTypeStore.Replace(root, path, type);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                SetStatusT("roadbuilder.status.change_failed", ("name", name), ("reason", exception.Message));
                return;
            }
            var parts = new List<ObjectEditStep> { new ObjectEditStep { RoadTypeChange = change } };
            AfterRoadTypeChange(change.AssetPath);
            var previous = OmsiRoadTypeStore.Read(change.Before);
            var keptRows = new HashSet<string>();
            foreach (var rowChange in TakeRows(rowRoads, path, previous, keptRows))
                parts.Add(new ObjectEditStep { AttachmentChange = rowChange });

            var keepConnections = SameTrafficLanes(OmsiRoadTypeStore.Read(change.Before), type);
            var stayed = new List<string>();
            foreach (var junction in junctions)
            {
                var steps = RemakeJunction(junction.Key, junction.Value, keepConnections, out var problem);
                if (steps == null)
                {
                    stayed.Add("#" + junction.Value.Id + " (" + problem + ")");
                    continue;
                }
                foreach (var step in steps)
                {
                    if (step is OmsiSplineChange spline)
                    {
                        RefreshSplineViews(spline);
                        parts.Add(new ObjectEditStep { SplineChange = spline });
                        rowRoads.UnionWith(spline.SplineIds);
                    }
                    else if (step is OmsiObjectChange item)
                        parts.Add(new ObjectEditStep { TileX = item.Tile.X, TileY = item.Tile.Y, ObjectId = item.Record.Id, Change = item });
                }
            }
            roadPorts = null;

            // The rows stand anew along every road of the type, at the new cross-section, and along the roads led to the junctions.
            foreach (var tile in currentSummary.Tiles)
                foreach (var spline in tile.Splines)
                    if (SameAsset(spline.AssetPath, path))
                        rowRoads.Add(spline.Id);
            var rowsFailed = new List<string>();
            var rowChanges = LayRows(rowRoads, keptRows, rowsFailed, out var rowObjectCount);
            foreach (var rowChange in rowChanges)
                parts.Add(new ObjectEditStep { AttachmentChange = rowChange });
            // Its bus lanes stay kept for buses where their paths are now.
            var ofType = new HashSet<int>();
            foreach (var tile in currentSummary.Tiles)
                foreach (var spline in tile.Splines)
                    if (SameAsset(spline.AssetPath, path))
                        ofType.Add(spline.Id);
            var busRules = KeepBusLanes(ofType, path, previous);
            if (busRules != null)
                parts.Add(new ObjectEditStep { SplineChange = busRules });
            rowLayout = null;
            rowObjects = null;
            PushHistory(new ObjectEditStep { Group = parts, Description = Loc.T("roadbuilder.step.change", ("name", name)) });
            StartInstallationScan(true);
            RefreshAfterChange(null);
            ShowRoadBuilder(false);
            roadPanel.ChooseProfile(path);
            var uses = SplinesUsing(path);
            var remade = junctions.Count - stayed.Count;
            SetStatus(Loc.T("roadbuilder.status.changed", ("name", name), ("width", type.Width), ("uses", uses), ("remade", remade), ("every", keepConnections ? "no" : "yes"),
                ("hasStayed", stayed.Count > 0 ? "yes" : "no"), ("stayed", string.Join(", ", stayed)), ("rows", rowObjectCount),
                ("hasFailed", rowsFailed.Count > 0 ? "yes" : "no"), ("failed", string.Join(", ", rowsFailed))));
        }

        /// <summary>The junctions on the map the editor made with a profile on one of their arms, by tile.</summary>
        private List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>> JunctionsMadeWith(string root, string path)
        {
            var found = new List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>>();
            var known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in currentSummary.Tiles)
                foreach (var record in tile.Objects)
                {
                    if (!OmsiGeneratedJunction.IsMadeHere(record.AssetPath))
                        continue;
                    if (!known.TryGetValue(record.AssetPath, out var uses))
                    {
                        var made = OmsiGeneratedJunction.Read(root, record.AssetPath, null);
                        uses = made != null && made.Arms.Exists(arm => SameAsset(arm.ProfilePath, path));
                        known[record.AssetPath] = uses;
                    }
                    if (uses)
                        found.Add(new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, record));
                }
            return found;
        }

        /// <summary>Whether two road types have as many lanes for traffic each way, so lane connections through junctions still fit.</summary>
        private static bool SameTrafficLanes(OmsiRoadType before, OmsiRoadType after)
        {
            if (before == null || after == null)
                return false;
            int Count(OmsiRoadType type, OmsiRoadLaneDirection direction) =>
                type.Lanes.FindAll(lane => (lane.Kind == OmsiRoadLaneKind.Driving || lane.Kind == OmsiRoadLaneKind.Bus) && lane.Direction == direction).Count;
            return Count(before, OmsiRoadLaneDirection.Forward) == Count(after, OmsiRoadLaneDirection.Forward) &&
                   Count(before, OmsiRoadLaneDirection.Backward) == Count(after, OmsiRoadLaneDirection.Backward);
        }

        /// <summary>
        /// Makes a junction the editor made again with the profiles of its arms as they are now, as changing its markings does,
        /// and leads the roads joined to its moved connection points. Null with the reason when it stays as it was.
        /// </summary>
        private List<object> RemakeJunction(OmsiTileSummary tile, OmsiObjectRecord record, bool keepConnections, out string problem)
        {
            problem = null;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var made = OmsiGeneratedJunction.Read(root, record.AssetPath, RoadProfiles().Get);
            if (made == null)
            {
                problem = Loc.T("roadbuilder.problem.unreadable");
                return null;
            }
            var rebuild = OmsiJunctionRebuild.Prepare(currentSummary, Junctions(), made, tile, record);
            if (rebuild == null)
            {
                problem = Loc.T("roadbuilder.problem.mismatch");
                return null;
            }
            var plain = PlainRoadTexture();
            var turn = rebuild.PlanTurn(0, made.Arms[0].HeadingDegrees, plain);
            if (turn.HasErrors)
            {
                problem = turn.Problem ?? Loc.T("roadbuilder.problem.keep");
                return null;
            }
            string assetPath;
            try
            {
                assetPath = OmsiGeneratedObjectStore.Write(root, currentProjectName, made.Name,
                    WithSignalReport(OmsiJunctionGenerator.Junction(turn.Arms, plain, "Mapforge " + made.Name, made.Markings, made.Areas, made.Arrows, keepConnections ? made.Connections : null, made.Signals)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                problem = exception.Message;
                return null;
            }
            OmsiJunctionInsertionResult result;
            try
            {
                result = session.RebuildJunction(currentSummary, rebuild, turn, assetPath);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is IOException || exception is ArgumentException)
            {
                Debug.LogException(exception);
                problem = exception.Message;
                return null;
            }
            if (!result.Succeeded)
            {
                problem = Loc.T("roadbuilder.problem.references", ("references", DescribeReferences(result.Blockers)));
                return null;
            }
            return new List<object>(result.Steps);
        }

        /// <summary>
        /// After a road type's file changed, by saving, deleting, undo or redo: the profiles are read again and the splines
        /// of it drawn anew; the road picker draws its preview again and lists it while it exists.
        /// </summary>
        private void AfterRoadTypeChange(string path)
        {
            roadProfiles = null;
            session?.ForgetProfiles();
            roadGuideLines = null;
            if (currentSummary == null)
                return;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            if (File.Exists(OmsiRelativePath.Resolve(root, path)))
                madeRoadTypes.Add(path);
            else
                madeRoadTypes.Remove(path);
            var tiles = currentSummary.Tiles.FindAll(tile => tile.Splines.Exists(spline => SameAsset(spline.AssetPath, path)));
            if (groundRenderer != null && !groundRenderer.RefreshProfiles(tiles))
                Debug.Log("Spline surfaces of the changed road type are built once the ground has finished loading.");
            mapCanvas?.RefreshSplines();
            roadPanel?.ForgetProfile(path);
            UpdateRoadProfiles();
        }

        /// <summary>
        /// Deletes the road type being edited, once no spline of the map uses it and the search finds no map of the
        /// installation, no workspace tile and no made junction naming it. Undo writes it back.
        /// </summary>
        private void DeleteRoadType()
        {
            var path = roadBuilder.EditingPath;
            if (currentSummary == null || path == null)
                return;
            if (!CanEdit)
            {
                SetStatus(ReadOnlyMessage("readonly.action.delete_road_types"));
                return;
            }
            var name = Path.GetFileNameWithoutExtension(path);
            var uses = SplinesUsing(path);
            if (uses > 0)
            {
                SetStatusT("roadbuilder.status.stays_splines", ("name", name), ("uses", uses));
                return;
            }
            if (roadTypeUsers != null && !roadTypeUsers.IsCompleted)
                return;
            var root = OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath);
            var workspace = session?.Workspace?.WorkspacePath;
            var task = Task.Run(() => OmsiRoadTypeStore.UsedBy(root, workspace, path));
            roadTypeUsers = task;
            SetStatusT("roadbuilder.status.looking", ("name", name));
            IVisualElementScheduledItem poll = null;
            poll = roadBuilder.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                if (!ReferenceEquals(task, roadTypeUsers))
                    return;
                roadTypeUsers = null;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatusT("roadbuilder.status.look_failed", ("name", name), ("reason", task.Exception?.GetBaseException().Message ?? string.Empty));
                    return;
                }
                if (task.Result != null)
                {
                    SetStatusT("roadbuilder.status.stays_named", ("name", name), ("users", task.Result),
                        ("made", task.Result.EndsWith(".sco", StringComparison.OrdinalIgnoreCase) ? "yes" : "no"));
                    return;
                }
                if (!string.Equals(roadBuilder.EditingPath, path, StringComparison.OrdinalIgnoreCase))
                    return;
                OmsiRoadTypeChange change;
                try
                {
                    change = OmsiRoadTypeStore.Delete(root, path);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
                {
                    Debug.LogException(exception);
                    SetStatusT("roadbuilder.status.delete_failed", ("name", name), ("reason", exception.Message));
                    return;
                }
                PushHistory(new ObjectEditStep { RoadTypeChange = change, Description = Loc.T("roadbuilder.step.delete", ("name", name)) });
                AfterRoadTypeChange(path);
                ShowRoadBuilder(false);
                SetStatusT("roadbuilder.status.deleted", ("name", name));
            }).Every(100);
        }

        /// <summary>Takes the road chosen in the road tool apart into lanes, as From the chosen road does.</summary>
        private void TakeChosenRoadApart()
        {
            var path = roadPanel?.Profile;
            if (currentSummary == null || path == null)
                return;
            var name = Path.GetFileNameWithoutExtension(path);
            var notes = new List<string>();
            OmsiRoadType type = null;
            if (OmsiRoadTypeStore.IsMade(path))
            {
                try
                {
                    type = OmsiRoadTypeStore.Read(File.ReadAllBytes(OmsiRelativePath.Resolve(OmsiObjectOutlineCatalog.InstallationRootOfMap(currentSummary.RootPath), path)));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    type = null;
                }
            }
            if (type == null)
            {
                var profile = RoadProfiles().Get(path);
                type = profile != null ? OmsiRoadType.FromProfile(profile, notes) : null;
            }
            if (type == null)
            {
                SetStatusT("roadbuilder.status.nothing_to_split", ("name", name));
                return;
            }
            roadBuilder.SetType(type);
            SetStatusT("roadbuilder.status.split", ("name", name), ("count", type.Lanes.Count), ("width", type.Width), ("hasNotes", notes.Count > 0 ? "yes" : "no"),
                ("notes", string.Join(" ", notes)));
        }

        /// <summary>
        /// For command-line checks: opens the road builder, a road type made with it (<paramref name="edit"/>) or the lanes
        /// taken from a profile (<paramref name="from"/>), takes the lanes given (as in the .sli line, such as
        /// <c>"walk 3 | drive 3.25 back | drive 3.25 | walk 3"</c>), picks a lane or the line after lane n, and saves under
        /// a name, saves the road type edited over itself or deletes it when asked. The status goes to the log as "Road builder:".
        /// </summary>
        private void RunRoadBuilderArgument(string lanes, string select, string line, string saveName, string edit, string from, bool replace, bool delete)
        {
            ShowRoadBuilder(true);
            if (edit != null)
                EditRoadType(edit.Trim());
            if (from != null)
            {
                roadPanel.ChooseProfile(from.Trim());
                roadBuilder.SetChosenProfile(roadPanel.Profile);
                TakeChosenRoadApart();
            }
            if (!string.IsNullOrWhiteSpace(lanes))
            {
                if (OmsiRoadType.TryParse(lanes, out var type))
                    roadBuilder.SetType(type);
                else
                    Debug.LogWarning("Road builder: the lanes \"" + lanes + "\" cannot be read.");
            }
            if (int.TryParse(select, out var lane))
                roadBuilder.SelectLane(lane - 1);
            if (int.TryParse(line, out var after))
                roadBuilder.SelectLine(after - 1);
            var shown = roadBuilder.Type.Describe();
            if (saveName != null)
            {
                roadBuilder.SetName(saveName);
                SaveRoadType(roadBuilder.RoadName, roadBuilder.Type);
            }
            else if (replace && roadBuilder.EditingPath != null)
                ReplaceRoadType(roadBuilder.Type);
            else if (delete && roadBuilder.EditingPath != null)
                DeleteRoadType();
            Debug.Log("Road builder: " + shown + " | " + statusLabel?.text);
        }
    }
}
