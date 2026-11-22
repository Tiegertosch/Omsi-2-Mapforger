using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// M7 step 0: keeps the two numbers M7 brings down from rising again. Both are ceilings that may only fall; M7 step 6 sets them to the
    /// acceptance (no hand-built <c>dialog-overlay</c> outside the dialog factory, under 20 inline sizes). Lines marked
    /// <c>// computed</c> (values calculated at run time, such as progress in percent or window geometry) are exempt.
    /// </summary>
    public sealed class DialogCodeRulesTests
    {
        /// <summary>Constructions of <c>dialog-overlay</c> in Scripts/UI (*.cs) and EditorShell.uxml; today none in code and 6 in the UXML.</summary>
        private const int DialogOverlayCeiling = 6;

        /// <summary>
        /// Inline size, spacing and flex assignments (<c>style.width/height/min/max, margin*, padding*, flexBasis/Grow/Shrink</c>) in
        /// Scripts/UI. Wide count of the owner's decision 5A; today 64.
        /// </summary>
        private const int InlineSizeCeiling = 64;

        private static readonly Regex InlineSize = new Regex(
            @"style\.(width|height|minWidth|minHeight|maxWidth|maxHeight|margin[A-Za-z]*|padding[A-Za-z]*|flexBasis|flexGrow|flexShrink)\b",
            RegexOptions.Compiled);

        private static string ProjectRoot()
        {
            var folder = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(folder))
            {
                if (Directory.Exists(Path.Combine(folder, "Assets", "_Project", "Scripts", "UI")))
                    return folder;
                folder = Path.GetDirectoryName(folder);
            }
            throw new DirectoryNotFoundException("Assets/_Project/Scripts/UI was not found from " + Directory.GetCurrentDirectory());
        }

        [Test]
        public void HandBuiltDialogOverlaysDoNotGrow()
        {
            var root = ProjectRoot();
            var count = 0;
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Assets", "_Project", "Scripts", "UI"), "*.cs", SearchOption.AllDirectories))
                foreach (var line in File.ReadAllLines(file))
                    // The dialog factory (M7 step 1) is the one place that builds the overlay.
                    if (line.Contains("\"dialog-overlay\"") && !line.Contains("// computed") && Path.GetFileName(file) != "DialogFactory.cs")
                        count++;
            foreach (var line in File.ReadAllLines(Path.Combine(root, "Assets", "_Project", "UI", "Resources", "EditorShell.uxml")))
                if (line.Contains("dialog-overlay"))
                    count++;
            Assert.LessOrEqual(count, DialogOverlayCeiling,
                "dialog-overlay is built by hand " + count + " times (ceiling " + DialogOverlayCeiling + "). New dialogs must come from the dialog factory (M7); "
                + "if you removed constructions, lower DialogOverlayCeiling to " + count + ".");
            TestContext.WriteLine("dialog-overlay constructions: " + count);
        }

        [Test]
        public void InlineSizesInScriptsUiDoNotGrow()
        {
            var root = ProjectRoot();
            var count = 0;
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Assets", "_Project", "Scripts", "UI"), "*.cs", SearchOption.AllDirectories))
                foreach (var line in File.ReadAllLines(file))
                    if (InlineSize.IsMatch(line) && !line.Contains("// computed"))
                        count += InlineSize.Matches(line).Count;
            Assert.LessOrEqual(count, InlineSizeCeiling,
                "Scripts/UI sets " + count + " inline sizes, spacings and flex values (ceiling " + InlineSizeCeiling + "). Use a USS class in EditorShell.uss instead; "
                + "a value calculated at run time gets the comment '// computed'. If you removed some, lower InlineSizeCeiling to " + count + ".");
            TestContext.WriteLine("inline sizes: " + count);
        }
    }
}
