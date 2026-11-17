using System;
using System.Collections.Generic;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// When the label lines of an element in the inspector are written (ADR 060, M4 "Labels"): Enter and leaving the lines write the
    /// lines together as one undo step, and only if one differs from what was written last. There is no button to apply them.
    /// </summary>
    internal static class LabelEditRules
    {
        /// <summary>Whether any line differs from the one written last (compared exactly; a missing line counts as empty).</summary>
        public static bool Differs(IReadOnlyList<string> written, IReadOnlyList<string> current)
        {
            var count = Math.Max(written?.Count ?? 0, current?.Count ?? 0);
            for (var index = 0; index < count; index++)
            {
                var before = written != null && index < written.Count ? written[index] ?? string.Empty : string.Empty;
                var now = current != null && index < current.Count ? current[index] ?? string.Empty : string.Empty;
                if (!string.Equals(before, now, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether leaving a line writes the lines: not while the focus only moves to another line of the same element, which is
        /// still being typed, and only if something changed.
        /// </summary>
        public static bool WritesOnFocusLoss(bool focusMovesToAnotherLine, IReadOnlyList<string> written, IReadOnlyList<string> current)
        {
            return !focusMovesToAnotherLine && Differs(written, current);
        }

        /// <summary>
        /// Whether lines drawn in an earlier state may still be written: the same map view and the same chrono variant as now (compared
        /// by identity), and the element still in its tile. Otherwise they would land in a state they were not typed for.
        /// </summary>
        public static bool StillApplies(object editSummary, object editVariant, object nowSummary, object nowVariant, bool elementInTile)
        {
            return elementInTile && editSummary != null && ReferenceEquals(editSummary, nowSummary) && ReferenceEquals(editVariant, nowVariant);
        }

        /// <summary>What happens to label lines typed and not written when the state is left.</summary>
        public enum FlushAction
        {
            /// <summary>Nothing to write, or the lines belong to another state: they are dropped silently.</summary>
            Drop,

            /// <summary>Written as one undo step.</summary>
            Write,

            /// <summary>Not editable now: kept and shown again until they can be written.</summary>
            Carry,

            /// <summary>Typed lines that are not written because the map is read again after a failed roll-back; the user is told.</summary>
            Discard
        }

        /// <summary>
        /// Decides what leaving a state does with one element's typed lines. <paramref name="discard"/> is set when the workspace is
        /// considered damaged (the map is read again after a failed roll-back): nothing is written then, not even into files that may
        /// disagree with memory.
        /// </summary>
        public static FlushAction Decide(bool stillApplies, bool differs, bool canEdit, bool discard)
        {
            if (!stillApplies || !differs)
                return FlushAction.Drop;
            if (discard)
                return FlushAction.Discard;
            return canEdit ? FlushAction.Write : FlushAction.Carry;
        }

        /// <summary>
        /// Whether a change of a bus stop's first label (its name) also renames the stop in Busstops.cfg: only when the name really
        /// changed (compared trimmed) and the timetable lists the stop under the old name, so a name set there on purpose is not overwritten.
        /// </summary>
        /// <param name="listedName">The name Busstops.cfg gives the stop, or null when it does not list it.</param>
        public static bool RenamesListedStop(string oldLabel, string newLabel, string listedName)
        {
            if (listedName == null || string.IsNullOrWhiteSpace(newLabel))
                return false;
            var before = (oldLabel ?? string.Empty).Trim();
            var after = newLabel.Trim();
            return !string.Equals(before, after, StringComparison.Ordinal) && string.Equals(listedName, before, StringComparison.Ordinal);
        }

        /// <summary>Whether "Add to stop list" can list a stop: the object is not listed yet and its first label (the name) is not empty.</summary>
        public static bool CanAddToStopList(string firstLabel, bool listed)
        {
            return !listed && !string.IsNullOrWhiteSpace(firstLabel);
        }

        /// <summary>Whether "Remove from stop list" is available: the stop is listed and no trip stops there. A null list of users means unknown and blocks.</summary>
        public static bool CanRemoveFromStopList(bool listed, IReadOnlyList<string> tripsUsingStop)
        {
            return listed && tripsUsingStop != null && tripsUsingStop.Count == 0;
        }

        /// <summary>The first trips of a list for a tooltip (at most <paramref name="limit"/>) and how many more there are.</summary>
        public static IReadOnlyList<string> TripsToShow(IReadOnlyList<string> trips, int limit, out int more)
        {
            if (trips == null || trips.Count == 0)
            {
                more = 0;
                return new List<string>();
            }
            var shown = limit < 1 ? 1 : limit;
            more = Math.Max(0, trips.Count - shown);
            var result = new List<string>();
            for (var index = 0; index < trips.Count && index < shown; index++)
                result.Add(trips[index]);
            return result;
        }

        /// <summary>Whether Enter writes the lines: only if something changed.</summary>
        public static bool WritesOnEnter(IReadOnlyList<string> written, IReadOnlyList<string> current)
        {
            return Differs(written, current);
        }
    }
}
