using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The cross-section of a road type seen from above, left to right in the direction the road is built (briefing of 18 November 2026): one block
    /// per element, as wide as the element is, with its number, arrow, short name and width as far as they fit, its surface (the texture, or the
    /// colour of its kind), tram rails and a mark for a row of objects; the lines between the elements as they will be painted; and clickable
    /// boundaries between neighbours that select the line (decision 3). All elements share one scale, so the proportions are true; when the
    /// narrowest would be drawn too narrow to hit, the strip zooms in uniformly and scrolls sideways (<see cref="RoadTypeWindowRules.StripScale"/>)
    /// and keeps the selection in view. A click selects, a drag reorders, the arrow keys step through the elements when the strip has the focus.
    /// It follows an <see cref="OmsiRoadTypeDraft"/> by its <see cref="OmsiRoadTypeDraft.Version"/> and changes only the blocks that changed,
    /// so a width typed in the properties does not rebuild the strip.
    /// </summary>
    internal sealed class RoadCrossSectionElement : VisualElement
    {
        /// <summary>The height of the strip's drawing; the scroll bar, when there is one, comes below.</summary>
        public const float StripHeight = 120f;

        private const float ScrollBarHeight = 12f;
        private const float BoundaryPixels = 4f;
        private const float DragPixels = 6f;

        private sealed class View
        {
            public int Id;
            public VisualElement Root;
            public Label Number;
            public IconElement Arrow;
            public Label Short;
            public Label Width;
            public VisualElement RailLeft;
            public VisualElement RailRight;
            public IconElement RowIcon;
            public string Signature;
            public string ShortText;
            public bool HasDirection;
            public bool HasRow;
            public string WidthText;
            public float Left;
            public float Pixels;
        }

        private readonly RoadSurfaceCatalog surfaces;
        private readonly ScrollView scroll;
        private readonly VisualElement track;
        private readonly MarkingsOverlay overlay;
        private readonly VisualElement dropMark;
        private readonly Dictionary<int, View> views = new Dictionary<int, View>();
        private readonly List<VisualElement> handles = new List<VisualElement>();
        private readonly List<int> handleLeftIds = new List<int>();
        private OmsiRoadTypeDraft draft;
        private OmsiRoadTypeDraft syncedDraft;
        private int syncedVersion = -1;
        private float syncedViewport = -1f;
        private int syncedSelection = int.MinValue;
        private float[] edges = new float[1];
        private float scale;

        // A press on an element: where it began, whether it became a drag and the gap it would be dropped into.
        private int pressedId = -1;
        private float pressedX;
        private bool dragging;
        private int dropGap = -1;

        public RoadCrossSectionElement(RoadSurfaceCatalog surfaces)
        {
            this.surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
            AddToClassList("road-section");
            focusable = true;
            tabIndex = 0;

            scroll = new ScrollView(ScrollViewMode.Horizontal) { verticalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("road-section-scroll");
            scroll.style.height = StripHeight; // computed: strip height from the zoom state
            Add(scroll);
            track = scroll.contentContainer;
            track.AddToClassList("road-section-track");
            track.style.height = StripHeight; // computed: strip height

            overlay = new MarkingsOverlay();
            track.Add(overlay);
            dropMark = new VisualElement { pickingMode = PickingMode.Ignore };
            dropMark.AddToClassList("road-section-drop");
            dropMark.style.display = DisplayStyle.None;
            track.Add(dropMark);

            scroll.RegisterCallback<GeometryChangedEvent>(_ => Sync());
            RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        /// <summary>An element was clicked (by its id).</summary>
        public event Action<int> ElementClicked;

        /// <summary>A boundary was clicked, by the id of the element left of it.</summary>
        public event Action<int> BoundaryClicked;

        /// <summary>An element was dragged to another place: its id and the index it should take.</summary>
        public event Action<int, int> MoveRequested;

        /// <summary>The first problem of an element as a translated text, or null; the window sets it (the texts come from the window's language).</summary>
        public Func<int, string> ProblemOf { get; set; }

        /// <summary>The pixels per metre the strip is drawn at; 0 before it was laid out. Zoomed in when it is more than the width fitted.</summary>
        public float Scale => scale;

        public bool Zoomed => scroll.style.height.value.value > StripHeight + 1f; // computed: strip height from the zoom state

        public float TotalPixels => edges[edges.Length - 1];

        public void SetDraft(OmsiRoadTypeDraft value)
        {
            draft = value;
            Sync(true);
        }

        /// <summary>Brings the blocks up to date with the draft; does nothing when neither the draft nor the room changed, unless forced.</summary>
        public void Sync(bool force = false)
        {
            if (draft == null)
                return;
            var viewport = Mathf.Floor(scroll.contentViewport.layout.width);
            if (viewport < 1f)
                return;
            var selection = draft.SelectionKind == OmsiRoadDraftSelectionKind.None ? -1 : draft.SelectedId * 2 + (draft.SelectionKind == OmsiRoadDraftSelectionKind.Boundary ? 1 : 0);
            if (!force && ReferenceEquals(draft, syncedDraft) && draft.Version == syncedVersion && Mathf.Abs(viewport - syncedViewport) < 0.5f && selection == syncedSelection)
                return;
            syncedDraft = draft;
            syncedVersion = draft.Version;
            syncedViewport = viewport;
            syncedSelection = selection;

            var ids = draft.ElementIds;
            var count = ids.Count;
            var lanes = new List<OmsiRoadLane>(count);
            var widths = new double[count];
            for (var index = 0; index < count; index++)
            {
                lanes.Add(draft.LaneOf(ids[index]));
                widths[index] = lanes[index].Width;
            }

            // The blocks that went away, and the new ones.
            var structure = false;
            var gone = new List<int>();
            foreach (var pair in views)
                if (draft.IndexOf(pair.Key) < 0)
                    gone.Add(pair.Key);
            foreach (var id in gone)
            {
                views[id].Root.RemoveFromHierarchy();
                views.Remove(id);
                structure = true;
            }
            foreach (var id in ids)
                if (!views.ContainsKey(id))
                {
                    var created = CreateView(id);
                    views[id] = created;
                    track.Add(created.Root);
                    structure = true;
                }

            // The scale: the widths fitted into the room, or zoomed in so the narrowest stays hittable.
            scale = RoadTypeWindowRules.StripScale(widths, viewport);
            edges = RoadTypeWindowRules.StripEdges(widths, scale);
            var zoomed = edges[count] > viewport + 0.5f;
            scroll.style.height = StripHeight + (zoomed ? ScrollBarHeight : 0f); // computed: strip height from the zoom state
            track.style.width = Mathf.Max(viewport, edges[count]); // computed: track width from the lane edges

            var problems = new Dictionary<int, string>();
            for (var index = 0; index < count; index++)
            {
                var id = ids[index];
                var view = views[id];
                view.Left = edges[index];
                view.Pixels = edges[index + 1] - edges[index];
                view.Root.style.left = view.Left;
                view.Root.style.width = view.Pixels; // computed: lane width in pixels
                var problem = ProblemOf?.Invoke(id);
                if (problem != null)
                    problems[id] = problem;
                Update(view, lanes[index], index, problem);
                ThinLabels(view);
            }

            // The boundaries between neighbours.
            while (handles.Count > Math.Max(0, count - 1))
            {
                handles[handles.Count - 1].RemoveFromHierarchy();
                handles.RemoveAt(handles.Count - 1);
                handleLeftIds.RemoveAt(handleLeftIds.Count - 1);
                structure = true;
            }
            while (handles.Count < count - 1)
            {
                var handle = new VisualElement();
                handle.AddToClassList("road-section-boundary");
                var slot = handles.Count;
                handle.RegisterCallback<ClickEvent>(evt =>
                {
                    if (slot < handleLeftIds.Count)
                    {
                        Focus();
                        BoundaryClicked?.Invoke(handleLeftIds[slot]);
                    }
                    evt.StopPropagation();
                });
                handles.Add(handle);
                handleLeftIds.Add(-1);
                track.Add(handle);
                structure = true;
            }
            var snapshots = new List<MarkingsOverlay.Boundary>();
            for (var index = 0; index + 1 < count; index++)
            {
                var handle = handles[index];
                handleLeftIds[index] = ids[index];
                handle.style.left = edges[index + 1] - BoundaryPixels - 0.5f;
                handle.style.width = BoundaryPixels * 2f + 1f; // computed: handle width from the boundary pixels
                var selected = draft.SelectionKind == OmsiRoadDraftSelectionKind.Boundary && draft.SelectedId == ids[index];
                handle.EnableInClassList("road-section-boundary-selected", selected);
                var marking = draft.MarkingAfter(ids[index]);
                handle.tooltip = LocBindings.CardTooltip(Loc.T("roadbuilder.window.line.tooltip", ("number", index + 1), ("marking", Loc.T(MarkingKey(marking)))), // i18n:keys
                    Loc.T("roadbuilder.window.line.tooltip_text"));
                snapshots.Add(new MarkingsOverlay.Boundary { X = edges[index + 1], Marking = marking, LeftRaised = lanes[index].Raised, RightRaised = lanes[index + 1].Raised });
            }
            overlay.Set(snapshots);

            if (structure)
            {
                overlay.BringToFront();
                foreach (var handle in handles)
                    handle.BringToFront();
                dropMark.BringToFront();
            }
            RevealSelection();
        }

        /// <summary>Scrolls the selected element or boundary into view; a strip that fits needs no scrolling.</summary>
        public void RevealSelection()
        {
            if (draft == null || draft.SelectionKind == OmsiRoadDraftSelectionKind.None || !Zoomed && edges[edges.Length - 1] <= scroll.contentViewport.layout.width + 0.5f)
                return;
            var index = draft.SelectedIndex;
            if (index < 0 || index >= edges.Length - 1)
                return;
            var left = edges[index];
            var right = edges[index + 1];
            if (draft.SelectionKind == OmsiRoadDraftSelectionKind.Boundary)
            {
                left = right - BoundaryPixels;
                right += BoundaryPixels;
            }
            var offset = scroll.scrollOffset;
            var target = RoadTypeWindowRules.ScrollToReveal(left, right, offset.x, scroll.contentViewport.layout.width);
            if (Mathf.Abs(target - offset.x) > 0.5f)
                scroll.scrollOffset = new Vector2(target, 0f);
        }

        private View CreateView(int id)
        {
            var view = new View { Id = id, Root = new VisualElement { pickingMode = PickingMode.Position } };
            view.Root.AddToClassList("road-section-element");
            view.Number = new Label();
            view.Number.AddToClassList("road-section-number");
            view.Arrow = new IconElement("arrow-up");
            view.Arrow.AddToClassList("road-section-arrow");
            view.Short = new Label();
            view.Short.AddToClassList("road-section-kind");
            view.Width = new Label();
            view.Width.AddToClassList("road-section-width");
            view.RailLeft = new VisualElement();
            view.RailLeft.AddToClassList("road-section-rail");
            view.RailLeft.AddToClassList("road-section-rail-left");
            view.RailRight = new VisualElement();
            view.RailRight.AddToClassList("road-section-rail");
            view.RailRight.AddToClassList("road-section-rail-right");
            view.RowIcon = new IconElement("tree-deciduous");
            view.RowIcon.AddToClassList("road-section-row");
            foreach (var part in new VisualElement[] { view.RailLeft, view.RailRight, view.Number, view.Arrow, view.Short, view.Width, view.RowIcon })
            {
                part.pickingMode = PickingMode.Ignore;
                view.Root.Add(part);
            }
            var chosen = id;
            view.Root.RegisterCallback<PointerDownEvent>(evt => OnPointerDown(evt, chosen));
            view.Root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            view.Root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            view.Root.RegisterCallback<PointerCaptureOutEvent>(_ => CancelPress());
            return view;
        }

        /// <summary>Writes what changed of an element into its block; one that is the same as before is left alone.</summary>
        private void Update(View view, OmsiRoadLane lane, int index, string problem)
        {
            var selected = draft.SelectionKind == OmsiRoadDraftSelectionKind.Element && draft.SelectedId == view.Id;
            var texture = lane.Texture != null ? surfaces.Preview(lane.Texture) : null;
            var signature = string.Join("|", lane.Kind, lane.Width.ToString("R", CultureInfo.InvariantCulture), lane.Direction, lane.Texture, texture != null, lane.Shared, lane.RowObject,
                lane.RowInterval.ToString("R", CultureInfo.InvariantCulture), selected, problem, index, Loc.Manager?.Generation);
            if (signature == view.Signature)
                return;
            view.Signature = signature;
            var root = view.Root;
            foreach (OmsiRoadLaneKind kind in Enum.GetValues(typeof(OmsiRoadLaneKind)))
                root.EnableInClassList("road-section-element-" + OmsiRoadLane.Word(kind), kind == lane.Kind);
            root.EnableInClassList("road-section-element-selected", selected);
            root.EnableInClassList("road-section-element-problem", problem != null);
            root.EnableInClassList("road-section-element-textured", texture != null);
            root.style.backgroundImage = texture != null ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.Null);
            view.Number.text = (index + 1).ToString(CultureInfo.InvariantCulture);
            view.Arrow.SetIcon(lane.Direction == OmsiRoadLaneDirection.Forward ? "arrow-up" : "arrow-down");
            view.HasDirection = lane.HasDirection;
            view.HasRow = lane.RowObject != null;
            var shortText = Loc.T(RoadTypeWindowRules.ShortKey(lane.Kind)); // i18n:keys
            view.ShortText = shortText;
            view.Short.text = shortText;
            view.WidthText = RoadTypeWindowRules.FormatWidth(lane.Width);
            view.Width.text = view.WidthText;
            var tram = lane.Kind == OmsiRoadLaneKind.Tram;
            view.RailLeft.style.display = tram ? DisplayStyle.Flex : DisplayStyle.None;
            view.RailRight.style.display = tram ? DisplayStyle.Flex : DisplayStyle.None;
            root.tooltip = Tooltip(lane, index, problem);
        }

        private static string Tooltip(OmsiRoadLane lane, int index, string problem)
        {
            var title = Loc.T("roadbuilder.window.element.tooltip", ("number", index + 1), ("kind", Loc.T(RoadTypeWindowRules.LaneKey(lane.Kind))), ("width", lane.Width)); // i18n:keys
            var lines = new List<string>();
            if (lane.HasDirection)
                lines.Add(Loc.T("roadbuilder.window.tip.direction", ("direction", Loc.T(lane.Direction == OmsiRoadLaneDirection.Forward ? "roadbuilder.panel.forward" : "roadbuilder.panel.backward")))); // i18n:keys
            if (lane.Kind == OmsiRoadLaneKind.Tram && lane.Shared)
                lines.Add(Loc.T("roadbuilder.window.tip.cars"));
            lines.Add(Loc.T("roadbuilder.window.tip.surface", ("surface", lane.Texture != null ? Path.GetFileNameWithoutExtension(lane.Texture) : Loc.T("roadbuilder.panel.standard"))));
            if (lane.RowObject != null)
                lines.Add(Loc.T("roadbuilder.window.tip.row", ("name", Path.GetFileNameWithoutExtension(lane.RowObject)), ("interval", lane.RowInterval)));
            if (problem != null)
                lines.Add(problem);
            return LocBindings.CardTooltip(title, string.Join("\n", lines));
        }

        /// <summary>Writes as much of a block's label as its drawn width allows, so the labels of narrow elements never overlap.</summary>
        private static void ThinLabels(View view)
        {
            var level = RoadTypeWindowRules.LabelLevel(view.Pixels - 4f, (view.ShortText ?? string.Empty).Length, (view.WidthText ?? string.Empty).Length + 2);
            view.Number.style.display = level >= RoadLabelLevel.Number ? DisplayStyle.Flex : DisplayStyle.None;
            view.Short.style.display = level >= RoadLabelLevel.Short ? DisplayStyle.Flex : DisplayStyle.None;
            view.Width.style.display = level >= RoadLabelLevel.Full ? DisplayStyle.Flex : DisplayStyle.None;
            view.Arrow.style.display = view.HasDirection && view.Pixels >= 22f ? DisplayStyle.Flex : DisplayStyle.None;
            view.RowIcon.style.display = view.HasRow && view.Pixels >= 30f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ----- pointer and keys -----

        private float TrackX(IPointerEvent evt)
        {
            return track.WorldToLocal(evt.position).x;
        }

        private void OnPointerDown(PointerDownEvent evt, int id)
        {
            if (evt.button != 0)
                return;
            pressedId = id;
            pressedX = TrackX(evt);
            dragging = false;
            dropGap = -1;
            Focus();
            ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (pressedId < 0)
                return;
            var x = TrackX(evt);
            if (!dragging && Mathf.Abs(x - pressedX) >= DragPixels)
                dragging = true;
            if (!dragging)
                return;
            var gap = RoadTypeWindowRules.DropGap(edges, x);
            if (gap == dropGap)
                return;
            dropGap = gap;
            dropMark.style.left = edges[Mathf.Clamp(gap, 0, edges.Length - 1)] - 1.5f;
            dropMark.style.display = DisplayStyle.Flex;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (pressedId < 0)
                return;
            var id = pressedId;
            var wasDragging = dragging;
            var gap = dropGap;
            ((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
            CancelPress();
            evt.StopPropagation();
            var from = draft != null ? draft.IndexOf(id) : -1;
            if (wasDragging && from >= 0)
            {
                var to = RoadTypeWindowRules.DropIndex(from, gap);
                if (to != from && gap >= 0)
                {
                    MoveRequested?.Invoke(id, to);
                    return;
                }
            }
            ElementClicked?.Invoke(id);
        }

        private void CancelPress()
        {
            pressedId = -1;
            dragging = false;
            dropGap = -1;
            dropMark.style.display = DisplayStyle.None;
        }

        /// <summary>Left and Right step through the elements, Home and End go to the first and the last, while the strip has the focus.</summary>
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (draft == null || draft.Count == 0 || evt.ctrlKey || evt.commandKey || evt.altKey)
                return;
            var current = draft.SelectedIndex;
            int next;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                    next = current < 0 ? 0 : Mathf.Max(0, current - (draft.SelectionKind == OmsiRoadDraftSelectionKind.Boundary ? 0 : 1));
                    break;
                case KeyCode.RightArrow:
                    next = current < 0 ? 0 : Mathf.Min(draft.Count - 1, current + 1);
                    break;
                case KeyCode.Home:
                    next = 0;
                    break;
                case KeyCode.End:
                    next = draft.Count - 1;
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
            ElementClicked?.Invoke(draft.IdAt(next));
        }

        private static string MarkingKey(OmsiRoadMarking marking)
        {
            return MarkingKeys[(int)marking];
        }

        // In the order of the enum.
        private static readonly string[] MarkingKeys =
        {
            "roadbuilder.panel.marking.none", "roadbuilder.panel.marking.dashed", "roadbuilder.panel.marking.solid", "roadbuilder.panel.marking.longdashed",
            "roadbuilder.panel.marking.double", "roadbuilder.panel.marking.soliddashed", "roadbuilder.panel.marking.dashedsolid", "roadbuilder.panel.marking.broad",
            "roadbuilder.panel.marking.broaddashed"
        };

        /// <summary>Draws the lines on the boundaries between the elements as they will be painted.</summary>
        private sealed class MarkingsOverlay : VisualElement
        {
            public struct Boundary
            {
                public float X;
                public OmsiRoadMarking Marking;
                public bool LeftRaised;
                public bool RightRaised;
            }

            private static readonly Color Paint = new Color(0.95f, 0.95f, 0.93f);
            private List<Boundary> boundaries = new List<Boundary>();

            public MarkingsOverlay()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("road-section-markings");
                generateVisualContent += Draw;
            }

            public void Set(List<Boundary> value)
            {
                boundaries = value;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                var painter = context.painter2D;
                var top = 6f;
                var bottom = contentRect.height - 6f;
                foreach (var boundary in boundaries)
                {
                    if (boundary.Marking == OmsiRoadMarking.None || boundary.LeftRaised && boundary.RightRaised)
                        continue;
                    // A line at a kerb shows on the carriageway side, as it is painted.
                    var x = boundary.X + (boundary.LeftRaised ? 4f : boundary.RightRaised ? -4f : 0f);
                    switch (boundary.Marking)
                    {
                        case OmsiRoadMarking.Double:
                            Stroke(painter, x - 2.5f, top, bottom, false, false, false);
                            Stroke(painter, x + 2.5f, top, bottom, false, false, false);
                            break;
                        case OmsiRoadMarking.SolidDashed:
                            Stroke(painter, x - 2.5f, top, bottom, false, false, false);
                            Stroke(painter, x + 2.5f, top, bottom, true, false, false);
                            break;
                        case OmsiRoadMarking.DashedSolid:
                            Stroke(painter, x - 2.5f, top, bottom, true, false, false);
                            Stroke(painter, x + 2.5f, top, bottom, false, false, false);
                            break;
                        default:
                            Stroke(painter, x, top, bottom, boundary.Marking != OmsiRoadMarking.Solid && boundary.Marking != OmsiRoadMarking.Broad,
                                boundary.Marking == OmsiRoadMarking.LongDashed, boundary.Marking == OmsiRoadMarking.Broad || boundary.Marking == OmsiRoadMarking.BroadDashed);
                            break;
                    }
                }
            }

            private static void Stroke(Painter2D painter, float x, float top, float bottom, bool dashed, bool longDashes, bool broad)
            {
                painter.strokeColor = Paint;
                painter.lineWidth = broad ? 4f : 2f;
                painter.lineCap = LineCap.Butt;
                painter.BeginPath();
                if (!dashed)
                {
                    painter.MoveTo(new Vector2(x, top));
                    painter.LineTo(new Vector2(x, bottom));
                }
                else
                {
                    var dash = longDashes ? 10f : 6f;
                    var step = longDashes ? 26f : broad ? 12f : 15f;
                    for (var y = top; y < bottom; y += step)
                    {
                        painter.MoveTo(new Vector2(x, y));
                        painter.LineTo(new Vector2(x, Mathf.Min(bottom, y + dash)));
                    }
                }
                painter.Stroke();
            }
        }
    }
}
