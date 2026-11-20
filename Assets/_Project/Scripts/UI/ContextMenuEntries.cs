using System.Collections.Generic;

namespace OmsiMapEditor.UI
{
    /// <summary>What the right-click menu is for: several selected objects, one object, a spline or empty ground (a tile).</summary>
    internal enum ContextMenuKind
    {
        Selection,
        Object,
        Spline,
        Tile,
        /// <summary>A row of objects along a road (spline attachments), found by the pointer near one of its objects.</summary>
        Row,
    }

    /// <summary>What an entry of the context menu is: an action, a divider line or the note that the map is read-only.</summary>
    internal enum ContextMenuEntryType
    {
        Item,
        Divider,
        ReadOnlyNote,
    }

    /// <summary>The action an entry stands for; the shell maps each to what it does.</summary>
    internal enum ContextMenuAction
    {
        None,
        OpenProperties,
        RenameEntryPoint,
        RenameBusStop,
        ChangeType,
        EditLabels,
        OpenPaths,
        Copy,
        Duplicate,
        LevelGround,
        RandomizeHeadings,
        DeleteObjects,
        DeleteEntryPoint,
        EditSplineHeights,
        DeleteSpline,
        ChangeSplineType,
        DeleteSplineInVariant,
        OpenTileProperties,
        Paste,
        RevertTile,
        OpenMarkings,
        OpenTrafficRules,
        OpenLanes,
        OpenTrafficLights,
        /// <summary>A signal head: opens the traffic lights of the crossing it shows.</summary>
        OpenTrafficLightsOfHead,
        EditRow,
        DeleteRow,
    }

    /// <summary>One entry of the context menu in order: its text key, the shortcut shown at the right, whether it is dangerous (red).</summary>
    internal readonly struct ContextMenuEntry
    {
        public readonly ContextMenuEntryType Type;
        public readonly ContextMenuAction Action;
        public readonly string TextKey;
        public readonly string Shortcut;
        public readonly bool Danger;

        /// <summary>The key of the reason why the entry cannot be chosen now; null for an entry that can. Such an entry is shown greyed with its reason.</summary>
        public readonly string DisabledReasonKey;

        public ContextMenuEntry(ContextMenuEntryType type, ContextMenuAction action, string textKey, string shortcut, bool danger, string disabledReasonKey = null)
        {
            Type = type;
            Action = action;
            TextKey = textKey;
            Shortcut = shortcut;
            Danger = danger;
            DisabledReasonKey = disabledReasonKey;
        }
    }

    /// <summary>The switches that decide the entries; the shell reads them from its state, so the choice itself needs no shell.</summary>
    internal struct ContextMenuState
    {
        /// <summary>The whole map can be edited (<c>CanEdit</c>).</summary>
        public bool CanEdit;

        /// <summary>Objects can be edited (<c>CanEditObjects</c>); also true where only objects of a variant may change.</summary>
        public bool CanEditObjects;

        /// <summary>Only the splines of a variant can be edited (<c>CanEditSplinesInVariant</c>).</summary>
        public bool CanEditSplinesInVariant;

        /// <summary>The inspector has the group of AI paths (<c>PathsGroupShown</c>).</summary>
        public bool PathsGroupShown;

        /// <summary>The object is an entry point (never while a variant is edited).</summary>
        public bool IsEntryPoint;

        /// <summary>The object is a bus stop (never while a variant is edited).</summary>
        public bool IsBusStop;

        /// <summary>The clipboard holds something to paste.</summary>
        public bool ClipboardFilled;

        /// <summary>The tile can be reverted: the whole map is editable, the tile is edited and was not added in the project.</summary>
        public bool TileRevertible;

        /// <summary>The object is a junction the editor made (MapWorks): markings, lanes and traffic lights can be edited on it.</summary>
        public bool IsMadeJunction;

        /// <summary>The object has arms (connection points) the traffic rules tool can give a right of way.</summary>
        public bool HasPriorityArms;

        /// <summary>The object has at least two arms: the traffic lights tool can show (and, on a junction the editor made, change) its signals.</summary>
        public bool HasSignalArms;

        /// <summary>The object is a signal head whose crossing is a junction the editor made.</summary>
        public bool IsHeadOfMadeCrossing;
    }

