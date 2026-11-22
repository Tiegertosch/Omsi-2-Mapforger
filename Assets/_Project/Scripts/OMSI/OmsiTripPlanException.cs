using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A trip or track cannot be written because its plan has problems. Carries the trip and the coded problems so the editor can show them in its own language;
    /// <see cref="Exception.Message"/> stays English for logs. Derives from <see cref="InvalidOperationException"/> so existing handlers keep catching it.
    /// </summary>
    public sealed class OmsiTripPlanException : InvalidOperationException
    {
        /// <summary>The name of the trip, or empty when the caller has no name for it.</summary>
        public string TripName { get; }

        public IReadOnlyList<OmsiPlanProblem> Problems { get; }

        public OmsiTripPlanException(string tripName, IEnumerable<OmsiPlanProblem> problems)
            : this(tripName, (problems ?? Enumerable.Empty<OmsiPlanProblem>()).ToList())
        {
        }

        private OmsiTripPlanException(string tripName, List<OmsiPlanProblem> problems)
            : base((string.IsNullOrEmpty(tripName) ? "The trip" : "The trip " + tripName) + " has problems: " + string.Join(" ", problems))
        {
            TripName = tripName ?? string.Empty;
            Problems = problems;
        }
    }
}
