using NUnit.Framework;
using OmsiMapEditor.Core.Localization;

namespace OmsiMapEditor.Tests
{
    /// <summary>M8 step 6: only language files go into the player, never state files of developer tools.</summary>
    public sealed class LocaleShippingFilterTests
    {
        [TestCase("languages.json", true)]
        [TestCase("en-US/barui.json", true)]
        [TestCase("de-DE\\status2.json", true)]
        [TestCase("pt-PT/dev.json", true)]
        [TestCase(".omc/state.json", false)]
        [TestCase("en-US/.omc/state.json", false)]
        [TestCase("en-US/.hidden.json", false)]
        [TestCase("README.md", false)]
        [TestCase("en-US/notes.txt", false)]
        [TestCase("en-US/sub/more.json", false)]
        [TestCase("other/file.json", false)]
        [TestCase("notes.json", false)]
        [TestCase("", false)]
        public void OnlyLanguageFilesShip(string path, bool expected)
        {
            Assert.That(LocaleShippingFilter.ShouldShip(path), Is.EqualTo(expected), path);
        }
    }
}
