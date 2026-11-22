using System;
using System.Globalization;
using System.IO;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>What a key does in the road type window.</summary>
    internal enum RoadWindowKeyAction
    {
        /// <summary>Nothing of the window's own; the key still does not reach the map while the focus is in the window.</summary>
        None,
        Undo,
        Redo
    }

    /// <summary>What the footer of the road type window tells about the state of the draft, in the order of what matters most.</summary>
    internal enum RoadFooterState
    {
        /// <summary>The saved road type changed under the draft (decision 2 of 18 November 2026).</summary>
        Stale,

        /// <summary>The draft cannot be saved as it is.</summary>
        Invalid,

        /// <summary>The draft differs from the saved road type.</summary>
        Changed,

        /// <summary>A new road type that is not saved yet.</summary>
        NewUnsaved,

        /// <summary>The draft is the saved road type.</summary>
        Unchanged
    }

    /// <summary>Why Save road type is not possible, or None when it is.</summary>
    internal enum RoadSaveBlock
    {
        None,
        ReadOnly,
        NoChanges,
        Problems,
        Stale,

        /// <summary>The road type lies outside the open project's own folder: it may only be saved as a new type (decision 1).</summary>
        NotOwned
    }

    /// <summary>What the main button of the footer does with the draft.</summary>
    internal enum RoadPrimarySave
    {
        /// <summary>A new road type is made.</summary>
        Create,

        /// <summary>A saved road type of the open project is written anew, after asking when the map uses it.</summary>
        Overwrite,

        /// <summary>A saved road type of another project, or not made by MapWorks, can only be saved as a new type of the open project.</summary>
        SaveAsNew
    }

    /// <summary>What the bar between the work area and the footer says or asks, in the order of what matters most.</summary>
    internal enum RoadNotice
    {
        None,

        /// <summary>The road type is used by the open map: update all uses, save as a new type, or cancel.</summary>
        SaveChoice,

        /// <summary>The name of the new road type to save as.</summary>
        SaveName,

        /// <summary>Delete the road type? Yes or cancel.</summary>
        DeleteConfirm,

        /// <summary>The road type cannot be deleted, and why.</summary>
        DeleteBlocked,

        /// <summary>The search of the other maps before deleting is running.</summary>
        Searching,

        /// <summary>Saving or deleting failed; the draft stays and it can be tried again.</summary>
        Error,

        /// <summary>The saved road type changed outside the window: reload it or keep the draft and save it as a new type.</summary>
        Stale,

        /// <summary>The saved road type is gone: keep the draft and save it as a new type.</summary>
        Missing,

        /// <summary>The road type lies outside the open project's folder: it opens to be saved as a new type only.</summary>
        Foreign
    }

    /// <summary>How much of an element's label fits into its drawn width.</summary>
    internal enum RoadLabelLevel
    {
        None,
        Number,
        Short,
        Full
    }

    /// <summary>What the actions under the preview can do for the selection.</summary>
    internal struct RoadActionState
    {
        public bool CanMoveLeft;
        public bool CanMoveRight;
        public bool CanDuplicate;
        public bool CanRemove;
        public bool CanAdd;

        /// <summary>The translated reason an action is not possible, as a key; null when none is blocked for a reason worth telling.</summary>
        public string AddBlockedKey;
    }

    /// <summary>
    /// The decisions of the road type window that need no panel (ADR 098, road type window): what the footer says, when saving is blocked and
    /// why, which actions the selection allows, how a typed width is read, how much of a label fits, and which keys the window takes. Pure, so
    /// it is tested without a window; the texts are keys of <c>roadbuilder.json</c> that the window translates.
    /// </summary>
    internal static class RoadTypeWindowRules
    {
        private static readonly string[] LaneKeys =
        {
            "roadbuilder.panel.lane.driving", "roadbuilder.panel.lane.sidewalk", "roadbuilder.panel.lane.median", "roadbuilder.panel.lane.shoulder",
            "roadbuilder.panel.lane.bus", "roadbuilder.panel.lane.bike", "roadbuilder.panel.lane.parking", "roadbuilder.panel.lane.tram"
        };

        private static readonly string[] LaneShortKeys =
        {
            "roadbuilder.panel.lane.driving.short", "roadbuilder.panel.lane.sidewalk.short", "roadbuilder.panel.lane.median.short", "roadbuilder.panel.lane.shoulder.short",
            "roadbuilder.panel.lane.bus.short", "roadbuilder.panel.lane.bike.short", "roadbuilder.panel.lane.parking.short", "roadbuilder.panel.lane.tram.short"
        };

        /// <summary>
        /// The card an arrow key moves to in a grid of cards: sideways the nearest enabled card in the same row, up and down the nearest row that
        /// way and in it the nearest column. -1 when there is none. across/down are -1, 0 or 1 (one of them non-zero).
        /// </summary>
        public static int NextCard(UnityEngine.Rect[] bounds, bool[] enabled, int from, int across, int down)
        {
            var origin = bounds[from].center;
            var half = bounds[from].height * 0.5f;
            var best = -1;
            var bestScore = float.MaxValue;
            for (var i = 0; i < bounds.Length; i++)
            {
                if (i == from || !enabled[i])
                    continue;
                var delta = bounds[i].center - origin;
                float score;
                if (across != 0)
                {
                    if (UnityEngine.Mathf.Abs(delta.y) > half || delta.x * across <= 0f)
                        continue;
                    score = UnityEngine.Mathf.Abs(delta.x);
                }
                else
                {
                    if (UnityEngine.Mathf.Abs(delta.y) <= half || delta.y * down <= 0f)
                        continue;
                    score = UnityEngine.Mathf.Abs(delta.y) * 4f + UnityEngine.Mathf.Abs(delta.x);
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>The key of a kind's name, such as "Driving lane".</summary>
        public static string LaneKey(OmsiRoadLaneKind kind)
        {
            return LaneKeys[(int)kind];
        }

        /// <summary>The key of a kind's short mark (LANE, WALK, BUS ...) for the preview.</summary>
        public static string ShortKey(OmsiRoadLaneKind kind)
        {
            return LaneShortKeys[(int)kind];
        }

        /// <summary>The key of a template's name; the templates have stable ids from their English names.</summary>
        public static string TemplateKey(string templateId)
        {
            switch (templateId)
            {
                case "2-lanes": return "roadbuilder.panel.template.lanes2";
                case "4-lanes": return "roadbuilder.panel.template.lanes4";
                case "2-plus-2-with-median": return "roadbuilder.panel.template.median";
                case "one-way": return "roadbuilder.panel.template.oneway";
                case "bus-lanes": return "roadbuilder.panel.template.bus";
                case "parking-and-bikes": return "roadbuilder.panel.template.parking";
                case "country-road": return "roadbuilder.panel.template.country";
                case "tram-in-the-street": return "roadbuilder.panel.template.tramstreet";
                case "tram-in-the-middle": return "roadbuilder.panel.template.trammiddle";
                case "footpath": return "roadbuilder.panel.template.footpath";
                default: return "roadbuilder.panel.template.lanes2";
            }
        }

        /// <summary>The key of the text that tells why a road type cannot be saved.</summary>
        public static string ProblemKey(OmsiRoadTypeProblemCode code)
        {
            switch (code)
            {
                case OmsiRoadTypeProblemCode.NoLanes: return "roadbuilder.window.problem.no_lanes";
                case OmsiRoadTypeProblemCode.TooManyLanes: return "roadbuilder.window.problem.too_many";
                case OmsiRoadTypeProblemCode.WidthOutOfRange: return "roadbuilder.window.problem.width";
                case OmsiRoadTypeProblemCode.RowNotAllowed: return "roadbuilder.window.problem.row_not_allowed";
                default: return "roadbuilder.window.problem.row_dense";
            }
        }

        /// <summary>What the footer says: the most important state first.</summary>
        public static RoadFooterState Footer(bool isNew, bool dirty, bool stale, int problems)
        {
            if (stale)
                return RoadFooterState.Stale;
            if (problems > 0)
                return RoadFooterState.Invalid;
            if (dirty)
                return RoadFooterState.Changed;
            return isNew ? RoadFooterState.NewUnsaved : RoadFooterState.Unchanged;
        }

        public static string FooterKey(RoadFooterState state)
        {
            switch (state)
            {
                case RoadFooterState.Stale: return "roadbuilder.window.status.stale";
                case RoadFooterState.Invalid: return "roadbuilder.window.status.invalid";
                case RoadFooterState.Changed: return "roadbuilder.window.status.changed";
                case RoadFooterState.NewUnsaved: return "roadbuilder.window.status.new";
                default: return "roadbuilder.window.status.unchanged";
            }
        }

        /// <summary>
        /// Why Save road type is blocked. A new road type is saved when it has no problem, changed or not; a saved one only when it has
        /// changes, no problem, is not stale and lies in the project's own folder.
        /// </summary>
        public static RoadSaveBlock SaveBlock(bool isNew, bool dirty, bool stale, int problems, bool canEdit, bool ownedByProject)
        {
            if (!canEdit)
                return RoadSaveBlock.ReadOnly;
            if (problems > 0)
                return RoadSaveBlock.Problems;
            if (isNew)
                return RoadSaveBlock.None;
            if (stale)
                return RoadSaveBlock.Stale;
            if (!ownedByProject)
                return RoadSaveBlock.NotOwned;
            return dirty ? RoadSaveBlock.None : RoadSaveBlock.NoChanges;
        }

        /// <summary>What the main button of the footer does: a new road type is made, a saved one of the project is written anew, any other can only be saved as new.</summary>
        public static RoadPrimarySave PrimarySave(bool isNew, bool ownedByProject)
        {
            if (isNew)
                return RoadPrimarySave.Create;
            return ownedByProject ? RoadPrimarySave.Overwrite : RoadPrimarySave.SaveAsNew;
        }

        /// <summary>Whether the footer has a second button, Save as new type: next to the main one for a saved road type of the project.</summary>
        public static bool ShowsSaveAsNew(bool isNew, bool ownedByProject)
        {
            return !isNew && ownedByProject;
        }

        /// <summary>
        /// Why a road type cannot be saved as a new one: not while the window is read-only or the draft has problems. Changes are not needed (a
        /// copy of a road type is one), and a stale draft may be kept this way.
        /// </summary>
        public static RoadSaveBlock SaveAsNewBlock(int problems, bool canEdit)
        {
            if (!canEdit)
                return RoadSaveBlock.ReadOnly;
            return problems > 0 ? RoadSaveBlock.Problems : RoadSaveBlock.None;
        }

        /// <summary>Why the main button of the footer is blocked, or None: the rules of the action it has (<see cref="PrimarySave"/>).</summary>
        public static RoadSaveBlock PrimaryBlock(bool isNew, bool dirty, bool stale, int problems, bool canEdit, bool ownedByProject)
        {
            return PrimarySave(isNew, ownedByProject) == RoadPrimarySave.SaveAsNew
                ? SaveAsNewBlock(problems, canEdit)
                : SaveBlock(isNew, dirty, stale, problems, canEdit, ownedByProject);
        }

        /// <summary>Whether Save asks first: when the open map uses the road type, so a person sees what changes with it. Nothing in the map changes otherwise.</summary>
        public static bool AsksBeforeOverwrite(int pieces, int junctions)
        {
            return pieces > 0 || junctions > 0;
        }

        /// <summary>Whether the road type can be deleted at all: a saved one of the project's folder, in a map that may change.</summary>
        public static bool CanDelete(bool isNew, bool ownedByProject, bool canEdit)
        {
            return !isNew && ownedByProject && canEdit;
        }

        /// <summary>Whether the road type may be deleted now: the open map must not use it (ADR 049); the window names the reason otherwise.</summary>
        public static bool MayDelete(int pieces, int junctions)
        {
            return pieces == 0 && junctions == 0;
        }

        /// <summary>What the bar shows: a question or message of its own first, then what the state of the road type asks for.</summary>
        /// <summary>Whether a question or message about the old state of the saved road type goes away when the road type turns stale: the questions and a failure whose Try again would write over it.</summary>
        public static bool DropsPendingWhenStale(RoadNotice pending)
        {
            return pending == RoadNotice.SaveChoice || pending == RoadNotice.DeleteConfirm || pending == RoadNotice.Error;
        }

        public static RoadNotice Notice(RoadNotice pending, bool isNew, bool stale, bool missing, bool ownedByProject)
        {
            if (pending != RoadNotice.None)
                return pending;
            if (isNew)
                return RoadNotice.None;
            if (stale)
                return missing ? RoadNotice.Missing : RoadNotice.Stale;
            return ownedByProject ? RoadNotice.None : RoadNotice.Foreign;
        }

        /// <summary>The key of the text for a failed save or delete, by what failed; the reason of the system goes in as {reason}, a missing file as {file}.</summary>
        public static string SaveFailureKey(Exception exception)
        {
            if (exception is FileNotFoundException)
                return "roadbuilder.window.error.missing";
            if (exception is UnauthorizedAccessException)
                return "roadbuilder.window.error.denied";
            if (exception is DirectoryNotFoundException)
                return "roadbuilder.window.error.folder";
            if (exception is IOException)
                return "roadbuilder.window.error.io";
            return "roadbuilder.window.error.invalid";
        }

        /// <summary>The key of the text for a remark of taking a road apart into lanes.</summary>
        public static string NoteKey(OmsiRoadTypeNoteCode code)
        {
            switch (code)
            {
                case OmsiRoadTypeNoteCode.WidthFitted: return "roadbuilder.window.note.width";
                case OmsiRoadTypeNoteCode.LanesLimited: return "roadbuilder.window.note.lanes";
                default: return "roadbuilder.window.note.tracks";
            }
        }

        /// <summary>The name offered for a copy of a saved road type: its file name and the word for copy, within the length of the name field.</summary>
        public static string CopyName(string fileName, string copyWord)
        {
            var name = ((fileName ?? string.Empty).Trim() + " " + (copyWord ?? string.Empty).Trim()).Trim();
            return name.Length > 60 ? name.Substring(name.Length - 60).TrimStart() : name;
        }

        public static string SaveBlockKey(RoadSaveBlock block)
        {
            switch (block)
            {
                case RoadSaveBlock.ReadOnly: return "roadbuilder.window.save.read_only";
                case RoadSaveBlock.NoChanges: return "roadbuilder.window.save.no_changes";
                case RoadSaveBlock.Problems: return "roadbuilder.window.save.problems";
                case RoadSaveBlock.Stale: return "roadbuilder.window.save.stale";
                case RoadSaveBlock.NotOwned: return "roadbuilder.window.save.not_owned";
                default: return null;
            }
        }

        /// <summary>What the actions under the preview can do: the arrows stop at the ends, nothing is possible without a selected element.</summary>
        public static RoadActionState Actions(int count, int selectedIndex, bool elementSelected)
        {
            var has = elementSelected && selectedIndex >= 0 && selectedIndex < count;
            var full = count >= OmsiRoadType.MaximumLanes;
            return new RoadActionState
            {
                CanMoveLeft = has && selectedIndex > 0,
                CanMoveRight = has && selectedIndex < count - 1,
                CanDuplicate = has && !full,
                CanRemove = has,
                CanAdd = !full,
                AddBlockedKey = full ? "roadbuilder.window.add.full" : null
            };
        }

        /// <summary>
        /// Reads a width typed in metres: a comma or a point as the decimal mark, rounded to 0.01 m. False for text that is no number, not
        /// finite or not above zero (the range of the kind is the draft's to judge).
        /// </summary>
        public static bool TryParseWidth(string text, out double metres)
        {
            metres = 0.0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            var cleaned = text.Trim().Replace(',', '.');
            if (cleaned.EndsWith("m", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(0, cleaned.Length - 1).TrimEnd();
            if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
                return false;
            metres = OmsiRoadTypeDraft.RoundWidth(value);
            return metres > 0.0;
        }

        /// <summary>A width as shown in a field: two decimals, the decimal mark of the culture.</summary>
        public static string FormatWidth(double metres, CultureInfo culture = null)
        {
            return metres.ToString("0.00", culture ?? CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// How much of an element's label fits into its drawn width in pixels: the short mark with the number and the width, the number alone,
        /// or nothing, so labels of narrow elements never overlap. The long text needs <paramref name="characterPixels"/> a character.
        /// </summary>
        public static RoadLabelLevel LabelLevel(float drawnPixels, int shortCharacters, int widthCharacters, float characterPixels = 6.5f)
        {
            const float Padding = 6f;
            if (drawnPixels >= Math.Max(shortCharacters, widthCharacters) * characterPixels + Padding)
                return RoadLabelLevel.Full;
            if (drawnPixels >= shortCharacters * characterPixels + Padding)
                return RoadLabelLevel.Short;
            if (drawnPixels >= 2f * characterPixels + 4f)
                return RoadLabelLevel.Number;
            return RoadLabelLevel.None;
        }

        /// <summary>The width a click on an element counts for: at least <paramref name="least"/> pixels, so a narrow element stays selectable; the drawing keeps its true width.</summary>
        public static float HitWidth(float drawnPixels, float least = 14f)
        {
            return Mathf.Max(drawnPixels, least);
        }

        /// <summary>The width in pixels of each element for a total, proportional to the real widths; no invisible minimum.</summary>
        public static float[] DrawnWidths(double[] widths, float totalPixels)
        {
            var sum = 0.0;
            foreach (var width in widths)
                sum += width;
            var result = new float[widths.Length];
            if (sum <= 0.0)
                return result;
            for (var index = 0; index < widths.Length; index++)
                result[index] = (float)(widths[index] / sum * totalPixels);
            return result;
        }

        // ----- the cross-section strip -----

        /// <summary>The narrowest an element may be drawn before the strip zooms in and scrolls (so its click target stays usable).</summary>
        public const float LeastElementPixels = 22f;

        /// <summary>The most the strip zooms in relative to its fitted scale.</summary>
        public const float MostZoom = 8f;

        /// <summary>
        /// Pixels per metre for the strip: the widths fitted exactly into the viewport, or, when the narrowest element would be drawn
        /// narrower than <paramref name="leastElementPixels"/>, zoomed in (up to <see cref="MostZoom"/> times) so that every element
        /// stays selectable by scrolling. All elements share the one scale, so the proportions stay true.
        /// </summary>
        public static float StripScale(double[] widths, float viewportPixels, float leastElementPixels = LeastElementPixels)
        {
            var sum = 0.0;
            var narrowest = double.MaxValue;
            foreach (var width in widths)
            {
                sum += width;
                narrowest = Math.Min(narrowest, width);
            }
            if (sum <= 0.0 || viewportPixels <= 0f)
                return 0f;
            var fitted = (float)(viewportPixels / sum);
            var wanted = (float)(leastElementPixels / Math.Max(narrowest, 0.01));
            return Math.Max(fitted, Math.Min(wanted, fitted * MostZoom));
        }

        /// <summary>The pixel positions of the edges of the elements (count + 1 values from 0), rounded from the running sum so no gap or overlap appears.</summary>
        public static float[] StripEdges(double[] widths, float scale)
        {
            var edges = new float[widths.Length + 1];
            var sum = 0.0;
            for (var index = 0; index < widths.Length; index++)
            {
                sum += widths[index];
                edges[index + 1] = Mathf.Round((float)(sum * scale));
            }
            return edges;
        }

        /// <summary>What lies under a pointer on the strip.</summary>
        internal struct StripHit
        {
            /// <summary>The element, or for a boundary the element left of it; -1 when there are no elements.</summary>
            public int Index;
            public bool Boundary;
        }

        /// <summary>
        /// What a pointer at <paramref name="x"/> hits: a boundary between two elements when it is within <paramref name="boundaryPixels"/>
        /// of one (the line wins there, so a boundary can always be picked), else the element under it; beyond the ends the first or last element.
        /// </summary>
        public static StripHit HitStrip(float[] edges, float x, float boundaryPixels = 4f)
        {
            var count = edges.Length - 1;
            if (count <= 0)
                return new StripHit { Index = -1 };
            for (var index = 0; index + 1 < count; index++)
                if (Mathf.Abs(edges[index + 1] - x) <= boundaryPixels)
                    return new StripHit { Index = index, Boundary = true };
            for (var index = 0; index < count; index++)
                if (x < edges[index + 1])
                    return new StripHit { Index = index };
            return new StripHit { Index = count - 1 };
        }

        /// <summary>The gap a dragged element is dropped into: 0 before the first element up to the count after the last, by the elements' centres.</summary>
        public static int DropGap(float[] edges, float x)
        {
            var count = edges.Length - 1;
            for (var index = 0; index < count; index++)
                if (x < (edges[index] + edges[index + 1]) * 0.5f)
                    return index;
            return Math.Max(0, count);
        }

        /// <summary>Where an element dropped into a gap ends up (the gap counts the element's own place); the element's own index when it stays.</summary>
        public static int DropIndex(int from, int gap)
        {
            return gap > from ? gap - 1 : gap;
        }

        /// <summary>The scroll offset that brings an element into view with the least movement; the offset itself when it is in view already.</summary>
        public static float ScrollToReveal(float left, float right, float offset, float viewport, float margin = 12f)
        {
            if (viewport <= 0f || right - left + 2f * margin >= viewport)
                return Mathf.Max(0f, left - margin);
            if (left - margin < offset)
                return Mathf.Max(0f, left - margin);
            if (right + margin > offset + viewport)
                return right + margin - viewport;
            return offset;
        }

        /// <summary>Reads a plain number typed in a field (comma or point), not rounded; false for text that is no finite number.</summary>
        public static bool TryParseNumber(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Whether the window takes the keyboard focus back after it drew itself: when the focus was in the window before, and what had it is gone
        /// (no focus at all, the focus left the window by itself), or is disabled or hidden now (a clicked button that became disabled). Without it the
        /// keys would reach the map. A window that had no focus is not given one, so drawing never takes the focus from the map.
        /// </summary>
        public static bool NeedsFocusBack(bool hadFocusInWindow, bool hasFocus, bool focusInWindow, bool focusReachable)
        {
            return hadFocusInWindow && (!hasFocus || !focusInWindow || !focusReachable);
        }

        /// <summary>What a key does while the focus is in the window: Ctrl+Z undoes, Ctrl+Y and Ctrl+Shift+Z redo; the rest is nothing.</summary>
        public static RoadWindowKeyAction KeyAction(KeyCode key, bool control, bool shift)
        {
            if (!control)
                return RoadWindowKeyAction.None;
            if (key == KeyCode.Z)
                return shift ? RoadWindowKeyAction.Redo : RoadWindowKeyAction.Undo;
            return key == KeyCode.Y ? RoadWindowKeyAction.Redo : RoadWindowKeyAction.None;
        }

        /// <summary>
        /// Whether the shell's key shortcuts (tool keys, Delete, Ctrl+Z/Y of the map) must stay silent for a key pressed while the focus is in the
        /// window: always, except Esc, which the shell treats to close the window with its question.
        /// </summary>
        public static bool SwallowsShortcut(KeyCode key)
        {
            return key != KeyCode.Escape;
        }
    }
}
