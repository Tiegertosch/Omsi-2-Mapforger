using System;
using System.Collections.Generic;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Undoes or redoes the parts of a grouped step, all or none: when a part fails, the parts already done are taken back, and so is
    /// the failed part itself when it got as far as changing whether it is in effect.
    /// </summary>
    internal static class StepGroup
    {
        /// <summary>
        /// Applies <paramref name="parts"/> in the order given. When one fails with anything but an <see cref="OmsiRollBackFailedException"/>,
        /// the failed part is taken back first when its <paramref name="states"/> changed, then the parts done before it, the last first, and
        /// the failure is thrown as it was; so every part is in effect, or not, as before the call. When taking back fails too, an
        /// <see cref="OmsiRollBackFailedException"/> reports both; it is given to <paramref name="noteRollBackFailure"/> before it is thrown.
        /// </summary>
        /// <param name="states">Whether each piece of a part is in effect; an empty list for a part without such a state.</param>
        public static void Apply<T>(IReadOnlyList<T> parts, Action<T> apply, Action<T> takeBack, Func<T, List<bool>> states,
            Action<OmsiRollBackFailedException> noteRollBackFailure)
        {
            if (parts == null)
                throw new ArgumentNullException(nameof(parts));
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));
            if (takeBack == null)
                throw new ArgumentNullException(nameof(takeBack));
            if (states == null)
                throw new ArgumentNullException(nameof(states));
            var done = 0;
            List<bool> before = null;
            try
            {
                for (; done < parts.Count; done++)
                {
                    before = states(parts[done]);
                    apply(parts[done]);
                }
            }
            catch (Exception failure) when (!(failure is OmsiRollBackFailedException))
            {
                try
                {
                    // A part that wrote its files and then failed, such as on fitting the terrain or drawing, is taken back as well.
                    if (done < parts.Count && before != null && !Same(before, states(parts[done])))
                        takeBack(parts[done]);
                    for (var index = done - 1; index >= 0; index--)
                        takeBack(parts[index]);
                }
                catch (Exception rollBackFailure)
                {
                    var failed = new OmsiRollBackFailedException(failure.Message + " Taking back the parts of the step done before failed as well, so the workspace may hold part of the step: " +
                                                                 rollBackFailure.Message, new AggregateException(failure, rollBackFailure));
                    noteRollBackFailure?.Invoke(failed);
                    throw failed;
                }
                throw;
            }
        }

        private static bool Same(List<bool> first, List<bool> second)
        {
            if (first.Count != second.Count)
                return false;
            for (var index = 0; index < first.Count; index++)
                if (first[index] != second[index])
                    return false;
            return true;
        }
    }
}
