using System;
using System.Collections.Generic;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// M5 step 3 (ADR 098): the inspector as a window that hangs at the selected element and follows it while the view pans, zooms or orbits,
    /// joined to it by a thin line. It is compact by default (a narrow window about 400 px high: the main data open, the details folded) and
    /// a button in its header switches to the full view and back; while the pointer drags something in the map the window turns transparent
    /// and lets the pointer through. With several objects or splines selected it hangs at their middle and only gives their number (and the
    /// delete button); a row hangs at its clicked object, a tile chosen on its own at the tile's middle; with nothing chosen it docks at the right edge. A setting (Settings, General) puts the inspector at the right edge always, as before.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private const string InspectorAtEdgeKey = "Mapforge.InspectorAtEdge";
        private const float PointerInspectorFullHeight = 560f;
        private const float PointerInspectorCompactHeight = 400f;
        private const int PointerInspectorTickMilliseconds = 33;
        private const float InspectorLinkDotSize = 7f;

        /// <summary>The window hangs at the element (the default); false when it stays at the right edge (setting or <c>-inspectorAtEdge</c>).</summary>
        private bool inspectorAtPointer;

        /// <summary>The full view is chosen (header button or <c>-inspectorFull</c>); kept for the session, not between runs.</summary>
        private bool inspectorFull;

        private bool inspectorAtEdgeArgument;
        private Toggle inspectorAtEdgeToggle;
        private Button inspectorViewButton;
        private IconElement inspectorViewIcon;
        private VisualElement inspectorLink;
        private VisualElement inspectorLinkDot;
        private bool inspectorBusy;
        private bool inspectorBusyForced;
        private float inspectorBusyUntil;
        private int inspectorFollowedStamp;
        private const float InspectorBusyHoldSeconds = 0.12f;
        private readonly List<VisualElement> inspectorIgnoredWhileBusy = new List<VisualElement>();

        // What the placement set last, so a tick that finds nothing moved sets no style.
        private Vector2 lastInspectorCorner = new Vector2(float.NaN, float.NaN);
        private Vector2 lastInspectorAnchor = new Vector2(float.NaN, float.NaN);
        private bool lastInspectorLinked;
        private Vector2 lastInspectorLinkFrom;
        private bool inspectorDocked;

        /// <summary>A tile was chosen on its own (click on the ground, Esc to clear): the window hangs at its middle; false right after a map opened.</summary>
        private bool tileChosen;

        /// <summary>Where the row was clicked (map coordinates), so the window hangs at that object of the row; null when it is not known.</summary>
        private OmsiMapPoint? rowClickedAt;

        /// <summary>Groups the owner opened or folded in the compact window, by id; not the remembered folds of the full view.</summary>
        private readonly Dictionary<string, bool> compactGroupChoice = new Dictionary<string, bool>(StringComparer.Ordinal);

        // Groups that hold what the compact window is for; they start open there (the other groups start folded).
        private static readonly HashSet<string> CompactMainGroups = new HashSet<string>(StringComparer.Ordinal) { "spline-heights" };

        private bool InspectorAtEdge => inspectorAtEdgeArgument || PlayerPrefs.GetInt(InspectorAtEdgeKey, 0) != 0;

        private void ConfigureInspectorPlacement(string[] args)
        {
            if (inspectorFrame == null)
                return;
            inspectorFull = Array.IndexOf(args, "-inspectorFull") >= 0;
            inspectorAtEdgeArgument = Array.IndexOf(args, "-inspectorAtEdge") >= 0;
            inspectorBusyForced = Array.IndexOf(args, "-inspectorBusy") >= 0; // for a picture of the transparent window

            // The line from the window to the element and the dot on it lie under the window, in the workspace.
            inspectorLink = new VisualElement { name = "inspector-link", pickingMode = PickingMode.Ignore };
            inspectorLink.AddToClassList("inspector-link");
            inspectorLinkDot = new VisualElement { name = "inspector-link-dot", pickingMode = PickingMode.Ignore };
            inspectorLinkDot.AddToClassList("inspector-link-dot");
            inspectorLink.style.display = DisplayStyle.None;
            inspectorLinkDot.style.display = DisplayStyle.None;
            var host = inspectorFrame.parent;
            var at = host.IndexOf(inspectorFrame);
            host.Insert(at, inspectorLinkDot);
            host.Insert(at, inspectorLink);

            // The button of the header that switches between the compact and the full view.
            var header = inspectorFrame.Q(className: "floating-panel-header");
            if (header != null)
            {
                inspectorViewButton = new Button(ToggleInspectorFull) { name = "inspector-view-button" };
                inspectorViewButton.AddToClassList("panel-collapse-button");
                inspectorViewIcon = new IconElement("maximize-2");
                inspectorViewButton.Add(inspectorViewIcon);
                var collapse = inspectorCollapseButton != null ? header.IndexOf(inspectorCollapseButton) : -1;
                header.Insert(collapse >= 0 ? collapse : header.childCount, inspectorViewButton);
            }

            if (inspectorAtEdgeToggle != null)
                inspectorAtEdgeToggle.SetValueWithoutNotify(PlayerPrefs.GetInt(InspectorAtEdgeKey, 0) != 0);
            ApplyInspectorPlacementMode();
            inspectorFrame.schedule.Execute(UpdateInspectorPlace).Every(PointerInspectorTickMilliseconds);
            // A change of the window's size (a group opened, another selection) places it again at once, not at the next tick.
            inspectorFrame.RegisterCallback<GeometryChangedEvent>(_ => UpdateInspectorPlace());
        }

        /// <summary>Applies the choices: at the element or at the edge, compact or full; also called when the setting changes.</summary>
        private void ApplyInspectorPlacementMode()
        {
            if (inspectorFrame == null)
                return;
            inspectorAtPointer = !InspectorAtEdge;
            inspectorFrame.EnableInClassList("inspector-frame-pointer", inspectorAtPointer);
            inspectorFrame.EnableInClassList("inspector-frame-compact", !inspectorFull);
            if (inspectorViewIcon != null)
                inspectorViewIcon.SetIcon(inspectorFull ? "minimize-2" : "maximize-2");
            if (inspectorFull)
                LocBindings.Tooltip(inspectorViewButton, "inspector.view.compact");
            else
                LocBindings.Tooltip(inspectorViewButton, "inspector.view.full");
            if (!inspectorAtPointer)
            {
                // Back at the right edge: the style sheet places it, the bottom bar sets how far it reaches down.
                inspectorFrame.style.left = StyleKeyword.Null;
                inspectorFrame.style.top = StyleKeyword.Null;
                inspectorFrame.style.right = StyleKeyword.Null;
                inspectorFrame.style.maxHeight = StyleKeyword.Null; // computed: room below the pointer
                SetInspectorBusy(false);
                HideInspectorLink();
                ApplyBottomBarHeight(bottomBarHeight);
            }
            lastInspectorCorner = new Vector2(float.NaN, float.NaN);
            inspectorDocked = false;
        }

        private void ToggleInspectorFull()
        {
            inspectorFull = !inspectorFull;
            ApplyInspectorPlacementMode();
            RenderInspector();
            UpdateInspectorPlace();
        }

        /// <summary>Whether a group of the inspector starts open: the full view remembers the folds, the compact window only what was chosen in it this session.</summary>
        private bool InspectorGroupStartsOpen(string id, bool defaultOpen)
        {
            if (inspectorFull)
                return !IsFolded(id, defaultOpen);
            return compactGroupChoice.TryGetValue(id, out var open) ? open : CompactMainGroups.Contains(id);
        }

        private void RememberInspectorGroup(string id, bool open)
        {
            if (inspectorFull)
                RememberFold(id, open);
            else
                compactGroupChoice[id] = open;
        }

        /// <summary>Whether the compact window shows the selection as a sum only: several objects or several splines.</summary>
        private bool InspectorShowsSumOnly => !inspectorFull && (selection.Count > 1 || selection.Count == 0 && selectedSpline != null && moreSplines.Count > 0);

        private void SetUpInspectorEdgeSetting(VisualElement root)
        {
            inspectorAtEdgeToggle = root.Q<Toggle>("settings-inspector-edge");
            if (inspectorAtEdgeToggle == null)
                return;
            inspectorAtEdgeToggle.SetValueWithoutNotify(PlayerPrefs.GetInt(InspectorAtEdgeKey, 0) != 0);
            inspectorAtEdgeToggle.RegisterValueChangedCallback(change =>
            {
                PlayerPrefs.SetInt(InspectorAtEdgeKey, change.newValue ? 1 : 0);
                PlayerPrefs.Save();
                inspectorAtEdgeArgument = false;
                ApplyInspectorPlacementMode();
            });
            LocBindings.Text(inspectorAtEdgeToggle, "settings.inspector_at_edge");
        }

        /// <summary>
        /// Every tick (33 ms): turns the window transparent while the pointer drags in the map, puts it beside what it shows and draws the
        /// line to it, or docks it at the right edge when nothing is selected. Only sets styles that changed; it reads nothing from disk or the map.
        /// </summary>
        private void UpdateInspectorPlace()
        {
            if (inspectorFrame == null || !inspectorAtPointer || !inspectorShown || workspaceElement == null)
            {
                if (inspectorBusy)
                    SetInspectorBusy(false);
                if (lastInspectorLinked)
                    HideInspectorLink();
                return;
            }
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            PlaceInspector();
            RecordInspectorPlacementTime(started);
        }

        /// <summary>
        /// Called every frame (LateUpdate): when the camera moved since the last frame the window is placed again at once. The 33 ms tick alone
        /// moved it in steps while the map moved with every pointer event, which showed as flicker on the transparent window during a pan.
        /// </summary>
        private void FollowInspectorWithCamera()
        {
            if (mapCanvas == null || inspectorFrame == null || !inspectorAtPointer || !inspectorShown)
                return;
            var stamp = mapCanvas.CameraStamp;
            if (stamp == inspectorFollowedStamp)
                return;
            inspectorFollowedStamp = stamp;
            UpdateInspectorPlace();
        }

        // Diagnostics: what the placement costs each time, logged three times ("Inspector placement:") so a slow map shows in the player log.
        private int placementRuns;
        private int placementLogs;
        private double placementTotalMilliseconds;
        private double placementMaxMilliseconds;

        private void RecordInspectorPlacementTime(long started)
        {
            var milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            placementRuns++;
            placementTotalMilliseconds += milliseconds;
            placementMaxMilliseconds = Math.Max(placementMaxMilliseconds, milliseconds);
            if (placementRuns < 20 || placementLogs >= 3 || placementRuns % 20 != 0)
                return;
            placementLogs++;
            Debug.Log("Inspector placement: " + placementRuns + " runs, mean " + (placementTotalMilliseconds / placementRuns).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                      " ms, longest " + placementMaxMilliseconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " ms" + (mapCanvas != null && mapCanvas.ThreeDimensional ? " (3D)" : " (2D)") +
                      ", selected objects " + selection.Count);
        }

        private void PlaceInspector()
        {
            // Transparent from the first real movement until a moment after the pointer is let go: the short hold keeps the window from
            // switching back and forth between two events of one drag. Only the pointer dims it; a wheel zoom just moves it.
            var moving = inspectorBusyForced || mapCanvas != null && mapCanvas.IsMovingWithPointer;
            SetInspectorBusy(InspectorPlacement.IsBusy(moving, Time.unscaledTime, ref inspectorBusyUntil, InspectorBusyHoldSeconds));
            var width = workspaceElement.resolvedStyle.width;
            var height = workspaceElement.resolvedStyle.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
                return;
            var area = new Rect(0f, 0f, width, height - BottomBarLayout.DistanceAbove(bottomBarHeight, BottomBarLayout.PopupGap));
            var limit = Mathf.Max(120f, Mathf.Min(inspectorFull ? PointerInspectorFullHeight : PointerInspectorCompactHeight, area.height - 2f * InspectorPlacement.Margin));
            inspectorFrame.style.maxHeight = limit; // computed: room below the pointer
            inspectorFrame.style.bottom = StyleKeyword.Auto;

            if (!TryInspectorAnchor(out var panelPoint, out var panelExtent))
            {
                // Docked at the right edge, nothing to join the window to.
                if (!inspectorDocked)
                {
                    inspectorFrame.style.left = StyleKeyword.Auto;
                    inspectorFrame.style.right = InspectorPlacement.Margin;
                    inspectorFrame.style.top = InspectorPlacement.Margin;
                    inspectorDocked = true;
                    lastInspectorCorner = new Vector2(float.NaN, float.NaN);
                }
                HideInspectorLink();
                return;
            }
            inspectorDocked = false;
            var frameWidth = inspectorFrame.resolvedStyle.width;
            var frameHeight = inspectorFrame.resolvedStyle.height;
            var size = new Vector2(float.IsNaN(frameWidth) || frameWidth <= 0f ? 300f : frameWidth, float.IsNaN(frameHeight) || frameHeight <= 0f ? limit : frameHeight);
            var anchor = workspaceElement.WorldToLocal(panelPoint);
            Vector2 corner;
            if (panelExtent.HasValue)
            {
                // The element has a size on the screen (a tile, several objects, a spline): beside that rectangle, not at its middle.
                var extent = panelExtent.Value;
                var low = workspaceElement.WorldToLocal(extent.min);
                var high = workspaceElement.WorldToLocal(extent.max);
                corner = InspectorPlacement.Place(Rect.MinMaxRect(low.x, low.y, high.x, high.y), anchor, size, area, InspectorPlacement.Clearance, InspectorPlacement.Margin);
            }
            else
                corner = InspectorPlacement.Place(anchor, size, area, InspectorPlacement.Clearance, InspectorPlacement.Margin);
            if (float.IsNaN(lastInspectorCorner.x) || (corner - lastInspectorCorner).sqrMagnitude > 0.01f)
            {
                inspectorFrame.style.right = StyleKeyword.Auto;
                inspectorFrame.style.left = corner.x;
                inspectorFrame.style.top = corner.y;
                lastInspectorCorner = corner;
            }

            // The line runs from the nearest point of the window to the element, when that lies in the free area.
            var from = InspectorPlacement.NearestPoint(new Rect(corner, size), anchor);
            var length = (anchor - from).magnitude;
            if (!area.Contains(anchor) || length < 3f)
            {
                HideInspectorLink();
                return;
            }
            if (lastInspectorLinked && (anchor - lastInspectorAnchor).sqrMagnitude < 0.01f && (from - lastInspectorLinkFrom).sqrMagnitude < 0.01f)
                return;
            var degrees = Mathf.Atan2(anchor.y - from.y, anchor.x - from.x) * Mathf.Rad2Deg;
            inspectorLink.style.display = DisplayStyle.Flex;
            inspectorLink.style.left = from.x;
            inspectorLink.style.top = from.y - 0.5f;
            inspectorLink.style.width = length; // computed: measured link length
            inspectorLink.style.rotate = new Rotate(new Angle(degrees, AngleUnit.Degree));
            inspectorLinkDot.style.display = DisplayStyle.Flex;
            inspectorLinkDot.style.left = anchor.x - InspectorLinkDotSize / 2f;
            inspectorLinkDot.style.top = anchor.y - InspectorLinkDotSize / 2f;
            lastInspectorAnchor = anchor;
            lastInspectorLinkFrom = from;
            lastInspectorLinked = true;
        }

        private void HideInspectorLink()
        {
            if (inspectorLink != null)
                inspectorLink.style.display = DisplayStyle.None;
            if (inspectorLinkDot != null)
                inspectorLinkDot.style.display = DisplayStyle.None;
            lastInspectorLinked = false;
        }

        /// <summary>
        /// While the pointer drags in the map the window is transparent and lets the pointer through; afterwards it is as before. The children
        /// are set too (picking mode is not inherited), and those that did not take the pointer before stay as they were.
        /// </summary>
        private void SetInspectorBusy(bool busy)
        {
            if (busy == inspectorBusy || inspectorFrame == null)
                return;
            inspectorBusy = busy;
            inspectorFrame.EnableInClassList("inspector-frame-busy", busy);
            // The frame itself lets the pointer through too, so a window moving under the pointer never takes hover or events from the map.
            inspectorFrame.pickingMode = busy ? PickingMode.Ignore : PickingMode.Position;
            if (busy)
            {
                inspectorIgnoredWhileBusy.Clear();
                inspectorFrame.Query<VisualElement>().ForEach(element =>
                {
                    if (element.pickingMode == PickingMode.Position)
                    {
                        element.pickingMode = PickingMode.Ignore;
                        inspectorIgnoredWhileBusy.Add(element);
                    }
                });
                return;
            }
            foreach (var element in inspectorIgnoredWhileBusy)
                element.pickingMode = PickingMode.Position;
            inspectorIgnoredWhileBusy.Clear();
        }

        // The splines the anchor was worked out for, so it is only worked out again when the selection changes.
        private OmsiSplineRecord anchorSpline;
        private OmsiSplineRecord anchorLastMoreSpline;
        private int anchorMoreCount = -1;
        private OmsiTileSummary anchorSplineTile;
        private double anchorSplineX;
        private double anchorSplineY;
        private double anchorSplineHeight;

        /// <summary>
        /// Where the selection is on the screen, in panel coordinates: the object, the middle of several objects, the middle of the spline or of
        /// several splines, the clicked object of the selected row, the middle of a tile chosen on its own. False with nothing chosen at all, or
        /// when the view is behind the point (an anchor beside the screen is still given: the window then keeps to the nearest edge).
        /// </summary>
        private bool TryInspectorAnchor(out Vector2 panelPoint, out Rect? panelExtent)
        {
            panelPoint = default;
            panelExtent = null;
            if (mapCanvas == null || currentSummary == null)
                return false;
            var size = currentSummary.TileSizeMeters;
            if (selection.Count > 0)
            {
                OmsiTileSummary reference = null;
                double sumX = 0.0;
                double sumY = 0.0;
                double sumHeight = 0.0;
                var count = 0;
                foreach (var item in selection)
                {
                    if (item.Tile == null || item.Record == null)
                        continue;
                    if (reference == null)
                        reference = item.Tile;
                    sumX += item.Record.LocalX + (item.Tile.X - reference.X) * size;
                    sumY += item.Record.LocalY + (item.Tile.Y - reference.Y) * size;
                    sumHeight += item.Record.Height;
                    count++;
                }
                if (count == 0 || !TryAnchorAt(reference, sumX / count, sumY / count, sumHeight / count, false, out panelPoint))
                    return false;
                if (count > 1)
                {
                    // The outline of several objects: rebuilt only when the selection or a position changed, projected only when the camera moved.
                    var dirty = extentKind != InspectorPlacement.Anchor.Objects || extentCount != count || extentSumX != sumX || extentSumY != sumY || extentSumHeight != sumHeight;
                    if (dirty)
                    {
                        extentPoints.Clear();
                        foreach (var item in selection)
                            if (item.Tile != null && item.Record != null)
                                extentPoints.Add(new ExtentPoint { Tile = item.Tile, X = item.Record.LocalX, Y = item.Record.LocalY, Height = item.Record.Height });
                        extentKind = InspectorPlacement.Anchor.Objects;
                        extentCount = count;
                        extentSumX = sumX;
                        extentSumY = sumY;
                        extentSumHeight = sumHeight;
                    }
                    panelExtent = ProjectExtent(dirty, false);
                }
                return true;
            }
            switch (InspectorPlacement.ChooseAnchor(0, selectedSpline != null && selectedSplineTile != null, selectedRow != null && activeTool == EditorTool.Rows,
                        tileChosen && selectedTile != null))
            {
                case InspectorPlacement.Anchor.Spline:
                {
                    var dirty = WorkOutSplineAnchor(size);
                    if (!TryAnchorAt(anchorSplineTile, anchorSplineX, anchorSplineY, anchorSplineHeight, true, out panelPoint))
                        return false;
                    panelExtent = ProjectExtent(dirty, true);
                    return true;
                }
                case InspectorPlacement.Anchor.Row:
                    WorkOutRowAnchor(size);
                    return anchorRowTile != null && TryAnchorAt(anchorRowTile, anchorRowX, anchorRowY, anchorRowHeight, true, out panelPoint);
                case InspectorPlacement.Anchor.Tile:
                {
                    // A tile chosen on its own hangs beside its four corners (in 3D on the ground there); its middle is where the line ends.
                    // Off the screen the window docks like for any element.
                    if (!TryAnchorAt(selectedTile, size / 2.0, size / 2.0, 0.0, false, out panelPoint))
                        return false;
                    var dirty = extentKind != InspectorPlacement.Anchor.Tile || !ReferenceEquals(extentTile, selectedTile);
                    if (dirty)
                    {
                        extentPoints.Clear();
                        extentPoints.Add(new ExtentPoint { Tile = selectedTile, X = 0.0, Y = 0.0 });
                        extentPoints.Add(new ExtentPoint { Tile = selectedTile, X = size, Y = 0.0 });
                        extentPoints.Add(new ExtentPoint { Tile = selectedTile, X = size, Y = size });
                        extentPoints.Add(new ExtentPoint { Tile = selectedTile, X = 0.0, Y = size });
                        extentKind = InspectorPlacement.Anchor.Tile;
                        extentTile = selectedTile;
                    }
                    // A corner that cannot be projected (behind the camera) leaves the tile without a rectangle: the window hangs at the middle.
                    panelExtent = ProjectExtent(dirty, false, true);
                    return true;
                }
                default:
                    return false;
            }
        }

        private struct ExtentPoint
        {
            public OmsiTileSummary Tile;
            public double X;
            public double Y;
            public double Height;
        }

        // The element's points (tile-local), kept until the selection changes, and their screen rectangle, kept until the camera or the view changes.
        private readonly List<ExtentPoint> extentPoints = new List<ExtentPoint>();
        private readonly List<Vector2> extentScreen = new List<Vector2>();
        private InspectorPlacement.Anchor extentKind = InspectorPlacement.Anchor.None;
        private OmsiTileSummary extentTile;
        private int extentCount = -1;
        private double extentSumX;
        private double extentSumY;
        private double extentSumHeight;
        private int extentCameraStamp;
        private bool extentThreeDimensional;
        private Vector2 extentWorkspaceSize;
        private Rect? extentRect;
        private bool extentHasRect;

        /// <summary>
        /// The panel rectangle of <see cref="extentPoints"/>, worked out again only when the points changed, the camera moved, the view switched
        /// between 2D and 3D or the workspace changed its size; otherwise the last one. Null when too few points can be projected (or, with
        /// <paramref name="allOrNothing"/>, when one cannot).
        /// </summary>
        private Rect? ProjectExtent(bool pointsChanged, bool heightsAreAbsolute, bool allOrNothing = false)
        {
            var stamp = mapCanvas.CameraStamp;
            var workspaceSize = new Vector2(workspaceElement.resolvedStyle.width, workspaceElement.resolvedStyle.height);
            if (!pointsChanged && extentHasRect && stamp == extentCameraStamp && extentThreeDimensional == mapCanvas.ThreeDimensional && extentWorkspaceSize == workspaceSize)
                return extentRect;
            extentHasRect = true;
            extentCameraStamp = stamp;
            extentThreeDimensional = mapCanvas.ThreeDimensional;
            extentWorkspaceSize = workspaceSize;
            extentScreen.Clear();
            foreach (var point in extentPoints)
            {
                var height = point.Height;
                if (TryAnchorAt(point.Tile, point.X, point.Y, height, heightsAreAbsolute, out var screen))
                    extentScreen.Add(screen);
                else if (allOrNothing)
                {
                    extentRect = null;
                    return null;
                }
            }
            extentRect = InspectorPlacement.TryBounds(extentScreen, out var bounds) ? bounds : (Rect?)null;
            return extentRect;
        }

        // The object of the selected row the window hangs at, worked out once per row and click.
        private OmsiAttachmentRow anchorRow;
        private OmsiMapPoint? anchorRowClick;
        private OmsiTileSummary anchorRowTile;
        private double anchorRowX;
        private double anchorRowY;
        private double anchorRowHeight;

        /// <summary>
        /// The object of the selected row the window hangs at: the one that was clicked (the nearest to the click), or the first of the row when
        /// no click is known (the row was chosen in the menu of another element or by argument).
        /// </summary>
        private void WorkOutRowAnchor(double tileSize)
        {
            if (ReferenceEquals(anchorRow, selectedRow) && Nullable.Equals(anchorRowClick, rowClickedAt))
                return;
            anchorRow = selectedRow;
            anchorRowClick = rowClickedAt;
            anchorRowTile = null;
            if (selectedRow == null || selectedRow.Records.Count == 0)
                return;
            var placed = Placed(selectedRow);
            if (placed.Count == 0)
                return;
            var chosen = placed[0];
            if (rowClickedAt.HasValue)
            {
                var best = double.MaxValue;
                foreach (var item in placed)
                {
                    var dx = item.WorldX - rowClickedAt.Value.X;
                    var dy = item.WorldY - rowClickedAt.Value.Y;
                    var distance = dx * dx + dy * dy;
                    if (distance < best)
                    {
                        best = distance;
                        chosen = item;
                    }
                }
            }
            var tile = selectedRow.Records[0].Key;
            var origin = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, 0.0, 0.0, tileSize);
            anchorRowTile = tile;
            anchorRowX = chosen.WorldX - origin.X;
            anchorRowY = chosen.WorldY - origin.Y;
            anchorRowHeight = chosen.Height;
        }

        /// <summary>Works out the spline anchor and the points of the spline's centre line; true when it was worked out anew (the selection changed).</summary>
        private bool WorkOutSplineAnchor(double tileSize)
        {
            var lastMore = moreSplines.Count > 0 ? moreSplines[moreSplines.Count - 1].Value : null;
            if (ReferenceEquals(anchorSpline, selectedSpline) && anchorMoreCount == moreSplines.Count && ReferenceEquals(anchorLastMoreSpline, lastMore) && anchorSplineTile == selectedSplineTile &&
                extentKind == InspectorPlacement.Anchor.Spline)
                return false;
            extentKind = InspectorPlacement.Anchor.Spline;
            extentPoints.Clear();
            anchorSpline = selectedSpline;
            anchorMoreCount = moreSplines.Count;
            anchorLastMoreSpline = lastMore;
            anchorSplineTile = selectedSplineTile;
            double sumX = 0.0;
            double sumY = 0.0;
            double sumHeight = 0.0;
            var count = 0;
            void Add(OmsiTileSummary tile, OmsiSplineRecord spline)
            {
                OmsiMapGeometry.GetLocalFrameAt(spline, spline.LengthMeters / 2.0, out var local, out _);
                sumX += local.X + (tile.X - selectedSplineTile.X) * tileSize;
                sumY += local.Y + (tile.Y - selectedSplineTile.Y) * tileSize;
                sumHeight += spline.Height + (spline.EndHeight - spline.Height) / 2.0 + 0.1;
                count++;
                // Five points along the centre line give the spline's extent on the screen.
                for (var step = 0; step <= 4; step++)
                {
                    var fraction = step / 4.0;
                    OmsiMapGeometry.GetLocalFrameAt(spline, spline.LengthMeters * fraction, out var along, out _);
                    extentPoints.Add(new ExtentPoint { Tile = tile, X = along.X, Y = along.Y, Height = spline.Height + (spline.EndHeight - spline.Height) * fraction + 0.1 });
                }
            }

            Add(selectedSplineTile, selectedSpline);
            foreach (var pair in moreSplines)
                if (pair.Key != null && pair.Value != null)
                    Add(pair.Key, pair.Value);
            anchorSplineX = sumX / count;
            anchorSplineY = sumY / count;
            anchorSplineHeight = sumHeight / count;
            return true;
        }

        /// <summary>A tile-local point on the screen; <paramref name="heightIsAbsolute"/> says whether the height is above sea level or above the ground (3D only).</summary>
        private bool TryAnchorAt(OmsiTileSummary tile, double localX, double localY, double height, bool heightIsAbsolute, out Vector2 panelPoint)
        {
            var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, localX, localY, currentSummary.TileSizeMeters);
            if (!mapCanvas.ThreeDimensional)
                return mapCanvas.TryScreenPosition(world, out panelPoint);
            var absolute = heightIsAbsolute ? height : mapCanvas.GroundAtWorld(world) + height;
            return mapCanvas.TryScreenPosition3D(tile, localX, localY, absolute, out panelPoint);
        }
    }
}
