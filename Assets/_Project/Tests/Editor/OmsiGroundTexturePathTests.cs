using System.IO;
using NUnit.Framework;
using OmsiMapEditor.OMSI;

namespace OmsiMapEditor.Tests
{
    /// <summary>Which files can be taken over as a ground texture and how they are written into the map (ADR 091).</summary>
    public sealed class OmsiGroundTexturePathTests
    {
        private static readonly string Installation = Path.Combine(Path.GetTempPath(), "OMSI 2");

        [Test]
        public void TextureFolderFileBecomesLowerCaseRelativeWithBackslashes()
        {
            var ok = OmsiGroundTexturePath.TryMakeRelative(Installation, Path.Combine(Installation, "Texture", "Gras.BMP"), out var relative, out var problem);
            Assert.That(ok, Is.True);
            Assert.That(problem, Is.EqualTo(OmsiGroundTexturePathProblem.None));
            Assert.That(relative, Is.EqualTo(@"texture\Gras.BMP"));
        }

        [Test]
        public void SubfolderAndOtherFoldersAreKept()
        {
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(Installation, Path.Combine(Installation, "maps", "Town", "texture", "a.dds"), out var relative, out _), Is.True);
            Assert.That(relative, Is.EqualTo(@"maps\Town\texture\a.dds"));
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(Installation + Path.DirectorySeparatorChar, Path.Combine(Installation, "Texture", "Sub", "b.tga"), out relative, out _), Is.True);
            Assert.That(relative, Is.EqualTo(@"texture\Sub\b.tga"));
        }

        [Test]
        public void FileOutsideTheInstallationIsRefused()
        {
            var outside = Path.Combine(Path.GetTempPath(), "OMSI 2 copy", "Texture", "gras.bmp");
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(Installation, outside, out var relative, out var problem), Is.False);
            Assert.That(problem, Is.EqualTo(OmsiGroundTexturePathProblem.OutsideInstallation));
            Assert.That(relative, Is.Null);
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(Installation, Path.Combine(Installation, "..", "gras.bmp"), out _, out problem), Is.False);
            Assert.That(problem, Is.EqualTo(OmsiGroundTexturePathProblem.OutsideInstallation));
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(Installation, Installation, out _, out problem), Is.False);
        }

        [Test]
        public void UnreadableFormatAndMissingInstallationAreRefused()
        {
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(Installation, Path.Combine(Installation, "Texture", "a.txt"), out _, out var problem), Is.False);
            Assert.That(problem, Is.EqualTo(OmsiGroundTexturePathProblem.UnsupportedType));
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(null, Path.Combine(Installation, "Texture", "a.bmp"), out _, out problem), Is.False);
            Assert.That(problem, Is.EqualTo(OmsiGroundTexturePathProblem.NoInstallation));
            Assert.That(OmsiGroundTexturePath.TryMakeRelative(Installation, "", out _, out problem), Is.False);
        }

        [Test]
        public void StartFolderIsTheLastFolderThenTheTextureFolderThenNothing()
        {
            var last = Path.Combine(Installation, "maps", "Town", "texture");
            var texture = Path.Combine(Installation, "Texture");
            Assert.That(OmsiGroundTexturePath.ChooseStartFolder(last, Installation, _ => true), Is.EqualTo(last));
            Assert.That(OmsiGroundTexturePath.ChooseStartFolder(last, Installation, path => path == texture), Is.EqualTo(texture));
            Assert.That(OmsiGroundTexturePath.ChooseStartFolder(null, Installation, _ => true), Is.EqualTo(texture));
            Assert.That(OmsiGroundTexturePath.ChooseStartFolder("", Installation, _ => true), Is.EqualTo(texture));
            Assert.That(OmsiGroundTexturePath.ChooseStartFolder(last, null, path => path == last), Is.EqualTo(last));
            Assert.That(OmsiGroundTexturePath.ChooseStartFolder(null, null, _ => true), Is.Null);
            Assert.That(OmsiGroundTexturePath.ChooseStartFolder(last, Installation, _ => false), Is.Null);
        }
    }
}
