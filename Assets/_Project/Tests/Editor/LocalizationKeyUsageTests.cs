using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OmsiMapEditor.Core.Localization;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// Checks the code against the English texts (ADR 068): every key the interface code asks for exists in locales/en-US, the
    /// arguments a call gives are exactly the placeholders of that text, and keys are written out in full, never built from pieces, so
    /// they can be found. A mistake here would show only as "[key]" on screen.
    /// </summary>
    public sealed class LocalizationKeyUsageTests
    {
        private static readonly Regex Call = new Regex(@"(?<![A-Za-z0-9_])(?:Loc\.T|Manager\.T|manager\.T|LocBindings\.Text|LocBindings\.Tooltip)\(", RegexOptions.CultureInvariant);

        private sealed class Use
        {
            public string File;
            public int Line;
            public string Key;
            public List<string> ArgumentNames = new List<string>();
            public bool Literal;
        }

        [Test]
        public void EveryKeyTheCodeAsksForExistsInEnglishWithMatchingPlaceholders()
        {
            var english = LocaleLoader.Load(English());
            var parsed = english.Messages.ToDictionary(pair => pair.Key, pair => MessageFormat.Parse(pair.Value));
            var problems = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (var use in FindUses())
            {
                var where = use.File + "(" + use.Line + ")";
                if (!use.Literal)
                {
                    problems.Add(where + ": the key is not written out as a text in quotes; keys built from pieces cannot be checked or found");
                    continue;
                }
                used.Add(use.Key);
                if (!parsed.TryGetValue(use.Key, out var message))
                {
                    problems.Add(where + ": the key \"" + use.Key + "\" is not in locales/en-US");
                    continue;
                }
                var wanted = message.Arguments.Select(argument => argument.Name).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
                var given = use.ArgumentNames.Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
                if (!wanted.SequenceEqual(given))
                    problems.Add(where + ": [" + use.Key + "] needs the arguments {" + string.Join(", ", wanted) + "} but the call gives {" + string.Join(", ", given) + "}");
            }

            Assert.IsEmpty(problems, "\n" + string.Join("\n", problems));
            var unused = english.Messages.Keys.Where(key => !used.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToList();
            if (unused.Count > 0)
                Console.WriteLine("Texts in locales/en-US that no code asks for yet: " + string.Join(", ", unused));
        }

        [Test]
        public void TheScannerSeesKeysArgumentsAndKeysBuiltFromPieces()
        {
            var text = "Loc.T(\"a.b\"); Loc.T(\"c.d\", (\"count\", n), (\"name\", Path.Combine(x, \"y\"))); LocBindings.Text(el, \"e.f\"); Loc.T(\"menu.\" + part);";
            var uses = Scan("x.cs", text);

            Assert.AreEqual(4, uses.Count);
            Assert.AreEqual("a.b", uses[0].Key);
            CollectionAssert.AreEqual(new[] { "count", "name" }, uses[1].ArgumentNames);
            Assert.AreEqual("e.f", uses[2].Key);
            Assert.IsFalse(uses[3].Literal);
        }

        [Test]
        public void TheTranslationManagerAnswersFromManyThreadsAtOnce()
        {
            var root = Path.Combine(Path.GetTempPath(), "mapforge-threads-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (var code in new[] { "en-US", "de-DE" })
                {
                    Directory.CreateDirectory(Path.Combine(root, code));
                    File.WriteAllText(Path.Combine(root, code, "locale.json"), "{ \"code\": \"" + code + "\", \"name\": \"" + code + "\", \"englishName\": \"" + code + "\", \"status\": \"approved\" }");
                }
                File.WriteAllText(Path.Combine(root, "en-US", "a.json"), "{ \"a.count\": \"{n, plural, one {# item} other {# items}}\", \"a.only\": \"Only English\", \"a.bad\": \"Broken {n, plural, one {x}\" }");
                File.WriteAllText(Path.Combine(root, "de-DE", "a.json"), "{ \"a.count\": \"{n, plural, one {# Eintrag} other {# Einträge}}\" }");
                var manager = new TranslationManager(root);
                var warnings = 0;
                manager.Warn = _ => System.Threading.Interlocked.Increment(ref warnings);
                manager.SetLanguage("de-DE");
                var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

                System.Threading.Tasks.Parallel.For(0, 4000, index =>
                {
                    try
                    {
                        var count = index % 3;
                        if (manager.T("a.count", ("n", count)) != (count == 1 ? "1 Eintrag" : count + " Einträge"))
                            failures.Add("count " + count);
                        if (manager.T("a.only") != "Only English")
                            failures.Add("fallback");
                        if (manager.T("a.nokey") != "[a.nokey]")
                            failures.Add("missing");
                    }
                    catch (Exception exception)
                    {
                        failures.Add(exception.GetType().Name);
                    }
                });

                Assert.IsEmpty(failures);
                // Each problem is reported once however many threads ran into it: the text missing in German and the missing key.
                Assert.AreEqual(2, warnings);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private static LocaleInfo English()
        {
            var folder = Path.Combine(LocalesRoot(), TranslationManager.ReferenceLanguage);
            var info = LocaleLoader.ReadInfo(folder, null);
            Assert.IsNotNull(info, "locales/en-US/locale.json must be usable");
            return info;
        }

        private static string LocalesRoot()
        {
            return Path.Combine(RepositoryRoot(), "locales");
        }

        private static string RepositoryRoot()
        {
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 6 && folder != null; depth++, folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(Path.Combine(folder, "locales")) && Directory.Exists(Path.Combine(folder, "Assets", "_Project", "Scripts")))
                    return folder;
            Assert.Fail("The repository root was not found from " + Directory.GetCurrentDirectory());
            return null;
        }

        private static List<Use> FindUses()
        {
            var uses = new List<Use>();
            var scripts = Path.Combine(RepositoryRoot(), "Assets", "_Project", "Scripts");
            foreach (var file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                var relative = file.Substring(scripts.Length + 1).Replace('\\', '/');
                // The system itself and its helpers name no keys of their own.
                if (relative.StartsWith("Core/Localization/", StringComparison.Ordinal) || relative == "UI/LocBindings.cs")
                    continue;
                uses.AddRange(Scan(relative, File.ReadAllText(file)));
            }
            return uses;
        }

        private static List<Use> Scan(string file, string text)
        {
            var uses = new List<Use>();
            foreach (Match match in Call.Matches(text))
            {
                var open = match.Index + match.Length;
                var arguments = Split(text, open);
                var keyIndex = match.Value.StartsWith("LocBindings", StringComparison.Ordinal) ? 1 : 0;
                var use = new Use { File = file, Line = text.Substring(0, match.Index).Count(c => c == '\n') + 1 };
                if (arguments.Count > keyIndex)
                {
                    var key = arguments[keyIndex].Trim();
                    var literal = Regex.Match(key, "^\"([^\"\\\\]*)\"$");
                    use.Literal = literal.Success;
                    use.Key = literal.Success ? literal.Groups[1].Value : key;
                    for (var index = keyIndex + 1; index < arguments.Count; index++)
                    {
                        var name = Regex.Match(arguments[index].Trim(), "^\\(\\s*\"([A-Za-z0-9_]+)\"\\s*,");
                        if (name.Success)
                            use.ArgumentNames.Add(name.Groups[1].Value);
                    }
                }
                uses.Add(use);
            }
            return uses;
        }

        /// <summary>The top-level arguments of the call whose opening parenthesis is just before <paramref name="start"/>.</summary>
        private static List<string> Split(string text, int start)
        {
            var parts = new List<string>();
            var depth = 0;
            var begin = start;
            for (var index = start; index < text.Length; index++)
            {
                var c = text[index];
                if (c == '"')
                {
                    for (index++; index < text.Length && text[index] != '"'; index++)
                        if (text[index] == '\\')
                            index++;
                    continue;
                }
                if (c == '(' || c == '{' || c == '[')
                    depth++;
                else if (c == ')' || c == '}' || c == ']')
                {
                    if (depth == 0)
                    {
                        parts.Add(text.Substring(begin, index - begin));
                        return parts;
                    }
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    parts.Add(text.Substring(begin, index - begin));
                    begin = index + 1;
                }
            }
            return parts;
        }
    }
}
