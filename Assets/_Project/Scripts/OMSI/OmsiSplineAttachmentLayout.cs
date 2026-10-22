using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One object of a spline attachment row, in world metres.</summary>
    public struct OmsiPlacedAttachment
    {
        public double WorldX;
        public double WorldY;

        /// <summary>Absolute height: the spline's height there plus the attachment's height.</summary>
        public double Height;

        /// <summary>The spline's direction there plus the attachment's own heading, clockwise from north.</summary>
        public double HeadingDegrees;
    }

    /// <summary>
    /// Places the objects of [splineAttachement] rows. Object j lies at the start distance plus j times the interval
    /// along the spline chain, while j times the interval does not exceed the range. The chain follows each spline's
    /// next neighbour by ID across the whole map, also where a neighbour is joined at its end and runs the other way, so
    /// a row is complete from its main record alone; [splineAttachement_repeater] records are not needed. Where a link
    /// is recorded on one side only, the chain continues to the spline that names this one and meets its end.
    /// </summary>
    public sealed class OmsiSplineAttachmentLayout
    {
        /// <summary>Neighbours must meet within this distance to continue the chain.</summary>
        private const double JoinTolerance = 1.0;

        private const int MaximumObjectsPerRow = 5000;
        private const int MaximumSplinesPerChain = 2000;

        private readonly double tileSize;
        private readonly Dictionary<int, KeyValuePair<OmsiTileSummary, OmsiSplineRecord>> splines =
            new Dictionary<int, KeyValuePair<OmsiTileSummary, OmsiSplineRecord>>();

        // Splines by the IDs they name as neighbours, for links recorded on one side only.
        private readonly Dictionary<int, List<int>> namedBy = new Dictionary<int, List<int>>();

        public OmsiSplineAttachmentLayout(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            tileSize = summary.TileSizeMeters;
            foreach (var tile in summary.Tiles)
            {
                foreach (var spline in tile.Splines)
                {
                    splines[spline.Id] = new KeyValuePair<OmsiTileSummary, OmsiSplineRecord>(tile, spline);
                    foreach (var neighbour in new[] { spline.PreviousId, spline.NextId })
                    {
                        if (neighbour == 0)
                            continue;
                        if (!namedBy.TryGetValue(neighbour, out var list))
                            namedBy[neighbour] = list = new List<int>();
                        list.Add(spline.Id);
                    }
                }
            }
        }

        /// <summary>The objects of a row; empty for repeaters, a missing spline or a broken record.</summary>
        public List<OmsiPlacedAttachment> Place(OmsiTileSummary tile, OmsiSplineAttachmentRecord record)
        {
            var result = new List<OmsiPlacedAttachment>();
            if (tile == null || record == null || record.IsRepeater || record.SplineIndex < 0 || record.SplineIndex >= tile.Splines.Count)
                return result;

            // The start distance counts from the beginning of the chain, which may lie before the record's spline.
            var chain = new List<Segment> { new Segment { Tile = tile, Spline = tile.Splines[record.SplineIndex], Reversed = false } };
            while (chain.Count < MaximumSplinesPerChain)
            {
                var before = Neighbour(chain[0], false, chain);
                if (before == null)
                    break;
                chain.Insert(0, before);
            }
            for (var index = 1; index < chain.Count; index++)
                chain[index].Begin = chain[index - 1].End;

            var single = !(record.Interval > 0.0);
            var last = single ? record.StartDistance : record.StartDistance + Math.Floor(Math.Max(0.0, record.Range) / record.Interval) * record.Interval;
            for (var number = 0; number < MaximumObjectsPerRow; number++)
            {
                var distance = record.StartDistance + (single ? 0.0 : number * record.Interval);
                if (distance > last + 1e-9 || !ExtendTo(chain, distance))
                    break;
                result.Add(PlaceAt(chain, distance, record));
                if (single)
                    break;
            }
            return result;
        }

        private sealed class Segment
        {
            public OmsiTileSummary Tile;
            public OmsiSplineRecord Spline;

            /// <summary>True when the chain runs from the spline's end to its start.</summary>
            public bool Reversed;

            public double Begin;
            public double End => Begin + Spline.LengthMeters;
        }

        /// <summary>Follows neighbours until the chain reaches the distance; false where the chain ends before it.</summary>
        private bool ExtendTo(List<Segment> chain, double distance)
        {
            while (chain[chain.Count - 1].End < distance - 1e-9)
            {
                if (chain.Count >= MaximumSplinesPerChain)
                    return false;
                var current = chain[chain.Count - 1];
                var following = Neighbour(current, true, chain);
                if (following == null)
                    return false;
                following.Begin = current.End;
                chain.Add(following);
            }
            return true;
        }

        /// <summary>
        /// The spline that continues the chain after <paramref name="current"/> (or leads into it when going back):
        /// the neighbour it names, or a spline naming it, whose end meets there. Splines already in the chain are skipped,
        /// so loops end.
        /// </summary>
        private Segment Neighbour(Segment current, bool forward, List<Segment> chain)
        {
            var atEnd = forward != current.Reversed;
            var meeting = WorldPoint(current.Tile, current.Spline, atEnd ? current.Spline.LengthMeters : 0.0);
            var candidates = new List<int>();
            var named = atEnd ? current.Spline.NextId : current.Spline.PreviousId;
            if (named != 0)
                candidates.Add(named);
            if (namedBy.TryGetValue(current.Spline.Id, out var naming))
                candidates.AddRange(naming);

            foreach (var id in candidates)
            {
                if (!splines.TryGetValue(id, out var next) || !(next.Value.LengthMeters > 0.0) || chain.Exists(item => ReferenceEquals(item.Spline, next.Value)))
                    continue;
                var startMeets = Distance(meeting, WorldPoint(next.Key, next.Value, 0.0)) <= JoinTolerance;
                var endMeets = Distance(meeting, WorldPoint(next.Key, next.Value, next.Value.LengthMeters)) <= JoinTolerance;
                // Going forward the chain enters the neighbour where they meet; going back it leaves it there.
                if (forward && (startMeets || endMeets))
                    return new Segment { Tile = next.Key, Spline = next.Value, Reversed = !startMeets };
                if (!forward && (endMeets || startMeets))
                    return new Segment { Tile = next.Key, Spline = next.Value, Reversed = !endMeets };
            }
            return null;
        }

        private OmsiPlacedAttachment PlaceAt(List<Segment> chain, double distance, OmsiSplineAttachmentRecord record)
        {
            var segment = chain[chain.Count - 1];
            foreach (var candidate in chain)
            {
                if (distance <= candidate.End + 1e-9)
                {
                    segment = candidate;
                    break;
                }
            }

            var spline = segment.Spline;
            var along = Math.Max(0.0, Math.Min(spline.LengthMeters, distance - segment.Begin));
            var onSpline = segment.Reversed ? spline.LengthMeters - along : along;
            OmsiMapGeometry.GetLocalFrameAt(spline, onSpline, out var position, out var heading);
            if (segment.Reversed)
                heading += Math.PI;
            var height = spline.Height + (spline.EndHeight - spline.Height) * onSpline / spline.LengthMeters;

            // To the right of the direction of travel; a mirrored spline turns its cross-section over.
            var offset = record.LateralOffset * (spline.IsMirrored ? -1.0 : 1.0);
            return new OmsiPlacedAttachment
            {
                WorldX = segment.Tile.X * tileSize + position.X + Math.Cos(heading) * offset,
                WorldY = segment.Tile.Y * tileSize + position.Y - Math.Sin(heading) * offset,
                Height = height + record.Height,
                HeadingDegrees = ((heading * 180.0 / Math.PI + record.HeadingDegrees) % 360.0 + 360.0) % 360.0
            };
        }

        private OmsiMapPoint WorldPoint(OmsiTileSummary tile, OmsiSplineRecord spline, double distance)
        {
            OmsiMapGeometry.GetLocalFrameAt(spline, distance, out var position, out _);
            return new OmsiMapPoint(tile.X * tileSize + position.X, tile.Y * tileSize + position.Y);
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
