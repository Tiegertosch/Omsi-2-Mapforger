using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// M8 step 0: every style class of our own in <c>EditorShell.uss</c> must be used by the product, that is named in the code under
    /// Scripts or in <c>EditorShell.uxml</c>. A class that only a test mentions is not used (a test that lists it would keep a dead rule alive).
    /// <para>
    /// Two rules, not a list of single names, decide what is exempt: (1) classes starting with <c>unity-</c> restyle Unity's own elements
    /// and are never set by our code; (2) a class built from a prefix, <c>AddToClassList("road-swatch-" + word)</c>, counts as used when the
    /// code adds a prefix ending in a hyphen to a class list and the class starts with that prefix.
    /// </para>
    /// <para>
    /// Since M8 step 0 the number of unused classes is a ceiling that may only fall. M8 step 1 removes the classes and sets
    /// <see cref="UnusedClassCeiling"/> to 0 (owner's decision 5A of 22 November 2026 is the same for the text keys: no exception list).
    /// </para>
    /// </summary>
    public sealed class UnusedStyleClassTests
    {
        /// <summary>M8 step 1 removed the 25 dead classes; no class without a use may come back.</summary>
        private const int UnusedClassCeiling = 0;

        private static readonly Regex Selector = new Regex(@"([^{}]+)\{", RegexOptions.Compiled);
        private static readonly Regex ClassName = new Regex(@"\.([A-Za-z_][A-Za-z0-9_-]*)", RegexOptions.Compiled);
        private static readonly Regex Token = new Regex(@"[A-Za-z_][A-Za-z0-9_-]*", RegexOptions.Compiled);
        private static readonly Regex ClassPrefix = new Regex(
            @"(?:AddToClassList|RemoveFromClassList|EnableInClassList|ToggleInClassList)\(\s*""([a-z][a-z0-9-]*-)""\s*\+", RegexOptions.Compiled);

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

        /// <summary>Every class named in a selector of the stylesheet text (comments removed first).</summary>
        internal static SortedSet<string> ClassesOf(string stylesheet)
        {
            var text = Regex.Replace(stylesheet, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            var classes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match selector in Selector.Matches(text))
                foreach (Match name in ClassName.Matches(selector.Groups[1].Value))
                    classes.Add(name.Groups[1].Value);
            return classes;
        }

        /// <summary>The classes of <paramref name="classes"/> that neither the tokens of the code nor a prefix of the code reach.</summary>
        internal static List<string> UnusedClasses(IEnumerable<string> classes, HashSet<string> codeTokens, IReadOnlyCollection<string> prefixes)
        {
            var unused = new List<string>();
            foreach (var name in classes)
            {
                if (name.StartsWith("unity-", StringComparison.Ordinal) || codeTokens.Contains(name))
                    continue;
                if (prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                    continue;
                unused.Add(name);
            }
            return unused;
        }

        private static IEnumerable<string> ProductSources(string root)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Assets", "_Project", "Scripts"), "*.cs", SearchOption.AllDirectories))
                yield return File.ReadAllText(file);
            yield return File.ReadAllText(Path.Combine(root, "Assets", "_Project", "UI", "Resources", "EditorShell.uxml"));
        }

        [Test]
        public void OwnStyleClassesWithoutAnyUseDoNotGrow()
        {
            var root = ProjectRoot();
            var classes = ClassesOf(File.ReadAllText(Path.Combine(root, "Assets", "_Project", "UI", "Resources", "EditorShell.uss")));
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in ProductSources(root))
            {
                foreach (Match token in Token.Matches(source))
                    tokens.Add(token.Value);
                foreach (Match prefix in ClassPrefix.Matches(source))
                    prefixes.Add(prefix.Groups[1].Value);
            }

            var unused = UnusedClasses(classes, tokens, prefixes);
            Assert.LessOrEqual(unused.Count, UnusedClassCeiling,
                unused.Count + " style classes of EditorShell.uss are used nowhere in Scripts or EditorShell.uxml (ceiling " + UnusedClassCeiling + "): "
                + string.Join(", ", unused) + ". Remove the class and its rules, or use it. If you removed some, lower UnusedClassCeiling to " + unused.Count + ".");
            TestContext.WriteLine("own classes: " + classes.Count + "; unused: " + unused.Count + " (" + string.Join(", ", unused) + "); prefixes: " + string.Join(", ", prefixes.Where(prefix => classes.Any(name => name.StartsWith(prefix, StringComparison.Ordinal)))));
        }

        [Test]
        public void TheRulesExemptOnlyUnityClassesAndPrefixBuiltClasses()
        {
            var css = "/* .commented-out { } */ .a, .b:hover .c { x: 0.5; } .unity-button { } .swatch-red, .swatch-blue { } .dead-one > Label { }";
            var classes = ClassesOf(css);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "dead-one", "swatch-blue", "swatch-red", "unity-button" }, classes);

            var tokens = new HashSet<string>(StringComparer.Ordinal) { "a", "c" };
            var prefixes = new[] { "swatch-" };
            CollectionAssert.AreEqual(new[] { "b", "dead-one" }, UnusedClasses(classes, tokens, prefixes));

            Assert.IsTrue(ClassPrefix.IsMatch("x.AddToClassList(\"swatch-\" + word);"));
            Assert.IsTrue(ClassPrefix.IsMatch("x.EnableInClassList(\"swatch-\" + word, on);"));
            Assert.IsFalse(ClassPrefix.IsMatch("x.AddToClassList(\"swatch-red\");"));
        }
    }
}
