using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    public sealed class OmsiChronoLinesTests
    {
        private string testRoot;
        private string original;
        private OmsiMapSession session;

        [SetUp]
        public void CreateMap()
        {
            testRoot = Path.Combine(Path.GetTempPath(), "mapforge-chronolines-" + Guid.NewGuid().ToString("N"));
            original = Path.Combine(testRoot, "OMSI 2", "maps", "Testmap");
            Directory.CreateDirectory(original);
            File.WriteAllBytes(Path.Combine(original, "global.cfg"),
                OmsiTextFile.Encode("[name]\r\nTestmap\r\n\r\n[NextIDCode]\r\n20\r\n\r\n[map]\r\n0\r\n0\r\ntile_0_0.map\r\n", OmsiTextEncoding.Utf16LittleEndian));
            File.WriteAllBytes(Path.Combine(original, "tile_0_0.map"), OmsiTextFile.Encode("[version]\r\n14\r\n\r\n", OmsiTextEncoding.Utf16LittleEndian));
            // As the stock variants write it: the settings in Windows-1252, the lines to switch off counted first.
            var folder = Path.Combine(original, "Chrono", "0550_LineE");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "Chrono.cfg"), OmsiTextFile.Encode(
                "\r\n[startdate]\r\n19900101\r\n\r\n[deactivate_lines]\r\n2\r\nE\r\nM32 & N32\r\n\r\n[ticketpack]\r\nTicketPacks\\Berlin_91\\Berlin_91.otp\r\n\r\n",
                OmsiTextEncoding.Windows1252));
            session = OmsiMapSession.OpenProject(Path.Combine(original, "global.cfg"), Path.Combine(testRoot, "Projects", "Test.omproj"));
        }

        [TearDown]
        public void DeleteMap()
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, true);
        }

        [Test]
        public void TheReaderTakesTheLinesAFileSwitchesOff()
        {
            var variant = OmsiChrono.ReadVariants(session.Workspace).Single();

            CollectionAssert.AreEqual(new[] { "E", "M32 & N32" }, variant.DeactivatedLines);
        }

        [Test]
        public void WritingTheSameLinesLeavesTheFileAsItWas()
        {
            var variant = Edit();
            var before = session.Workspace.ReadAllBytes(@"Chrono\0550_LineE\Chrono.cfg");

            var changed = session.SetDeactivatedLines(variant, new[] { "E", "M32 & N32" });

            Assert.IsFalse(changed);
            CollectionAssert.AreEqual(before, session.Workspace.ReadAllBytes(@"Chrono\0550_LineE\Chrono.cfg"));
        }

        [Test]
        public void ChangingTheLinesKeepsTheRestOfTheSettingsAndTheirEncoding()
        {
            var variant = Edit();

            Assert.IsTrue(session.SetDeactivatedLines(variant, new[] { "E", "5 & 5N", "X10" }));

            var bytes = session.Workspace.ReadAllBytes(@"Chrono\0550_LineE\Chrono.cfg");
            var text = OmsiTextFile.Decode(bytes);
            StringAssert.Contains("[startdate]\r\n19900101\r\n", text);
            StringAssert.Contains("[ticketpack]\r\nTicketPacks\\Berlin_91\\Berlin_91.otp\r\n", text);
            StringAssert.Contains("[deactivate_lines]\r\n3\r\nE\r\n5 & 5N\r\nX10\r\n", text);
            Assert.AreEqual(1, text.Split(new[] { "[deactivate_lines]" }, StringSplitOptions.None).Length - 1);
            // Windows-1252 as before, and the reader says the same.
            Assert.AreNotEqual(0xFF, bytes[0]);
            CollectionAssert.AreEqual(new[] { "E", "5 & 5N", "X10" }, OmsiChrono.ReadVariants(session.Workspace).Single().DeactivatedLines);
            CollectionAssert.AreEqual(new[] { "E", "5 & 5N", "X10" }, variant.DeactivatedLines);
        }

        [Test]
        public void SwitchingEveryLineBackOnTakesTheBlockAwayAndAnotherVariantCanStartFromNothing()
        {
            var variant = Edit();
            session.SetDeactivatedLines(variant, new string[0]);

            var text = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(@"Chrono\0550_LineE\Chrono.cfg"));
            StringAssert.DoesNotContain("deactivate_lines", text);
            StringAssert.Contains("[ticketpack]", text);
            Assert.IsEmpty(OmsiChrono.ReadVariants(session.Workspace).Single().DeactivatedLines);

            var made = session.CreateChronoVariant("Own", new DateTime(1993, 5, 1), null);
            var own = Edit(made.Name);
            session.SetDeactivatedLines(own, new[] { "76" });
            var written = OmsiTextFile.Decode(session.Workspace.ReadAllBytes(Path.Combine(made.Folder, "Chrono.cfg")));
            StringAssert.Contains("[startdate]\r\n19930501\r\n", written);
            StringAssert.Contains("[deactivate_lines]\r\n1\r\n76\r\n", written);
            CollectionAssert.AreEqual(new[] { "76" }, OmsiChrono.ReadVariants(session.Workspace).Single(item => item.Name == made.Name).DeactivatedLines);
        }

        [Test]
        public void OnlyTheEditedVariantAcceptsLinesAndOneNameIsOneLineOfText()
        {
            var variant = OmsiChrono.ReadVariants(session.Workspace).Single();
            Assert.Throws<InvalidOperationException>(() => session.SetDeactivatedLines(variant, new[] { "E" }));
            var edited = Edit();
            Assert.Throws<ArgumentException>(() => session.SetDeactivatedLines(edited, new[] { "two\r\nlines" }));
        }

        private OmsiChronoVariant Edit(string name = "0550_LineE")
        {
            var variants = OmsiChrono.ReadVariants(session.Workspace);
            var variant = variants.Single(item => item.Name == name);
            var day = variant.FirstDay ?? DateTime.Today;
            var upTo = variants.FindAll(v => string.CompareOrdinal(v.Name, variant.Name) <= 0 && v.IsActiveOn(day));
            OmsiChrono.Overlay(session.Read(), session.Workspace, upTo, out var report);
            session.EditChronoVariant(report.Variants.Single(v => v.Name == variant.Name), report);
            return report.Variants.Single(v => v.Name == variant.Name);
        }
    }
}
