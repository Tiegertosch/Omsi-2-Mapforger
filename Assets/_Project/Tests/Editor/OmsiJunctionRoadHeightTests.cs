using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// Road ends on junctions made here follow the junction when a move or turn by values lifts, lowers or tilts it
    /// (<see cref="OmsiMapSession.UpdateObjectsFollowingRoads"/>): the ends get the heights of the connection points, the splines are planned as
    /// <see cref="OmsiSplineEditing.PlanJointHeight"/> plans them, the terrain fits the roads, and all of it is written all or nothing.
    /// </summary>
    public sealed class OmsiJunctionRoadHeightTests
    {
        private const string Cross = @"Sceneryobjects\Test\cross.sco";
        private const string Road = @"Splines\road.sli";

        private string root;
        private string mapFolder;

        [SetUp]
        public void CreateMap()
        {
            root = Path.Combine(Path.GetTempPath(), "mapforge-lift-" + Guid.NewGuid().ToString("N"));
            mapFolder = Path.Combine(root, "maps", "Liftmap");
            Directory.CreateDirectory(mapFolder);
            WriteUtf16("global.cfg", "[NextIDCode]", "100", "", "[map]", "0", "0", "tile_0_0.map", "", "[map]", "1", "0", "tile_1_0.map", "");
            WriteUtf16("tile_0_0.map", "[version]", "14", "");
            WriteUtf16("tile_1_0.map", "[version]", "14", "");
            WriteFlatTerrain("tile_0_0.map.terrain", 2.0f);
            WriteFlatTerrain("tile_1_0.map.terrain", 2.0f);
            var splines = Path.Combine(root, "Splines");
            Directory.CreateDirectory(splines);
            // 8 m wide with kerbs 0.15 m high at its edges.
            File.WriteAllText(Path.Combine(splines, "road.sli"),
                "[texture]\r\nroad.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-4\r\n0.15\r\n0\r\n1\r\n\r\n[profilepnt]\r\n4\r\n0.15\r\n1\r\n1\r\n");
            // A junction 20 m square with a connection point 10 m east of its origin, facing east, and one 10 m south, facing south.
            Directory.CreateDirectory(Path.Combine(root, "Sceneryobjects", "Test"));
            File.WriteAllText(Path.Combine(root, "Sceneryobjects", "Test", "cross.sco"),
                "[splinehelper]\r\nSplines\\road.sli\r\n10\r\n0\r\n0\r\n90\r\n\r\n" +
                "[splinehelper]\r\nSplines\\road.sli\r\n0\r\n0\r\n-10\r\n180\r\n\r\n" +
                "[boundingbox]\r\n20\r\n20\r\n1\r\n0\r\n0\r\n0\r\n");
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        /// <summary>
        /// The junction at 295, 150 in tile 0, 0 at 2 m; its east point at 305, 150 in tile 1, 0 starts a road running 60 m east, its south
        /// point at 295, 140 ends a road from 295, 80. Both roads fit the terrain, which is fitted to them.
        /// </summary>
        private OmsiMapSession JunctionWithTwoRoads(out OmsiMapSummary summary, out OmsiObjectRecord junction, double pieceLength = 100.0)
        {
            return JunctionWithTwoRoadsIn("Lift", out summary, out junction, pieceLength);
        }

        private OmsiMapSession JunctionWithTwoRoadsIn(string project, out OmsiMapSummary summary, out OmsiObjectRecord junction, double pieceLength = 100.0)
        {
            var session = OpenProject(project);
            summary = session.Read();
            junction = session.PlaceObject(summary, summary.Tiles[0], Cross, 295, 150, 2, 0).Record;
            BuildRoad(session, summary, 305, 150, 365, 150, pieceLength);
            BuildRoad(session, summary, 295, 80, 295, 140, pieceLength);
            session.RefitAllTerrain(summary);
            return session;
        }

        private static void BuildRoad(OmsiMapSession session, OmsiMapSummary summary, double startX, double startY, double endX, double endY,
            double pieceLength = 100.0)
        {
            session.CreateRoad(summary, OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = startX, StartY = startY, StartHeight = 2, EndX = endX, EndY = endY, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual,
                MaximumPieceLength = pieceLength
            }, null), Road, null, null, true);
        }

        [TestCase(5.0, 100.0)]
        [TestCase(30.0, 100.0)]
        [TestCase(30.0, 25.0)]
        public void LiftingAJunctionGivesItsRoadEndsThePointHeightsAndUndoAndRedoGiveTheSameFiles(double lift, double pieceLength)
        {
            // With pieces of 25 m each road is three splines; the piece at the junction ramps, the others stay.
            var pieces = pieceLength < 60.0;
            var session = JunctionWithTwoRoads(out var summary, out var junction, pieceLength);
            var tile = summary.Tiles[0];
            var before = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var placesBefore = session.JunctionTerrainPlaces(summary);
            var east = End(summary, 305, 150);
            var south = End(summary, 295, 140);
            Assert.IsFalse(east.AtEnd, "The east road starts on the junction.");
            Assert.IsTrue(south.AtEnd, "The south road ends on the junction.");
            // What the joint-height editing plans for each end, with the junction's own points left out, as they move along.
            var others = Catalog().PortsAround(summary, east.Tile).Where(port => !ReferenceEquals(port.Object, junction)).ToList();
            var expected = OmsiSplineEditing.PlanJointHeight(summary, east, 2 + lift, others).Splines
                .Concat(OmsiSplineEditing.PlanJointHeight(summary, south, 2 + lift,
                    Catalog().PortsAround(summary, south.Tile).Where(port => !ReferenceEquals(port.Object, junction)).ToList()).Splines)
                .ToDictionary(geometry => geometry.Spline.Id, geometry => geometry.Preview());
            var untouched = summary.Tiles.SelectMany(candidate => candidate.Splines).Where(spline => !expected.ContainsKey(spline.Id))
                .ToDictionary(spline => spline.Id, Describe);
            Assert.AreEqual(pieces, untouched.Count > 0, "Only pieces of a road beyond the one at the junction stay.");

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile, junction, lift) });
            var placesLifted = session.JunctionTerrainPlaces(summary);
            session.RefitTerrainAtJunctions(summary, placesBefore, placesLifted);

            CollectionAssert.AreEqual(new[] { true }, outcome.Changed);
            CollectionAssert.AreEquivalent(new[] { east.Spline.Id, south.Spline.Id }, outcome.FollowedSplineIds);
            CollectionAssert.IsEmpty(outcome.KeptEnds);
            Assert.AreEqual(1, outcome.RoadChanges.Count, "Two splines on their own each are written together.");
            var ports = Catalog().Ports(tile, junction, summary.TileSizeMeters);
            Assert.AreEqual(ports[0].Height, End(summary, 305, 150).Height, 1e-6);
            Assert.AreEqual(ports[1].Height, End(summary, 295, 140).Height, 1e-6);
            Assert.AreEqual(2 + lift, ports[0].Height, 1e-9);
            foreach (var pair in expected)
                Assert.AreEqual(Describe(pair.Value), Describe(Spline(summary, pair.Key)), "Spline " + pair.Key + " as PlanJointHeight plans it.");
            foreach (var pair in untouched)
                Assert.AreEqual(pair.Value, Describe(Spline(summary, pair.Key)), "Spline " + pair.Key + " beyond the junction's pieces stays.");
            if (lift > 10)
                Assert.IsTrue(outcome.Warnings.Exists(warning => warning.Contains("steeper than")), "A steep ramp is reported.");
            // A 20 m piece rising 30 m is too steep for the terrain beside it to stay close; a 60 m road is not.
            AssertNoColumn(session, summary, 2 + lift, !pieces);
            CollectionAssert.IsEmpty(session.RefitAllTerrain(summary), "The local fit is the full fit.");
            AssertRecordsMatchFiles(session, summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
            var lifted = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);

            // Undo as the shell does it: the objects first, then the roads, the last first; then the terrain around the junction.
            UndoAll(session, summary, outcome, Lift(tile, junction, -lift));
            session.RefitTerrainAtJunctions(summary, placesLifted, session.JunctionTerrainPlaces(summary));
            OmsiMapWorkspaceTests.AssertSameFiles(before, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            AssertRecordsMatchFiles(session, summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);

            // Redo: the roads first, then the objects.
            var placesDown = session.JunctionTerrainPlaces(summary);
            foreach (var change in outcome.RoadChanges)
                session.Redo(summary, change);
            session.UpdateObjects(new[] { Lift(tile, junction, lift) });
            session.RefitTerrainAtJunctions(summary, placesDown, session.JunctionTerrainPlaces(summary));
            OmsiMapWorkspaceTests.AssertSameFiles(lifted, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            AssertRecordsMatchFiles(session, summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void WithoutTheRoadsFollowingALiftedJunctionStandsOnAColumnAboveThem()
        {
            // The behaviour before: the terrain under the junction rises to it, and near the arms above the roads left below.
            var session = JunctionWithTwoRoads(out var summary, out var junction);
            var places = session.JunctionTerrainPlaces(summary);
            session.UpdateObjects(new[] { Lift(summary.Tiles[0], junction, 30) });
            session.RefitTerrainAtJunctions(summary, places, session.JunctionTerrainPlaces(summary));

            var terrain = Terrain(session, "tile_1_0.map.terrain");
            // At 310, 145, a metre beside the east road at 2 m and 5 m from the junction: the slope of the column.
            Assert.Greater(terrain[2, 29], 5.0);
            // With the roads following, the terrain there lies within a metre of the road.
            var lifted = JunctionWithTwoRoadsIn("Following", out var followingSummary, out var followingJunction);
            var followingPlaces = lifted.JunctionTerrainPlaces(followingSummary);
            lifted.UpdateObjectsFollowingRoads(followingSummary, new[] { Lift(followingSummary.Tiles[0], followingJunction, 30) });
            lifted.RefitTerrainAtJunctions(followingSummary, followingPlaces, lifted.JunctionTerrainPlaces(followingSummary));
            Assert.AreEqual(RoadHeightAt(followingSummary, 310, 150), Terrain(lifted, "tile_1_0.map.terrain")[2, 29], 1.0);
        }

        [TestCase(3.0, 0.0)]
        [TestCase(0.0, 3.0)]
        [TestCase(-2.0, 4.0)]
        public void TiltingAJunctionGivesItsRoadEndsThePointHeights(double pitch, double bank)
        {
            var session = JunctionWithTwoRoads(out var summary, out var junction);
            var tile = summary.Tiles[0];
            var places = session.JunctionTerrainPlaces(summary);
            var before = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { new OmsiObjectValueEdit(tile, junction, 295, 150, 2, 0, pitch, bank) });
            var tilted = session.JunctionTerrainPlaces(summary);
            session.RefitTerrainAtJunctions(summary, places, tilted);

            var ports = Catalog().Ports(tile, junction, summary.TileSizeMeters);
            // The east point rises with the bank, the south one with the pitch; the heights are not those of a level junction.
            Assert.Greater(Math.Abs(ports[bank != 0 ? 0 : 1].Height - 2.0), 0.1);
            Assert.AreEqual(ports[0].Height, End(summary, 305, 150).Height, 1e-6);
            Assert.AreEqual(ports[1].Height, End(summary, 295, 140).Height, 1e-6);
            // A tilt moves the points a little sideways; the road ends stay where they were, still joined for the terrain.
            foreach (var port in ports)
                Assert.IsNotNull(OmsiSplineEnd.FindFree(summary, port.Position.X, port.Position.Y, 0.05), "Point " + port.Index + " keeps its road end.");
            CollectionAssert.IsEmpty(session.RefitAllTerrain(summary));

            UndoAll(session, summary, outcome, new OmsiObjectValueEdit(tile, junction, 295, 150, 2, 0, 0, 0));
            session.RefitTerrainAtJunctions(summary, tilted, session.JunctionTerrainPlaces(summary));
            OmsiMapWorkspaceTests.AssertSameFiles(before, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
        }

        [TestCase(3.0, 0.0)]
        [TestCase(0.0, 90.0)]
        public void MovingOrTurningAJunctionLeavesTheRoadsAsBefore(double east, double heading)
        {
            var session = JunctionWithTwoRoads(out var summary, out var junction);
            var tile = summary.Tiles[0];
            var splines = summary.Tiles.SelectMany(candidate => candidate.Splines).ToDictionary(spline => spline.Id, Describe);
            var roadTile = session.Workspace.ReadAllBytes("tile_1_0.map");

            // Also when it is lifted in the same edit: its points leave the road ends.
            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { new OmsiObjectValueEdit(tile, junction, 295 + east, 150, 7, heading, 0, 0) });

            CollectionAssert.AreEqual(new[] { true }, outcome.Changed);
            CollectionAssert.IsEmpty(outcome.RoadChanges);
            CollectionAssert.IsEmpty(outcome.FollowedSplineIds);
            CollectionAssert.AreEqual(roadTile, session.Workspace.ReadAllBytes("tile_1_0.map"), "The road's tile is not written.");
            foreach (var spline in summary.Tiles.SelectMany(candidate => candidate.Splines))
                Assert.AreEqual(splines[spline.Id], Describe(spline));
        }

        [Test]
        public void LiftingTwoJunctionsOnOneRoadGivesBothEndsTheirHeights()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile0 = summary.Tiles[0];
            var tile1 = summary.Tiles[1];
            var first = session.PlaceObject(summary, tile0, Cross, 295, 150, 2, 0).Record;
            // Turned round, its east point faces west at 365, 150: the road's other end.
            var second = session.PlaceObject(summary, tile1, Cross, 75, 150, 2, 180).Record;
            BuildRoad(session, summary, 305, 150, 365, 150);
            session.RefitAllTerrain(summary);
            var before = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var places = session.JunctionTerrainPlaces(summary);
            var road = End(summary, 305, 150).Spline.Id;

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile0, first, 5), Lift(tile1, second, 10) });
            var lifted = session.JunctionTerrainPlaces(summary);
            session.RefitTerrainAtJunctions(summary, places, lifted);

            var spline = Spline(summary, road);
            Assert.AreEqual(7.0, spline.Height, 1e-6);
            Assert.AreEqual(12.0, spline.EndHeight, 1e-6);
            // Each end in a round of its own, the second reading the spline as the first left it; the road rises evenly.
            Assert.AreEqual(2, outcome.RoadChanges.Count);
            Assert.AreEqual(spline.GradientStartPercent, spline.GradientEndPercent, 1e-6);
            CollectionAssert.AreEqual(new[] { road }, outcome.FollowedSplineIds);
            CollectionAssert.IsEmpty(session.RefitAllTerrain(summary));

            session.UpdateObjects(new[] { Lift(tile1, second, -10), Lift(tile0, first, -5) });
            for (var index = outcome.RoadChanges.Count - 1; index >= 0; index--)
                session.Undo(summary, outcome.RoadChanges[index]);
            session.RefitTerrainAtJunctions(summary, lifted, session.JunctionTerrainPlaces(summary));
            OmsiMapWorkspaceTests.AssertSameFiles(before, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
        }

        [Test]
        public void AJunctionLiftedWithOtherObjectsMovedSavesThemAllAndOnlyItsRoadsFollow()
        {
            var session = JunctionWithTwoRoads(out var summary, out var junction);
            var tile = summary.Tiles[0];
            var tree = session.PlaceObject(summary, tile, @"Sceneryobjects\tree.sco", 20, 20, 0, 0).Record;
            // A second junction lifted far from any road.
            var lone = session.PlaceObject(summary, tile, Cross, 100, 250, 2, 0).Record;

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[]
            {
                new OmsiObjectValueEdit(tile, tree, 25, 20, 1, 0, 0, 0), Lift(tile, junction, 4), Lift(tile, lone, 4)
            });

            CollectionAssert.AreEqual(new[] { true, true, true }, outcome.Changed);
            Assert.AreEqual(25.0, tree.LocalX, 1e-9);
            Assert.AreEqual(2, outcome.FollowedSplineIds.Count);
            Assert.AreEqual(6.0, End(summary, 305, 150).Height, 1e-6);
            Assert.AreEqual(6.0, End(summary, 295, 140).Height, 1e-6);
            // What the shell names in a chrono variant, where the roads stay with the base map: nothing is written for it.
            var writes = session.Workspace.WriteCount;
            CollectionAssert.AreEquivalent(outcome.FollowedSplineIds,
                session.JunctionRoadEnds(summary, new[] { Lift(tile, junction, 1) }).Select(end => end.End.Spline.Id));
            Assert.AreEqual(writes, session.Workspace.WriteCount);
        }

        [TestCase("tile_1_0.map")]
        [TestCase("tile_0_0.map")]
        [TestCase("tile_1_0.map.terrain")]
        public void ALiftWhoseWriteFailsLeavesEveryFileAndRecordAsTheyWere(string blocked)
        {
            // Only the east road, in tile 1, 0: the roads are written before the junction's tile 0, 0, so blocking that tile takes them back.
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var junction = session.PlaceObject(summary, tile, Cross, 295, 150, 2, 0).Record;
            BuildRoad(session, summary, 305, 150, 365, 150);
            session.RefitAllTerrain(summary);
            var files = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var splines = summary.Tiles.SelectMany(candidate => candidate.Splines).ToDictionary(spline => spline.Id, Describe);
            var raised = OmsiMapWorkspaceTests.WatchRollBackFailures(session);
            Assert.IsTrue(session.Workspace.IsModified(blocked), blocked + " has an edited copy, so writing it needs a save.");

            Exception failed;
            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, blocked, false))
                failed = Assert.Catch(() => session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile, junction, 30) }));

            OmsiMapWorkspaceTests.AssertWriteFailure(failed);
            Assert.IsEmpty(raised, "Everything was put back, so no failed roll-back is reported.");
            OmsiMapWorkspaceTests.AssertSameFiles(files, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            Assert.AreEqual(2.0, junction.Height, 1e-12);
            foreach (var spline in summary.Tiles.SelectMany(candidate => candidate.Splines))
                Assert.AreEqual(splines[spline.Id], Describe(spline));
            AssertRecordsMatchFiles(session, summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);

            // Without the obstacle the very lift goes through.
            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile, junction, 30) });
            Assert.AreEqual(1, outcome.FollowedSplineIds.Count);
            Assert.AreEqual(32.0, junction.Height, 1e-12);
            Assert.AreEqual(32.0, End(summary, 305, 150).Height, 1e-6);
        }

        [Test]
        public void ALiftWhoseRoadsCannotBePutBackReportsItOnce()
        {
            // Spline 10 of the original map runs 60 m east from the junction's east point. A first project lifts the junction to 7 m, which
            // gives the spline a new start; that tile becomes the original map's, so a lift to 7 m writes the original's bytes again and
            // takes the edited copy away, which needs no save, while putting the road back at 2 m needs one.
            WriteUtf16("tile_1_0.map", "[version]", "14", "",
                "[spline]", "0", Road, "10", "0", "0", "5", "2", "150", "90", "60", "0", "0", "0", "0", "0", "0", "0", "0", "");
            byte[] liftedTile;
            {
                var first = OpenProject();
                var firstSummary = first.Read();
                var firstJunction = first.PlaceObject(firstSummary, firstSummary.Tiles[0], Cross, 295, 150, 2, 0).Record;
                var lift = first.UpdateObjectsFollowingRoads(firstSummary, new[] { Lift(firstSummary.Tiles[0], firstJunction, 5) });
                CollectionAssert.AreEqual(new[] { 10 }, lift.FollowedSplineIds);
                liftedTile = first.Workspace.ReadAllBytes("tile_1_0.map");
                Directory.Delete(first.Workspace.WorkspacePath, true);
            }
            File.WriteAllBytes(Path.Combine(mapFolder, "tile_1_0.map"), liftedTile);

            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var junction = session.PlaceObject(summary, tile, Cross, 295, 150, 7, 0).Record;
            Assert.AreEqual(1, session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile, junction, -5) }).FollowedSplineIds.Count);
            Assert.AreEqual(2.0, End(summary, 305, 150).Height, 1e-6);
            Assert.IsTrue(session.Workspace.IsModified("tile_1_0.map"));
            var raised = OmsiMapWorkspaceTests.WatchRollBackFailures(session);

            OmsiRollBackFailedException failed;
            // The road goes back to the original's bytes; then tile 0, 0 cannot be saved, and neither can tile 1, 0 when its road is put back.
            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "tile_0_0.map", false))
            using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "tile_1_0.map", false))
                failed = Assert.Throws<OmsiRollBackFailedException>(() => session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile, junction, 5) }));

            Assert.AreEqual(1, raised.Count, "The failed roll-back is reported once.");
            Assert.AreSame(failed, raised[0]);
            var both = (AggregateException)failed.InnerException;
            OmsiMapWorkspaceTests.AssertWriteFailure(both.InnerExceptions[0]);
            OmsiMapWorkspaceTests.AssertWriteFailure(both.InnerExceptions[1]);
            Assert.AreEqual(2.0, junction.Height, 1e-12, "The junction's record stays as it was.");
            // Part of the step stayed in the files: the road's tile is the original again.
            Assert.IsFalse(session.Workspace.IsModified("tile_1_0.map"));
        }

        [Test]
        public void AnUndoOfTwoRoundsFailingAtItsLastPartFitsTheWholeMapAndGivesTheFilesBeforeTheUndo()
        {
            // Two junctions on one road: its ends follow in two rounds, S1 and S2, recorded as the shell records them, before the objects.
            var session = OpenProject();
            var summary = session.Read();
            var tile0 = summary.Tiles[0];
            var tile1 = summary.Tiles[1];
            var first = session.PlaceObject(summary, tile0, Cross, 295, 150, 2, 0).Record;
            var second = session.PlaceObject(summary, tile1, Cross, 75, 150, 2, 180).Record;
            BuildRoad(session, summary, 305, 150, 365, 150);
            session.RefitAllTerrain(summary);
            var places = session.JunctionTerrainPlaces(summary);
            var edits = new[] { Lift(tile0, first, 5), Lift(tile1, second, 10) };
            var values = edits.Select(edit => new[] { Values(edit.Record), new[] { edit.X, edit.Y, edit.Z, edit.HeadingDegrees, edit.PitchDegrees, edit.BankDegrees } }).ToList();
            var outcome = session.UpdateObjectsFollowingRoads(summary, edits);
            session.RefitTerrainAtJunctions(summary, places, session.JunctionTerrainPlaces(summary));
            Assert.AreEqual(2, outcome.RoadChanges.Count);
            var lifted = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var parts = new List<GroupPart>();
            foreach (var change in outcome.RoadChanges)
                parts.Add(new GroupPart { Change = change });
            for (var index = 0; index < edits.Length; index++)
                parts.Add(new GroupPart { Tile = edits[index].Tile, Record = edits[index].Record, Before = values[index][0], After = values[index][1] });

            // The undo as the shell's ApplyStep does it: the objects back, then S2, then S1, whose tile cannot be written.
            var units = StepGroup.Runs(Enumerable.Reverse(parts).ToList(), part => part.Change == null);
            var raised = OmsiMapWorkspaceTests.WatchRollBackFailures(session);
            Exception failed = Assert.Catch(() => StepGroup.Apply(units, unit =>
            {
                if (unit[0].Change == null)
                    ApplyValues(session, unit, true);
                else if (ReferenceEquals(unit[0].Change, outcome.RoadChanges[0]))
                    using (OmsiMapWorkspaceTests.BlockWrites(session.Workspace, "tile_1_0.map", false))
                        session.Undo(summary, unit[0].Change);
                else
                    session.Undo(summary, unit[0].Change);
            }, unit =>
            {
                if (unit[0].Change == null)
                    ApplyValues(session, Enumerable.Reverse(unit).ToList(), false);
                else
                    session.Redo(summary, unit[0].Change);
            }, unit => unit.Where(part => part.Change != null).Select(part => part.Change.IsApplied).ToList(), null));

            OmsiMapWorkspaceTests.AssertWriteFailure(failed);
            Assert.IsEmpty(raised);
            Assert.AreEqual(7.0, first.Height, 1e-12);
            Assert.AreEqual(12.0, second.Height, 1e-12);
            // S2 was redone with the junctions still down, then they were lifted again: the junctions lie as before the undo, so a fit at
            // the junctions that changed finds nothing to do, yet the terrain at S2 was fitted to the junctions below.
            CollectionAssert.IsNotEmpty(session.RefitAllTerrain(summary), "The terrain taken back is not that of a full fit.");
            // What the shell does now after such a failure: a full fit, which gives the files of before the undo.
            OmsiMapWorkspaceTests.AssertSameFiles(lifted, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            AssertRecordsMatchFiles(session, summary);
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void AnEndOnAPointOfAnObjectThatIsNotLiftedStaysAndIsNamed()
        {
            // An object placed before the junction, whose east point lies on the junction's south point at 295, 140: the terrain fitting
            // joins the south road's end to it, so the end stays with it.
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var other = session.PlaceObject(summary, tile, Cross, 285, 140, 2, 0).Record;
            var junction = session.PlaceObject(summary, tile, Cross, 295, 150, 2, 0).Record;
            BuildRoad(session, summary, 305, 150, 365, 150);
            BuildRoad(session, summary, 295, 80, 295, 140);
            session.RefitAllTerrain(summary);
            var east = End(summary, 305, 150).Spline.Id;
            var south = End(summary, 295, 140).Spline.Id;

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile, junction, 5) });

            CollectionAssert.AreEqual(new[] { east }, outcome.FollowedSplineIds);
            CollectionAssert.AreEqual(new[] { "#" + south + ": on a point of object #" + other.Id + ", which is not lifted or tilted" }, outcome.KeptEnds);
            Assert.AreEqual(7.0, End(summary, 305, 150).Height, 1e-6);
            Assert.AreEqual(2.0, End(summary, 295, 140).Height, 1e-6);
            Assert.AreEqual(7.0, junction.Height, 1e-12);
        }

        [Test]
        public void AnEditThatMovedTheJunctionAlongLiftsItWithoutItsRoads()
        {
            // As the shell gives the value edit after moving the object into another tile: the place is that after the move, so only the
            // flag tells that its points left the road ends.
            var session = JunctionWithTwoRoads(out var summary, out var junction);
            var tile = summary.Tiles[0];
            var splines = summary.Tiles.SelectMany(candidate => candidate.Splines).ToDictionary(spline => spline.Id, Describe);
            var moved = new OmsiObjectValueEdit(tile, junction, junction.LocalX, junction.LocalY, junction.Height + 5, junction.HeadingDegrees,
                junction.PitchDegrees, junction.BankDegrees, true);
            CollectionAssert.IsEmpty(session.JunctionRoadEnds(summary, new[] { moved }));
            Assert.AreEqual(2, session.JunctionRoadEnds(summary, new[] { Lift(tile, junction, 5) }).Count, "Without the flag the roads would follow.");

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { moved });

            CollectionAssert.AreEqual(new[] { true }, outcome.Changed);
            Assert.AreEqual(7.0, junction.Height, 1e-12);
            CollectionAssert.IsEmpty(outcome.RoadChanges);
            CollectionAssert.IsEmpty(outcome.FollowedSplineIds);
            CollectionAssert.IsEmpty(outcome.KeptEnds);
            foreach (var spline in summary.Tiles.SelectMany(candidate => candidate.Splines))
                Assert.AreEqual(splines[spline.Id], Describe(spline));
        }

        [Test]
        public void ATiltThatTakesAPointOffItsRoadEndLeavesThatEndAndNamesIt()
        {
            // A pitch of 10 degrees takes the south point, 10 m from the origin, 15 cm towards it: further than the 5 cm the terrain
            // fitting joins. The east point, on the axis of the pitch, keeps its place and height.
            var session = JunctionWithTwoRoads(out var summary, out var junction);
            var tile = summary.Tiles[0];
            var south = End(summary, 295, 140).Spline.Id;
            var splines = summary.Tiles.SelectMany(candidate => candidate.Splines).ToDictionary(spline => spline.Id, Describe);

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { new OmsiObjectValueEdit(tile, junction, 295, 150, 2, 0, 10, 0) });

            CollectionAssert.AreEqual(new[] { true }, outcome.Changed);
            CollectionAssert.AreEqual(new[] { "#" + south + ": the tilt takes point 2 of junction #" + junction.Id + " off the road end" }, outcome.KeptEnds);
            CollectionAssert.IsEmpty(outcome.RoadChanges);
            foreach (var spline in summary.Tiles.SelectMany(candidate => candidate.Splines))
                Assert.AreEqual(splines[spline.Id], Describe(spline));
            Assert.IsNull(OmsiSplineEnd.FindFree(summary, Catalog().Ports(tile, junction, summary.TileSizeMeters)[1].Position.X,
                Catalog().Ports(tile, junction, summary.TileSizeMeters)[1].Position.Y, 0.05), "The south point left the road end.");
        }

        [Test]
        public void ALoopWithBothEndsOnOneJunctionFollowsAtBothEnds()
        {
            var session = OpenProject();
            var summary = session.Read();
            var tile = summary.Tiles[0];
            var junction = session.PlaceObject(summary, tile, Cross, 295, 150, 2, 0).Record;
            // From the east point at 305, 150 to the south point at 295, 140.
            BuildRoad(session, summary, 305, 150, 295, 140);
            session.RefitAllTerrain(summary);
            var start = End(summary, 305, 150);
            var end = End(summary, 295, 140);
            Assert.AreEqual(start.Spline.Id, end.Spline.Id, "One spline joins both points.");
            Assert.IsFalse(start.AtEnd);
            Assert.IsTrue(end.AtEnd);
            var before = OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace);
            var places = session.JunctionTerrainPlaces(summary);

            var outcome = session.UpdateObjectsFollowingRoads(summary, new[] { Lift(tile, junction, 5) });
            var lifted = session.JunctionTerrainPlaces(summary);
            session.RefitTerrainAtJunctions(summary, places, lifted);

            var spline = Spline(summary, start.Spline.Id);
            Assert.AreEqual(7.0, spline.Height, 1e-6);
            Assert.AreEqual(7.0, spline.EndHeight, 1e-6);
            Assert.AreEqual(2, outcome.RoadChanges.Count, "Each end in a round of its own.");
            CollectionAssert.AreEqual(new[] { spline.Id }, outcome.FollowedSplineIds);
            CollectionAssert.IsEmpty(session.RefitAllTerrain(summary));

            UndoAll(session, summary, outcome, Lift(tile, junction, -5));
            session.RefitTerrainAtJunctions(summary, lifted, session.JunctionTerrainPlaces(summary));
            OmsiMapWorkspaceTests.AssertSameFiles(before, OmsiMapWorkspaceTests.WorkspaceFiles(session.Workspace));
            OmsiMapWorkspaceTests.AssertSummaryMatchesFiles(session, summary);
        }

        [Test]
        public void TheHistoryCountsObjectsAndTheSplinesThatFollowedThemApart()
        {
            using (EnglishTexts.Use())
            {
                Assert.AreEqual("the change of 1 object and 4 splines", EditorShellBootstrap.DescribeGroupChange(1, 4));
                Assert.AreEqual("the change of 2 objects and 1 spline", EditorShellBootstrap.DescribeGroupChange(2, 1));
                Assert.AreEqual("the change of 5 objects", EditorShellBootstrap.DescribeGroupChange(5, 0));
            }
        }

        private sealed class GroupPart
        {
            public OmsiSplineChange Change;
            public OmsiTileSummary Tile;
            public OmsiObjectRecord Record;
            public double[] Before;
            public double[] After;
        }

        /// <summary>Writes the values before or after of value parts together, as the shell's ApplyValueParts does.</summary>
        private static void ApplyValues(OmsiMapSession session, List<GroupPart> unit, bool undo)
        {
            session.UpdateObjects(unit.Select(part =>
            {
                var values = undo ? part.Before : part.After;
                return new OmsiObjectValueEdit(part.Tile, part.Record, values[0], values[1], values[2], values[3], values[4], values[5]);
            }).ToList());
        }

        private static double[] Values(OmsiObjectRecord item)
        {
            return new[] { item.LocalX, item.LocalY, item.Height, item.HeadingDegrees, item.PitchDegrees, item.BankDegrees };
        }

        private void UndoAll(OmsiMapSession session, OmsiMapSummary summary, OmsiValueEditOutcome outcome, OmsiObjectValueEdit back)
        {
            session.UpdateObjects(new[] { back });
            for (var index = outcome.RoadChanges.Count - 1; index >= 0; index--)
                session.Undo(summary, outcome.RoadChanges[index]);
        }

        /// <summary>
        /// Under the arms the terrain lies no higher than the road over it, with <paramref name="besideToo"/> also 5 m to either side no more
        /// than a metre above it, and under the junction just below its plane at <paramref name="plane"/>: no column stands under the junction
        /// above the roads.
        /// </summary>
        private static void AssertNoColumn(OmsiMapSession session, OmsiMapSummary summary, double plane, bool besideToo)
        {
            var east = Terrain(session, "tile_1_0.map.terrain");
            var south = Terrain(session, "tile_0_0.map.terrain");
            // Under the east road along y 150 from x 305 (column 1 of tile 1, 0) to 365; beside it a steep ramp meets the junction's slopes.
            for (var column = 1; column <= 13; column++)
                foreach (var row in besideToo ? new[] { 29, 30, 31 } : new[] { 30 })
                {
                    var x = 300.0 + column * 5.0;
                    var height = RoadHeightAt(summary, x, 150.0);
                    // A metre beside the road the terrain may meet the junction's slopes, but no higher than a metre above the road.
                    Assert.LessOrEqual(east[column, row], height + (row == 30 ? 0.2 : 1.0), "Terrain at " + x + ", " + row * 5 + " above the east road at " + height + " m.");
                }
            // Under the south road along x 295 (column 59 of tile 0, 0) from y 80 (row 16) to 140 (row 28).
            for (var row = 16; row <= 27; row++)
                foreach (var column in besideToo ? new[] { 58, 59, 60 } : new[] { 59 })
                {
                    var y = row * 5.0;
                    var height = RoadHeightAt(summary, 295.0, y);
                    Assert.LessOrEqual(south[column, row], height + (column == 59 ? 0.2 : 1.0), "Terrain at " + column * 5 + ", " + y + " above the south road at " + height + " m.");
                }
            // Under the junction at 295, 150.
            Assert.AreEqual(plane - 0.05, south[59, 30], 0.01);
        }

        /// <summary>The height of the road whose centre line passes nearest to a world position, at that position.</summary>
        private static double RoadHeightAt(OmsiMapSummary summary, double x, double y)
        {
            var best = double.MaxValue;
            var height = double.NaN;
            foreach (var tile in summary.Tiles)
                foreach (var spline in tile.Splines)
                    for (var step = 0; step <= 1000; step++)
                    {
                        var along = spline.LengthMeters * step / 1000.0;
                        OmsiMapGeometry.GetLocalFrameAt(spline, along, out var local, out _);
                        var dx = tile.X * summary.TileSizeMeters + local.X - x;
                        var dy = tile.Y * summary.TileSizeMeters + local.Y - y;
                        var distance = dx * dx + dy * dy;
                        if (distance < best)
                        {
                            best = distance;
                            height = spline.HeightAt(along);
                        }
                    }
            return height;
        }

        private static OmsiTerrain Terrain(OmsiMapSession session, string path)
        {
            return OmsiTerrain.Parse(session.Workspace.ReadAllBytes(path));
        }

        private static OmsiObjectValueEdit Lift(OmsiTileSummary tile, OmsiObjectRecord item, double up)
        {
            return new OmsiObjectValueEdit(tile, item, item.LocalX, item.LocalY, item.Height + up, item.HeadingDegrees, item.PitchDegrees, item.BankDegrees);
        }

        private static OmsiSplineEnd End(OmsiMapSummary summary, double x, double y)
        {
            var end = OmsiSplineEnd.FindFree(summary, x, y, 0.05);
            Assert.IsNotNull(end, "A road end at " + x + ", " + y + ".");
            return end;
        }

        private static OmsiSplineRecord Spline(OmsiMapSummary summary, int id)
        {
            return summary.Tiles.SelectMany(tile => tile.Splines).Single(spline => spline.Id == id);
        }

        private static string Describe(OmsiSplineRecord spline)
        {
            return string.Join(" ", new[] { spline.LocalX, spline.LocalY, spline.Height, spline.HeadingDegrees, spline.LengthMeters, spline.RadiusMeters,
                spline.GradientStartPercent, spline.GradientEndPercent }.Select(OmsiNumberFormat.Format));
        }

        private OmsiJunctionCatalog Catalog()
        {
            return new OmsiJunctionCatalog(root);
        }

        private OmsiMapSession OpenProject(string project = "Lift")
        {
            return OmsiMapSession.OpenProject(Path.Combine(mapFolder, "global.cfg"), Path.Combine(root, "Projects", project + ".omproj"));
        }

        /// <summary>
        /// The objects and splines of the summary have the values the files hold, heights and orientation as well; the terrain's lowest and
        /// highest points are checked by <see cref="OmsiMapWorkspaceTests.AssertSummaryMatchesFiles"/>.
        /// </summary>
        private static void AssertRecordsMatchFiles(OmsiMapSession session, OmsiMapSummary summary)
        {
            var fresh = session.Read();
            Assert.AreEqual(fresh.Tiles.Count, summary.Tiles.Count);
            for (var index = 0; index < fresh.Tiles.Count; index++)
            {
                var name = summary.Tiles[index].FileName;
                CollectionAssert.AreEqual(fresh.Tiles[index].Objects.Select(DescribeObject), summary.Tiles[index].Objects.Select(DescribeObject), name);
                CollectionAssert.AreEqual(fresh.Tiles[index].Splines.Select(spline => spline.Id + " " + Describe(spline)),
                    summary.Tiles[index].Splines.Select(spline => spline.Id + " " + Describe(spline)), name);
            }
        }

        private static string DescribeObject(OmsiObjectRecord item)
        {
            return item.Id + " " + item.AssetPath + " " + string.Join(" ", new[] { item.LocalX, item.LocalY, item.Height, item.HeadingDegrees, item.PitchDegrees,
                item.BankDegrees }.Select(OmsiNumberFormat.Format));
        }

        private void WriteUtf16(string relative, params string[] lines)
        {
            var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(string.Join("\r\n", lines) + "\r\n")).ToArray();
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }

        private void WriteFlatTerrain(string relative, float height)
        {
            var bytes = new byte[4 + OmsiTerrain.Resolution * OmsiTerrain.Resolution * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(60), 0, bytes, 0, 4);
            for (var index = 0; index < OmsiTerrain.Resolution * OmsiTerrain.Resolution; index++)
                Buffer.BlockCopy(BitConverter.GetBytes(height), 0, bytes, 4 + index * 4, 4);
            File.WriteAllBytes(Path.Combine(mapFolder, relative), bytes);
        }
    }
}
