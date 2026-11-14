using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>New cant and skew for a placed spline, in the tile that holds it.</summary>
    public sealed class OmsiSplineCross
    {
        public OmsiTileSummary Tile { get; internal set; }
        public OmsiSplineRecord Spline { get; internal set; }
        public double CantStartPercent { get; internal set; }
        public double CantEndPercent { get; internal set; }

        /// <summary>The new skew at the start and the end; null where the tile has none (before version 14) or it stays as it is.</summary>
        public double? SkewStart { get; internal set; }

        public double? SkewEnd { get; internal set; }
    }

    /// <summary>What changing the cant or skew of a spline would do: the new values of every spline touched and what to check.</summary>
    public sealed class OmsiCrossPlan
    {
        public List<OmsiSplineCross> Splines { get; } = new List<OmsiSplineCross>();
        public List<OmsiRoadIssue> Issues { get; } = new List<OmsiRoadIssue>();
        public bool HasErrors => Issues.Exists(issue => issue.Severity == OmsiRoadIssueSeverity.Error);
    }

    public static partial class OmsiSplineEditing
    {
        /// <summary>Cant steeper than this, in percent, is reported: real roads bank by a few percent.</summary>
        public const double CantWarningPercent = 12.0;

        /// <summary>Values closer than this are the same value.</summary>
        private const double CrossTolerance = 0.0005;

        /// <summary>
        /// Plans a new cant and skew for a spline. A changed cant at a joined end is given to the spline joined there as well, so the
        /// road stays level across the joint: the same value where the two run on in the same direction, the opposite where they meet
        /// end to end (their right sides lie on opposite sides of the road). Skew is only the spline's own: how it meets its neighbour
        /// depends on the angle between them, so it is not carried over. A skew given for a tile before version 14 is ignored.
        /// </summary>
        public static OmsiCrossPlan PlanCross(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineRecord spline, double cantStart, double cantEnd,
            double? skewStart, double? skewEnd)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            var plan = new OmsiCrossPlan();
            foreach (var value in new[] { cantStart, cantEnd, skewStart ?? 0.0, skewEnd ?? 0.0 })
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "Cant and skew must be numbers.", 0.0, 0.0));
                    return plan;
                }

            var own = new OmsiSplineCross
            {
                Tile = tile, Spline = spline, CantStartPercent = cantStart, CantEndPercent = cantEnd,
                SkewStart = spline.SkewStart.HasValue ? skewStart ?? spline.SkewStart : null,
                SkewEnd = spline.SkewEnd.HasValue ? skewEnd ?? spline.SkewEnd : null
            };
            plan.Splines.Add(own);
            var size = summary.TileSizeMeters;
            foreach (var atEnd in new[] { false, true })
            {
                var wanted = atEnd ? cantEnd : cantStart;
                var before = atEnd ? spline.CantEndPercent : spline.CantStartPercent;
                if (Math.Abs(wanted - before) < CrossTolerance)
                    continue;
                var end = new OmsiSplineEnd(tile, spline, atEnd);
                var neighbour = Neighbour(summary, end);
                if (neighbour == null)
                    continue;
                var sameDirection = neighbour.AtEnd != atEnd;
                var carried = sameDirection ? wanted : -wanted;
                var other = plan.Splines.Find(entry => ReferenceEquals(entry.Spline, neighbour.Spline));
                if (other == null)
                {
                    other = new OmsiSplineCross
                    {
                        Tile = neighbour.Tile, Spline = neighbour.Spline, CantStartPercent = neighbour.Spline.CantStartPercent,
                        CantEndPercent = neighbour.Spline.CantEndPercent, SkewStart = neighbour.Spline.SkewStart, SkewEnd = neighbour.Spline.SkewEnd
                    };
                    plan.Splines.Add(other);
                }
                if (neighbour.AtEnd)
                    other.CantEndPercent = carried;
                else
                    other.CantStartPercent = carried;
                var at = end.Position(size);
                if (Math.Abs(carried) > CantWarningPercent)
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Spline #" + neighbour.Spline.Id + " gets a cant of " + carried.ToString("0.##", CultureInfo.InvariantCulture) + " %, steeper than " +
                        CantWarningPercent.ToString("0.##", CultureInfo.InvariantCulture) + " %.", at.X, at.Y));
            }
            foreach (var value in new[] { cantStart, cantEnd })
                if (Math.Abs(value) > CantWarningPercent)
                {
                    plan.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Spline #" + spline.Id + " gets a cant of " + value.ToString("0.##", CultureInfo.InvariantCulture) + " %, steeper than " +
                        CantWarningPercent.ToString("0.##", CultureInfo.InvariantCulture) + " %.", 0.0, 0.0));
                    break;
                }
            return plan;
        }
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Writes new cant and skew into placed splines (see <see cref="OmsiSplineEditing.PlanCross"/>), keeping every other field. One
        /// change for undo; null when nothing differs. A skew for a tile before version 14, which has none, is skipped.
        /// </summary>
        public OmsiSplineChange UpdateSplineCross(OmsiMapSummary summary, IEnumerable<OmsiSplineCross> crosses)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (crosses == null)
                throw new ArgumentNullException(nameof(crosses));
            var files = new EditedFiles(RequireWorkspace());
            var change = new OmsiSplineChange(OmsiSplineChangeKind.Geometry);
            foreach (var cross in crosses)
            {
                OmsiSplineView view = null;
                foreach (var candidate in OmsiTileRecords.Of<OmsiSplineView>(files.Tile(cross.Tile.FileName)))
                    if (candidate.Id == cross.Spline.Id)
                        view = candidate;
                if (view == null)
                    throw new InvalidOperationException("Spline " + cross.Spline.Id + " was not found in " + cross.Tile.FileName + ".");
                var differs = false;
                if (Math.Abs(view.CantStart - cross.CantStartPercent) >= 0.0005)
                {
                    view.CantStart = cross.CantStartPercent;
                    differs = true;
                }
                if (Math.Abs(view.CantEnd - cross.CantEndPercent) >= 0.0005)
                {
                    view.CantEnd = cross.CantEndPercent;
                    differs = true;
                }
                if (cross.SkewStart.HasValue && view.SkewStart.HasValue && Math.Abs(view.SkewStart.Value - cross.SkewStart.Value) >= 0.0005)
                {
                    view.SkewStart = cross.SkewStart;
                    differs = true;
                }
                if (cross.SkewEnd.HasValue && view.SkewEnd.HasValue && Math.Abs(view.SkewEnd.Value - cross.SkewEnd.Value) >= 0.0005)
                {
                    view.SkewEnd = cross.SkewEnd;
                    differs = true;
                }
                if (!differs)
                    continue;
                change.SplineIds.Add(cross.Spline.Id);
                if (!change.Tiles.Contains(cross.Tile))
                    change.Tiles.Add(cross.Tile);
            }
            if (change.SplineIds.Count == 0)
                return null;
            files.Collect(change.Files);
            WriteFiles(change.Files, false);
            FinishSplineStep(summary, change, () =>
            {
                RefreshSplines(summary, change);
                // The ground follows a road's cant, so the roads built here fit it again.
                ReshapeTerrain(summary, change);
            });
            return change;
        }
    }
}
