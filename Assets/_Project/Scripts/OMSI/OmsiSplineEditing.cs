using System;
using System.Collections.Generic;
using System.Globalization;

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

        /// <summary>
        /// True for the second of two arcs a spline becomes: a new spline split off it, carrying on to its old end; the
        /// spline itself keeps the first arc.
        /// </summary>
        public bool SplitOff { get; internal set; }

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
    /// Plans changes to placed splines, as OMSI stores them: straight lines and circular arcs. An arc is fixed by its chord
    /// and by how far it turns, so a spline can keep its shape while its ends move, as MoveIt does in Cities: Skylines.
    /// Heights stay at both ends of each spline and run evenly in between. Kinks where a changed spline meets another are
    /// reported.
    /// </summary>
    public static class OmsiSplineEditing
    {
        /// <summary>Headings closer than this meet without a kink.</summary>
        private const double KinkToleranceDegrees = 2.0;

        /// <summary>Spline ends closer than this meet, even when neither names the other as its neighbour.</summary>
        private const double TouchToleranceMeters = 0.05;

        /// <summary>A touching spline end continues the road when it turns by at most this much; more is a branch.</summary>
        private const double TouchTurnDegrees = 45.0;

        /// <summary>Gradients closer than this, in percentage points, meet without a noticeable bump.</summary>
        private const double GradientChangeTolerancePercent = 0.5;

        /// <summary>
        /// Plans moving the end of a placed spline to a new point. The moved spline and the spline joined at that end keep
        /// their other ends and how far they turn, so their shape follows the joint without bending; kinks this leaves are
        /// reported. With <paramref name="tangential"/> nothing kinks instead: the joint keeps the heading the road passes
        /// it with, and each spline runs to it from its other end, as one arc from a free end, and from a joined end, whose
        /// heading stays too, as two arcs where one cannot meet both; the second arc is then a new spline split off
        /// (<see cref="OmsiSplineGeometry.SplitOff"/>, written by <see cref="OmsiMapSession.ApplyJointMove"/>). A free
        /// joint keeps the heading at the spline's joined other end and bends to the point, or runs straight from a free one.
        /// </summary>
        public static OmsiJointMove PlanJointMove(OmsiMapSummary summary, OmsiSplineEnd end, double worldX, double worldY, bool tangential = false)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (end == null)
                throw new ArgumentNullException(nameof(end));
            if (tangential)
                return PlanTangentialJointMove(summary, end, worldX, worldY);
            var move = new OmsiJointMove();
            var size = summary.TileSizeMeters;
            var nodeHeight = end.Height;

            // The moved spline, from its other end to the new point, keeps how far it turns.
            var far = new OmsiSplineEnd(end.Tile, end.Spline, !end.AtEnd);
            var farPoint = far.Position(size);
            var leave = OmsiRoadPlanner.ChordHeading(farPoint.X, farPoint.Y, worldX, worldY) - Turn(end.Spline, end.AtEnd) / 2.0;
            if (!Plan(move, end.Tile, end.Spline, farPoint, leave, far.Height, worldX, worldY, nodeHeight, size, end.AtEnd, out var arrival))
                return move;
            ReportKink(move, summary, far, leave + 180.0, farPoint);

            // The spline joined at the moved end, from the new point to its own other end, keeps how far it turns as well.
            var neighbour = Neighbour(summary, end);
            if (neighbour == null)
                return move;
            var neighbourFar = new OmsiSplineEnd(neighbour.Tile, neighbour.Spline, !neighbour.AtEnd);
            var neighbourFarPoint = neighbourFar.Position(size);
            var neighbourLeave = OmsiRoadPlanner.ChordHeading(worldX, worldY, neighbourFarPoint.X, neighbourFarPoint.Y) -
                                 Turn(neighbour.Spline, !neighbour.AtEnd) / 2.0;
            if (!Plan(move, neighbour.Tile, neighbour.Spline, new OmsiMapPoint(worldX, worldY), neighbourLeave, nodeHeight, neighbourFarPoint.X,
                    neighbourFarPoint.Y, neighbourFar.Height, size, !neighbour.AtEnd, out var neighbourArrival))
                return move;
            var jointKink = Math.Abs(OmsiRoadPlanner.Normalize(neighbourLeave - arrival));
            if (jointKink > KinkToleranceDegrees)
                move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                    "Spline #" + end.Spline.Id + " and #" + neighbour.Spline.Id + " now meet with a kink of " + jointKink.ToString("0") + "°.",
                    worldX, worldY));
            ReportKink(move, summary, neighbourFar, neighbourArrival, neighbourFarPoint);
            return move;
        }

        /// <summary>
        /// Plans bending a placed spline through a point between its ends, which stay where they are; with
        /// <paramref name="straight"/> the spline runs straight instead. Kinks this leaves at joined ends are reported.
        /// </summary>
        public static OmsiJointMove PlanBend(OmsiMapSummary summary, OmsiTileSummary tile, OmsiSplineRecord spline, double worldX, double worldY,
            bool straight = false)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            var move = new OmsiJointMove();
            var size = summary.TileSizeMeters;
            var start = new OmsiSplineEnd(tile, spline, false);
            var finish = new OmsiSplineEnd(tile, spline, true);
            var from = start.Position(size);
            var to = finish.Position(size);
            var through = new OmsiMapPoint(worldX, worldY);
            // An arc turns by twice the change between the chords to and from any point on it.
            var turn = 0.0;
            if (!straight && Distance(from, through) > OmsiRoadPlanner.MinimumLength / 2.0 && Distance(through, to) > OmsiRoadPlanner.MinimumLength / 2.0)
                turn = 2.0 * OmsiRoadPlanner.Normalize(OmsiRoadPlanner.ChordHeading(worldX, worldY, to.X, to.Y) -
                                                       OmsiRoadPlanner.ChordHeading(from.X, from.Y, worldX, worldY));
            var leave = OmsiRoadPlanner.ChordHeading(from.X, from.Y, to.X, to.Y) - turn / 2.0;
            if (!Plan(move, tile, spline, from, leave, start.Height, to.X, to.Y, finish.Height, size, true, out var arrival))
                return move;
            ReportKink(move, summary, start, leave + 180.0, from);
            ReportKink(move, summary, finish, arrival, to);
            return move;
        }

        /// <summary>
        /// Plans a new height for a spline end and for the spline joined there; positions and shapes stay. Each changed
        /// spline keeps the height at its other end, and where that end is joined also its gradient there, so the road
        /// beyond stays smooth. OMSI changes a spline's gradient evenly along it, so with both other ends joined the two
        /// splines meet with different gradients, which is reported; where an other end is free, that spline follows the
        /// gradient of the joint instead. An other end on a connection point among <paramref name="ports"/> counts as joined
        /// and meets it level, as junction objects lie level; an end on such a point keeps its height, as the object would
        /// not follow. Gradients steeper than <paramref name="gradientWarningPercent"/> are reported.
        /// </summary>
        public static OmsiJointMove PlanJointHeight(OmsiMapSummary summary, OmsiSplineEnd end, double height, IEnumerable<OmsiJunctionPort> ports = null,
            double gradientWarningPercent = 10.0)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (end == null)
                throw new ArgumentNullException(nameof(end));
            var move = new OmsiJointMove();
            if (double.IsNaN(height) || double.IsInfinity(height))
            {
                move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "The height must be a number.", 0.0, 0.0));
                return move;
            }
            var size = summary.TileSizeMeters;
            var at = end.Position(size);
            var portList = ports == null ? new List<OmsiJunctionPort>() : new List<OmsiJunctionPort>(ports);
            var portAtJoint = PortAt(portList, at);
            if (portAtJoint != null)
            {
                move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error,
                    "This end of spline #" + end.Spline.Id + " sits on a connection point of object #" + portAtJoint.Object.Id +
                    ", which would not follow; move the object to change the height there.", at.X, at.Y));
                return move;
            }

            // Travel runs from the far end of the spline through the joint to the far end of its neighbour.
            var far = new OmsiSplineEnd(end.Tile, end.Spline, !end.AtEnd);
            var neighbour = Neighbour(summary, end);
            var neighbourFar = neighbour == null ? null : new OmsiSplineEnd(neighbour.Tile, neighbour.Spline, !neighbour.AtEnd);
            var farOnPort = PortAt(portList, far.Position(size)) != null;
            var neighbourFarOnPort = neighbourFar != null && PortAt(portList, neighbourFar.Position(size)) != null;
            var farJoined = farOnPort || Neighbour(summary, far) != null;
            var neighbourFarJoined = neighbourFar != null && (neighbourFarOnPort || Neighbour(summary, neighbourFar) != null);
            // The gradient a joined other end keeps: level on a connection point, else the one it has.
            var keptFarGradient = farOnPort ? 0.0 : -far.OutwardGradientPercent;
            var keptNeighbourFarGradient = neighbourFar == null || neighbourFarOnPort ? 0.0 : neighbourFar.OutwardGradientPercent;
            var length = end.Spline.LengthMeters;
            var rise = (height - far.Height) / length * 100.0;
            double farGradient, jointGradient, neighbourJointGradient = 0.0, neighbourFarGradient = 0.0;
            if (neighbour == null)
            {
                // A free joint: the spline keeps its gradient at a joined far end, or else rises evenly.
                farGradient = farJoined ? keptFarGradient : rise;
                jointGradient = 2.0 * rise - farGradient;
            }
            else
            {
                var neighbourLength = neighbour.Spline.LengthMeters;
                var neighbourRise = (neighbourFar.Height - height) / neighbourLength * 100.0;
                if (farJoined)
                {
                    farGradient = keptFarGradient;
                    jointGradient = 2.0 * rise - farGradient;
                    if (!neighbourFarJoined)
                    {
                        neighbourJointGradient = jointGradient;
                        neighbourFarGradient = 2.0 * neighbourRise - jointGradient;
                    }
                    else
                    {
                        neighbourFarGradient = keptNeighbourFarGradient;
                        neighbourJointGradient = 2.0 * neighbourRise - neighbourFarGradient;
                    }
                }
                else if (neighbourFarJoined)
                {
                    neighbourFarGradient = keptNeighbourFarGradient;
                    neighbourJointGradient = 2.0 * neighbourRise - neighbourFarGradient;
                    jointGradient = neighbourJointGradient;
                    farGradient = 2.0 * rise - jointGradient;
                }
                else
                {
                    // Both far ends free: the joint takes the gradient from one far end to the other.
                    jointGradient = (neighbourFar.Height - far.Height) / (length + neighbourLength) * 100.0;
                    farGradient = 2.0 * rise - jointGradient;
                    neighbourJointGradient = jointGradient;
                    neighbourFarGradient = 2.0 * neighbourRise - jointGradient;
                }
            }

            AddHeights(move, size, end, height, farGradient, jointGradient);
            if (neighbour != null)
            {
                // The neighbour's gradients run away from the joint; towards it they change sign.
                AddHeights(move, size, neighbour, height, -neighbourFarGradient, -neighbourJointGradient);
                var change = Math.Abs(neighbourJointGradient - jointGradient);
                if (change > GradientChangeTolerancePercent)
                    move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Spline #" + end.Spline.Id + " and #" + neighbour.Spline.Id + " now meet with gradients " + Percent(change) +
                        " apart, as both their other ends stay joined.", at.X, at.Y));
            }
            foreach (var geometry in move.Splines)
            {
                var steepest = Math.Max(Math.Abs(geometry.GradientStartPercent), Math.Abs(geometry.GradientEndPercent));
                if (steepest > gradientWarningPercent)
                    move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Spline #" + geometry.Spline.Id + " gets a gradient of " + Percent(steepest) + ", steeper than " +
                        Percent(gradientWarningPercent) + ".", at.X, at.Y));
            }
            return move;
        }

        /// <summary>
        /// The new heights of a spline with one end at a joint: gradients are given along travel from its far end to the
        /// joint and stored the way the record runs.
        /// </summary>
        private static void AddHeights(OmsiJointMove move, double size, OmsiSplineEnd joint, double height, double farGradient, double jointGradient)
        {
            var spline = joint.Spline;
            // The record's start is the far end when the joint is the record's end.
            var startsAtFar = joint.AtEnd;
            move.Splines.Add(new OmsiSplineGeometry
            {
                Tile = joint.Tile, Spline = spline, LocalX = spline.LocalX, LocalY = spline.LocalY,
                Height = startsAtFar ? new OmsiSplineEnd(joint.Tile, spline, false).Height : height,
                HeadingDegrees = spline.HeadingDegrees, LengthMeters = spline.LengthMeters, RadiusMeters = spline.RadiusMeters,
                GradientStartPercent = startsAtFar ? farGradient : -jointGradient,
                GradientEndPercent = startsAtFar ? jointGradient : -farGradient
            });
        }

        /// <summary>The middle of a placed spline in world metres, where its handle for bending sits.</summary>
        public static OmsiMapPoint CurvePoint(OmsiTileSummary tile, OmsiSplineRecord spline, double tileSizeMeters)
        {
            if (tile == null)
                throw new ArgumentNullException(nameof(tile));
            if (spline == null)
                throw new ArgumentNullException(nameof(spline));
            OmsiMapGeometry.GetLocalFrameAt(spline, spline.LengthMeters / 2.0, out var local, out _);
            return new OmsiMapPoint(tile.X * tileSizeMeters + local.X, tile.Y * tileSizeMeters + local.Y);
        }

        /// <summary>How far a spline turns in degrees, positive to the right, travelled along its record or against it.</summary>
        private static double Turn(OmsiSplineRecord spline, bool alongRecord)
        {
            var turn = Math.Abs(spline.RadiusMeters) > 0.001 ? spline.LengthMeters / spline.RadiusMeters * 180.0 / Math.PI : 0.0;
            return alongRecord ? turn : -turn;
        }

        /// <summary>
        /// Reports a kink where a changed spline, now leaving <paramref name="end"/> outwards with
        /// <paramref name="outwardHeading"/>, meets the spline joined there, which stays as it is.
        /// </summary>
        private static void ReportKink(OmsiJointMove move, OmsiMapSummary summary, OmsiSplineEnd end, double outwardHeading, OmsiMapPoint at)
        {
            var other = Neighbour(summary, end);
            if (other == null)
                return;
            // The joined spline leaves the joint outwards from itself, into the changed spline; without a kink that is the
            // opposite of the changed spline's outward heading.
            var kink = Math.Abs(OmsiRoadPlanner.Normalize(other.OutwardHeadingDegrees - outwardHeading - 180.0));
            if (kink > KinkToleranceDegrees)
                move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                    "Spline #" + end.Spline.Id + " now meets #" + other.Spline.Id + " with a kink of " + kink.ToString("0") + "°.", at.X, at.Y));
        }

        private static OmsiJointMove PlanTangentialJointMove(OmsiMapSummary summary, OmsiSplineEnd end, double worldX, double worldY)
        {
            var move = new OmsiJointMove();
            var size = summary.TileSizeMeters;
            var nodeHeight = end.Height;
            var joint = new OmsiMapPoint(worldX, worldY);
            var far = new OmsiSplineEnd(end.Tile, end.Spline, !end.AtEnd);
            var farPoint = far.Position(size);
            var neighbour = Neighbour(summary, end);
            if (neighbour == null)
            {
                // A free end: the spline keeps its heading at a joined other end and bends to the point, or runs straight.
                var farHeading = Neighbour(summary, far) != null
                    ? far.OutwardHeadingDegrees + 180.0
                    : OmsiRoadPlanner.ChordHeading(farPoint.X, farPoint.Y, worldX, worldY);
                Plan(move, end.Tile, end.Spline, farPoint, farHeading, far.Height, worldX, worldY, nodeHeight, size, end.AtEnd, out _);
                return move;
            }

            // The joint keeps the heading the road passes it with; each spline runs to it from its other end without a kink.
            var through = end.OutwardHeadingDegrees;
            if (!PlanSmooth(move, summary, far, joint, through, nodeHeight, size, true))
                return move;
            PlanSmooth(move, summary, new OmsiSplineEnd(neighbour.Tile, neighbour.Spline, !neighbour.AtEnd), joint, through, nodeHeight, size, false);
            return move;
        }

        /// <summary>
        /// Plans a spline between its other end <paramref name="far"/> and the joint, which the road passes with heading
        /// <paramref name="through"/>, arriving at the joint from the far end or leaving it towards it. A joined far end keeps
        /// its heading as well, so the spline becomes two arcs where one cannot meet both headings: the second one a new
        /// spline split off it (<see cref="OmsiSplineGeometry.SplitOff"/>). A free far end gets one arc from the joint.
        /// </summary>
        private static bool PlanSmooth(OmsiJointMove move, OmsiMapSummary summary, OmsiSplineEnd far, OmsiMapPoint joint, double through, double nodeHeight,
            double size, bool arriving)
        {
            var farPoint = far.Position(size);
            // The record starts at the joint when the far end is the record's end.
            var recordStartsAtJoint = far.AtEnd;
            if (Neighbour(summary, far) == null)
            {
                // From the joint along the road, backwards when the spline arrives at it.
                return Plan(move, far.Tile, far.Spline, joint, arriving ? through + 180.0 : through, nodeHeight, farPoint.X, farPoint.Y, far.Height, size,
                    recordStartsAtJoint, out _);
            }
            if (Distance(farPoint, joint) < OmsiRoadPlanner.MinimumLength)
            {
                move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Error, "Spline #" + far.Spline.Id + " would become too short.", joint.X, joint.Y));
                return false;
            }
            // Travel from the far end to the joint, or from the joint to the far end, with the headings at both.
            var pieces = arriving
                ? OmsiRoadPlanner.Biarc(farPoint.X, farPoint.Y, far.OutwardHeadingDegrees + 180.0, joint.X, joint.Y, through)
                : OmsiRoadPlanner.Biarc(joint.X, joint.Y, through, farPoint.X, farPoint.Y, far.OutwardHeadingDegrees);
            if (pieces.Count == 2 && SameCircle(pieces[0], pieces[1]))
                pieces = new List<OmsiSplinePiece> { Joined(pieces[0], pieces[1]) };
            if (pieces.Count == 2 && Math.Min(pieces[0].LengthMeters, pieces[1].LengthMeters) < MinimumArcMeters)
            {
                // Too short a second arc to be a spline of its own: one arc, with the small kink it leaves reported.
                var start = arriving ? farPoint : joint;
                var heading = arriving ? far.OutwardHeadingDegrees + 180.0 : through;
                var end = arriving ? joint : farPoint;
                if (!Plan(move, far.Tile, far.Spline, start, heading, arriving ? far.Height : nodeHeight, end.X, end.Y, arriving ? nodeHeight : far.Height, size,
                        arriving != recordStartsAtJoint, out var arrival))
                    return false;
                var wanted = arriving ? through : far.OutwardHeadingDegrees;
                var kink = Math.Abs(OmsiRoadPlanner.Normalize(arrival - wanted));
                if (kink > KinkToleranceDegrees)
                    move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning,
                        "Spline #" + far.Spline.Id + " now meets the next spline with a kink of " + kink.ToString("0") + "°.", end.X, end.Y));
                return true;
            }
            foreach (var piece in pieces)
                if (piece.RadiusMeters != 0.0 && piece.LengthMeters > Math.PI * Math.Abs(piece.RadiusMeters))
                    move.Issues.Add(new OmsiRoadIssue(OmsiRoadIssueSeverity.Warning, "Spline #" + far.Spline.Id + " would turn by more than half a circle.",
                        joint.X, joint.Y));
            AddPieces(move, far.Tile, far.Spline, pieces, arriving ? far.Height : nodeHeight, arriving ? nodeHeight : far.Height, size,
                arriving != recordStartsAtJoint);
            return true;
        }

        /// <summary>A second arc shorter than this is not split off; the spline stays one arc.</summary>
        private const double MinimumArcMeters = 1.0;

        /// <summary>True when two arcs lie on one circle, or both on one straight line, so one spline can be both.</summary>
        private static bool SameCircle(OmsiSplinePiece first, OmsiSplinePiece second)
        {
            if (first.RadiusMeters == 0.0 || second.RadiusMeters == 0.0)
                return first.RadiusMeters == 0.0 && second.RadiusMeters == 0.0 &&
                       Math.Abs(OmsiRoadPlanner.Normalize(first.HeadingDegrees - second.HeadingDegrees)) < 1e-6;
            return Math.Abs(first.RadiusMeters - second.RadiusMeters) < 1e-6 * Math.Max(1.0, Math.Abs(first.RadiusMeters));
        }

        private static OmsiSplinePiece Joined(OmsiSplinePiece first, OmsiSplinePiece second)
        {
            return new OmsiSplinePiece
            {
                StartX = first.StartX, StartY = first.StartY, HeadingDegrees = first.HeadingDegrees, RadiusMeters = first.RadiusMeters,
                LengthMeters = first.LengthMeters + second.LengthMeters
            };
        }

        /// <summary>
        /// Stores arcs planned in travel order as a spline, the way its record runs: the first part keeps the spline, a
        /// second one is split off. Heights run evenly from the first point to the last.
        /// </summary>
        private static void AddPieces(OmsiJointMove move, OmsiTileSummary tile, OmsiSplineRecord spline, List<OmsiSplinePiece> pieces, double fromHeight,
            double toHeight, double size, bool recordStartsAtFrom)
        {
            var length = 0.0;
            foreach (var piece in pieces)
                length += piece.LengthMeters;
            var gradient = (toHeight - fromHeight) / length * 100.0;
            // The parts as the record runs, each with its start, heading, radius and start height.
            var parts = new List<double[]>();
            var travelled = 0.0;
            foreach (var piece in pieces)
            {
                var height = fromHeight + travelled * gradient / 100.0;
                if (recordStartsAtFrom)
                {
                    parts.Add(new[] { piece.StartX, piece.StartY, piece.HeadingDegrees, piece.RadiusMeters, piece.LengthMeters, height });
                }
                else
                {
                    // Against travel: the part starts where the piece ends, turned round, bending the other way.
                    piece.PointAt(piece.LengthMeters, out var endX, out var endY, out _);
                    var endHeading = piece.RadiusMeters == 0.0
                        ? piece.HeadingDegrees
                        : piece.HeadingDegrees + piece.LengthMeters / piece.RadiusMeters * 180.0 / Math.PI;
                    parts.Insert(0, new[] { endX, endY, endHeading + 180.0, -piece.RadiusMeters, piece.LengthMeters,
                        height + piece.LengthMeters * gradient / 100.0 });
                }
                travelled += piece.LengthMeters;
            }
            var recordGradient = recordStartsAtFrom ? gradient : -gradient;
            for (var index = 0; index < parts.Count; index++)
            {
                var part = parts[index];
                move.Splines.Add(new OmsiSplineGeometry
                {
                    Tile = tile, Spline = spline, LocalX = part[0] - tile.X * size, LocalY = part[1] - tile.Y * size, Height = part[5],
                    HeadingDegrees = (part[2] % 360.0 + 360.0) % 360.0, LengthMeters = part[4], RadiusMeters = part[3],
                    GradientStartPercent = recordGradient, GradientEndPercent = recordGradient, SplitOff = index > 0
                });
            }
        }

        /// <summary>
        /// The spline end joined to <paramref name="end"/>: by neighbour ID, or else one that touches it and continues the
        /// road (<see cref="Touching"/>); null when there is none.
        /// </summary>
        public static OmsiSplineEnd Neighbour(OmsiMapSummary summary, OmsiSplineEnd end)
        {
            var id = end.AtEnd ? end.Spline.NextId : end.Spline.PreviousId;
            if (id == 0)
                return Touching(summary, end);
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

        /// <summary>The connection point at a world position, within <see cref="TouchToleranceMeters"/>, or null.</summary>
        private static OmsiJunctionPort PortAt(List<OmsiJunctionPort> ports, OmsiMapPoint point)
        {
            OmsiJunctionPort best = null;
            var bestDistance = TouchToleranceMeters;
            foreach (var port in ports)
            {
                var distance = Distance(port.Position, point);
                if (distance > bestDistance)
                    continue;
                best = port;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>
        /// The end of another spline lying at <paramref name="end"/> without either naming the other, as splines of the
        /// original maps often meet at tile borders: within <see cref="TouchToleranceMeters"/>, itself without a neighbour
        /// ID and leaving the joint the opposite way within <see cref="TouchTurnDegrees"/>, so it continues the road
        /// rather than branching off. The nearest such end, or null.
        /// </summary>
        private static OmsiSplineEnd Touching(OmsiMapSummary summary, OmsiSplineEnd end)
        {
            var size = summary.TileSizeMeters;
            var point = end.Position(size);
            var outward = end.OutwardHeadingDegrees;
            OmsiSplineEnd best = null;
            var bestDistance = TouchToleranceMeters;
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                {
                    if (ReferenceEquals(spline, end.Spline))
                        continue;
                    for (var side = 0; side < 2; side++)
                    {
                        var candidate = new OmsiSplineEnd(tile, spline, side == 1);
                        if (!candidate.IsFree)
                            continue;
                        var distance = Distance(candidate.Position(size), point);
                        if (distance > bestDistance ||
                            Math.Abs(OmsiRoadPlanner.Normalize(candidate.OutwardHeadingDegrees - outward - 180.0)) > TouchTurnDegrees)
                            continue;
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            return best;
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

        private static string Percent(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture) + " %";
        }

        private static double Distance(OmsiMapPoint a, OmsiMapPoint b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
    }
}
