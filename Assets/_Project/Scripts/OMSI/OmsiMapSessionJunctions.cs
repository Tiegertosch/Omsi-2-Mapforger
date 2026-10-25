using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What inserting a junction into a road did, step by step in order, for undo; or what stopped it.</summary>
    public sealed class OmsiJunctionInsertionResult
    {
        internal OmsiJunctionInsertionResult(IReadOnlyList<object> steps, OmsiObjectRecord junction, IReadOnlyList<OmsiReference> blockers)
        {
            Steps = steps;
            Junction = junction;
            Blockers = blockers;
        }

        /// <summary>Each an <see cref="OmsiSplineChange"/> or an <see cref="OmsiObjectChange"/>; undo them in reverse order.</summary>
        public IReadOnlyList<object> Steps { get; }

        public OmsiObjectRecord Junction { get; }
        public IReadOnlyList<OmsiReference> Blockers { get; }
        public bool Succeeded => Junction != null;
    }

    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Inserts a junction object into a road as planned: splits the road at both cuts, deletes the part between them,
        /// places the object and leads a short piece of the road's profile from each cut to its connection point, planned
        /// with two arcs so it meets the point's position, heading and height. Nothing changes while objects ride on the
        /// part to remove or timetables or chrono changes name it; those references are returned. When a step fails, the
        /// steps before it are taken back.
        /// </summary>
        public OmsiJunctionInsertionResult InsertJunction(OmsiMapSummary summary, OmsiJunctionInsertionPlan plan)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (plan.Problem != null)
                throw new InvalidOperationException(plan.Problem);

            var chain = plan.Chain;
            chain.FrameAt(plan.CutStart, out var cutStart, out _, out _);
            chain.FrameAt(plan.CutEnd, out var cutEnd, out _, out _);
            var steps = new List<object>();
            try
            {
                var blockers = CutOut(summary, chain, plan.CutStart, plan.CutEnd, steps);
                if (blockers != null)
                    return Fail(summary, steps, blockers);
                var placed = PlaceObject(summary, plan.Tile, plan.AssetPath, plan.LocalX, plan.LocalY, plan.Height, plan.HeadingDegrees);
                steps.Add(placed);
                steps.Add(LeadToPort(summary, cutStart, plan.PortAtStart, plan.ProfilePath, plan.AlignsTerrain));
                steps.Add(LeadToPort(summary, cutEnd, plan.PortAtEnd, plan.ProfilePath, plan.AlignsTerrain));
                return new OmsiJunctionInsertionResult(steps, placed.Record, new OmsiReference[0]);
            }
            catch
            {
                Revert(summary, steps);
                throw;
            }
        }

        /// <summary>
        /// Puts a junction made again with a turned arm (<see cref="OmsiJunctionRebuild.PlanTurn"/>) in place of the one
        /// it replaces, written beforehand to <paramref name="assetPath"/>, and leads the road on each moved connection point
        /// to it as planned: the road is cut beyond the new point, the part before the cut goes and a short piece closes the
        /// gap, as when a junction is inserted. Nothing changes while objects ride on a part to remove or timetables or chrono
        /// changes name it; those references are returned. When a step fails, the steps before it are taken back.
        /// </summary>
        public OmsiJunctionInsertionResult RebuildJunction(OmsiMapSummary summary, OmsiJunctionRebuild rebuild, OmsiJunctionArmTurn turn, string assetPath)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (rebuild == null)
                throw new ArgumentNullException(nameof(rebuild));
            if (turn == null)
                throw new ArgumentNullException(nameof(turn));
            if (turn.HasErrors)
                throw new InvalidOperationException(turn.Problem ?? "A road cannot be led to its point: " +
                                                    turn.Leads.Find(lead => lead.Piece.HasErrors).Piece.Issues.Find(issue => issue.Severity == OmsiRoadIssueSeverity.Error).Message);

            var steps = new List<object>();
            try
            {
                steps.Add(ReplaceObject(summary, rebuild.Tile, rebuild.Junction, assetPath));
                foreach (var lead in turn.Leads)
                {
                    // Each change reads the splines again, so the road is found anew by the spline that was on the point.
                    var spline = Find(summary, lead.SplineId, out var tile);
                    if (spline == null)
                        throw new InvalidOperationException("Spline " + lead.SplineId + " was not found to lead it to the junction.");
                    var from = lead.AtEnd ? spline.LengthMeters : 0.0;
                    var cut = lead.AtEnd ? from - lead.CutMeters : from + lead.CutMeters;
                    var chain = OmsiSplineChain.Around(summary, tile, spline, OmsiJunctionRebuild.LeadReachMeters);
                    var blockers = CutOut(summary, chain, Math.Min(from, cut), Math.Max(from, cut), steps);
                    if (blockers != null)
                        return Fail(summary, steps, blockers);
                    steps.Add(LeadToPort(summary, lead.Cut, lead.Port, lead.ProfilePath, lead.AlignsTerrain));
                }
                return new OmsiJunctionInsertionResult(steps, rebuild.Junction, new OmsiReference[0]);
            }
            catch
            {
                Revert(summary, steps);
                throw;
            }
        }

        /// <summary>
        /// Cuts the part of a road between two distances along a chain out: splits where a cut falls inside a spline and
        /// deletes every piece between, adding the changes to <paramref name="steps"/>. Nothing changes while objects ride
        /// on the part or timetables or chrono changes name it; those references are returned, otherwise null.
        /// </summary>
        private IReadOnlyList<OmsiReference> CutOut(OmsiMapSummary summary, OmsiSplineChain chain, double from, double to, List<object> steps)
        {
            var affected = chain.Segments.FindAll(segment => segment.End > from && segment.Start < to);
            var references = BuildReferenceIndex();
            var blockers = new List<OmsiReference>();
            foreach (var segment in affected)
            {
                foreach (var reference in references.To(segment.Spline.Id))
                    if (reference.Kind != SplineNeighbourReference)
                        blockers.Add(reference);
                if (segment.Spline.AlignsTerrain && !ShapesTerrain(segment.Spline))
                    throw new InvalidOperationException("Roads of the original map with terrain alignment cannot be cut yet: their terrain would change.");
            }
            if (blockers.Count > 0)
                return blockers;

            var size = summary.TileSizeMeters;
            var candidates = new List<int>();
            foreach (var segment in affected)
                candidates.Add(segment.Spline.Id);

            // Split where the cuts fall inside a spline; a cut at a joint needs none.
            foreach (var cut in new[] { to, from })
            {
                var segment = chain.At(cut);
                var local = OmsiSplineChain.LocalDistance(segment, cut);
                if (local <= OmsiRoadPlanner.MinimumLength || local >= segment.Spline.LengthMeters - OmsiRoadPlanner.MinimumLength)
                    continue;
                var current = Find(summary, segment.Spline.Id, out var tile);
                var split = SplitSpline(summary, tile, current, local);
                if (!split.Succeeded)
                    return split.Blockers;
                steps.Add(split.Change);
                candidates.Add(split.Change.SplineIds[1]);
            }

            // Every piece whose middle lies between the cuts goes.
            foreach (var id in candidates)
            {
                var current = Find(summary, id, out var tile);
                if (current == null)
                    continue;
                OmsiMapGeometry.GetLocalFrameAt(current, current.LengthMeters / 2.0, out var middle, out _);
                var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, middle.X, middle.Y, size);
                var along = chain.Project(world.X, world.Y);
                if (along <= from || along >= to)
                    continue;
                var deleted = DeleteSpline(summary, tile, current);
                if (!deleted.Succeeded)
                    return deleted.Blockers;
                steps.Add(deleted.Change);
            }
            return null;
        }

        /// <summary>
        /// Builds a short piece of a road's profile from the free road end at a cut to a connection point, planned with two
        /// arcs so it meets the point's position, heading and height (<see cref="OmsiJunctionInsertion.PieceToPort"/>).
        /// </summary>
        private OmsiSplineChange LeadToPort(OmsiMapSummary summary, OmsiMapPoint cut, OmsiJunctionPort port, string profilePath, bool alignsTerrain)
        {
            var join = OmsiSplineEnd.FindFree(summary, cut.X, cut.Y, 0.05);
            if (join == null)
                throw new InvalidOperationException("The cut road end was not found after cutting.");
            var piece = OmsiJunctionInsertion.PieceToPort(join.Position(summary.TileSizeMeters), join.OutwardHeadingDegrees, join.Height, join.AtEnd, port);
            return join.AtEnd
                ? CreateRoad(summary, piece, profilePath, join, null, alignsTerrain)
                : CreateRoad(summary, piece, profilePath, null, join, alignsTerrain);
        }

        private OmsiJunctionInsertionResult Fail(OmsiMapSummary summary, List<object> steps, IReadOnlyList<OmsiReference> blockers)
        {
            Revert(summary, steps);
            return new OmsiJunctionInsertionResult(new object[0], null, blockers);
        }

        /// <summary>Takes back the steps done so far, the last first.</summary>
        private void Revert(OmsiMapSummary summary, List<object> steps)
        {
            for (var index = steps.Count - 1; index >= 0; index--)
            {
                if (steps[index] is OmsiSplineChange spline)
                    Undo(summary, spline);
                else if (steps[index] is OmsiObjectChange item)
                    Undo(summary, item);
            }
            steps.Clear();
        }

        private static OmsiSplineRecord Find(OmsiMapSummary summary, int id, out OmsiTileSummary tile)
        {
            foreach (var candidate in summary.Tiles)
            {
                var spline = candidate.Splines.Find(record => record.Id == id);
                if (spline != null)
                {
                    tile = candidate;
                    return spline;
                }
            }
            tile = null;
            return null;
        }
    }
}
