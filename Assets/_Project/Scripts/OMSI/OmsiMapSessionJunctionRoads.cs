using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A road end on a connection point of a junction made in this editor that a move or turn by values lifts, lowers or tilts, with the
    /// height the point gets (<see cref="OmsiMapSession.JunctionRoadEnds"/>).
    /// </summary>
    public sealed class OmsiJunctionRoadEnd
    {
        internal OmsiJunctionRoadEnd(OmsiSplineEnd end, OmsiObjectRecord junction, int portIndex, double height)
        {
            End = end;
            Junction = junction;
            PortIndex = portIndex;
            Height = height;
        }

        public OmsiSplineEnd End { get; }
        public OmsiObjectRecord Junction { get; }

        /// <summary>The connection point the end lies on, from 0.</summary>
        public int PortIndex { get; }

        /// <summary>The height of the connection point after the edit, in metres.</summary>
        public double Height { get; }
    }

    /// <summary>What <see cref="OmsiMapSession.UpdateObjectsFollowingRoads"/> did.</summary>
    public sealed class OmsiValueEditOutcome
    {
        /// <summary>For each edit, whether it changed the object; as <see cref="OmsiMapSession.UpdateObjects"/> returns it.</summary>
        public IReadOnlyList<bool> Changed { get; internal set; }

        /// <summary>The road heights changed before the objects, in the order made; undone last first, after the objects.</summary>
        public List<OmsiSplineChange> RoadChanges { get; } = new List<OmsiSplineChange>();

        /// <summary>The splines whose end followed a junction.</summary>
        public List<int> FollowedSplineIds { get; } = new List<int>();

        /// <summary>Road ends that stayed as they were, with why, such as one on a point of another object that does not move.</summary>
        public List<string> KeptEnds { get; } = new List<string>();

        /// <summary>Warnings of the height planning, such as steep gradients, each once.</summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>A road end already at the height it is given is left alone.</summary>
        private const double FollowHeightToleranceMeters = 0.0005;

        /// <summary>
        /// The road ends that follow junctions made here when <paramref name="edits"/> change their height, pitch or bank only: each end on
        /// one of their connection points as the terrain fitting finds it (<see cref="PortAt"/>), with the point's height after the edit. A
        /// junction that moves sideways or turns keeps today's behaviour, as its points leave the road ends: none of its ends is listed; nor
        /// are those of an edit that is <see cref="OmsiObjectValueEdit.Moved"/>. Ends near a lifted point that stay as they are, with why,
        /// go to <paramref name="keptEnds"/> when given: one joined to a point of another object that is not lifted or tilted, and one whose
        /// point a tilt takes further from it than the terrain fitting joins. Nothing is written.
        /// </summary>
        public List<OmsiJunctionRoadEnd> JunctionRoadEnds(OmsiMapSummary summary, IReadOnlyList<OmsiObjectValueEdit> edits, List<string> keptEnds = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (edits == null)
                throw new ArgumentNullException(nameof(edits));
            var result = new List<OmsiJunctionRoadEnd>();
            if (Workspace == null)
                return result;
            var size = summary.TileSizeMeters;
            var lifted = new List<LiftedJunction>();
            // Each object file is looked up once: the catalogue compares paths ignoring case, which is slow on Mono.
            var hasPoints = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var edit in edits)
            {
                if (edit == null || edit.Moved || !LiftsOrTiltsOnly(edit))
                    continue;
                var record = edit.Record;
                if (record.Id < FirstNewId())
                    continue;
                var path = record.AssetPath ?? string.Empty;
                if (!hasPoints.TryGetValue(path, out var known))
                {
                    ShapeCatalogs();
                    known = shapeJunctions.Helpers(path).Count > 0;
                    hasPoints.Add(path, known);
                }
                if (!known)
                    continue;
                lifted.Add(new LiftedJunction
                {
                    Record = record,
                    Before = shapeJunctions.Ports(edit.Tile, record, size),
                    After = shapeJunctions.Ports(edit.Tile, record, size, edit.X, edit.Y, edit.Z, edit.HeadingDegrees, edit.PitchDegrees, edit.BankDegrees)
                });
            }
            if (lifted.Count == 0)
                return result;

            var tolerance = new OmsiJunctionTolerances().PositionMeters;
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                    foreach (var atEnd in new[] { false, true })
                    {
                        var end = new OmsiSplineEnd(tile, spline, atEnd);
                        var position = end.Position(size);
                        if (!lifted.Exists(junction => junction.Before.Exists(port => Near(port.Position, position, tolerance))))
                            continue;
                        // The point the terrain fitting joins the end to, which is the first found when points of two objects meet there.
                        var joined = PortAt(summary, end);
                        if (joined == null)
                            continue;
                        var junction = lifted.Find(candidate => ReferenceEquals(candidate.Record, joined.Object));
                        if (junction == null)
                        {
                            keptEnds?.Add("#" + spline.Id + ": on a point of object #" + joined.Object.Id + ", which is not lifted or tilted");
                            continue;
                        }
                        if (joined.Index >= junction.After.Count)
                            continue;
                        var point = junction.After[joined.Index];
                        if (!Near(point.Position, position, tolerance))
                        {
                            keptEnds?.Add("#" + spline.Id + ": the tilt takes point " + (joined.Index + 1) + " of junction #" + junction.Record.Id + " off the road end");
                            continue;
                        }
                        result.Add(new OmsiJunctionRoadEnd(end, joined.Object, joined.Index, point.Height));
                    }
            return result;
        }

        /// <summary>A junction an edit lifts, lowers or tilts, with its connection points before and after.</summary>
        private sealed class LiftedJunction
        {
            public OmsiObjectRecord Record;
            public List<OmsiJunctionPort> Before;
            public List<OmsiJunctionPort> After;
        }

        private static bool Near(OmsiMapPoint first, OmsiMapPoint second, double tolerance)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;
            return dx * dx + dy * dy <= tolerance * tolerance;
        }

        /// <summary>
        /// Moves or turns objects by values like <see cref="UpdateObjects"/>, and gives the road ends on junctions made here that the edits
        /// lift, lower or tilt (<see cref="JunctionRoadEnds"/>) the height of their connection point, each together with the spline joined
        /// there, as <see cref="OmsiSplineEditing.PlanJointHeight"/> plans it; the other end of each spline keeps its height. The roads are
        /// written first, in as few <see cref="UpdateSplines"/> as keep each spline to one plan at a time, then the objects; so the terrain
        /// the roads' steps fit is never that of a lifted junction above roads left below. All or nothing: a failure takes back the roads
        /// already written and is thrown as it was; when that fails too, <see cref="OmsiRollBackFailedException"/> reports both and
        /// <see cref="RollBackFailed"/> is raised. An end whose plan has an error stays as it was and is named in
        /// <see cref="OmsiValueEditOutcome.KeptEnds"/>, as are the ends <see cref="JunctionRoadEnds"/> leaves. The terrain around the junctions is fitted by the caller, as after any move
        /// (<see cref="RefitTerrainAtJunctions"/>).
        /// </summary>
        public OmsiValueEditOutcome UpdateObjectsFollowingRoads(OmsiMapSummary summary, IReadOnlyList<OmsiObjectValueEdit> edits)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (edits == null)
                throw new ArgumentNullException(nameof(edits));
            var outcome = new OmsiValueEditOutcome();
            var ends = JunctionRoadEnds(summary, edits, outcome.KeptEnds);
            if (ends.Count == 0)
            {
                outcome.Changed = UpdateObjects(edits);
                return outcome;
            }

            // The points of the lifted junctions do not hold their road ends: they move with them.
            var moving = new HashSet<OmsiObjectRecord>();
            foreach (var end in ends)
                moving.Add(end.Junction);
            // The points the plans keep their roads joined to, per tile; the objects are written only after the roads, so they hold for
            // every round.
            var fixedPorts = new Dictionary<OmsiTileSummary, List<OmsiJunctionPort>>();
            try
            {
                var pending = new List<OmsiJunctionRoadEnd>(ends);
                while (pending.Count > 0)
                {
                    // One round plans each spline once; an end whose plan meets a spline planned in this round waits for the next, which
                    // reads the spline as this round left it, such as a spline with both ends on lifted junctions.
                    var planned = new HashSet<int>();
                    var geometries = new List<OmsiSplineGeometry>();
                    var waiting = new List<OmsiJunctionRoadEnd>();
                    // The splines as the round before left them, looked up once per round.
                    var splines = new Dictionary<int, KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();
                    foreach (var candidate in summary.Tiles)
                        foreach (var record in candidate.Splines)
                            splines[record.Id] = new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(candidate, record);
                    foreach (var item in pending)
                    {
                        var id = item.End.Spline.Id;
                        if (!splines.TryGetValue(id, out var found))
                        {
                            outcome.KeptEnds.Add("#" + id + ": the spline was not found");
                            continue;
                        }
                        var tile = found.Key;
                        var spline = found.Value;
                        var end = new OmsiSplineEnd(tile, spline, item.End.AtEnd);
                        if (Math.Abs(end.Height - item.Height) < FollowHeightToleranceMeters)
                            continue;
                        if (!fixedPorts.TryGetValue(tile, out var ports))
                        {
                            ports = new List<OmsiJunctionPort>();
                            foreach (var port in shapeJunctions.PortsAround(summary, tile))
                                if (!moving.Contains(port.Object))
                                    ports.Add(port);
                            fixedPorts.Add(tile, ports);
                        }
                        var plan = OmsiSplineEditing.PlanJointHeight(summary, end, item.Height, ports);
                        if (plan.HasErrors)
                        {
                            outcome.KeptEnds.Add("#" + id + ": " + plan.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);
                            continue;
                        }
                        if (plan.Splines.Exists(geometry => planned.Contains(geometry.Spline.Id)))
                        {
                            waiting.Add(item);
                            continue;
                        }
                        foreach (var geometry in plan.Splines)
                        {
                            planned.Add(geometry.Spline.Id);
                            geometries.Add(geometry);
                        }
                        foreach (var issue in plan.Issues)
                            if (!outcome.Warnings.Contains(issue.Message))
                                outcome.Warnings.Add(issue.Message);
                        if (!outcome.FollowedSplineIds.Contains(id))
                            outcome.FollowedSplineIds.Add(id);
                    }
                    if (geometries.Count > 0)
                    {
                        var change = UpdateSplines(summary, geometries);
                        if (change != null)
                            outcome.RoadChanges.Add(change);
                    }
                    pending = waiting;
                }
                outcome.Changed = UpdateObjects(edits);
            }
            catch (Exception failure) when (!(failure is OmsiRollBackFailedException))
            {
                // The roads written go back, the last first; their steps fit the terrain anew with the junctions as they still lie.
                var written = new List<OmsiSplineChange>(outcome.RoadChanges);
                RollBackAfter(failure, () => UndoAll(summary, written));
                throw;
            }
            return outcome;
        }

        /// <summary>True when an edit keeps the object's place and heading as written and changes its height, pitch or bank.</summary>
        private static bool LiftsOrTiltsOnly(OmsiObjectValueEdit edit)
        {
            var record = edit.Record;
            if (OmsiNumberFormat.Format(edit.X) != OmsiNumberFormat.Format(record.LocalX) ||
                OmsiNumberFormat.Format(edit.Y) != OmsiNumberFormat.Format(record.LocalY) ||
                Math.Abs(OmsiRoadPlanner.Normalize(edit.HeadingDegrees - record.HeadingDegrees)) > 1e-9)
                return false;
            return OmsiNumberFormat.Format(edit.Z) != OmsiNumberFormat.Format(record.Height) ||
                   OmsiNumberFormat.Format(edit.PitchDegrees) != OmsiNumberFormat.Format(record.PitchDegrees) ||
                   OmsiNumberFormat.Format(edit.BankDegrees) != OmsiNumberFormat.Format(record.BankDegrees);
        }
    }
}
