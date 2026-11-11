using System.Linq;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>[path_2], the rail keywords, [scaleTexByLength], the texture offset, mirrored v, skew and [patchwork_chain] of spline profiles (ADR 090).</summary>
    public sealed class OmsiSplineSurfaceExtrasTests
    {
        private static OmsiSplineProfile Profile(string afterTexture = "", string tail = "")
        {
            var text = "[texture]\r\nx.bmp\r\n" + afterTexture + "\r\n[profile]\r\n0\r\n[profilepnt]\r\n-1\r\n0\r\n0\r\n0.5\r\n[profilepnt]\r\n1\r\n0\r\n1\r\n0.5\r\n" + tail;
            return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(text), @"Splines\x.sli");
        }

        private static OmsiSurfaceMesh Single(OmsiSplineRecord spline, OmsiSplineProfile profile)
        {
            return OmsiSplineSurface.Build(spline, profile).Single();
        }

        private static double V(OmsiSurfaceMesh mesh, int vertex)
        {
            return mesh.Uvs[vertex * 2 + 1];
        }

        [Test]
        public void APath2LaneCountsAsALaneInTheOrderItStandsAndTheRailKeywordsAreKept()
        {
            var profile = Profile("", "[path]\r\n0\r\n-2\r\n0.1\r\n3\r\n1\r\n[path_2]\r\n0\r\n2\r\n0.1\r\n3\r\n0\r\n-0.05\r\n[path]\r\n1\r\n0\r\n0.1\r\n2\r\n2\r\n" +
                                  "[rail_enh]\r\n200\r\n0\r\n30\r\n0.02\r\n1\r\n2\r\n3\r\n4\r\n[third_rail]\r\n1\r\n2\r\n3\r\n4\r\n5\r\n6\r\n");
            Assert.AreEqual(3, profile.Lanes.Count);
            Assert.AreEqual(2.0, profile.Lanes[1].X, 1e-9);
            Assert.AreEqual(3.0, profile.Lanes[1].Width, 1e-9);
            Assert.AreEqual(0, profile.Lanes[1].Direction);
            Assert.AreEqual(1, profile.Lanes[2].Kind);
            Assert.AreEqual(1, profile.RailEnhancements.Count);
            Assert.AreEqual(200.0, profile.RailEnhancements[0][0], 1e-9);
            Assert.AreEqual(8, profile.RailEnhancements[0].Length);
            Assert.AreEqual(1, profile.ThirdRails.Count);
            Assert.AreEqual(6, profile.ThirdRails[0].Length);
        }

        [Test]
        public void ATextureThatScalesByLengthRunsItsVOverTheWholeSplineNotPerMetre()
        {
            var spline = new OmsiSplineRecord { HeadingDegrees = 0, LengthMeters = 20 };
            Assert.AreEqual(10.0, V(Single(spline, Profile()), 2), 1e-9);
            var scaled = Single(spline, Profile("[scaleTexByLength]"));
            Assert.AreEqual(0.0, V(scaled, 0), 1e-9);
            Assert.AreEqual(0.5, V(scaled, 2), 1e-9);
        }

        [Test]
        public void TheTextureOffsetOfAChainShiftsVAndAMirroredSplineTurnsItRound()
        {
            var profile = Profile();
            // 5 m into the chain: 2.5 repeats; the whole repeat is left out, as it changes nothing.
            var offset = Single(new OmsiSplineRecord { HeadingDegrees = 0, LengthMeters = 20, ChainOffset = 5.0 }, profile);
            Assert.AreEqual(0.5, V(offset, 0), 1e-9);
            Assert.AreEqual(10.5, V(offset, 2), 1e-9);
            var mirrored = Single(new OmsiSplineRecord { HeadingDegrees = 0, LengthMeters = 20, IsMirrored = true }, profile);
            Assert.AreEqual(-10.0, V(mirrored, 2), 1e-9);
        }

        [Test]
        public void ASkewShiftsACrossSectionPointAlongTheSplineByItsLateralDistance()
        {
            var spline = new OmsiSplineRecord { HeadingDegrees = 0, LengthMeters = 10, LocalX = 100, LocalY = 50, SkewStart = 1.0, SkewEnd = 1.0 };
            var mesh = Single(spline, Profile());
            // North-bound: the left point (x -1) lies one metre behind its cross-section, the right one (x +1) one metre ahead.
            Assert.AreEqual(49.0, mesh.Positions[2], 1e-9);
            Assert.AreEqual(51.0, mesh.Positions[5], 1e-9);
            // And the texture follows the shifted distance.
            Assert.AreEqual(0.5 * -1.0, V(mesh, 0), 1e-9);
        }

        [Test]
        public void APatchworkChainLaysEveryStretchOfTheSplineWithTheOrderOpenOmsiDraws()
        {
            // Chain A-B-A, one part each way: the stretches alternate part 1 (A to B) and part 2 (B to A), ending on the first letter.
            var patchwork = "[patchwork_chain]\r\n10\r\nABA\r\n11\r\n00\r\n";
            var profile = Profile(patchwork);
            var spline = new OmsiSplineRecord { Id = 42, HeadingDegrees = 0, LengthMeters = 35 };
            var order = OmsiSplinePatchwork.For(profile, spline, 1);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, order.Order);
            Assert.AreEqual(2, order.Parts);

            var mesh = Single(spline, profile);
            // Every stretch has vertices of its own: 4 stretches (per-stretch stations) of 4 vertices each.
            Assert.AreEqual(order.Stations * 4, mesh.VertexCount);
            // The first stretch shows the first half of the texture: v from 0 to 0.5; the second the other half: from 0.5 to 1.
            var perStretch = order.PerStretch;
            Assert.AreEqual(0.0, V(mesh, 0), 1e-9);
            Assert.AreEqual(0.5, V(mesh, (perStretch - 1) * 4 + 2), 1e-9);
            Assert.AreEqual(0.5, V(mesh, perStretch * 4), 1e-9);
            Assert.AreEqual(1.0, V(mesh, (2 * perStretch - 1) * 4 + 2), 1e-9);
        }

        [Test]
        public void AChainOMSIWouldLeaveOutIsLeftOutHere()
        {
            // Two letters but no weight for the part: not taken; the texture runs per metre.
            var profile = Profile("[patchwork_chain]\r\n10\r\nAA\r\n\r\n0\r\n");
            Assert.IsNull(OmsiSplinePatchwork.For(profile, new OmsiSplineRecord { Id = 1, LengthMeters = 30 }, 1));
            Assert.IsFalse(new OmsiPatchworkChain { SegmentLength = 10, Chain = "AB", Weights = "11", Invertable = "00" }.IsValid);
            Assert.IsFalse(new OmsiPatchworkChain { SegmentLength = 10, Chain = "AB", Weights = "1", Invertable = "2" }.IsValid);
            Assert.IsTrue(new OmsiPatchworkChain { SegmentLength = 10, Chain = "AB", Weights = "1", Invertable = "1" }.IsValid);
        }
    }
}
