using System;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>The way of a signal route over the rails (ADR 105, M4).</summary>
    public sealed class OmsiSignalRouteFinderTests
    {
        private static OmsiMapPath Rail(int id, int path, double x0, double y0, double x1, double y1, bool onSpline = true)
        {
            return new OmsiMapPath
            {
                TileX = 0, TileY = 0, ElementId = id, PathIndex = path, OnSpline = onSpline, Kind = 2, Direction = 2,
                LengthMeters = Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)),
                Points = new[] { new OmsiMapPoint(x0, y0), new OmsiMapPoint((x0 + x1) / 2.0, (y0 + y1) / 2.0), new OmsiMapPoint(x1, y1) }
            };
        }

        private static string Text(OmsiSignalRouteWay way)
        {
            return string.Join(" ", way.Entries.Select(entry => entry.ElementId + "/" + entry.Path));
        }

        private static int TileIndex(int x, int y)
        {
            return 0;
        }

        // Three rails eastwards (heading 90°), each 100 m.
        private static OmsiPathNetwork Line()
        {
            return OmsiPathNetwork.Build(new[] { Rail(1, 0, 0, 0, 100, 0), Rail(2, 0, 100, 0, 200, 0), Rail(3, 0, 200, 0, 300, 0) }, 2);
        }

        // A rail into a switch (object 10: path 0 straight on, path 1 to the left), each branch going on over a rail of its own.
        private static OmsiPathNetwork Switch()
        {
            return OmsiPathNetwork.Build(new[]
            {
                Rail(1, 0, 0, 0, 100, 0), Rail(10, 0, 100, 0, 150, 0, false), Rail(10, 1, 100, 0, 150, 10, false), Rail(4, 0, 150, 0, 250, 0), Rail(5, 0, 150, 10, 250, 40)
            }, 2);
        }

        [Test]
        public void AStraightLineRunsFromTheSignalToTheTarget()
        {
            var rails = Line();
            // South of an eastward rail is its right: a signal at the end of rail 1 starts the route on rail 2.
            var atEnd = new OmsiSignalPose(7, new OmsiMapPoint(95, -3), 90.0);
            var search = OmsiSignalRouteFinder.Find(rails, atEnd, OmsiSignalRouteTarget.TrackAt(0, 0, 3, 0), TileIndex);
            Assert.IsTrue(search.Found, search.Message);
            Assert.AreEqual("2/0 3/0", Text(search.Ways.Single()));
            Assert.AreEqual(200.0, search.Ways[0].LengthMeters, 1e-3);
            Assert.AreEqual(1, search.SignalTrack.Path.ElementId);
            Assert.IsFalse(search.StartsOnSignalTrack);
            Assert.AreEqual(0, search.Ways[0].Entries[0].TileIndex);

            // In the first half of its rail it starts on that rail; the choice can be turned either way.
            var atStart = new OmsiSignalPose(7, new OmsiMapPoint(10, -3), 90.0);
            Assert.AreEqual("1/0 2/0 3/0", Text(OmsiSignalRouteFinder.Find(rails, atStart, OmsiSignalRouteTarget.TrackAt(0, 0, 3, 0), TileIndex).Ways[0]));
            Assert.AreEqual("2/0 3/0", Text(OmsiSignalRouteFinder.Find(rails, atStart, OmsiSignalRouteTarget.TrackAt(0, 0, 3, 0), TileIndex,
                new OmsiSignalRouteSearchOptions { StartOnSignalTrack = false }).Ways[0]));

            // A target signal ends the route on the rail it stands beside.
            var target = new OmsiSignalPose(8, new OmsiMapPoint(195, -3), 90.0);
            var toSignal = OmsiSignalRouteFinder.Find(rails, atEnd, OmsiSignalRouteTarget.Signal(target), TileIndex);
            Assert.AreEqual("2/0", Text(toSignal.Ways.Single()));
            Assert.AreEqual(8, toSignal.Ways[0].EndSignalId);
        }

        [Test]
        public void TheSignalsHeadingGivesTheWayOfTravel()
        {
            var rails = Line();
            // Facing west, north of the rail (its right in that way), at the west end of rail 2: the route goes on over rail 1.
            var west = new OmsiSignalPose(7, new OmsiMapPoint(105, 3), 270.0);
            var back = OmsiSignalRouteFinder.Find(rails, west, OmsiSignalRouteTarget.TrackAt(0, 0, 1, 0), TileIndex);
            Assert.AreEqual("1/0", Text(back.Ways.Single()));
            Assert.IsFalse(back.SignalTrack.Forward);
            var ahead = OmsiSignalRouteFinder.Find(rails, west, OmsiSignalRouteTarget.TrackAt(0, 0, 3, 0), TileIndex);
            Assert.IsFalse(ahead.Found);
            Assert.AreEqual(OmsiSignalRouteSearchProblem.NoWay, ahead.Problem);
            StringAssert.Contains("No rail leads", ahead.Message);

            // A signal across the rails (heading 0) stands beside none of them.
            var across = OmsiSignalRouteFinder.Find(rails, new OmsiSignalPose(7, new OmsiMapPoint(50, -3), 0.0), OmsiSignalRouteTarget.TrackAt(0, 0, 3, 0), TileIndex);
            Assert.AreEqual(OmsiSignalRouteSearchProblem.NoTrackAtSignal, across.Problem);
            Assert.IsNull(across.SignalTrack);
        }

        [Test]
        public void ASwitchGivesBothBranchesShortestFirst()
        {
            var rails = Switch();
            var signal = new OmsiSignalPose(7, new OmsiMapPoint(95, -3), 90.0);
            var straight = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.TrackAt(0, 0, 4, 0), TileIndex);
            Assert.AreEqual("10/0 4/0", Text(straight.Ways.Single()));
            var left = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.TrackAt(0, 0, 5, 0), TileIndex);
            Assert.AreEqual("10/1 5/0", Text(left.Ways.Single()));

            // Up to the next signal on either branch: both ways, the shorter first, each with the signal it ends at.
            var signals = new[] { new OmsiSignalPose(21, new OmsiMapPoint(245, -3), 90.0), new OmsiSignalPose(22, new OmsiMapPoint(245, 34), 73.3), signal };
            var next = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.NextSignal(signals), TileIndex);
            Assert.AreEqual(2, next.Ways.Count);
            Assert.AreEqual("10/0 4/0", Text(next.Ways[0]));
            Assert.AreEqual(21, next.Ways[0].EndSignalId);
            Assert.AreEqual("10/1 5/0", Text(next.Ways[1]));
            Assert.AreEqual(22, next.Ways[1].EndSignalId);
            Assert.Less(next.Ways[0].LengthMeters, next.Ways[1].LengthMeters);
            Assert.AreEqual(2, next.Ways[0].Lanes.Count);

            // At most the ways asked for.
            var one = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.NextSignal(signals), TileIndex, new OmsiSignalRouteSearchOptions { MaximumWays = 1 });
            Assert.AreEqual("10/0 4/0", Text(one.Ways.Single()));
            Assert.IsTrue(one.MoreWaysPossible);
        }

        [Test]
        public void WithoutAWayTheSearchSaysWhy()
        {
            var gap = OmsiPathNetwork.Build(new[] { Rail(1, 0, 0, 0, 100, 0), Rail(2, 0, 160, 0, 260, 0) }, 2);
            var signal = new OmsiSignalPose(7, new OmsiMapPoint(10, -3), 90.0);
            var cut = OmsiSignalRouteFinder.Find(gap, signal, OmsiSignalRouteTarget.TrackAt(0, 0, 2, 0), TileIndex);
            Assert.AreEqual(OmsiSignalRouteSearchProblem.NoWay, cut.Problem);
            Assert.IsEmpty(cut.Ways);

            var rails = Line();
            var far = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.TrackAt(0, 0, 3, 0), TileIndex, new OmsiSignalRouteSearchOptions { MaximumLengthMeters = 150.0 });
            Assert.AreEqual(OmsiSignalRouteSearchProblem.TooFar, far.Problem);
            StringAssert.Contains("150 m", far.Message);
            var steps = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.TrackAt(0, 0, 3, 0), TileIndex, new OmsiSignalRouteSearchOptions { MaximumSteps = 1 });
            Assert.AreEqual(OmsiSignalRouteSearchProblem.StepLimit, steps.Problem);
            var noRail = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.TrackAt(0, 0, 99, 0), TileIndex);
            Assert.AreEqual(OmsiSignalRouteSearchProblem.TargetHasNoTrack, noRail.Problem);
            var lostTarget = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.Signal(new OmsiSignalPose(8, new OmsiMapPoint(500, 500), 90.0)), TileIndex);
            Assert.AreEqual(OmsiSignalRouteSearchProblem.TargetHasNoTrack, lostTarget.Problem);
        }

        [Test]
        public void ASignalAlongASplinePrefersThatSplineAndOneOnTheRightComesFirst()
        {
            // Two rails 2 m apart, both eastwards, the signal halfway between: it is on the right (south) of the northern one only.
            var rails = OmsiPathNetwork.Build(new[] { Rail(1, 0, 0, 0, 100, 0), Rail(2, 0, 0, 2, 100, 2) }, 2);
            Assert.AreEqual(2, OmsiSignalRouteFinder.SignalTrack(rails, new OmsiSignalPose(7, new OmsiMapPoint(10, 1.0), 90.0), 6.0, out _, out _).Path.ElementId);
            Assert.AreEqual(1, OmsiSignalRouteFinder.SignalTrack(rails, new OmsiSignalPose(7, new OmsiMapPoint(10, 1.2), 90.0, 1), 6.0, out _, out _).Path.ElementId);
            var along = OmsiSignalRouteFinder.SignalTrack(rails, new OmsiSignalPose(7, new OmsiMapPoint(30, -2), 90.0), 6.0, out var meters, out var length);
            Assert.AreEqual(1, along.Path.ElementId);
            Assert.AreEqual(30.0, meters, 1e-6);
            Assert.AreEqual(100.0, length, 1e-6);
        }

        [Test]
        public void TheNearestLaneIsTheOneAClickIsClosestToWithinReach()
        {
            var rails = Switch();
            Assert.AreEqual(1, OmsiSignalRouteFinder.NearestLane(rails, new OmsiMapPoint(50, 1.5), 3.0).Path.ElementId);
            var branch = OmsiSignalRouteFinder.NearestLane(rails, new OmsiMapPoint(125, 6.5), 3.0);
            Assert.AreEqual(10, branch.Path.ElementId);
            Assert.AreEqual(1, branch.Path.PathIndex);
            Assert.IsNull(OmsiSignalRouteFinder.NearestLane(rails, new OmsiMapPoint(50, 20), 3.0));
        }

        [Test]
        public void TheOverlapRunsOnPastTheTargetSignalAsFarAsTheTrackGoes()
        {
            var rails = Line();
            var start = new OmsiSignalPose(7, new OmsiMapPoint(95, -3), 90.0);
            var target = OmsiSignalRouteTarget.Signal(new OmsiSignalPose(8, new OmsiMapPoint(195, -3), 90.0));
            Assert.AreEqual("2/0", Text(OmsiSignalRouteFinder.Find(rails, start, target, TileIndex).Ways.Single()));

            var one = OmsiSignalRouteFinder.Find(rails, start, target, TileIndex, new OmsiSignalRouteSearchOptions { OverlapPieces = 1 }).Ways.Single();
            Assert.AreEqual("2/0 3/0", Text(one));
            Assert.AreEqual(1, one.OverlapEntries);
            Assert.AreEqual(200.0, one.LengthMeters, 1e-3);
            Assert.AreEqual(2, one.Lanes.Count);
            Assert.AreEqual(8, one.EndSignalId);

            // The track ends after rail 3: more overlap adds nothing more.
            var three = OmsiSignalRouteFinder.Find(rails, start, target, TileIndex, new OmsiSignalRouteSearchOptions { OverlapPieces = 3 }).Ways.Single();
            Assert.AreEqual("2/0 3/0", Text(three));
            Assert.AreEqual(1, three.OverlapEntries);

            // A target path ends exactly where it is told to, with no overlap.
            var track = OmsiSignalRouteFinder.Find(rails, start, OmsiSignalRouteTarget.TrackAt(0, 0, 2, 0), TileIndex, new OmsiSignalRouteSearchOptions { OverlapPieces = 2 });
            Assert.AreEqual("2/0", Text(track.Ways.Single()));
        }

        [Test]
        public void TheOverlapTakesTheShorterBranchAtASwitch()
        {
            var rails = Switch();
            var start = new OmsiSignalPose(7, new OmsiMapPoint(10, -3), 90.0);
            var target = OmsiSignalRouteTarget.Signal(new OmsiSignalPose(8, new OmsiMapPoint(95, -3), 90.0));
            var one = OmsiSignalRouteFinder.Find(rails, start, target, TileIndex, new OmsiSignalRouteSearchOptions { OverlapPieces = 1 }).Ways.Single();
            Assert.AreEqual("1/0 10/0", Text(one), "path 0 of the switch is the shorter branch");
            var two = OmsiSignalRouteFinder.Find(rails, start, target, TileIndex, new OmsiSignalRouteSearchOptions { OverlapPieces = 2 }).Ways.Single();
            Assert.AreEqual("1/0 10/0 4/0", Text(two));
            Assert.AreEqual(2, two.OverlapEntries);
        }

        [Test]
        public void TheWaysAreSortedAndMeasuredUpToTheTargetWithoutTheOverlap()
        {
            // The straight branch reaches its signal first but runs on over a long rail; the left branch is longer to its signal, its overlap short.
            var rails = OmsiPathNetwork.Build(new[]
            {
                Rail(1, 0, 0, 0, 100, 0), Rail(10, 0, 100, 0, 150, 0, false), Rail(10, 1, 100, 0, 150, 10, false),
                Rail(4, 0, 150, 0, 250, 0), Rail(6, 0, 250, 0, 550, 0), Rail(5, 0, 150, 10, 270, 10), Rail(7, 0, 270, 10, 290, 10)
            }, 2);
            var signal = new OmsiSignalPose(7, new OmsiMapPoint(95, -3), 90.0);
            var signals = new[] { new OmsiSignalPose(21, new OmsiMapPoint(245, -3), 90.0), new OmsiSignalPose(22, new OmsiMapPoint(265, 7), 90.0), signal };
            var search = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.NextSignal(signals), TileIndex, new OmsiSignalRouteSearchOptions { OverlapPieces = 1 });
            Assert.AreEqual(2, search.Ways.Count, search.Message);
            Assert.AreEqual("10/0 4/0 6/0", Text(search.Ways[0]));
            Assert.AreEqual("10/1 5/0 7/0", Text(search.Ways[1]));

            // Up to the target the straight way is shorter; with its overlap it would be the longer one.
            Assert.AreEqual(150.0, search.Ways[0].RouteLengthMeters, 1e-3);
            Assert.AreEqual(2, search.Ways[0].RouteEntryCount);
            Assert.AreEqual(1, search.Ways[0].OverlapEntries);
            Assert.AreEqual(450.0, search.Ways[0].LengthMeters, 1e-3);
            Assert.Less(search.Ways[0].RouteLengthMeters, search.Ways[1].RouteLengthMeters);
            Assert.Greater(search.Ways[0].LengthMeters, search.Ways[1].LengthMeters);
            Assert.AreEqual(2, search.Ways[1].RouteEntryCount);
        }

        [Test]
        public void TheNextSignalSearchCanSkipSignalsThatAreNotCandidates()
        {
            var rails = Switch();
            var signal = new OmsiSignalPose(7, new OmsiMapPoint(95, -3), 90.0);
            var signals = new[] { new OmsiSignalPose(21, new OmsiMapPoint(245, -3), 90.0), new OmsiSignalPose(22, new OmsiMapPoint(245, 34), 73.3), signal };
            var only22 = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.NextSignal(signals), TileIndex,
                new OmsiSignalRouteSearchOptions { NextSignalCandidates = new System.Collections.Generic.HashSet<int> { 22 } });
            Assert.AreEqual("10/1 5/0", Text(only22.Ways.Single()));
            Assert.AreEqual(22, only22.Ways[0].EndSignalId);

            // A signal behind a skipped one is reached over its track: 21 skipped, 22 not a candidate either: nothing.
            var none = OmsiSignalRouteFinder.Find(rails, signal, OmsiSignalRouteTarget.NextSignal(signals), TileIndex,
                new OmsiSignalRouteSearchOptions { NextSignalCandidates = new System.Collections.Generic.HashSet<int>() });
            Assert.IsFalse(none.Found);
        }
    }
}
