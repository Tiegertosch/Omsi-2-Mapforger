using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>The areas the navigation of the signals window offers; the pages of the tool are shown inside them (ADR 098, signal window).</summary>
    internal enum SignalArea
    {
        Groups,
        Phases,
        Check,
        Assistant
    }

    /// <summary>The pages of the signals window. Phases and timeline share the area "Phases and times", proposal and templates the area "Assistant".</summary>
    internal enum SignalTab
    {
        Movements,
        Phases,
        Timeline,
        Check,
        Wizard,
        Templates
    }

    /// <summary>How the program of a crossing can be shown as phases.</summary>
    internal enum SignalPhaseView
    {
        /// <summary>No groups: nothing to show.</summary>
        Empty,

        /// <summary>The program is a phase plan: phases with the groups that go and their green times.</summary>
        Plan,

        /// <summary>The program is not a phase plan (individual times, other red, stretches that no plan writes back); it is shown by its windows and edited on the timeline, never converted.</summary>
        NotRepresentable
    }

    /// <summary>What the footer says about the draft.</summary>
    internal enum SignalChangeState
    {
        /// <summary>The crossing is only read: OMSI's own or another project's; there is nothing to apply.</summary>
        ReadOnly,

        /// <summary>The draft equals the applied state.</summary>
        Applied,

        /// <summary>The draft has changes not yet applied.</summary>
        Unapplied
    }

    /// <summary>The ways out of the signals window that would drop a draft with changes.</summary>
    internal enum SignalExitKind
    {
        /// <summary>Another tool is chosen (the close button and Esc choose the drag tool).</summary>
        OtherTool,

        /// <summary>Another crossing is clicked.</summary>
        OtherCrossing,

        /// <summary>A change that is written at once (a group added, renamed or taken away, a movement given a group, an exception, the assistant, a template); its action runs again once the draft is clean.</summary>
        Action,

        /// <summary>Another map, a chrono view, a tile reverted, added or deleted, a project saved: the view that holds the crossing would be replaced. The payload is the action that shows the view; it runs again once the draft is clean, and Keep editing cancels the change of view.</summary>
        ViewChange
    }

    /// <summary>What the person chose in the prompt that asks about unapplied changes.</summary>
    internal enum SignalPromptChoice
    {
        Apply,
        Discard,
        KeepEditing
    }

    /// <summary>A way out that waits for the answer of the prompt; <see cref="Payload"/> says where to (the tool, the crossing).</summary>
    internal sealed class SignalExit
    {
        public SignalExitKind Kind;
        public object Payload;

        /// <summary>What to do when the way out is given up (Keep editing, or the draft became clean by another way): shows again what the change already moved, such as the date of a chrono view. Null for nothing.</summary>
        public Action Cancelled;
    }

    /// <summary>
    /// Decides when leaving the signals window needs a question: a draft with changes is never dropped without one. The window asks inside
    /// itself (apply, discard, keep editing) and carries out the way out that waited once the draft is clean.
    /// </summary>
    internal sealed class SignalLeaveGuard
    {
        private bool answering;

        /// <summary>The way out that waits for an answer, or null.</summary>
        public SignalExit Pending { get; private set; }

        public bool Asking => Pending != null;

        /// <summary>True while the answer of the question is being carried out (Apply or Discard run, which clean the draft): the question must not be dropped as stale then.</summary>
        public bool Answering => answering;

        /// <summary>Starts taking an answer: Apply and Discard clean the draft before <see cref="Answer"/> reads the waiting way out.</summary>
        public void BeginAnswer()
        {
            answering = true;
        }

        /// <summary>Ends taking an answer; see <see cref="BeginAnswer"/>.</summary>
        public void EndAnswer()
        {
            answering = false;
        }

        /// <summary>
        /// The draft became clean by another way than the question (Discard or Apply in the footer, undo): the question is stale and goes away
        /// with the way out that waited. Returns that way out, or null when nothing was dropped. Never while an answer is carried out.
        /// </summary>
        public SignalExit DropIfStale(bool dirty)
        {
            if (dirty || answering || Pending == null)
                return null;
            var dropped = Pending;
            Pending = null;
            return dropped;
        }

        /// <summary>True when the way out may be taken now (the draft has no changes); else the question is asked and the way out waits.</summary>
        /// <remarks>A way out that waited and is replaced (or no longer needed) is given up: its <see cref="SignalExit.Cancelled"/> runs.</remarks>
        public bool Request(bool dirty, SignalExit exit)
        {
            var replaced = Pending;
            Pending = dirty ? exit : null;
            if (replaced != null && !ReferenceEquals(replaced, exit))
                replaced.Cancelled?.Invoke();
            return !dirty;
        }

        /// <summary>
        /// Takes the answer. Returns the way out to take now: after Apply or Discard once the draft is clean, never after Keep editing, and
        /// not after an Apply that left the draft dirty (it failed), which keeps the question open.
        /// </summary>
        public SignalExit Answer(SignalPromptChoice choice, bool dirtyAfter)
        {
            var waiting = Pending;
            if (waiting == null)
                return null;
            if (choice == SignalPromptChoice.KeepEditing)
            {
                Pending = null;
                return null;
            }
            if (dirtyAfter)
                return null;
            Pending = null;
            return waiting;
        }

        /// <summary>The question is moot (the crossing went away by a way that could not wait); the way out that waited is given up and its <see cref="SignalExit.Cancelled"/> runs.</summary>
        public void Clear()
        {
            var dropped = Pending;
            Pending = null;
            dropped?.Cancelled?.Invoke();
        }
    }

    /// <summary>
    /// The identity of the signal groups of a crossing. A group is a number in the program (its place in the file), which moves when a group
    /// before it is taken away; its colour must not. Each group carries an id of this session, the colour comes from the id, so a colour stays
    /// with its group when it is renamed, moved or another group is deleted. Nothing is written to a file: the ids start again as 0, 1, 2
    /// when a crossing is opened.
    /// </summary>
    internal sealed class SignalGroupIdentities
    {
        /// <summary>
        /// The colours that tell groups apart. None is red, yellow or green (hues of 20 to 165 degrees and the red around 0), which are kept for
        /// the states of a light; each is far enough from the others to be told apart on a dark map.
        /// </summary>
        private static readonly Color[] Palette =
        {
            new Color32(0x4D, 0x8C, 0xF5, 255), new Color32(0xA8, 0x70, 0xFF, 255), new Color32(0x21, 0xC2, 0xD6, 255), new Color32(0xF0, 0x6B, 0xC7, 255),
            new Color32(0x7A, 0x8C, 0xFF, 255), new Color32(0xA3, 0xB3, 0xCC, 255), new Color32(0xC9, 0xA0, 0xFF, 255), new Color32(0x5F, 0xA8, 0xFF, 255)
        };

        private readonly List<int> ids = new List<int>();

        public int Count => ids.Count;

        public static int PaletteSize => Palette.Length;

        /// <summary>The colour of a slot of the palette; a slot beyond the palette wraps around.</summary>
        public static Color ColourOfSlot(int slot)
        {
            return Palette[((slot % Palette.Length) + Palette.Length) % Palette.Length];
        }

        /// <summary>The ids of the groups are 0, 1, 2 ... again: a crossing opened, or a program that was replaced.</summary>
        public void Reset(int count)
        {
            ids.Clear();
            for (var index = 0; index < count; index++)
                ids.Add(index);
        }

        /// <summary>Makes the number of ids the number of groups; a program that changed by itself (undo, assistant) starts the ids again.</summary>
        public void Sync(int count)
        {
            if (ids.Count != count)
                Reset(count);
        }

        public int IdOf(int group)
        {
            return group >= 0 && group < ids.Count ? ids[group] : group;
        }

        public Color ColourOf(int group)
        {
            return ColourOfSlot(IdOf(group));
        }

        /// <summary>The ids as they are now, to put back when the change that moved them failed.</summary>
        public int[] Snapshot()
        {
            return ids.ToArray();
        }

        /// <summary>Puts the ids back as a <see cref="Snapshot"/> had them: a change that was not written (the file could not be made) leaves the colours where they were.</summary>
        public void Restore(int[] snapshot)
        {
            ids.Clear();
            if (snapshot != null)
                ids.AddRange(snapshot);
        }

        /// <summary>A group was added at the end: it takes the lowest id no group has, so the colour of a deleted group comes back first.</summary>
        public int Added()
        {
            var id = 0;
            while (ids.Contains(id))
                id++;
            ids.Add(id);
            return id;
        }

        /// <summary>A group was taken away; the groups after it move up in the program and keep their ids.</summary>
        public void Removed(int group)
        {
            if (group >= 0 && group < ids.Count)
                ids.RemoveAt(group);
        }

        /// <summary>A group moved to another place of the list; it keeps its id.</summary>
        public void Moved(int from, int to)
        {
            if (from < 0 || from >= ids.Count || to < 0 || to >= ids.Count || from == to)
                return;
            var id = ids[from];
            ids.RemoveAt(from);
            ids.Insert(to, id);
        }
    }

    /// <summary>The place and size of a floating window inside the area around it; pure, so it can be tested without a panel.</summary>
    internal static class FloatingWindowRules
    {
        /// <summary>Where a window of this size stands when it is put at <paramref name="position"/>: inside the area, or at its top left when it is larger than it.</summary>
        public static Vector2 Clamp(Vector2 position, Vector2 size, Vector2 area)
        {
            return new Vector2(Mathf.Clamp(position.x, 0f, Mathf.Max(0f, area.x - size.x)), Mathf.Clamp(position.y, 0f, Mathf.Max(0f, area.y - size.y)));
        }

        /// <summary>A size within the least and the most the area allows; the least gives way when the area is smaller than it.</summary>
        public static Vector2 ClampSize(Vector2 size, Vector2 least, Vector2 area)
        {
            return new Vector2(Mathf.Clamp(size.x, Mathf.Min(least.x, area.x), Mathf.Max(0f, area.x)), Mathf.Clamp(size.y, Mathf.Min(least.y, area.y), Mathf.Max(0f, area.y)));
        }

        /// <summary>The most a window may take of the area: the margin at both sides and at the top, the bottom margin (the bottom bar) at the bottom.</summary>
        public static Vector2 MaxSize(Vector2 area, float margin, float bottom)
        {
            return new Vector2(Mathf.Max(0f, area.x - 2f * margin), Mathf.Max(0f, area.y - margin - bottom));
        }

        /// <summary>
        /// The size and the place of a window after the area around it changed: the size within the least and <see cref="MaxSize"/>, the place
        /// inside the area above the bottom margin. <paramref name="shownHeight"/> is the height the window shows (a collapsed window is as high as its title bar).
        /// </summary>
        public static (Vector2 Position, Vector2 Size) Fit(Vector2 position, Vector2 size, float shownHeight, Vector2 least, Vector2 area, float margin, float bottom)
        {
            var fitted = ClampSize(size, least, MaxSize(area, margin, bottom));
            var shown = new Vector2(fitted.x, Mathf.Min(shownHeight, fitted.y));
            var place = Clamp(position, shown, new Vector2(area.x, Mathf.Max(0f, area.y - bottom)));
            return (place, fitted);
        }

        /// <summary>The first place of a window: at the right of the area with a margin, at the top.</summary>
        public static Vector2 FirstPlace(Vector2 size, Vector2 area, float margin)
        {
            return Clamp(new Vector2(area.x - size.x - margin, margin), size, area);
        }
    }

    /// <summary>What the signals window shows, decided from the state of the tool; pure rules without the window.</summary>
    internal static class SignalWindowRules
    {
        /// <summary>
        /// What the footer says: nothing without a crossing, else the question that waits for a second click, else why the crossing is
        /// only read, else the state of the draft.
        /// </summary>
        public static string FooterText(bool hasCrossing, string confirmText, SignalChangeState state, string readOnlyReason, string unapplied, string applied)
        {
            if (!hasCrossing)
                return string.Empty;
            if (!string.IsNullOrEmpty(confirmText))
                return confirmText;
            if (state == SignalChangeState.ReadOnly)
                return readOnlyReason ?? string.Empty;
            return state == SignalChangeState.Unapplied ? unapplied : applied;
        }

        /// <summary>The area a page belongs to.</summary>
        public static SignalArea AreaOf(SignalTab page)
        {
            switch (page)
            {
                case SignalTab.Phases:
                case SignalTab.Timeline:
                    return SignalArea.Phases;
                case SignalTab.Check:
                    return SignalArea.Check;
                case SignalTab.Wizard:
                case SignalTab.Templates:
                    return SignalArea.Assistant;
                default:
                    return SignalArea.Groups;
            }
        }

        /// <summary>The page an area opens with; the area of the phases and the assistant remember the page last open in them.</summary>
        public static SignalTab PageOf(SignalArea area, SignalTab lastPhasePage, SignalTab lastAssistantPage)
        {
            switch (area)
            {
                case SignalArea.Phases: return lastPhasePage == SignalTab.Timeline ? SignalTab.Timeline : SignalTab.Phases;
                case SignalArea.Check: return SignalTab.Check;
                case SignalArea.Assistant: return lastAssistantPage == SignalTab.Templates ? SignalTab.Templates : SignalTab.Wizard;
                default: return SignalTab.Movements;
            }
        }

        /// <summary>Whether the switch between two pages of an area shows (phases/timeline, proposal/templates).</summary>
        public static bool HasSubPages(SignalArea area)
        {
            return area == SignalArea.Phases || area == SignalArea.Assistant;
        }

        /// <summary>The text of the question about unapplied changes: the way out that waits says what would happen.</summary>
        public static string PromptKey(SignalExitKind? waiting)
        {
            return waiting == SignalExitKind.ViewChange ? "signals.prompt.text.view" : "signals.prompt.text";
        }

        /// <summary>
        /// Where a point (panel pixels) has to stand to be seen beside a floating window that covers it: the point moves into the largest free
        /// part of the map left of, right of or above the window, no closer than <paramref name="margin"/> to its edges, by the shortest way.
        /// Zero when the window is clear of the point (by more than the margin) or no part is large enough. <paramref name="map"/> is the
        /// visible area of the map (without the bottom bar).
        /// </summary>
        public static Vector2 ShiftAroundWindow(Vector2 point, Rect window, Rect map, float margin)
        {
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || window.width < 1f || window.height < 1f || float.IsNaN(window.x) || float.IsNaN(window.y))
                return Vector2.zero;
            var covered = point.x >= window.xMin - margin && point.x <= window.xMax + margin && point.y >= window.yMin - margin && point.y <= window.yMax + margin;
            if (!covered)
                return Vector2.zero;
            var parts = new[]
            {
                Rect.MinMaxRect(map.xMin, map.yMin, window.xMin, map.yMax),
                Rect.MinMaxRect(window.xMax, map.yMin, map.xMax, map.yMax),
                Rect.MinMaxRect(map.xMin, map.yMin, map.xMax, window.yMin)
            };
            var best = -1;
            var bestArea = 0f;
            for (var index = 0; index < parts.Length; index++)
            {
                if (parts[index].width <= margin * 2f || parts[index].height <= margin * 2f)
                    continue;
                var area = parts[index].width * parts[index].height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = index;
                }
            }
            if (best < 0)
                return Vector2.zero;
            var part = parts[best];
            var target = new Vector2(Mathf.Clamp(point.x, part.xMin + margin, part.xMax - margin), Mathf.Clamp(point.y, part.yMin + margin, part.yMax - margin));
            return target - point;
        }

        /// <summary>The state the footer reports.</summary>
        public static SignalChangeState ChangeState(bool editable, bool dirty)
        {
            return !editable ? SignalChangeState.ReadOnly : dirty ? SignalChangeState.Unapplied : SignalChangeState.Applied;
        }

        /// <summary>Apply and Discard are usable only for a draft with changes on a crossing that can be edited.</summary>
        public static bool DraftActionsEnabled(bool editable, bool dirty)
        {
            return editable && dirty;
        }

        /// <summary>How the program of the crossing can be shown as phases: a plan, or not representable, which is flagged and never converted.</summary>
        public static SignalPhaseView PhaseViewOf(OmsiSignalProgram program)
        {
            if (program == null || program.Lights.Count == 0)
                return SignalPhaseView.Empty;
            return OmsiSignalPhasePlan.Detect(program) != null ? SignalPhaseView.Plan : SignalPhaseView.NotRepresentable;
        }

        /// <summary>
        /// The line the status bar shows for the window: why the crossing cannot be changed when that is so, what the next click does, and last
        /// what the crossing is. Empty parts are left out.
        /// </summary>
        public static string Hint(string crossing, string readOnlyReason, string nextClick)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(readOnlyReason))
                lines.Add(readOnlyReason);
            if (!string.IsNullOrWhiteSpace(nextClick))
                lines.Add(nextClick);
            if (!string.IsNullOrWhiteSpace(crossing))
                lines.Add(crossing);
            return string.Join("\n", lines);
        }

        /// <summary>The area a finding of the check is mended in: the timing of the groups, or the groups, movements and heads.</summary>
        public static SignalArea AreaOfFinding(OmsiSignalFindingKind kind)
        {
            switch (kind)
            {
                case OmsiSignalFindingKind.HeadWithoutGroup:
                case OmsiSignalFindingKind.GroupWithoutHead:
                case OmsiSignalFindingKind.NoHeads:
                case OmsiSignalFindingKind.UnboundMovements:
                    return SignalArea.Groups;
                default:
                    return SignalArea.Phases;
            }
        }

        /// <summary>The time the preview goes to after a change of the program: the one it was at, held within the new cycle.</summary>
        public static double ClampPreviewTime(double time, double cycle)
        {
            if (cycle <= 0 || double.IsNaN(time))
                return 0;
            return Math.Max(0, Math.Min(time, cycle));
        }
    }
}
