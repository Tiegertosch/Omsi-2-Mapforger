using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiJunctionTests
    {
        private const string Junction = @"Sceneryobjects\Test\junction.sco";

        private string installation;

        [SetUp]
        public void CreateInstallation()
        {
            installation = Path.Combine(Path.GetTempPath(), "mapforge-junctions-" + Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(installation, "Sceneryobjects", "Test");
            Directory.CreateDirectory(folder);
            // Two points: 10 m ahead facing ahead, and 10 m to the left, 0.5 m up, facing left.
            File.WriteAllBytes(Path.Combine(folder, "junction.sco"), Encoding.ASCII.GetBytes(
                "[splinehelper]\r\nSplines\\road.sli\r\n0\r\n0\r\n10\r\n0\r\n\r\n" +
                "[splinehelper]\r\nSplines\\other.sli\r\n-10\r\n0.5\r\n0\r\n-90\r\n"));
        }

        [TearDown]
        public void RemoveInstallation()
        {
            if (Directory.Exists(installation))
                Directory.Delete(installation, true);
        }

        [Test]
        public void PortsTurnWithTheObjectAndCarryItsHeight()
        {
            var catalog = new OmsiJunctionCatalog(installation);
            var tile = new OmsiTileSummary();
            // Facing east: ahead is east, left is north.
            var item = new OmsiObjectRecord { AssetPath = Junction, Id = 5, LocalX = 100, LocalY = 100, Height = 2, HeadingDegrees = 90 };

            var ports = catalog.Ports(tile, item, 300);

            Assert.AreEqual(2, ports.Count);
            Assert.AreEqual(110.0, ports[0].Position.X, 1e-9);
            Assert.AreEqual(100.0, ports[0].Position.Y, 1e-9);
            Assert.AreEqual(2.0, ports[0].Height, 1e-9);
            Assert.AreEqual(90.0, ports[0].OutwardHeadingDegrees, 1e-9);
            Assert.AreEqual(100.0, ports[1].Position.X, 1e-9);
            Assert.AreEqual(110.0, ports[1].Position.Y, 1e-9);
            Assert.AreEqual(2.5, ports[1].Height, 1e-9);
            Assert.AreEqual(0.0, ports[1].OutwardHeadingDegrees, 1e-9);
            Assert.AreEqual(@"Splines\other.sli", ports[1].ProfilePath);

            // A positive pitch lowers the front: the point 10 m ahead sinks by 10 m × sin 10°.
            item.PitchDegrees = 10;
            Assert.AreEqual(2.0 - 10.0 * Math.Sin(10.0 * Math.PI / 180.0), catalog.Ports(tile, item, 300)[0].Height, 1e-9);

            // An object without the file stays known as unresolved and has no points.
            var missing = new OmsiObjectRecord { AssetPath = @"Sceneryobjects\Test\missing.sco" };
            CollectionAssert.IsEmpty(catalog.Ports(tile, missing, 300));
            StringAssert.Contains("not found", catalog.Problem(missing.AssetPath));
            Assert.IsNull(catalog.Problem(Junction));
        }

        [Test]
        public void ChecksTellConnectedPointsFromOnesToReviewInvalidAndUnconnected()
        {
            var catalog = new OmsiJunctionCatalog(installation);
            var summary = new OmsiMapSummary();
            var tile = new OmsiTileSummary { IsAvailable = true };
            summary.Tiles.Add(tile);
            var item = new OmsiObjectRecord { AssetPath = Junction, Id = 5, LocalX = 100, LocalY = 100, Height = 2, HeadingDegrees = 90 };
            tile.Objects.Add(item);
            // Leaving the first point eastwards at its height: connected, with the profile the object names.
            tile.Splines.Add(new OmsiSplineRecord
            {
                AssetPath = @"Splines\road.sli", Id = 20, LocalX = 110, LocalY = 100, Height = 2, EndHeight = 2, HeadingDegrees = 90, LengthMeters = 20
            });
            // Arriving from the north 0.3 m short of the second point.
            var arriving = new OmsiSplineRecord
            {
                AssetPath = @"Splines\road.sli", Id = 21, LocalX = 100, LocalY = 130, Height = 2.5, EndHeight = 2.5, HeadingDegrees = 180, LengthMeters = 19.7
            };
            tile.Splines.Add(arriving);

            var checks = OmsiJunctionCatalog.Check(summary, catalog.Ports(tile, item, 300));

            Assert.AreEqual(OmsiJunctionPortState.Connected, checks[0].State, checks[0].Message);
            Assert.AreEqual(20, checks[0].End.Spline.Id);
            Assert.IsFalse(checks[0].End.AtEnd);
            Assert.AreEqual(OmsiJunctionPortState.NeedsReview, checks[1].State, checks[1].Message);
            StringAssert.Contains("0.30 m", checks[1].Message);

            // The free point can be snapped to; the connected one cannot.
            var ports = catalog.AllPorts(summary);
            Assert.AreSame(item, OmsiJunctionCatalog.FindFree(summary, ports, 100.5, 110, 1.0)?.Object);
            Assert.AreEqual(1, OmsiJunctionCatalog.FindFree(summary, ports, 100.5, 110, 1.0).Index);
            Assert.IsNull(OmsiJunctionCatalog.FindFree(summary, ports, 110.5, 100, 1.0));

            // Reaching the second point exactly but heading into the object is invalid; turned 5° and 0.2 m low it needs review.
            arriving.LengthMeters = 20;
            arriving.LocalY = 90;
            arriving.HeadingDegrees = 0;
            Assert.AreEqual(OmsiJunctionPortState.Invalid, OmsiJunctionCatalog.Check(summary, ports)[1].State);
            // Arriving with heading 185 from 20 m back along it.
            arriving.HeadingDegrees = 185;
            arriving.LocalX = 100 + 20 * Math.Sin(5.0 * Math.PI / 180.0);
            arriving.LocalY = 110 + 20 * Math.Cos(5.0 * Math.PI / 180.0);
            arriving.Height = 2.3;
            arriving.EndHeight = 2.3;
            var turned = OmsiJunctionCatalog.Check(summary, ports)[1];
            Assert.AreEqual(OmsiJunctionPortState.NeedsReview, turned.State, turned.Message);
            Assert.AreEqual(5.0, turned.HeadingOffsetDegrees, 1e-6);
            Assert.AreEqual(-0.2, turned.HeightOffsetMeters, 1e-9);
            StringAssert.Contains("profile road.sli instead of other.sli", turned.Message);

            // Without splines nearby both points are unconnected.
            tile.Splines.Clear();
            Assert.IsTrue(OmsiJunctionCatalog.Check(summary, ports).All(check => check.State == OmsiJunctionPortState.Unconnected));

            // A second junction turned round, its first point on the first one's: they meet face to face and connect without a spline.
            var facing = new OmsiObjectRecord { AssetPath = Junction, Id = 6, LocalX = 120, LocalY = 100, Height = 2, HeadingDegrees = 270 };
            tile.Objects.Add(facing);
            var all = catalog.AllPorts(summary);
            var met = OmsiJunctionCatalog.Check(summary, all);
            Assert.AreEqual(OmsiJunctionPortState.Connected, met[0].State, met[0].Message);
            Assert.AreSame(facing, met[0].Partner.Object);
            Assert.AreEqual(OmsiJunctionPortState.Connected, met[2].State, met[2].Message);
            Assert.IsNull(OmsiJunctionCatalog.FindFree(summary, all, 110.2, 100, 1.0));
        }
    }
}
