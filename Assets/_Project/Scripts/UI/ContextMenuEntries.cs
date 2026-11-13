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
        DeleteObjects,
        DeleteEntryPoint,
        EditSplineHeights,
        DeleteSpline,
        ChangeSplineType,
        DeleteSplineInVariant,
        OpenTileProperties,
        Paste,
        RevertTile,
    }

    /// <summary>One entry of the context menu in order: its text key, the shortcut shown at the right, whether it is dangerous (red).</summary>
    internal readonly struct ContextMenuEntry
    {
        public readonly ContextMenuEntryType Type;
        public readonly ContextMenuAction Action;
        public readonly string TextKey;
        public readonly string Shortcut;
        public readonly bool Danger;

        public ContextMenuEntry(ContextMenuEntryType type, ContextMenuAction action, string textKey, string shortcut, bool danger)
        {
            Type = type;
            Action = action;
            TextKey = textKey;
            Shortcut = shortcut;
            Danger = danger;
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

        private static void Item(List<ContextMenuEntry> entries, ContextMenuAction action, string textKey, string shortcut = null, bool danger = false)
        {
            entries.Add(new ContextMenuEntry(ContextMenuEntryType.Item, action, textKey, shortcut, danger));
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
}
