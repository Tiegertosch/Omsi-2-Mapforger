using System.Collections.Generic;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The rules of the line tool's route strip (ADR 098, M4), free of any control: the strip shows the list of the direction being made, and a stop is
    /// moved by its place in that list. The same stop may stand in a list more than once (a ring begins and ends at one), so everything is by position.
    /// </summary>
    internal static class LineStrip
    {
        /// <summary>Moves the item at <paramref name="from"/> so that it ends up at <paramref name="to"/>; false when nothing changes or an index is outside.</summary>
        public static bool Move<T>(IList<T> list, int from, int to)
        {
            if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to)
                return false;
            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
            return true;
        }

        /// <summary>
        /// The place the item at <paramref name="index"/> takes when it moves one place to the left (<paramref name="delta"/> -1) or to the right (+1);
        /// -1 when it is already at that end of the list or the index is outside.
        /// </summary>
        public static int StepTarget(int index, int delta, int count)
        {
            var target = index + delta;
            return index < 0 || index >= count || target < 0 || target >= count ? -1 : target;
        }

        /// <summary>
        /// Where a dragged stop ends up when it is dropped in front of the place <paramref name="slot"/> (0 is before the first, the count after the last).
        /// </summary>
        public static int DropTarget(int from, int slot)
        {
            return slot > from ? slot - 1 : slot;
        }

        /// <summary>The selected place after the item at <paramref name="from"/> moved to <paramref name="to"/>.</summary>
        public static int SelectionAfterMove(int selected, int from, int to)
        {
            if (selected < 0 || from == to)
                return selected;
            if (selected == from)
                return to;
            if (from < selected && to >= selected)
                return selected - 1;
            if (from > selected && to <= selected)
                return selected + 1;
            return selected;
        }

        /// <summary>The place Delete takes out in the line tool: the selected one, or -1 when none is selected or it lies outside (the key does nothing then).</summary>
        public static int DeleteIndex(int selected, int count)
        {
            return selected >= 0 && selected < count ? selected : -1;
        }

        /// <summary>The selected place after the item at <paramref name="removed"/> was taken out: none when it was the selected one.</summary>
        public static int SelectionAfterRemove(int selected, int removed)
        {
            if (selected < 0 || selected == removed)
                return -1;
            return removed < selected ? selected - 1 : selected;
        }
    }
}
