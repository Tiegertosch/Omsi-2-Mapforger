using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiPathProfilesTests
    {
        private const string Street =
            "Invisible Street\r\n\r\n[onlyeditor]\r\n\r\n[texture]\r\nhelper_path_cars_1.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-1.5\r\n0.1\r\n0\r\n-0.1\r\n\r\n" +
            "[profilepnt]\r\n1.5\r\n0.1\r\n1\r\n-0.1\r\n\r\n[path]\r\n0\r\n0\r\n0.100\r\n3\r\n0\r\n";

        private const string Sidewalk =
            "Invisible Sidewalk\r\n\r\n[onlyeditor]\r\n\r\n[texture]\r\nhelper_path_cars_1.bmp\r\n\r\n[profile]\r\n0\r\n\r\n[profilepnt]\r\n-1.0\r\n0.1\r\n0\r\n-0.1\r\n\r\n" +
            "[profilepnt]\r\n1.0\r\n0.1\r\n1\r\n-0.1\r\n\r\n[path]\r\n1\r\n0\r\n0.100\r\n2\r\n2\r\n";

        private static OmsiSplineProfile Parse(string text)
        {
            return OmsiSplineProfile.Parse(Encoding.ASCII.GetBytes(text), @"Splines\x.sli");
        }

        [Test]
        public void AStreetPathGetsTheWidthTheStripAndTheDirectionYouChoose()
        {
            var profile = Parse(OmsiPathProfiles.Compose(Street, 2.5, true));
            Assert.AreEqual(2.5, OmsiPathProfiles.WidthOf(profile).Value, 1e-9);
            Assert.IsTrue(OmsiPathProfiles.IsBothWays(profile));
            Assert.IsTrue(profile.EditorOnly);
            Assert.AreEqual(0, OmsiPathSnapping.PathToolKind(profile));
            // The helper strip is as wide as the lane.
            var text = OmsiPathProfiles.Compose(Street, 2.5, false);
            StringAssert.Contains("[profilepnt]\r\n-1.25\r\n", text);
            StringAssert.Contains("[profilepnt]\r\n1.25\r\n", text);
            Assert.IsFalse(OmsiPathProfiles.IsBothWays(Parse(text)));
        }

        [Test]
        public void ASidewalkPathKeepsItsDirectionAndChangesOnlyItsWidth()
        {
            var profile = Parse(OmsiPathProfiles.Compose(Sidewalk, 4.0, false));
            Assert.AreEqual(4.0, OmsiPathProfiles.WidthOf(profile).Value, 1e-9);
            Assert.IsTrue(OmsiPathProfiles.IsBothWays(profile));
            Assert.AreEqual(1, OmsiPathSnapping.PathToolKind(profile));
        }

        [Test]
        public void ChoosingAnotherWidthOfAMadeTypeDoesNotPileUpTitles()
        {
            var once = OmsiPathProfiles.Compose(Street, 2.5, true);
            var twice = OmsiPathProfiles.Compose(once, 4.0, false);
            StringAssert.StartsWith("Invisible Street (4 m)\r\n", twice);
            StringAssert.DoesNotContain("2.5 m", twice);
        }

        [Test]
        public void ThePathTypeIsNamedAfterItsBaseWidthAndDirection()
        {
            Assert.AreEqual("invis_street_2_5m", OmsiPathProfiles.VariantName("invis_street", 2.5, false));
            Assert.AreEqual("invis_street_3m_2way", OmsiPathProfiles.VariantName("invis_street", 3.0, true));
            Assert.AreEqual("invis_street", OmsiPathProfiles.BaseName("invis_street_2_5m_2way"));
            Assert.AreEqual("invis_street", OmsiPathProfiles.BaseName("invis_street_4m"));
            Assert.AreEqual("invis_street", OmsiPathProfiles.BaseName("invis_street"));
        }

        [Test]
        public void ANewPathTypeIsWrittenOnceBelowTheProjectAndTheProfileItselfComesBackForItsOwnValues()
        {
            var root = Path.Combine(Path.GetTempPath(), "mapforge-paths-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Splines"));
                File.WriteAllText(Path.Combine(root, "Splines", "invis_street.sli"), Street, Encoding.ASCII);
                // Its own width and direction: no file.
                Assert.AreEqual(@"Splines\invis_street.sli", OmsiPathProfiles.Create(root, "Test", @"Splines\invis_street.sli", 3.0, false));
                var made = OmsiPathProfiles.Create(root, "Test", @"Splines\invis_street.sli", 2.5, true);
                Assert.AreEqual(@"Splines\Mapforge\Test\invis_street_2_5m_2way.sli", made);
                var profile = OmsiSplineProfile.Parse(File.ReadAllBytes(Path.Combine(root, made)), made);
                Assert.AreEqual(2.5, OmsiPathProfiles.WidthOf(profile).Value, 1e-9);
                Assert.IsTrue(OmsiPathProfiles.IsBothWays(profile));
                // Again from the made type: its name keeps the base, and the same file comes back.
                Assert.AreEqual(made, OmsiPathProfiles.Create(root, "Test", made, 2.5, true));
                Assert.AreEqual(@"Splines\Mapforge\Test\invis_street_4m.sli", OmsiPathProfiles.Create(root, "Test", made, 4.0, false));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
