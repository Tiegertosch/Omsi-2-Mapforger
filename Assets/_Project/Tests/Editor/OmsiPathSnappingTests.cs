using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiPathSnappingTests
    {
        private static OmsiMapPath Path(int kind, params double[] coordinates)
        {
            var points = new List<OmsiMapPoint>();
            for (var index = 0; index + 1 < coordinates.Length; index += 2)
                points.Add(new OmsiMapPoint(coordinates[index], coordinates[index + 1]));
            return new OmsiMapPath { Kind = kind, Points = points.ToArray() };
        }

        [Test]
        public void ThePointerSnapsToTheNearestPointOfAPathOfTheKindWithTheDirectionThere()
        {
            var pavement = Path(1, 0, 0, 0, 20);
            var carriageway = Path(0, 3, 0, 3, 20);
            var snap = OmsiPathSnapping.Nearest(new[] { carriageway, pavement }, 1, 1.0, 7.5, 2.0);

            Assert.AreSame(pavement, snap.Path);
            Assert.AreEqual(0.0, snap.X, 1e-9);
            Assert.AreEqual(7.5, snap.Y, 1e-9);
            Assert.AreEqual(1.0, snap.DistanceMeters, 1e-9);
            // The path runs north.
            Assert.AreEqual(0.0, snap.HeadingDegrees, 1e-9);
        }

        [Test]
        public void ThePointSticksToTheEndsOfThePathAndNothingFartherThanTheReachIsFound()
        {
            var path = Path(1, 0, 0, 10, 0);
            var past = OmsiPathSnapping.Nearest(new[] { path }, 1, 12.0, 0.5, 3.0);
            Assert.AreEqual(10.0, past.X, 1e-9);
            Assert.AreEqual(0.0, past.Y, 1e-9);
            Assert.AreEqual(90.0, past.HeadingDegrees, 1e-9);

            Assert.IsNull(OmsiPathSnapping.Nearest(new[] { path }, 1, 5.0, 4.0, 3.0));
            Assert.IsNull(OmsiPathSnapping.Nearest(new[] { path }, 0, 5.0, 0.5, 3.0));
        }

        [Test]
        public void TheNearerOfTwoPathsWins()
        {
            var far = Path(1, 0, 0, 10, 0);
            var near = Path(1, 0, 2, 10, 2);
            var snap = OmsiPathSnapping.Nearest(new[] { far, near }, 1, 5.0, 1.5, 3.0);
            Assert.AreSame(near, snap.Path);
            Assert.AreEqual(2.0, snap.Y, 1e-9);
        }

        [Test]
        public void OnlyAnEditorOnlyProfileWithASingleLaneIsAPathToolProfile()
        {
            // As OMSI's invisible sidewalk and street are: one path each, shown only in the editor.
            const string sidewalk = "Invisible\r\n[onlyeditor]\r\n[texture]\r\nhelper.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n-1.5\r\n0.1\r\n0\r\n-0.1\r\n[profilepnt]\r\n1.5\r\n0.1\r\n1\r\n-0.1\r\n[path]\r\n1\r\n0\r\n-2\r\n2\r\n2\r\n";
            const string street = "Invisible\r\n[onlyeditor]\r\n[texture]\r\nhelper.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n-1.5\r\n0.1\r\n0\r\n-0.1\r\n[profilepnt]\r\n1.5\r\n0.1\r\n1\r\n-0.1\r\n[path]\r\n0\r\n0\r\n0.1\r\n3\r\n0\r\n";
            const string visible = "Road\r\n[texture]\r\nroad.bmp\r\n[profile]\r\n0\r\n[profilepnt]\r\n-1.5\r\n0.1\r\n0\r\n-0.1\r\n[profilepnt]\r\n1.5\r\n0.1\r\n1\r\n-0.1\r\n[path]\r\n1\r\n0\r\n-2\r\n2\r\n2\r\n";
            Assert.AreEqual(1, OmsiPathSnapping.PathToolKind(OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(sidewalk), @"Splines\invis_sidewalk.sli")));
            Assert.AreEqual(0, OmsiPathSnapping.PathToolKind(OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(street), @"Splines\invis_street.sli")));
            Assert.IsNull(OmsiPathSnapping.PathToolKind(OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(visible), @"Splines\road.sli")));
            Assert.IsNull(OmsiPathSnapping.PathToolKind(null));
        }
    }
}
