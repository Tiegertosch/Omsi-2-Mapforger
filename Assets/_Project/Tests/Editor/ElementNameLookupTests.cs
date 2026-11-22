using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// M8 step 4: a name the code looks up with <c>Q("name")</c>, <c>Q&lt;T&gt;("name")</c> or <c>Query</c> must exist, either as
    /// <c>name="..."</c> in <c>EditorShell.uxml</c> or as a text the code gives to an element itself (<c>name = "..."</c>, a helper
    /// argument, a prefix such as <c>"group-" + id</c>). A lookup that finds nothing returns null silently; a rename in one place
    /// without the other left a menu without its button until now. No exception list: the rule is that the name appears somewhere
    /// else than in the lookups themselves.
    /// </summary>
    public sealed class ElementNameLookupTests
    {
        private static readonly Regex Lookup = new Regex(
            @"\.Q(?:uery)?(?:<[^>()]+(?:<[^>()]+>)?>)?\(\s*(?:name:\s*)?""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex UxmlName = new Regex(@"\bname=""([^""]+)""", RegexOptions.Compiled);

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

        private static string Uxml(string root)
        {
            return File.ReadAllText(Path.Combine(root, "Assets", "_Project", "UI", "Resources", "EditorShell.uxml"));
        }

        private static string[] Sources(string root)
        {
            return Directory.GetFiles(Path.Combine(root, "Assets", "_Project", "Scripts"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText).ToArray();
        }

        /// <summary>The names looked up in <paramref name="sources"/> that neither <paramref name="uxmlNames"/> nor any other text literal provides.</summary>
        internal static List<string> NamesWithoutElement(IEnumerable<string> sources, ISet<string> uxmlNames)
        {
            var looked = new SortedSet<string>(StringComparer.Ordinal);
            var rest = new List<string>();
            foreach (var source in sources)
            {
                foreach (Match match in Lookup.Matches(source))
                    looked.Add(match.Groups[1].Value);
                rest.Add(Lookup.Replace(source, string.Empty));
            }

            var missing = new List<string>();
            foreach (var name in looked)
            {
                if (uxmlNames.Contains(name))
                    continue;
                var literal = "\"" + name + "\"";
                if (rest.Any(text => text.Contains(literal)))
                    continue;
                missing.Add(name);
            }
            return missing;
        }

        [Test]
        public void EveryNameTheCodeLooksUpExists()
        {
            var root = ProjectRoot();
            var uxmlNames = new HashSet<string>(UxmlName.Matches(Uxml(root)).Cast<Match>().Select(match => match.Groups[1].Value), StringComparer.Ordinal);
            var missing = NamesWithoutElement(Sources(root), uxmlNames);
            Assert.IsEmpty(missing, "The code looks up names that EditorShell.uxml and the code never give to an element: " + string.Join(", ", missing));
        }

        [Test]
        public void TheRenamedElementsAreFoundUnderTheirNewNames()
        {
            var root = ProjectRoot();
            var uxml = Uxml(root);
            var code = string.Join("\n", Sources(root));
            foreach (var name in new[] { "tools-menu-button", "tools-menu", "bar-popup-close-button" })
            {
                StringAssert.Contains("name=\"" + name + "\"", uxml, name + " is missing in EditorShell.uxml.");
                StringAssert.Contains("\"" + name + "\"", code, name + " is never used in the code.");
            }
            foreach (var old in new[] { "view-menu", "project-collapse-button" })
            {
                StringAssert.DoesNotContain(old, uxml);
                StringAssert.DoesNotContain(old, code);
            }
            StringAssert.DoesNotContain("RenderProjectBrowser", code);
        }

        [Test]
        public void ALookupWithoutAnElementIsReported()
        {
            var uxmlNames = new HashSet<string>(StringComparer.Ordinal) { "present" };
            var sources = new[]
            {
                "root.Q<Button>(\"present\"); root.Q(\"ghost\"); root.Query<Label>(\"made\"); new Label { name = \"made\" }; root.Q<VisualElement>(\"group-\" + id); x.name = \"group-\" + id;"
            };
            CollectionAssert.AreEqual(new[] { "ghost" }, NamesWithoutElement(sources, uxmlNames));
        }
    }
}