    /// <summary>
    /// The entries of the context menu (ADR 098): which a kind of hit offers in which order, with its dividers, shortcuts and danger
    /// mark, from the switches alone. <see cref="EditorShellBootstrap"/> builds the menu from the list and maps the actions.
    /// </summary>
    internal static class ContextMenuEntries
    {
        public static List<ContextMenuEntry> For(ContextMenuKind kind, ContextMenuState state)
        {
            var entries = new List<ContextMenuEntry>();
            switch (kind)
            {
                case ContextMenuKind.Selection:
                    AddSelection(entries, state);
                    break;
                case ContextMenuKind.Object:
                    AddObject(entries, state);
                    break;
                case ContextMenuKind.Spline:
                    AddSpline(entries, state);
                    break;
                case ContextMenuKind.Row:
                    AddRow(entries, state);
                    break;
                default:
                    AddTile(entries, state);
                    break;
            }
            return entries;
        }

        private static void AddSelection(List<ContextMenuEntry> entries, ContextMenuState state)
        {
            Item(entries, ContextMenuAction.OpenProperties, "context.properties");
            if (!state.CanEditObjects)
            {
                Note(entries);
                return;
            }
            Divider(entries);
            Item(entries, ContextMenuAction.Copy, "context.copy", "Ctrl+C");
            Item(entries, ContextMenuAction.Duplicate, "context.duplicate", "Ctrl+D");
            if (state.CanEdit)
                Item(entries, ContextMenuAction.LevelGround, "context.level");
            // The compact inspector gives only the sum and the delete button of a group; these two actions live here and in the full view (ADR 098).
            Item(entries, ContextMenuAction.RandomizeHeadings, "context.randomize");
            Divider(entries);
            Item(entries, ContextMenuAction.DeleteObjects, "context.delete_many", "Del", true);
        }

        private static void AddObject(List<ContextMenuEntry> entries, ContextMenuState state)
        {
            Item(entries, ContextMenuAction.OpenProperties, "context.properties");
            if (state.IsEntryPoint)
                Item(entries, ContextMenuAction.RenameEntryPoint, "context.rename_entry");
            if (state.IsBusStop)
                Item(entries, ContextMenuAction.RenameBusStop, "context.rename_busstop");
            if (state.CanEditObjects)
            {
                Item(entries, ContextMenuAction.ChangeType, "context.change_type");
                Item(entries, ContextMenuAction.EditLabels, "context.labels");
            }
            if (state.PathsGroupShown)
                Item(entries, ContextMenuAction.OpenPaths, "context.paths");
            AddJunctionTools(entries, state);
            if (!state.CanEditObjects)
            {
                Note(entries);
                return;
            }
            Divider(entries);
            Item(entries, ContextMenuAction.Copy, "context.copy", "Ctrl+C");
            Item(entries, ContextMenuAction.Duplicate, "context.duplicate", "Ctrl+D");
            if (state.CanEdit)
                Item(entries, ContextMenuAction.LevelGround, "context.level");
            Divider(entries);
            if (state.IsEntryPoint)
                Item(entries, ContextMenuAction.DeleteEntryPoint, "context.delete_entry", null, true);
            else
                Item(entries, ContextMenuAction.DeleteObjects, "context.delete", "Del", true);
        }

        /// <summary>
        /// The tools that open on this junction, after a divider (ADR 098, M5): each only where the tool can work on it, the way the tool's own
        /// click decides. Markings, lanes and traffic lights need a junction the editor made (ADR 085) and the whole map; the traffic rules work
        /// on any object with arms and need only editable objects. A signal head opens the lights of its crossing.
        /// </summary>
        private static void AddJunctionTools(List<ContextMenuEntry> entries, ContextMenuState state)
        {
            // Where objects cannot be edited at all the note says so; the tools are not listed there.
            var junction = state.CanEditObjects && (state.IsMadeJunction || state.HasPriorityArms);
            var head = state.IsHeadOfMadeCrossing && state.CanEdit;
            if (!(junction || head))
                return;
            Divider(entries);
            if (junction)
            {
                // A junction from another map is greyed with the reason, not left out: the tool exists, it just cannot work on this one.
                var changeReason = state.IsMadeJunction ? (state.CanEdit ? null : ReasonReadOnlyMap) : ReasonForeignJunction;
                Item(entries, ContextMenuAction.OpenMarkings, "context.markings", null, false, changeReason);
                if (state.HasPriorityArms)
                    Item(entries, ContextMenuAction.OpenTrafficRules, "context.traffic_rules");
                Item(entries, ContextMenuAction.OpenLanes, "context.lanes", null, false, changeReason);
                if (changeReason == null)
                    Item(entries, ContextMenuAction.OpenTrafficLights, "context.traffic_lights");
                else if (!state.IsMadeJunction && state.CanEdit && state.HasSignalArms)
                    // The tool shows the signals of any crossing as OMSI's file has them, with nothing to change.
                    Item(entries, ContextMenuAction.OpenTrafficLights, "context.traffic_lights_view");
                else
                    Item(entries, ContextMenuAction.OpenTrafficLights, "context.traffic_lights", null, false,
                        state.IsMadeJunction || !state.CanEdit ? ReasonReadOnlyMap : ReasonFewArms);
            }
            if (head)
                Item(entries, ContextMenuAction.OpenTrafficLightsOfHead, "context.edit_traffic_lights");
        }

