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
            var affected = chain.Segments.FindAll(segment => segment.End > plan.CutStart && segment.Start < plan.CutEnd);
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
                return new OmsiJunctionInsertionResult(new object[0], null, blockers);

            var size = summary.TileSizeMeters;
            chain.FrameAt(plan.CutStart, out var cutStart, out _, out _);
            chain.FrameAt(plan.CutEnd, out var cutEnd, out _, out _);
            var candidates = new List<int>();
            foreach (var segment in affected)
                candidates.Add(segment.Spline.Id);

            var steps = new List<object>();
            try
            {
                // Split where the cuts fall inside a spline; a cut at a joint needs none.
                foreach (var cut in new[] { plan.CutEnd, plan.CutStart })
                {
                    var segment = chain.At(cut);
                    var local = OmsiSplineChain.LocalDistance(segment, cut);
                    if (local <= OmsiRoadPlanner.MinimumLength || local >= segment.Spline.LengthMeters - OmsiRoadPlanner.MinimumLength)
                        continue;
                    var current = Find(summary, segment.Spline.Id, out var tile);
                    var split = SplitSpline(summary, tile, current, local);
                    if (!split.Succeeded)
                        return Fail(summary, steps, split.Blockers);
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
                    if (along <= plan.CutStart || along >= plan.CutEnd)
                        continue;
                    var deleted = DeleteSpline(summary, tile, current);
                    if (!deleted.Succeeded)
                        return Fail(summary, steps, deleted.Blockers);
                    steps.Add(deleted.Change);
                }

                var placed = PlaceObject(summary, plan.Tile, plan.AssetPath, plan.LocalX, plan.LocalY, plan.Height, plan.HeadingDegrees);
                steps.Add(placed);

                foreach (var pair in new[]
                         {
                             new KeyValuePair<OmsiMapPoint, OmsiJunctionPort>(cutStart, plan.PortAtStart),
                             new KeyValuePair<OmsiMapPoint, OmsiJunctionPort>(cutEnd, plan.PortAtEnd)
                         })
                {
                    var join = OmsiSplineEnd.FindFree(summary, pair.Key.X, pair.Key.Y, 0.05);
                    if (join == null)
                        throw new InvalidOperationException("The cut road end was not found after cutting.");
                    var port = pair.Value;
                    var position = join.Position(size);
                    var piece = OmsiRoadPlanner.Plan(new OmsiRoadRequest
                    {
                        StartX = position.X, StartY = position.Y, StartHeadingDegrees = join.OutwardHeadingDegrees, StartHeight = join.Height,
                        EndX = port.Position.X, EndY = port.Position.Y, EndHeight = port.Height, EndJoinHeadingDegrees = port.OutwardHeadingDegrees + 180.0,
                        HeightMode = OmsiRoadHeightMode.Manual
                    }, null);
                    steps.Add(CreateRoad(summary, piece, plan.ProfilePath, join, null, plan.AlignsTerrain));
                }
                return new OmsiJunctionInsertionResult(steps, placed.Record, new OmsiReference[0]);
            }
            catch
            {
                Revert(summary, steps);
                throw;
            }
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
