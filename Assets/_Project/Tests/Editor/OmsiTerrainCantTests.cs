using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiTerrainCantTests
    {
        private const string Strips = "[profile]\r\n0\r\n[profilepnt]\r\n-4\r\n0.15\r\n0\r\n1\r\n[profilepnt]\r\n4\r\n0.15\r\n1\r\n1\r\n";
        private const string HeightProfiles = "[heightprofile]\r\n-4\r\n0\r\n0\r\n0\r\n\r\n[heightprofile]\r\n0\r\n4\r\n0\r\n0\r\n";

        private static float Height(float[] heights, int column, int row)
        {
            return heights[row * OmsiTerrain.Resolution + column];
        }

        private static OmsiSplineProfile Parse(string text)
        {
            return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(text), @"Splines\road.sli");
        }

        [Test]
        public void TheHalfCantWidthComesFromTheFileOrTheReachOfItsHeightProfiles()
        {
            Assert.AreEqual(4.0, Parse(Strips + HeightProfiles).HalfCantWidth.Value, 1e-9);
            Assert.AreEqual(3.0, Parse("[halfcantwidth]\r\n3\r\n" + Strips + HeightProfiles).HalfCantWidth.Value, 1e-9);
            Assert.AreEqual(0.0, Parse(Strips).HalfCantWidth.Value, 1e-9, "A type without height profiles has no cant, as OMSI takes it.");
            Assert.AreEqual(4.0, Parse(Strips + HeightProfiles).Mirrored().HalfCantWidth.Value, 1e-9);
        }

        [Test]
        public void TheGroundFollowsTheCantOfARoadAcrossItsWidth()
        {
            var profile = Parse(Strips + HeightProfiles);
            var plan = OmsiRoadPlanner.Plan(new OmsiRoadRequest
            {
                StartX = 150, StartY = 100, StartHeight = 2, EndX = 150, EndY = 200, EndHeight = 2, HeightMode = OmsiRoadHeightMode.Manual, MaximumPieceLength = 200
            }, null);
            var spline = plan.Pieces[0].AsRecord();
            spline.CantStartPercent = 5.0;
            spline.CantEndPercent = 5.0;
            var road = new OmsiShapedRoad(new OmsiTileSummary(), spline, profile, 300);

            var heights = new float[OmsiTerrain.Resolution * OmsiTerrain.Resolution];
            OmsiTerrainShaping.Shape(heights, 0, 0, 300, new[] { road }, new OmsiTerrainShapeSettings());

            // Heading north the right side is east. A cant of 5 % lowers it by 0.2 m at the edge, 4 m out, and raises the left edge by as much; the
            // ground 1 m outside either edge starts 5 cm below the kerb there. Under the middle nothing changes.
            Assert.AreEqual(2.10, Height(heights, 30, 30), 1e-4);
            Assert.AreEqual(2.10 - 0.20, Height(heights, 31, 30), 1e-4);
            Assert.AreEqual(2.10 + 0.20, Height(heights, 29, 30), 1e-4);
        }
    }
}