        internal const string ReasonForeignJunction = "context.reason.foreign_junction";
        internal const string ReasonReadOnlyMap = "context.reason.readonly_map";
        internal const string ReasonFewArms = "context.reason.few_arms";

        private static void AddRow(List<ContextMenuEntry> entries, ContextMenuState state)
        {
            if (!state.CanEdit)
            {
                Note(entries);
                return;
            }
            Item(entries, ContextMenuAction.EditRow, "context.edit_row");
            Divider(entries);
            Item(entries, ContextMenuAction.DeleteRow, "context.delete_row", null, true);
        }

        private static void AddSpline(List<ContextMenuEntry> entries, ContextMenuState state)
        {
            Item(entries, ContextMenuAction.OpenProperties, "context.properties");
            if (state.CanEdit)
                Item(entries, ContextMenuAction.EditSplineHeights, "context.spline_heights");
            if (state.PathsGroupShown)
                Item(entries, ContextMenuAction.OpenPaths, "context.paths");
            if (state.CanEdit)
            {
                Divider(entries);
                Item(entries, ContextMenuAction.DeleteSpline, "context.delete_spline", "Del", true);
            }
            else if (state.CanEditSplinesInVariant)
            {
                Divider(entries);
                Item(entries, ContextMenuAction.ChangeSplineType, "context.change_spline_type");
                Item(entries, ContextMenuAction.DeleteSplineInVariant, "context.delete_spline_variant", "Del", true);
            }
            else
                Note(entries);
        }

        private static void AddTile(List<ContextMenuEntry> entries, ContextMenuState state)
        {
            Item(entries, ContextMenuAction.OpenTileProperties, "context.tile_properties");
            if (!state.CanEditObjects)
            {
                Note(entries);
                return;
            }
            if (state.ClipboardFilled)
                Item(entries, ContextMenuAction.Paste, "context.paste", "Ctrl+V");
            if (state.TileRevertible)
            {
                Divider(entries);
                Item(entries, ContextMenuAction.RevertTile, "context.revert_tile");
            }
        }

        private static void Item(List<ContextMenuEntry> entries, ContextMenuAction action, string textKey, string shortcut = null, bool danger = false,
            string disabledReasonKey = null)
        {
            entries.Add(new ContextMenuEntry(ContextMenuEntryType.Item, action, textKey, shortcut, danger, disabledReasonKey));
        }

        private static void Divider(List<ContextMenuEntry> entries)
        {
            entries.Add(new ContextMenuEntry(ContextMenuEntryType.Divider, ContextMenuAction.None, null, null, false));
        }

        private static void Note(List<ContextMenuEntry> entries)
        {
            entries.Add(new ContextMenuEntry(ContextMenuEntryType.ReadOnlyNote, ContextMenuAction.None, "context.readonly", null, false));
        }
    }

    /// <summary>
    /// The keyboard focus of the open menu (ADR 098, M5): Up and Down move it over the active entries, dividers and disabled entries are
    /// skipped, and it wraps round. Pure, so the order is tested without a menu.
    /// </summary>
    internal static class ContextMenuNavigation
    {
        /// <summary>
        /// The entry the focus moves to: <paramref name="active"/> says for each entry in the menu whether it can be chosen,
        /// <paramref name="current"/> is where the focus is (-1 for none), <paramref name="direction"/> is 1 for down and -1 for up.
        /// Without a focus, down goes to the first and up to the last active entry. -1 when no entry is active.
        /// </summary>
        public static int Next(IReadOnlyList<bool> active, int current, int direction)
        {
            var count = active.Count;
            if (count == 0)
                return -1;
            var step = direction < 0 ? -1 : 1;
            var from = current < 0 || current >= count ? (step > 0 ? -1 : count) : current;
            for (var tried = 0; tried < count; tried++)
            {
                from = ((from + step) % count + count) % count;
                if (active[from])
                    return from;
            }
            return -1;
        }
    }
}
