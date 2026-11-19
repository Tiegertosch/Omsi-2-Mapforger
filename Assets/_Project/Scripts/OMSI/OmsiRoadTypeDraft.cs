using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Which side of the selection a new element goes to: before it (left) or after it (right).</summary>
    public enum OmsiRoadDraftSide
    {
        Before,
        After
    }

    /// <summary>What is selected in a road type draft: nothing, an element, or the boundary (the line) right of an element.</summary>
    public enum OmsiRoadDraftSelectionKind
    {
        None,
        Element,
        Boundary
    }

    /// <summary>Why a road type cannot be saved; the window turns each code into a translated text.</summary>
    public enum OmsiRoadTypeProblemCode
    {
        /// <summary>No element at all.</summary>
        NoLanes,

        /// <summary>More than <see cref="OmsiRoadType.MaximumLanes"/> elements; <see cref="OmsiRoadTypeProblem.Limit"/> holds it.</summary>
        TooManyLanes,

        /// <summary>An element is narrower or wider than its kind allows; Minimum and Maximum hold the range.</summary>
        WidthOutOfRange,

        /// <summary>An element of a kind that cannot hold a row of objects has one.</summary>
        RowNotAllowed,

        /// <summary>A row's objects stand closer than <see cref="OmsiRoadLane.MinimumRowInterval"/>; Minimum holds it.</summary>
        RowTooDense
    }

    /// <summary>
    /// One reason a road type cannot be saved, with the values a translated text needs. It matches one entry of
    /// <see cref="OmsiRoadType.Problems"/>, whose English texts stay for the writer and the command line.
    /// </summary>
    public sealed class OmsiRoadTypeProblem
    {
        public OmsiRoadTypeProblem(OmsiRoadTypeProblemCode code, int laneIndex, int laneId, OmsiRoadLaneKind kind, double minimum, double maximum, int limit)
        {
            Code = code;
            LaneIndex = laneIndex;
            LaneId = laneId;
            Kind = kind;
            Minimum = minimum;
            Maximum = maximum;
            Limit = limit;
        }

        public OmsiRoadTypeProblemCode Code { get; }

        /// <summary>The element's index from the left, or -1 for a problem of the whole road type.</summary>
        public int LaneIndex { get; }

        /// <summary>The element's identity in the draft, or -1 for a problem of the whole road type or one without a draft.</summary>
        public int LaneId { get; }

        public OmsiRoadLaneKind Kind { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public int Limit { get; }

        /// <summary>The arguments of the translated text: lane (number from 1), kind (its .sli word), min, max (metres, two decimals), limit.</summary>
        public List<KeyValuePair<string, string>> Arguments()
        {
            var arguments = new List<KeyValuePair<string, string>>();
            if (LaneIndex >= 0)
            {
                arguments.Add(new KeyValuePair<string, string>("lane", (LaneIndex + 1).ToString(CultureInfo.InvariantCulture)));
                arguments.Add(new KeyValuePair<string, string>("kind", OmsiRoadLane.Word(Kind)));
            }
            if (Code == OmsiRoadTypeProblemCode.WidthOutOfRange || Code == OmsiRoadTypeProblemCode.RowTooDense)
                arguments.Add(new KeyValuePair<string, string>("min", Minimum.ToString("0.00", CultureInfo.InvariantCulture)));
            if (Code == OmsiRoadTypeProblemCode.WidthOutOfRange)
                arguments.Add(new KeyValuePair<string, string>("max", Maximum.ToString("0.00", CultureInfo.InvariantCulture)));
            if (Code == OmsiRoadTypeProblemCode.TooManyLanes)
                arguments.Add(new KeyValuePair<string, string>("limit", Limit.ToString(CultureInfo.InvariantCulture)));
            return arguments;
        }

        /// <summary>
        /// The reasons a road type cannot be saved, in the order and by the rules of <see cref="OmsiRoadType.Problems"/>.
        /// </summary>
        /// <param name="ids">The elements' identities by index, or null.</param>
        public static List<OmsiRoadTypeProblem> Of(OmsiRoadType type, IReadOnlyList<int> ids = null)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            var problems = new List<OmsiRoadTypeProblem>();
            if (type.Lanes.Count == 0)
                problems.Add(new OmsiRoadTypeProblem(OmsiRoadTypeProblemCode.NoLanes, -1, -1, OmsiRoadLaneKind.Driving, 0.0, 0.0, 0));
            if (type.Lanes.Count > OmsiRoadType.MaximumLanes)
                problems.Add(new OmsiRoadTypeProblem(OmsiRoadTypeProblemCode.TooManyLanes, -1, -1, OmsiRoadLaneKind.Driving, 0.0, 0.0, OmsiRoadType.MaximumLanes));
            for (var index = 0; index < type.Lanes.Count; index++)
            {
                var lane = type.Lanes[index];
                var id = ids != null && index < ids.Count ? ids[index] : -1;
                var minimum = OmsiRoadLane.MinimumWidth(lane.Kind);
                var maximum = OmsiRoadTypeDraft.MaximumWidthAt(type, index);
                if (!(lane.Width >= minimum) || lane.Width > maximum + 1e-9)
                    problems.Add(new OmsiRoadTypeProblem(OmsiRoadTypeProblemCode.WidthOutOfRange, index, id, lane.Kind, minimum, maximum, 0));
                if (lane.RowObject != null && !OmsiRoadLane.CanHoldRow(lane.Kind))
                    problems.Add(new OmsiRoadTypeProblem(OmsiRoadTypeProblemCode.RowNotAllowed, index, id, lane.Kind, 0.0, 0.0, 0));
                if (lane.RowObject != null && !(lane.RowInterval >= OmsiRoadLane.MinimumRowInterval))
                    problems.Add(new OmsiRoadTypeProblem(OmsiRoadTypeProblemCode.RowTooDense, index, id, lane.Kind, OmsiRoadLane.MinimumRowInterval, 0.0, 0));
            }
            return problems;
        }
    }

    /// <summary>A starting point for a road type (<see cref="OmsiRoadType.Templates"/>) with a stable id and its key figures.</summary>
    public sealed class OmsiRoadTypeTemplate
    {
        private OmsiRoadTypeTemplate(string id, string name, string description)
        {
            Id = id;
            Name = name;
            Description = description;
            var type = OmsiRoadType.Parse(description);
            Width = type.Width;
            Elements = type.Lanes.Count;
            TrafficLanes = OmsiRoadTypeDraft.CountTrafficLanes(type);
        }

        /// <summary>A stable id from the English name, such as "tram-in-the-street".</summary>
        public string Id { get; }

        /// <summary>The English name, as <see cref="OmsiRoadType.Templates"/> has it.</summary>
        public string Name { get; }

        public string Description { get; }
        public double Width { get; }
        public int Elements { get; }
        public int TrafficLanes { get; }

        /// <summary>A new road type of the template's lanes.</summary>
        public OmsiRoadType Type()
        {
            return OmsiRoadType.Parse(Description);
        }

        public static IReadOnlyList<OmsiRoadTypeTemplate> All { get; } = Make();

        /// <summary>The template of an id, or null.</summary>
        public static OmsiRoadTypeTemplate Find(string id)
        {
            foreach (var template in All)
                if (string.Equals(template.Id, id, StringComparison.Ordinal))
                    return template;
            return null;
        }

        public static string IdOf(string name)
        {
            var id = new System.Text.StringBuilder();
            var dash = false;
            foreach (var character in (name ?? string.Empty).ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    if (dash && id.Length > 0)
                        id.Append('-');
                    id.Append(character);
                    dash = false;
                }
                else if (character == '+')
                {
                    id.Append("-plus-");
                    dash = false;
                }
                else
                    dash = true;
            }
            return id.ToString().Replace("--", "-").Trim('-');
        }

        private static List<OmsiRoadTypeTemplate> Make()
        {
            var templates = new List<OmsiRoadTypeTemplate>();
            foreach (var pair in OmsiRoadType.Templates)
                templates.Add(new OmsiRoadTypeTemplate(IdOf(pair.Key), pair.Key, pair.Value));
            return templates;
        }
    }

    /// <summary>
    /// Whether the open project may overwrite or delete a road type: only the ones in its own folder
    /// <c>Splines\Mapforge\&lt;project&gt;\</c> (owner's decision of 18 November 2026, point 1). Others, made by other
    /// projects or not by MapWorks at all, open only to be saved as a new type in the project's folder.
    /// </summary>
    public static class OmsiRoadTypeOwnership
    {
        /// <summary>The project's folder relative to the installation, as <see cref="OmsiRoadTypeStore.Write"/> names it.</summary>
        public static string ProjectFolder(string projectName)
        {
            return OmsiRoadTypeStore.Folder + "\\" + OmsiGeneratedObjectStore.SafeName(projectName, "Project");
        }

        /// <summary>Whether a road type's .sli lies right in the project's folder (not in a subfolder such as texture).</summary>
        public static bool InProjectFolder(string assetPath, string projectName)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;
            var path = assetPath.Replace('/', '\\').TrimStart('\\');
            var folder = ProjectFolder(projectName) + "\\";
            if (!path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                return false;
            var rest = path.Substring(folder.Length);
            return rest.Length > 0 && rest.IndexOf('\\') < 0 && rest.EndsWith(".sli", StringComparison.OrdinalIgnoreCase) && rest.IndexOf("..", StringComparison.Ordinal) < 0;
        }
    }

    /// <summary>
    /// The road type window's working copy of a road type (briefing of 18 November 2026): elements with stable identities,
    /// commands with their own undo history, a selection that follows undo and redo, and a saved baseline to tell changes
    /// by. Nothing here writes a file; saving stays with <see cref="OmsiRoadTypeStore"/> and the map's history.
    /// </summary>
    public sealed class OmsiRoadTypeDraft
    {
        public const double WidthStep = 0.05;
        public const double WidthRounding = 0.01;

        private sealed class Element
        {
            public int Id;
            public OmsiRoadLane Lane;
        }

        private sealed class State
        {
            public List<Element> Elements;
            public OmsiRoadDraftSelectionKind SelectionKind;
            public int SelectionId;
        }

        private sealed class Step
        {
            public State Before;
            public State After;
            public string Gesture;
        }

        private readonly List<Element> elements = new List<Element>();
        private readonly List<Step> undoSteps = new List<Step>();
        private readonly List<Step> redoSteps = new List<Step>();
        private OmsiRoadType baseline;
        private string baselineDescription;
        private int nextId = 1;
        private bool gestureOpen;

        /// <param name="saved">The road type as saved, or the starting point of a new one; null for an empty one.</param>
        /// <param name="assetPath">The saved road type's path relative to the installation; null for a new one.</param>
        public OmsiRoadTypeDraft(OmsiRoadType saved, string assetPath = null)
        {
            baseline = saved != null ? saved.Copy() : new OmsiRoadType();
            baselineDescription = baseline.Describe();
            AssetPath = assetPath;
            Load(baseline);
        }

        /// <summary>The saved road type's path; null while it has not been saved.</summary>
        public string AssetPath { get; private set; }

        public bool IsNew => AssetPath == null;

        /// <summary>Rises with every change of the elements or the selection, so a view can tell when to redraw.</summary>
        public int Version { get; private set; }

        /// <summary>The saved road type changed under the draft, such as by an undo on the map (decision 2).</summary>
        public bool IsStale { get; private set; }

        public int Count => elements.Count;

        public IReadOnlyList<int> ElementIds
        {
            get
            {
                var ids = new List<int>(elements.Count);
                foreach (var element in elements)
                    ids.Add(element.Id);
                return ids;
            }
        }

        public int IndexOf(int id)
        {
            for (var index = 0; index < elements.Count; index++)
                if (elements[index].Id == id)
                    return index;
            return -1;
        }

        public int IdAt(int index)
        {
            return elements[index].Id;
        }

        /// <summary>A copy of the element's lane; change it through the draft's commands.</summary>
        public OmsiRoadLane LaneOf(int id)
        {
            var index = IndexOf(id);
            return index < 0 ? null : elements[index].Lane.Copy();
        }

        /// <summary>The draft as a road type of its own, to save or show.</summary>
        public OmsiRoadType ToRoadType()
        {
            var type = new OmsiRoadType();
            foreach (var element in elements)
                type.Lanes.Add(element.Lane.Copy());
            return type;
        }

        /// <summary>The road type as last saved (or started from).</summary>
        public OmsiRoadType Baseline => baseline.Copy();

        public double Width
        {
            get
            {
                var width = 0.0;
                foreach (var element in elements)
                    width += element.Lane.Width;
                return width;
            }
        }

        /// <summary>The lanes road vehicles drive on (<see cref="OmsiRoadLane.CarriesTraffic"/>); sidewalks and medians are none.</summary>
        public int TrafficLanes => CountTrafficLanes(ToRoadType());

        public static int CountTrafficLanes(OmsiRoadType type)
        {
            var count = 0;
            foreach (var lane in type.Lanes)
                if (lane.CarriesTraffic)
                    count++;
            return count;
        }

        /// <summary>Whether the draft differs from the saved road type, by their descriptions as the .sli keeps them.</summary>
        public bool IsDirty => !string.Equals(ToRoadType().Describe(), baselineDescription, StringComparison.Ordinal);

        public List<OmsiRoadTypeProblem> Problems()
        {
            return OmsiRoadTypeProblem.Of(ToRoadType(), ElementIds);
        }

        /// <summary>Whether the draft can be saved: it has no problems.</summary>
        public bool CanSave => Problems().Count == 0;

        // ---- Selection ----

        public OmsiRoadDraftSelectionKind SelectionKind { get; private set; }

        /// <summary>The selected element, or for a boundary the element left of it; -1 for none.</summary>
        public int SelectedId { get; private set; } = -1;

        public int SelectedIndex => SelectionKind == OmsiRoadDraftSelectionKind.None ? -1 : IndexOf(SelectedId);

        public bool SelectElement(int id)
        {
            if (IndexOf(id) < 0)
                return false;
            Select(OmsiRoadDraftSelectionKind.Element, id);
            return true;
        }

        /// <summary>Selects the line between an element and the next one; there is none right of the last element.</summary>
        public bool SelectBoundary(int leftId)
        {
            var index = IndexOf(leftId);
            if (index < 0 || index + 1 >= elements.Count)
                return false;
            Select(OmsiRoadDraftSelectionKind.Boundary, leftId);
            return true;
        }

        public void ClearSelection()
        {
            Select(OmsiRoadDraftSelectionKind.None, -1);
        }

        private void Select(OmsiRoadDraftSelectionKind kind, int id)
        {
            gestureOpen = false;
            if (SelectionKind == kind && SelectedId == id)
                return;
            SelectionKind = kind;
            SelectedId = id;
            Version++;
        }

        // ---- Widths ----

        /// <summary>The widest a lane may be where it stands: its kind's maximum, twice that for a raised lane with kerbs on both sides.</summary>
        public static double MaximumWidthAt(OmsiRoadType type, int index)
        {
            var maximum = OmsiRoadLane.MaximumWidth(type.Lanes[index].Kind);
            if (type.KerbOn(index, -1) && type.KerbOn(index, 1))
                maximum *= 2.0;
            return maximum;
        }

        public bool WidthRange(int id, out double minimum, out double maximum)
        {
            var index = IndexOf(id);
            if (index < 0)
            {
                minimum = maximum = 0.0;
                return false;
            }
            minimum = OmsiRoadLane.MinimumWidth(elements[index].Lane.Kind);
            maximum = MaximumWidthAt(ToRoadType(), index);
            return true;
        }

        public static double RoundWidth(double width)
        {
            return Math.Round(width, 2, MidpointRounding.AwayFromZero);
        }

        // ---- Commands ----

        /// <summary>
        /// Adds an element of a kind before or after the selection (at the start or the end without one) and selects it. It
        /// takes the selected element's direction when both have one. Fails at <see cref="OmsiRoadType.MaximumLanes"/>.
        /// </summary>
        /// <returns>The new element's id, or -1.</returns>
        public int Add(OmsiRoadLaneKind kind, OmsiRoadDraftSide side)
        {
            if (elements.Count >= OmsiRoadType.MaximumLanes)
                return -1;
            var lane = new OmsiRoadLane { Kind = kind, Width = OmsiRoadLane.DefaultWidth(kind) };
            var selected = SelectedIndex;
            int at;
            if (selected < 0)
                at = side == OmsiRoadDraftSide.Before ? 0 : elements.Count;
            else
            {
                at = side == OmsiRoadDraftSide.Before ? selected : selected + 1;
                var near = elements[selected].Lane;
                if (lane.HasDirection && near.HasDirection)
                    lane.Direction = near.Direction;
            }
            var id = nextId++;
            Do(null, () =>
            {
                elements.Insert(at, new Element { Id = id, Lane = lane });
                SelectionKind = OmsiRoadDraftSelectionKind.Element;
                SelectedId = id;
            });
            return id;
        }

        /// <summary>
        /// Removes an element; the last one may go too, leaving an empty draft that cannot be saved. When it was selected,
        /// its neighbour is selected instead; another selection stays.
        /// </summary>
        public bool Remove(int id)
        {
            var index = IndexOf(id);
            if (index < 0)
                return false;
            Do(null, () =>
            {
                elements.RemoveAt(index);
                var keep = SelectionKind != OmsiRoadDraftSelectionKind.None && SelectedId != id &&
                           (SelectionKind != OmsiRoadDraftSelectionKind.Boundary || IndexOf(SelectedId) + 1 < elements.Count);
                if (keep)
                    return;
                if (elements.Count == 0)
                {
                    SelectionKind = OmsiRoadDraftSelectionKind.None;
                    SelectedId = -1;
                }
                else
                {
                    SelectionKind = OmsiRoadDraftSelectionKind.Element;
                    SelectedId = elements[Math.Min(index, elements.Count - 1)].Id;
                }
            });
            return true;
        }

        public bool CanMove(int id, int by)
        {
            var index = IndexOf(id);
            return index >= 0 && (by == -1 || by == 1) && index + by >= 0 && index + by < elements.Count;
        }

        /// <summary>Moves an element one place left (-1) or right (1); its line on the right goes with it.</summary>
        public bool Move(int id, int by)
        {
            if (!CanMove(id, by))
                return false;
            var index = IndexOf(id);
            Do(null, () =>
            {
                var element = elements[index];
                elements.RemoveAt(index);
                elements.Insert(index + by, element);
            });
            return true;
        }

        /// <summary>Moves an element to another index, as dragging it does.</summary>
        public bool MoveTo(int id, int index)
        {
            var from = IndexOf(id);
            if (from < 0 || index < 0 || index >= elements.Count || index == from)
                return false;
            Do(null, () =>
            {
                var element = elements[from];
                elements.RemoveAt(from);
                elements.Insert(index, element);
            });
            return true;
        }

        /// <summary>Adds a copy of an element with all its settings right after it, under a new identity, and selects it.</summary>
        /// <returns>The copy's id, or -1.</returns>
        public int Duplicate(int id)
        {
            var index = IndexOf(id);
            if (index < 0 || elements.Count >= OmsiRoadType.MaximumLanes)
                return -1;
            var copy = elements[index].Lane.Copy();
            var newId = nextId++;
            Do(null, () =>
            {
                elements.Insert(index + 1, new Element { Id = newId, Lane = copy });
                SelectionKind = OmsiRoadDraftSelectionKind.Element;
                SelectedId = newId;
            });
            return newId;
        }

        /// <summary>
        /// Changes an element's kind and keeps what fits: the width when the new kind allows it (else its default), the
        /// direction when it has one, the surface and the line; the row only where the kind can hold one; "cars too" only on a tram.
        /// </summary>
        public bool SetKind(int id, OmsiRoadLaneKind kind)
        {
            var index = IndexOf(id);
            if (index < 0 || elements[index].Lane.Kind == kind)
                return false;
            return Change(id, null, lane =>
            {
                lane.Kind = kind;
                if (!lane.HasDirection)
                    lane.Direction = OmsiRoadLaneDirection.Forward;
                if (kind != OmsiRoadLaneKind.Tram)
                    lane.Shared = false;
                if (!OmsiRoadLane.CanHoldRow(kind))
                {
                    lane.RowObject = null;
                    lane.RowInterval = OmsiRoadLane.DefaultRowInterval;
                    lane.RowPlace = OmsiRoadRowPlace.Kerb;
                    lane.RowTurn = 0.0;
                }
            }, () =>
            {
                // The width is checked with the new kind in place, so a raised lane's double width counts.
                var at = IndexOf(id);
                var type = ToRoadType();
                var lane = elements[at].Lane;
                if (!(lane.Width >= OmsiRoadLane.MinimumWidth(kind)) || lane.Width > MaximumWidthAt(type, at) + 1e-9)
                    lane.Width = OmsiRoadLane.DefaultWidth(kind);
            });
        }

        /// <summary>
        /// Sets an element's width, rounded to <see cref="WidthRounding"/>. A width outside the kind's range is kept as typed
        /// and shows as a problem; zero, negative and non-numbers are refused.
        /// </summary>
        /// <param name="gesture">Calls with the same gesture, one after the other, make one undo step, such as typing or holding +.</param>
        public bool SetWidth(int id, double width, string gesture = null)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0.0)
                return false;
            var rounded = RoundWidth(width);
            if (rounded <= 0.0)
                return false;
            return Change(id, gesture, lane => lane.Width = rounded);
        }

        /// <summary>Steps an element's width by <see cref="WidthStep"/> times <paramref name="steps"/>, kept within its range.</summary>
        public bool StepWidth(int id, int steps, string gesture = null)
        {
            if (!WidthRange(id, out var minimum, out var maximum))
                return false;
            var width = RoundWidth(elements[IndexOf(id)].Lane.Width + steps * WidthStep);
            width = Math.Max(minimum, Math.Min(maximum, width));
            return Change(id, gesture, lane => lane.Width = RoundWidth(width));
        }

        /// <summary>Sets the way traffic uses an element; only for kinds with a direction.</summary>
        public bool SetDirection(int id, OmsiRoadLaneDirection direction)
        {
            var lane = LaneOf(id);
            if (lane == null || !lane.HasDirection)
                return false;
            return Change(id, null, each => each.Direction = direction);
        }

        /// <summary>Lets road vehicles use a tram track too ("cars too"); only for tram lanes.</summary>
        public bool SetShared(int id, bool shared)
        {
            var lane = LaneOf(id);
            if (lane == null || lane.Kind != OmsiRoadLaneKind.Tram)
                return false;
            return Change(id, null, each => each.Shared = shared);
        }

        /// <summary>Sets an element's surface texture; null or empty for the base game's.</summary>
        public bool SetTexture(int id, string texture)
        {
            var value = string.IsNullOrWhiteSpace(texture) ? null : texture;
            return Change(id, null, lane => lane.Texture = value);
        }

        /// <summary>Sets the object of an element's row; null or empty for none. Only kinds that can hold a row get one.</summary>
        public bool SetRowObject(int id, string assetPath, double? turn = null, string gesture = null)
        {
            var lane = LaneOf(id);
            if (lane == null)
                return false;
            var value = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath;
            if (value != null && !OmsiRoadLane.CanHoldRow(lane.Kind))
                return false;
            return Change(id, gesture, each =>
            {
                each.RowObject = value;
                if (turn.HasValue)
                    each.RowTurn = turn.Value;
            });
        }

        /// <summary>Sets the metres between a row's objects; a value below the minimum is kept and shows as a problem.</summary>
        public bool SetRowInterval(int id, double interval, string gesture = null)
        {
            if (double.IsNaN(interval) || double.IsInfinity(interval) || interval <= 0.0)
                return false;
            return Change(id, gesture, lane => lane.RowInterval = Math.Round(interval, 2));
        }

        public bool SetRowPlace(int id, OmsiRoadRowPlace place, string gesture = null)
        {
            return Change(id, gesture, lane => lane.RowPlace = place);
        }

        /// <summary>Sets the degrees a row's objects are turned by, clockwise, taken to 0 up to 360.</summary>
        public bool SetRowTurn(int id, double turn, string gesture = null)
        {
            if (double.IsNaN(turn) || double.IsInfinity(turn))
                return false;
            var value = (turn % 360.0 + 360.0) % 360.0;
            return Change(id, gesture, lane => lane.RowTurn = value);
        }

        /// <summary>Sets the line between an element and the next; null lets the editor choose (<see cref="OmsiRoadType.AutoMarking"/>).</summary>
        public bool SetMarking(int leftId, OmsiRoadMarking? marking)
        {
            var index = IndexOf(leftId);
            if (index < 0 || index + 1 >= elements.Count)
                return false;
            return Change(leftId, null, lane => lane.RightMarking = marking);
        }

        /// <summary>The line drawn between an element and the next: the chosen one, else the automatic one.</summary>
        public OmsiRoadMarking MarkingAfter(int leftId)
        {
            var index = IndexOf(leftId);
            return index < 0 ? OmsiRoadMarking.None : ToRoadType().MarkingAfter(index);
        }

        /// <summary>Replaces all elements with a road type's, such as a template or one taken from the chosen road, as one undo step.</summary>
        public bool Apply(OmsiRoadType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            var lanes = new List<Element>();
            foreach (var lane in type.Lanes)
                lanes.Add(new Element { Id = nextId++, Lane = lane.Copy() });
            var selected = SelectedIndex;
            Do(null, () =>
            {
                elements.Clear();
                elements.AddRange(lanes);
                if (elements.Count == 0)
                {
                    SelectionKind = OmsiRoadDraftSelectionKind.None;
                    SelectedId = -1;
                }
                else
                {
                    SelectionKind = OmsiRoadDraftSelectionKind.Element;
                    SelectedId = elements[Math.Max(0, Math.Min(selected, elements.Count - 1))].Id;
                }
            });
            return true;
        }

        public bool ApplyTemplate(string templateId)
        {
            var template = OmsiRoadTypeTemplate.Find(templateId);
            return template != null && Apply(template.Type());
        }

        // ---- History ----

        public bool CanUndo => undoSteps.Count > 0;
        public bool CanRedo => redoSteps.Count > 0;

        /// <summary>Ends the input gesture under way, so the next change makes an undo step of its own (on leaving a field, releasing a key).</summary>
        public void EndGesture()
        {
            gestureOpen = false;
        }

        public bool Undo()
        {
            if (undoSteps.Count == 0)
                return false;
            var step = undoSteps[undoSteps.Count - 1];
            undoSteps.RemoveAt(undoSteps.Count - 1);
            redoSteps.Add(step);
            Restore(step.Before);
            return true;
        }

        public bool Redo()
        {
            if (redoSteps.Count == 0)
                return false;
            var step = redoSteps[redoSteps.Count - 1];
            redoSteps.RemoveAt(redoSteps.Count - 1);
            undoSteps.Add(step);
            Restore(step.After);
            return true;
        }

        /// <summary>
        /// Goes back to the saved road type as one undoable step; does nothing on a clean draft. Elements keep their ids
        /// by position, and the selection stays at its place where it can.
        /// </summary>
        public void Discard()
        {
            if (!IsDirty)
                return;
            Do(null, () =>
            {
                var selectedIndex = SelectedIndex;
                var kind = SelectionKind;
                var oldIds = new List<int>(elements.Count);
                foreach (var element in elements)
                    oldIds.Add(element.Id);
                elements.Clear();
                for (var i = 0; i < baseline.Lanes.Count; i++)
                    elements.Add(new Element { Id = i < oldIds.Count ? oldIds[i] : nextId++, Lane = baseline.Lanes[i].Copy() });
                SelectionKind = OmsiRoadDraftSelectionKind.None;
                SelectedId = -1;
                if (selectedIndex >= 0 && selectedIndex < elements.Count && (kind != OmsiRoadDraftSelectionKind.Boundary || selectedIndex + 1 < elements.Count))
                {
                    SelectionKind = kind;
                    SelectedId = elements[selectedIndex].Id;
                }
            });
        }

        /// <summary>
        /// Takes the draft as the new saved state after saving and forgets the history; elements and selection stay.
        /// </summary>
        /// <param name="assetPath">The path it was saved under, such as a new type's real file name.</param>
        public void Rebase(string assetPath)
        {
            baseline = ToRoadType();
            baselineDescription = baseline.Describe();
            AssetPath = assetPath;
            undoSteps.Clear();
            redoSteps.Clear();
            gestureOpen = false;
            IsStale = false;
            Version++;
        }

        /// <summary>
        /// Marks the draft stale when the saved road type now reads otherwise than its baseline, such as after an undo on the map, and
        /// clears the mark when it reads as the baseline again, such as after the redo.
        /// </summary>
        /// <param name="onDisk">The saved road type as the file holds it now; null when the file is gone or holds none.</param>
        /// <returns>Whether it is stale now.</returns>
        public bool CheckSaved(OmsiRoadType onDisk)
        {
            var description = onDisk == null ? null : onDisk.Describe();
            var stale = !string.Equals(description, baselineDescription, StringComparison.Ordinal);
            if (stale != IsStale)
            {
                IsStale = stale;
                Version++;
            }
            return IsStale;
        }

        // ---- Internals ----

        private bool Change(int id, string gesture, Action<OmsiRoadLane> change, Action after = null)
        {
            var index = IndexOf(id);
            if (index < 0)
                return false;
            var probe = elements[index].Lane.Copy();
            change(probe);
            if (after == null && Same(probe, elements[index].Lane))
                return true;
            var key = gesture == null ? null : gesture + "#" + id.ToString(CultureInfo.InvariantCulture);
            Do(key, () =>
            {
                change(elements[IndexOf(id)].Lane);
                after?.Invoke();
            });
            return true;
        }

        private void Do(string gesture, Action action)
        {
            var merge = gesture != null && gestureOpen && undoSteps.Count > 0 && undoSteps[undoSteps.Count - 1].Gesture == gesture;
            var before = Capture();
            action();
            var after = Capture();
            if (merge)
                undoSteps[undoSteps.Count - 1].After = after;
            else
                undoSteps.Add(new Step { Before = before, After = after, Gesture = gesture });
            redoSteps.Clear();
            gestureOpen = gesture != null;
            Version++;
        }

        private State Capture()
        {
            var copy = new List<Element>(elements.Count);
            foreach (var element in elements)
                copy.Add(new Element { Id = element.Id, Lane = element.Lane.Copy() });
            return new State { Elements = copy, SelectionKind = SelectionKind, SelectionId = SelectedId };
        }

        private void Restore(State state)
        {
            elements.Clear();
            foreach (var element in state.Elements)
                elements.Add(new Element { Id = element.Id, Lane = element.Lane.Copy() });
            SelectionKind = state.SelectionKind;
            SelectedId = state.SelectionId;
            var index = IndexOf(SelectedId);
            if (index < 0 || SelectionKind == OmsiRoadDraftSelectionKind.Boundary && index + 1 >= elements.Count)
            {
                SelectionKind = OmsiRoadDraftSelectionKind.None;
                SelectedId = -1;
            }
            gestureOpen = false;
            Version++;
        }

        private void Load(OmsiRoadType type)
        {
            elements.Clear();
            foreach (var lane in type.Lanes)
                elements.Add(new Element { Id = nextId++, Lane = lane.Copy() });
            SelectionKind = OmsiRoadDraftSelectionKind.None;
            SelectedId = -1;
        }

        private static bool Same(OmsiRoadLane a, OmsiRoadLane b)
        {
            return a.Kind == b.Kind && a.Width.Equals(b.Width) && a.Direction == b.Direction && a.RightMarking == b.RightMarking &&
                   string.Equals(a.Texture, b.Texture, StringComparison.Ordinal) && a.Shared == b.Shared &&
                   string.Equals(a.RowObject, b.RowObject, StringComparison.Ordinal) && a.RowInterval.Equals(b.RowInterval) &&
                   a.RowPlace == b.RowPlace && a.RowTurn.Equals(b.RowTurn);
        }
    }
}
