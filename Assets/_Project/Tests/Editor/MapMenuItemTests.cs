using System.IO;
using NUnit.Framework;
using OmsiMapEditor.UI;

namespace OmsiMapEditor.Tests
{
    /// <summary>M8 step 5, rest: menu entries that do nothing without a map are greyed out until one is loaded.</summary>
    public sealed class MapMenuItemTests
    {
        [Test]
        public void EntriesNeedingAMapAreUsableOnlyWithOne()
        {
            Assert.That(EditorShellBootstrap.MapMenuItemEnabled(false), Is.False);
            Assert.That(EditorShellBootstrap.MapMenuItemEnabled(true), Is.True);
        }

        [Test]
        public void EveryListedEntryExistsInTheShellAndRoadBuilderIsAmongThem()
        {
            var folder = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(folder, "Assets", "_Project", "UI", "Resources", "EditorShell.uxml")))
                folder = Path.GetDirectoryName(folder);
            var uxml = File.ReadAllText(Path.Combine(folder, "Assets", "_Project", "UI", "Resources", "EditorShell.uxml"));
            foreach (var name in EditorShellBootstrap.MapMenuItemNames)
                StringAssert.Contains("name=\"" + name + "\"", uxml, name + " is missing in EditorShell.uxml.");
            Assert.That(EditorShellBootstrap.MapMenuItemNames, Does.Contain("road-builder-menu-item"));
            Assert.That(EditorShellBootstrap.MapMenuItemNames, Does.Not.Contain("inspector-menu-item"));
        }
    }
}
