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
    /// A row of the map taken as one: the records of a row laid in parts per tile (<see cref="OmsiAttachmentRows.Plan"/>),
    /// or a single record. Its values are seen along the chain in the direction of its first record's spline.
    /// </summary>
    public sealed class OmsiAttachmentRow
    {
        /// <summary>The records in order along the chain, with their tiles.</summary>
        public List<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>> Records { get; } = new List<KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>>();

        /// <summary>The tile and spline the chain is seen from.</summary>
        public OmsiTileSummary Tile { get; internal set; }
        public OmsiSplineRecord Spline { get; internal set; }

        /// <summary>What the row looks like along that chain: its offset to the right, heading against the chain's direction.</summary>
        public OmsiAttachmentRowRequest Request { get; internal set; }

        /// <summary>Distances along the chain of the first and the last object.</summary>
        public double From { get; internal set; }
        public double To { get; internal set; }

        public int ObjectCount { get; internal set; }

        /// <summary>
        /// True when [splineAttachement_repeater] records continue the row on other tiles. Such a row keeps its records;
        /// a change sets its values in the record and every continuation, without planning it again.
        /// </summary>
        public bool Continues { get; internal set; }
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

            // One record for the objects on consecutive splines of one tile that the road runs along in one direction: a
            // record lays its objects only along its own tile's splines, and in its spline's direction.
            var groups = new List<List<int>>();
            var segments = new List<int>();
            for (var index = 0; index < distances.Count; index++)
            {
                plan.Objects.Add(layout.PointAt(chain, distances[index], request.LateralOffset, request.Height, request.HeadingDegrees));
                segments.Add(SegmentAt(chain, distances[index]));
                if (groups.Count == 0 || !Continuous(chain, segments[index - 1], segments[index]))
                    groups.Add(new List<int>());
                groups[groups.Count - 1].Add(index);
            }

            foreach (var group in groups)
            {
                // Where the road runs against the splines, the record starts at the group's last object and lays the
                // others back along the road, seeing it turned round: offset and heading turn with it.
                var reversed = chain[segments[group[0]]].Reversed;
                var anchorIndex = reversed ? group[group.Count - 1] : group[0];
                var anchor = chain[segments[anchorIndex]];
                var onSpline = reversed ? anchor.End - distances[anchorIndex] : distances[anchorIndex] - anchor.Begin;
                plan.Parts.Add(new OmsiAttachmentRowPart
                {
                    Tile = anchor.Tile, Spline = anchor.Spline, SplineIndex = anchor.Tile.Splines.IndexOf(anchor.Spline),
                    StartDistance = layout.ChainOffsetOf(anchor.Tile, anchor.Spline) + Math.Max(0.0, onSpline),
                    LateralOffset = reversed ? -request.LateralOffset : request.LateralOffset,
                    HeadingDegrees = Normalize360(request.HeadingDegrees + (reversed ? 180.0 : 0.0)),
                    Range = interval > 0.0 ? (group.Count - 1) * interval + RangeSlackMeters : 0.0, Count = group.Count
                });
            }

            // Laid out again as openOMSI will, every record must give exactly the objects of its part.
            for (var part = 0; part < plan.Parts.Count; part++)
            {
                var objects = layout.Place(plan.Parts[part].Tile, Record(plan.Parts[part], request));
                var planned = groups[part].ConvertAll(index => plan.Objects[index]);
                if (objects.Count != planned.Count || !objects.TrueForAll(placed => planned.Exists(item => Distance(item, placed) <= SamePlaceMeters)))
                {
                    plan.Problem = "The row could not be laid as planned along this road.";
                    return plan;
                }
            }
            return plan;
        }

        /// <summary>The segment of a chain a distance lies on; one right on a joint belongs to the spline before.</summary>
        private static int SegmentAt(List<OmsiChainSegment> chain, double distance)
        {
            for (var index = 0; index < chain.Count; index++)
                if (distance <= chain[index].End + 1e-9)
                    return index;
            return chain.Count - 1;
        }

        /// <summary>Whether the chain from one segment to another stays in one tile and runs one way through it.</summary>
        private static bool Continuous(List<OmsiChainSegment> chain, int from, int to)
        {
            for (var index = from + 1; index <= to; index++)
                if (!ReferenceEquals(chain[index].Tile, chain[from].Tile) || chain[index].Reversed != chain[from].Reversed)
                    return false;
            return true;
        }

        /// <summary>
        /// The row an existing record belongs to: the record, and the records of the same object, distance between objects,
        /// height, offset and heading along the same chain that carry it on at that distance, as a row laid in parts per tile.
        /// </summary>
        public static OmsiAttachmentRow RowOf(OmsiMapSummary summary, OmsiSplineAttachmentLayout layout, OmsiTileSummary tile, OmsiSplineAttachmentRecord record)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            if (record.IsRepeater)
                throw new InvalidOperationException("A continuation of a row on another tile is changed with its row.");
            if (record.SplineIndex < 0 || record.SplineIndex >= tile.Splines.Count)
                throw new InvalidOperationException("Row " + record.Id + " names no spline of its tile.");

            var spline = tile.Splines[record.SplineIndex];
            var chain = layout.Chain(tile, spline);
            var own = Along(layout, chain, tile, record);
            if (own == null)
                throw new InvalidOperationException("Row " + record.Id + " has no objects along its road.");
            var continued = new HashSet<int>();
            foreach (var candidate in summary.Tiles)
                foreach (var item in candidate.SplineAttachments)
                    if (item.IsRepeater)
                        continued.Add(item.Id);
            var row = new OmsiAttachmentRow
            {
                Tile = tile, Spline = spline, Continues = continued.Contains(record.Id),
                Request = new OmsiAttachmentRowRequest
                {
                    AssetPath = record.AssetPath, LateralOffset = own.Offset, Height = record.Height, HeadingDegrees = own.Heading,
                    Interval = Math.Max(0.0, record.Interval)
                }
            };

            var members = new List<RowPiece> { own };
            if (!row.Continues && record.Interval > 0.0)
            {
                var candidates = new List<RowPiece>();
                var seen = new HashSet<OmsiTileSummary>();
                foreach (var segment in chain)
                {
                    if (!seen.Add(segment.Tile))
                        continue;
                    foreach (var other in segment.Tile.SplineAttachments)
                    {
                        if (ReferenceEquals(other, record) || other.IsRepeater || continued.Contains(other.Id) ||
                            !string.Equals(other.AssetPath, record.AssetPath, StringComparison.OrdinalIgnoreCase) ||
                            Math.Abs(other.Interval - record.Interval) > 1e-6 || Math.Abs(other.Height - record.Height) > 1e-6)
                            continue;
                        var piece = Along(layout, chain, segment.Tile, other);
                        if (piece != null && Math.Abs(piece.Offset - own.Offset) <= SamePlaceMeters && Math.Abs(Turn(piece.Heading, own.Heading)) < 0.01)
                            candidates.Add(piece);
                    }
                }
                // Carried on at one interval, before and after.
                var grown = true;
                while (grown)
                {
                    grown = false;
                    foreach (var piece in candidates)
                    {
                        if (members.Contains(piece))
                            continue;
                        if (Math.Abs(piece.First - members[members.Count - 1].Last - record.Interval) <= SamePlaceMeters)
                        {
                            members.Add(piece);
                            grown = true;
                        }
                        else if (Math.Abs(members[0].First - piece.Last - record.Interval) <= SamePlaceMeters)
                        {
                            members.Insert(0, piece);
                            grown = true;
                        }
                    }
                }
            }
            foreach (var piece in members)
            {
                row.Records.Add(new KeyValuePair<OmsiTileSummary, OmsiSplineAttachmentRecord>(piece.Tile, piece.Record));
                row.ObjectCount += piece.Count;
            }
            row.From = members[0].First;
            row.To = members[members.Count - 1].Last;
            return row;
        }

        /// <summary>
        /// Plans an existing row again with other values over the same stretch of its road: from its first object on, as
        /// many objects as fit up to its last. A continued row keeps its records; the plan then shows its first record
        /// with the new values and has no parts.
        /// </summary>
        public static OmsiAttachmentRowPlan Replan(OmsiMapSummary summary, OmsiSplineAttachmentLayout layout, OmsiAttachmentRow row, OmsiAttachmentRowRequest request)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (!row.Continues)
                return Plan(summary, layout, row.Tile, row.Spline, request, row.From, row.To);
            var first = row.Records[0];
            var plan = new OmsiAttachmentRowPlan { Request = request, Chain = layout.Chain(row.Tile, row.Spline) };
            plan.Objects.AddRange(layout.Place(first.Key, Changed(first.Value, request)));
            return plan;
        }

        /// <summary>
        /// A record of a continued row with new values; start and range stay. Continuations repeat the values of their
        /// master record (TileRecordInventory), so they get the same ones.
        /// </summary>
        internal static OmsiSplineAttachmentRecord Changed(OmsiSplineAttachmentRecord record, OmsiAttachmentRowRequest now)
        {
            return new OmsiSplineAttachmentRecord
            {
                AssetPath = now.AssetPath, Id = record.Id, SplineIndex = record.SplineIndex, LateralOffset = now.LateralOffset, Height = now.Height,
                StartDistance = record.StartDistance, HeadingDegrees = Normalize360(now.HeadingDegrees), Interval = Math.Max(0.0, now.Interval),
                Range = record.Range, IsRepeater = record.IsRepeater
            };
        }

        /// <summary>Values shown to a thousandth count as unchanged within half of it.</summary>
        private const double SameValue = 0.0006;

        /// <summary>Whether two sets of row values are the same, to the thousandth they are shown with.</summary>
        public static bool Same(OmsiAttachmentRowRequest first, OmsiAttachmentRowRequest second)
        {
            if (first == null || second == null)
                return first == second;
            return string.Equals(first.AssetPath, second.AssetPath, StringComparison.OrdinalIgnoreCase) && Math.Abs(first.LateralOffset - second.LateralOffset) < SameValue &&
                   Math.Abs(first.Height - second.Height) < SameValue && Math.Abs(first.Interval - second.Interval) < SameValue &&
                   Math.Abs(Turn(first.HeadingDegrees, second.HeadingDegrees)) < SameValue;
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

        /// <summary>A record of the map seen along a chain: where its first and last object lie, its offset and heading.</summary>
        private sealed class RowPiece
        {
            public OmsiTileSummary Tile;
            public OmsiSplineAttachmentRecord Record;
            public double First;
            public double Last;
            public int Count;
            public double Offset;
            public double Heading;
        }

        /// <summary>Where a record's objects lie along a chain, or null when its spline is not part of it or it has none.</summary>
        private static RowPiece Along(OmsiSplineAttachmentLayout layout, List<OmsiChainSegment> chain, OmsiTileSummary tile, OmsiSplineAttachmentRecord record)
        {
            if (record.SplineIndex < 0 || record.SplineIndex >= tile.Splines.Count)
                return null;
            var spline = tile.Splines[record.SplineIndex];
            var segment = chain.Find(item => ReferenceEquals(item.Tile, tile) && ReferenceEquals(item.Spline, spline));
            if (segment == null)
                return null;
            var objects = layout.Place(tile, record);
            if (objects.Count == 0)
                return null;
            // Each object along the chain, from the spline it stands on.
            var first = double.MaxValue;
            var last = double.MinValue;
            foreach (var placed in objects)
            {
                var on = chain.Find(item => ReferenceEquals(item.Spline, placed.Spline));
                if (on == null)
                    return null;
                var distance = on.Begin + (on.Reversed ? on.Spline.LengthMeters - placed.AlongSpline : placed.AlongSpline);
                first = Math.Min(first, distance);
                last = Math.Max(last, distance);
            }
            // A record on a spline the chain runs against sees the road turned round.
            var reversed = segment.Reversed;
            return new RowPiece
            {
                Tile = tile, Record = record, Count = objects.Count, First = first, Last = last,
                Offset = reversed ? -record.LateralOffset : record.LateralOffset, Heading = Normalize360(record.HeadingDegrees + (reversed ? 180.0 : 0.0))
            };
        }

        /// <summary>The turn from one heading to another, between -180° and 180°.</summary>
        private static double Turn(double from, double to)
        {
            var turn = Normalize360(to - from);
            return turn > 180.0 ? turn - 360.0 : turn;
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
