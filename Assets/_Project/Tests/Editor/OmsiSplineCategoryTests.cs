using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiSplineCategoryTests
    {
        private static OmsiSplineProfile Profile(string lanes, string strips = null)
        {
            strips = strips ?? "[texture]\r\nroad.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n1\r\n";
            return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(strips + lanes), @"Splines\Test\x.sli");
        }

        private static string Lane(int kind, double x, double width, int direction)
        {
            return "[path]\r\n" + kind + "\r\n" + x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n0.1\r\n" +
                   width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n" + direction + "\r\n";
        }

        [Test]
        public void ProfilesAreSortedIntoTabsByTheirLanes()
        {
            Assert.AreEqual(OmsiSplineCategory.TwoLanes, OmsiSplineCategories.Of(Profile(Lane(0, -2, 3.5, 1) + Lane(0, 2, 3.5, 0))));
            Assert.AreEqual(OmsiSplineCategory.OneWay, OmsiSplineCategories.Of(Profile(Lane(0, -2, 3.5, 0) + Lane(0, 2, 3.5, 0))));
            Assert.AreEqual(OmsiSplineCategory.FourLanes,
                OmsiSplineCategories.Of(Profile(Lane(0, -3, 2, 1) + Lane(0, -1, 2, 1) + Lane(0, 1, 2, 0) + Lane(0, 3, 2, 0))));
            Assert.AreEqual(OmsiSplineCategory.SixLanes,
                OmsiSplineCategories.Of(Profile(Lane(0, -3.5, 1.4, 1) + Lane(0, -2, 1.4, 1) + Lane(0, -0.6, 1.4, 1) + Lane(0, 0.6, 1.4, 0) + Lane(0, 2, 1.4, 0))));
            // A gap between lanes running opposite ways with a raised strip between is a median.
            var median = "[texture]\r\nroad.bmp\r\n[texture]\r\ngrass.bmp\r\n" +
                         "[profile]\r\n0\r\n[profilepnt]\r\n-8\r\n0.1\r\n0\r\n1\r\n[profilepnt]\r\n-1\r\n0.1\r\n1\r\n1\r\n" +
                         "[profile]\r\n1\r\n[profilepnt]\r\n-1\r\n0.25\r\n0\r\n1\r\n[profilepnt]\r\n1\r\n0.25\r\n1\r\n1\r\n" +
                         "[profile]\r\n0\r\n[profilepnt]\r\n1\r\n0.1\r\n0\r\n1\r\n[profilepnt]\r\n8\r\n0.1\r\n1\r\n1\r\n";
            Assert.AreEqual(OmsiSplineCategory.Median, OmsiSplineCategories.Of(Profile(Lane(0, -4.5, 3.5, 1) + Lane(0, 4.5, 3.5, 0), median)));
            Assert.AreEqual(OmsiSplineCategory.Rail, OmsiSplineCategories.Of(Profile("[path]\r\n2\r\n0\r\n0.449\r\n0\r\n2\r\n")));
            Assert.AreEqual(OmsiSplineCategory.Path, OmsiSplineCategories.Of(Profile(Lane(1, 0, 2, 2))));
            Assert.AreEqual(OmsiSplineCategory.Other, OmsiSplineCategories.Of(Profile(string.Empty)));
            Assert.AreEqual("3-4 lanes", OmsiSplineCategories.Name(OmsiSplineCategory.FourLanes));
        }

        [Test]
        public void AnUnreadablePathKeepsItsPlaceWithoutCountingAsALaneForVehicles()
        {
            // A commented explanation opens with [path] in some rail profiles; the rail path follows.
            var rail = Profile("[path]\r\n{type}\r\n{offsetX}\r\n{offsetZ}\r\n{width}\r\n\r\n[path]\r\n2\r\n0\r\n0.449\r\n0\r\n2\r\n");

            Assert.AreEqual(2, rail.Lanes.Count);
            Assert.AreEqual(OmsiSplineProfile.UnreadableLane, rail.Lanes[0].Kind);
            Assert.AreEqual(OmsiSplineCategory.Rail, OmsiSplineCategories.Of(rail));
            Assert.IsFalse(OmsiRoadJunctions.IsRoad(rail));
        }

        [Test]
        public void APreviewShowsTheTopSurfaceInItsTextureWithArrowsForTheLanes()
        {
            // Asphalt in the middle, raised pavements beside it, in two plain textures.
            var profile = Profile(Lane(0, -2, 3.5, 1) + Lane(0, 2, 3.5, 0),
                "[texture]\r\nside.bmp\r\n[texture]\r\nroad.bmp\r\n" +
                "[profile]\r\n0\r\n[profilepnt]\r\n-6\r\n0.25\r\n0\r\n1\r\n[profilepnt]\r\n-4\r\n0.25\r\n1\r\n1\r\n" +
                "[profile]\r\n1\r\n[profilepnt]\r\n-4\r\n0.1\r\n0\r\n1\r\n[profilepnt]\r\n4\r\n0.1\r\n1\r\n1\r\n" +
                "[profile]\r\n0\r\n[profilepnt]\r\n4\r\n0.25\r\n0\r\n1\r\n[profilepnt]\r\n6\r\n0.25\r\n1\r\n1\r\n");
            var red = new OmsiImage(1, 1, 4, new byte[] { 200, 0, 0, 255 });
            var blue = new OmsiImage(1, 1, 4, new byte[] { 0, 0, 200, 255 });

            var pixels = OmsiSplineThumbnail.Render(profile, path => path.EndsWith("side.bmp") ? red : blue, 52, 40);

            byte[] At(int x, int y) => pixels.Skip((y * 52 + x) * 4).Take(4).ToArray();
            // 12 m over 48 pixels: a quarter metre each. The pavement on the left, the road beside it, nothing in the margin.
            CollectionAssert.AreEqual(new byte[] { 200, 0, 0, 255 }, At(4, 5));
            CollectionAssert.AreEqual(new byte[] { 0, 0, 200, 255 }, At(14, 5));
            Assert.AreEqual(0, At(0, 5)[3]);
            // The lane 2 m right of the middle points up with a white arrow; the one left of it down.
            CollectionAssert.AreEqual(new byte[] { 255, 255, 255, 255 }, At(34, 20));
            CollectionAssert.AreEqual(new byte[] { 255, 255, 255, 255 }, At(18, 20));
            CollectionAssert.AreEqual(new byte[] { 0, 0, 200, 255 }, At(26, 20));
        }
    }
}
