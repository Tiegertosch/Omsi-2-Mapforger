using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What a new row of objects along a road should look like, relative to the road's direction at the spline it starts from.</summary>
    public sealed class OmsiAttachmentRowRequest
    {
        public string AssetPath { get; set; }

        /// <summary>Metres to the right of the road; negative to the left.</summary>
        public double LateralOffset { get; set; }

        public double Height { get; set; }

        /// <summary>Turn of each object against the road's direction, clockwise.</summary>
        public double HeadingDegrees { get; set; }

        /// <summary>Distance between objects; 0 places a single object.</summary>
        public double Interval { get; set; } = 25.0;
    }

    /// <summary>One [splineAttachement] of a planned row: the objects that fall into one tile, anchored on a spline of it.</summary>
    public sealed class OmsiAttachmentRowPart
    {
        public OmsiTileSummary Tile { get; internal set; }
        public OmsiSplineRecord Spline { get; internal set; }

        /// <summary>The anchor spline in the tile's spline order.</summary>
        public int SplineIndex { get; internal set; }

        /// <summary>The record's values, counted the way a row anchored on that spline counts its chain.</summary>
        public double StartDistance { get; internal set; }

        public double LateralOffset { get; internal set; }
        public double HeadingDegrees { get; internal set; }
        public double Range { get; internal set; }
        public int Count { get; internal set; }
    }

    /// <summary>A planned row: its records, one per tile, and where its objects come to lie.</summary>
    public sealed class OmsiAttachmentRowPlan
    {
        public OmsiAttachmentRowRequest Request { get; internal set; }
        public List<OmsiAttachmentRowPart> Parts { get; } = new List<OmsiAttachmentRowPart>();
        public List<OmsiPlacedAttachment> Objects { get; } = new List<OmsiPlacedAttachment>();

        /// <summary>The road the row follows, in the direction of the spline it was planned from.</summary>
        public List<OmsiChainSegment> Chain { get; internal set; }

        /// <summary>Why the row cannot be laid, or null.</summary>
        public string Problem { get; internal set; }
    }

    /// <summary>
    /// Plans rows of objects along a road as OMSI stores them (TileRecordInventory): a [splineAttachement] names a spline
    /// of its tile, and its start distance counts from the beginning of that spline's whole chain. A row is laid as one
    /// record per tile its objects fall into, each anchored on a spline of that tile, so no [splineAttachement_repeater]
    /// is needed; their meaning could not be confirmed (Berlin-Spandau: the first object number matches in 29 of 210).
    /// </summary>
    public static class OmsiAttachmentRows
    {
        /// <summary>A row ends where its last object is; a little more range keeps rounding from losing it.</summary>
        private const double RangeSlackMeters = 0.001;

        private const double SamePlaceMeters = 0.01;
        private const int MostObjects = 2000;

        /// <summary>
        /// Plans a row along the chain through a spline, between two distances along it in that spline's direction, or,
        /// without them, along the whole chain from junction or free end to junction or free end, centred so the gaps at
        /// both ends are equal.
        /// </summary>
        public static OmsiAttachmentRowPlan Plan(OmsiMapSummary summary, OmsiSplineAttachmentLayout layout, OmsiTileSummary tile, OmsiSplineRecord spline,
            OmsiAttachmentRowRequest request, double? from = null, double? to = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var plan = new OmsiAttachmentRowPlan { Request = request, Chain = layout.Chain(tile, spline) };
            var chain = plan.Chain;
            var length = chain[chain.Count - 1].End;
            var interval = Math.Max(0.0, request.Interval);

            // The distances of the objects along the chain.
            var distances = new List<double>();
            if (from.HasValue && to.HasValue)
            {
                var first = Math.Max(0.0, Math.Min(length, Math.Min(from.Value, to.Value)));
                var last = Math.Max(0.0, Math.Min(length, Math.Max(from.Value, to.Value)));
                var count = interval > 0.0 ? (int)Math.Floor((last - first) / interval + 1e-9) + 1 : 1;
                for (var number = 0; number < Math.Min(count, MostObjects); number++)
                    distances.Add(first + number * interval);
            }
            else
            {
                var count = interval > 0.0 ? Math.Max(1, (int)Math.Floor(length / interval + 1e-9)) : 1;
                var span = (count - 1) * interval;
                for (var number = 0; number < Math.Min(count, MostObjects); number++)
                    distances.Add((length - span) / 2.0 + number * interval);
            }

            // Grouped by the tile each object falls into, in order along the road.
            var size = summary.TileSizeMeters;
            var groups = new List<KeyValuePair<OmsiTileSummary, List<double>>>();
            foreach (var distance in distances)
            {
                var placed = layout.PointAt(chain, distance, request.LateralOffset, request.Height, request.HeadingDegrees);
                plan.Objects.Add(placed);
                var tileX = (int)Math.Floor(placed.WorldX / size);
                var tileY = (int)Math.Floor(placed.WorldY / size);
                var holder = summary.Tiles.Find(candidate => candidate.X == tileX && candidate.Y == tileY && candidate.IsAvailable);
                if (holder == null)
                {
                    plan.Problem = "Objects of the row would lie outside the map's tiles, near " + tileX + ", " + tileY + ".";
                    return plan;
                }
                if (groups.Count == 0 || !ReferenceEquals(groups[groups.Count - 1].Key, holder))
                    groups.Add(new KeyValuePair<OmsiTileSummary, List<double>>(holder, new List<double>()));
                groups[groups.Count - 1].Value.Add(distance);
            }

            // Objects in a tile the road does not run through stay with the record before them, or else the one after.
            for (var index = 0; index < groups.Count; index++)
            {
                if (chain.Exists(segment => ReferenceEquals(segment.Tile, groups[index].Key)))
                    continue;
                var into = index > 0 ? index - 1 : index + 1;
                if (into >= groups.Count)
                {
                    plan.Problem = "The road does not run through tile " + groups[index].Key.X + ", " + groups[index].Key.Y + ", where the row lies.";
                    return plan;
                }
                if (into < index)
                    groups[into].Value.AddRange(groups[index].Value);
                else
                    groups[into].Value.InsertRange(0, groups[index].Value);
                groups.RemoveAt(index--);
            }

            foreach (var group in groups)
            {
                // The anchor: the tile's spline under the group's first object, or else the tile's first one along the road.
                var first = group.Value[0];
                var anchor = chain.Find(segment => ReferenceEquals(segment.Tile, group.Key) && first >= segment.Begin - 1e-9 && first <= segment.End + 1e-9) ??
                             chain.Find(segment => ReferenceEquals(segment.Tile, group.Key));
                // A row anchored on a spline the chain runs against counts from the chain's other end and sees the road
                // turned round: its start, offset and heading turn with it.
                var reversed = anchor.Reversed;
                var start = double.MaxValue;
                foreach (var distance in group.Value)
                    start = Math.Min(start, reversed ? length - distance : distance);
                plan.Parts.Add(new OmsiAttachmentRowPart
                {
                    Tile = group.Key, Spline = anchor.Spline, SplineIndex = group.Key.Splines.IndexOf(anchor.Spline), StartDistance = start,
                    LateralOffset = reversed ? -request.LateralOffset : request.LateralOffset,
                    HeadingDegrees = Normalize360(request.HeadingDegrees + (reversed ? 180.0 : 0.0)),
                    Range = interval > 0.0 ? (group.Value.Count - 1) * interval + RangeSlackMeters : 0.0, Count = group.Value.Count
                });
            }

            // Laid out again as OMSI will, every record must give exactly the planned objects.
            var expected = 0;
            foreach (var part in plan.Parts)
            {
                var objects = layout.Place(part.Tile, Record(part, request));
                foreach (var placed in objects)
                {
                    if (expected >= plan.Objects.Count || Distance(placed, plan.Objects[expected]) > SamePlaceMeters)
                    {
                        // Objects may come in the other order in a part counted against the road.
                        if (!plan.Objects.Exists(item => Distance(item, placed) <= SamePlaceMeters))
                        {
                            plan.Problem = "The row could not be laid as planned along this road.";
                            return plan;
                        }
                    }
                    expected++;
                }
            }
            if (expected != plan.Objects.Count)
                plan.Problem = "The row could not be laid as planned along this road.";
            return plan;
        }

        /// <summary>The distance along a chain nearest to a world position.</summary>
        public static double Project(OmsiSplineAttachmentLayout layout, List<OmsiChainSegment> chain, double worldX, double worldY)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (chain == null || chain.Count == 0)
                throw new ArgumentException("A chain is required.", nameof(chain));
            var length = chain[chain.Count - 1].End;
            var best = 0.0;
            var bestSquared = double.MaxValue;
            for (var distance = 0.0; distance <= length + 1e-9; distance += 0.25)
            {
                var point = layout.PointAt(chain, Math.Min(distance, length), 0.0, 0.0, 0.0);
                var squared = (point.WorldX - worldX) * (point.WorldX - worldX) + (point.WorldY - worldY) * (point.WorldY - worldY);
                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    best = Math.Min(distance, length);
                }
            }
            return best;
        }

        /// <summary>The record a part of a row is written as.</summary>
        public static OmsiSplineAttachmentRecord Record(OmsiAttachmentRowPart part, OmsiAttachmentRowRequest request)
        {
            if (part == null)
                throw new ArgumentNullException(nameof(part));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            return new OmsiSplineAttachmentRecord
            {
                AssetPath = request.AssetPath, SplineIndex = part.SplineIndex, LateralOffset = part.LateralOffset, Height = request.Height,
                StartDistance = part.StartDistance, HeadingDegrees = part.HeadingDegrees, Interval = Math.Max(0.0, request.Interval), Range = part.Range
            };
        }

        private static double Distance(OmsiPlacedAttachment first, OmsiPlacedAttachment second)
        {
            var dx = first.WorldX - second.WorldX;
            var dy = first.WorldY - second.WorldY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Normalize360(double degrees)
        {
            return (degrees % 360.0 + 360.0) % 360.0;
        }
    }
}
