using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>New geometry for a placed spline, in the local metres of the tile that holds it.</summary>
    public sealed class OmsiSplineGeometry
    {
        public OmsiTileSummary Tile { get; internal set; }
        public OmsiSplineRecord Spline { get; internal set; }
        public double LocalX { get; internal set; }
        public double LocalY { get; internal set; }
        public double Height { get; internal set; }
        public double HeadingDegrees { get; internal set; }
        public double LengthMeters { get; internal set; }
        public double RadiusMeters { get; internal set; }
        public double GradientStartPercent { get; internal set; }
        public double GradientEndPercent { get; internal set; }

        /// <summary>The spline with this geometry, for drawing a preview; the placed record is not changed.</summary>
        public OmsiSplineRecord Preview()
        {
            return new OmsiSplineRecord
            {
                AssetPath = Spline.AssetPath, Id = Spline.Id, PreviousId = Spline.PreviousId, NextId = Spline.NextId, LocalX = LocalX, LocalY = LocalY,
                Height = Height, HeadingDegrees = HeadingDegrees, LengthMeters = LengthMeters, RadiusMeters = RadiusMeters,
                GradientStartPercent = GradientStartPercent, GradientEndPercent = GradientEndPercent,
                EndHeight = Height + LengthMeters * (GradientStartPercent + GradientEndPercent) / 200.0, IsMirrored = Spline.IsMirrored,
                IsSplineH = Spline.IsSplineH, AlignsTerrain = Spline.AlignsTerrain
            };
        }
    }

    /// <summary>What moving a spline end would do: the new geometry of the splines that meet there and what to check.</summary>
    public sealed class OmsiJointMove
    {
        public List<OmsiSplineGeometry> Splines { get; } = new List<OmsiSplineGeometry>();
        public List<OmsiRoadIssue> Issues { get; } = new List<OmsiRoadIssue>();
        public bool HasErrors => Issues.Exists(issue => issue.Severity == OmsiRoadIssueSeverity.Error);
    }

    /// <summary>
    /// Plans moving the end of a placed spline to a new point, as OMSI stores splines: straight lines and circular arcs.
    /// The moved spline keeps its other end; where that end is joined to another spline it also keeps its heading there and
    /// bends in an arc, where it is free it runs straight. A spline joined at the moved end starts there tangentially and
    /// keeps its own other end; a kink where that end meets the next spline is reported. Where that would turn the joined
    /// spline by more than a right angle, as when a joint is pulled back, it keeps the heading at its other end instead and
    /// the kink at the moved joint is reported. Heights stay at both ends of each spline and run evenly in between.
    /// </summary>
    public static class OmsiSplineEditing
    {
        /// <summary>Headings closer than this meet without a kink.</summary>
        private const double KinkToleranceDegrees = 2.0;

        /// <summary>A joined spline that would turn by more than this to stay tangential keeps its far heading instead.</summary>
        private const double MaximumTangentialTurnDegrees = 90.0;

        public static OmsiJointMove PlanJointMove(OmsiMapSummary summary, OmsiSplineEnd end, double worldX, double worldY)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (end == null)
                throw new ArgumentNullException(nameof(end));
            var move = new OmsiJointMove();
            var size = summary.TileSizeMeters;
            var joint = end.Position(size);
            var nodeHeight = end.Height;

            // The moved spline, planned from its other end towards the new point.
            var far = new OmsiSplineEnd(end.Tile, end.Spline, !end.AtEnd);
            var farPoint = far.Position(size);
            // Leaving the far end into the spline: opposite to the way a road would leave the spline there.
            var farHeading = far.OutwardHeadingDegrees + 180.0;
            var farJoined = !far.IsFree;
            if (!farJoined)
                farHeading = OmsiRoadPlanner.ChordHeading(farPoint.X, farPoint.Y, worldX, worldY);
            // The record starts at the far end when the moved end is its end.
            if (!Plan(move, end.Tile, end.Spline, farPoint, farHeading, far.Height, worldX, worldY, nodeHeight, size, end.AtEnd, out var arrival))
                return move;

            // The spline joined at the moved end continues tangentially from the new point to its own other end.
            var neighbour = Neighbour(summary, end);
            if (neighbour == null)
                return move;
            var neighbourFar = new OmsiSplineEnd(neighbour.Tile, neighbour.Spline, !neighbour.AtEnd);
            var neighbourFarPoint = neighbourFar.Position(size);
            // The neighbour's record starts at the joint when it touches the joint with its start.
            OmsiRoadPlanner.TangentArc(worldX, worldY, arrival, neighbourFarPoint.X, neighbourFarPoint.Y, out _, out _, out var tangentialArrival);
            if (Math.Abs(OmsiRoadPlanner.Normalize(tangentialArrival - arrival)) > MaximumTangentialTurnDegrees ||
                Math.Abs(tangentialArrival - arrival) > 180.0)
            {
                // Pulled back: the neighbour keeps its far end and heading, and bends from there to the joint.
                var neighbourFarHeading = neighbourFar.IsFree
                    ? OmsiRoadPlanner.ChordHeading(neighbourFarPoint.X, neighbourFarPoint.Y, worldX, worldY)
                    : neighbourFar.OutwardHeadingDegrees + 180.0;
                if (!Plan(move, neighbour.Tile, neighbour.Spline, neighbourFarPoint, neighbourFarHeading, neighbourFar.Height, worldX, worldY,
                        nodeHeight, size, neighbour.AtEnd, out var backArrival))
                    return move;
                // The neighbour arrives at the joint heading backwards along the road; it should continue the moved spline.
                var jointKink = Math.Abs(OmsiRoadPlanner.Normalize(backArrival + 180.0 - arrival));
                if (jointKink > KinkToleranceDegrees)
                    move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Spline #" + end.Spline.Id + " and #" + neighbour.Spline.Id + " now meet with a kink of " + jointKink.ToString("0") + "°.",
                        worldX, worldY));
                return move;
            }
            if (!Plan(move, neighbour.Tile, neighbour.Spline, new OmsiMapPoint(worldX, worldY), arrival, nodeHeight, neighbourFarPoint.X,
                    neighbourFarPoint.Y, neighbourFar.Height, size, !neighbour.AtEnd, out var neighbourArrival))
                return move;
            if (!neighbourFar.IsFree)
            {
                // Continuing into the next spline means leaving the neighbour's far end the way that spline goes on.
                var onwards = neighbourFar.OutwardHeadingDegrees;
                var kink = Math.Abs(OmsiRoadPlanner.Normalize(onwards - neighbourArrival));
                if (kink > KinkToleranceDegrees)
                    move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Spline #" + neighbour.Spline.Id + " now meets the next spline with a kink of " + kink.ToString("0") + "°.",
                        neighbourFarPoint.X, neighbourFarPoint.Y));
            }
            return move;
        }

        /// <summary>The spline end joined to <paramref name="end"/> by neighbour ID, or null.</summary>
        public static OmsiSplineEnd Neighbour(OmsiMapSummary summary, OmsiSplineEnd end)
        {
            var id = end.AtEnd ? end.Spline.NextId : end.Spline.PreviousId;
            if (id == 0)
                return null;
            var point = end.Position(summary.TileSizeMeters);
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    if (spline.Id != id)
                        continue;
                    // The neighbour's end that names this spline, or else its end nearer to the joint.
                    var atStart = spline.PreviousId == end.Spline.Id;
                    var atEnd = spline.NextId == end.Spline.Id;
                    if (atStart != atEnd)
                        return new OmsiSplineEnd(tile, spline, atEnd);
                    var start = OmsiMapGeometry.GetSplineEndpoint(tile, spline, summary.TileSizeMeters, false);
                    var finish = OmsiMapGeometry.GetSplineEndpoint(tile, spline, summary.TileSizeMeters, true);
                    return new OmsiSplineEnd(tile, spline, Distance(finish, point) < Distance(start, point));
                }
            return null;
        }

        /// <summary>
        /// Plans one spline as an arc from a point with a heading to another point and stores it the way the record runs,
        /// from its start to its end.
        /// </summary>
        /// <param name="recordStartsAtFrom">True when the record's start is the first point, false when it is the second.</param>
        /// <param name="arrival">The heading at the second point, in the direction of travel from the first.</param>
        private static bool Plan(OmsiJointMove move, OmsiTileSummary tile, OmsiSplineRecord spline, OmsiMapPoint from, double heading, double fromHeight,
            double toX, double toY, double toHeight, double size, bool recordStartsAtFrom, out double arrival)
        {
            arrival = 0.0;
            var chord = Distance(from, new OmsiMapPoint(toX, toY));
            if (chord < OmsiRoadPlanner.MinimumLength)
            {
                move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "Spline #" + spline.Id + " would become too short.", toX, toY));
                return false;
            }
            OmsiRoadPlanner.TangentArc(from.X, from.Y, heading, toX, toY, out var length, out var radius, out arrival);
            if (Math.Abs(radius) > 0.0 && length > Math.PI * Math.Abs(radius))
                move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning, "Spline #" + spline.Id + " would turn by more than half a circle.", toX, toY));

            // Travel runs from the first point to the second; the record keeps its own direction, so it may run the other way.
            double startX, startY, startHeading, startHeight, endHeight, recordRadius;
            if (recordStartsAtFrom)
            {
                startX = from.X;
                startY = from.Y;
                startHeading = heading;
                startHeight = fromHeight;
                endHeight = toHeight;
                recordRadius = radius;
            }
            else
            {
                startX = toX;
                startY = toY;
                startHeading = arrival + 180.0;
                startHeight = toHeight;
                endHeight = fromHeight;
                recordRadius = -radius;
            }
            var gradient = (endHeight - startHeight) / length * 100.0;
            move.Splines.Add(new OmsiSplineGeometry
            {
                Tile = tile, Spline = spline, LocalX = startX - tile.X * size, LocalY = startY - tile.Y * size, Height = startHeight,
                HeadingDegrees = (startHeading % 360.0 + 360.0) % 360.0, LengthMeters = length, RadiusMeters = recordRadius,
                GradientStartPercent = gradient, GradientEndPercent = gradient
            });
            return true;
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }
}
