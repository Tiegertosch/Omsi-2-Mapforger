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
    /// <para>
    /// A key passed on to a helper or chosen by a condition is still found: any text in the code that looks like a key of an area the
    /// English files have (<c>menu.file</c>) must be one. A call whose key is a variable needs <c>i18n:keys</c> in a comment on its line.
    /// </para>
    /// </summary>
    public sealed class LocalizationKeyUsageTests
    {
        private static readonly Regex Call = new Regex(
            @"(?<![A-Za-z0-9_])(?:Loc\.T|SetStatusT|UpperLabel|Manager\.T|manager\.T|LocBindings\.Text|LocBindings\.Tooltip|LocBindings\.NewLabel|LocBindings\.NewButton|LocBindings\.TextUpper|LocBindings\.WithTooltip)\(",
            RegexOptions.CultureInvariant);

        private static readonly Regex StringLiteral = new Regex("\"([a-z][a-z0-9]*(?:\\.[a-z0-9_]+)+)\"", RegexOptions.CultureInvariant);

        private sealed class Use
        {
            public string File;
            public int Line;
            public string Key;
            public List<string> ArgumentNames = new List<string>();
            public bool Literal;

            /// <summary>The key was found as a text in the code, not in a call, so the arguments are not known.</summary>
            public bool ArgumentsUnknown;
        }

        [Test]
        public void EveryKeyTheCodeAsksForExistsInEnglishWithMatchingPlaceholders()
        {
            var english = LocaleLoader.Load(English());
            var parsed = english.Messages.ToDictionary(pair => pair.Key, pair => MessageFormat.Parse(pair.Value));
            var areas = new HashSet<string>(english.Messages.Keys.Select(key => key.Substring(0, key.IndexOf('.'))), StringComparer.Ordinal);
            var problems = new List<string>();
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (var use in FindUses(areas))
            {
                var where = use.File + "(" + use.Line + ")";
                if (!use.Literal)
                {
                    problems.Add(where + ": the key is not written out as a text in quotes; keys built from pieces cannot be checked or found" +
                                 " (a variable key needs i18n:keys in a comment on the line)");
                    continue;
                }
                used.Add(use.Key);
                if (!parsed.TryGetValue(use.Key, out var message))
                {
                    problems.Add(where + ": the key \"" + use.Key + "\" is not in locales/en-US");
                    continue;
                }
                if (use.ArgumentsUnknown)
                    continue;
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
        public void ADeclaredVariableKeyIsAllowedAndKeyLikeTextsAreFoundWherever()
        {
            var text = "var l = LocBindings.NewLabel(key); // i18n:keys\nvar t = Card(\"welcome.card\", cond ? \"menu.file\" : \"global.cfg\");";
            var areas = new HashSet<string> { "menu", "welcome" };

            Assert.AreEqual(0, Scan("x.cs", text).Count);
            var found = LiteralKeys("x.cs", text, areas).Select(use => use.Key).ToList();
            CollectionAssert.AreEquivalent(new[] { "welcome.card", "menu.file" }, found);
        }

        [Test]
        public void TextsThatChooseTheirPartsReadRightInBothLanguages()
        {
            foreach (var code in new[] { "en-US", "de-DE" })
            {
                var info = LocaleLoader.ReadInfo(Path.Combine(RepositoryRoot(), "locales", code), null);
                var messages = LocaleLoader.Load(info).Messages;
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                string Format(string key, params (string Name, object Value)[] arguments)
                {
                    return MessageFormat.Parse(messages[key]).Format(culture, code, arguments);
                }

                var plain = Format("bugreport.status", ("clipboard", "yes"), ("hasfile", "no"), ("file", string.Empty));
                var full = Format("bugreport.status", ("clipboard", "no"), ("hasfile", "yes"), ("file", "MapWorks-log-1.txt"));
                Assert.IsFalse(plain.Contains("{") || plain.Contains("}"), code + ": " + plain);
                StringAssert.DoesNotContain("MapWorks-log-1.txt", plain);
                StringAssert.Contains("MapWorks-log-1.txt", full);
                Assert.Greater(full.Length, plain.Length, code);

                var crashLog = Format("bugreport.included", ("what", "crashlog"), ("log", "yes"));
                var mapOnly = Format("bugreport.included", ("what", "map"), ("log", "no"));
                Assert.IsFalse(crashLog.Contains("{") || mapOnly.Contains("}"), code);
                Assert.Greater(crashLog.Length, mapOnly.Length, code);
                StringAssert.Contains("%USERPROFILE%", mapOnly);
            }
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
            var folder = Path.Combine(RepositoryRoot(), "locales", TranslationManager.ReferenceLanguage);
            var info = LocaleLoader.ReadInfo(folder, null);
            Assert.IsNotNull(info, "locales/en-US/locale.json must be usable");
            return info;
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

        private static List<Use> FindUses(HashSet<string> areas)
        {
            var uses = new List<Use>();
            var scripts = Path.Combine(RepositoryRoot(), "Assets", "_Project", "Scripts");
            foreach (var file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                var relative = file.Substring(scripts.Length + 1).Replace('\\', '/');
                // The system itself and its helpers name no keys of their own.
                if (relative.StartsWith("Core/Localization/", StringComparison.Ordinal) || relative == "UI/LocBindings.cs")
                    continue;
                var text = File.ReadAllText(file);
                uses.AddRange(Scan(relative, text));
                uses.AddRange(LiteralKeys(relative, text, areas));
            }
            return uses;
        }

        private static int LineOf(string text, int index)
        {
            var line = 1;
            for (var at = 0; at < index; at++)
                if (text[at] == '\n')
                    line++;
            return line;
        }

        /// <summary>Every text that looks like a key of a known area (menu.file, welcome.tagline): it must be one.</summary>
        private static List<Use> LiteralKeys(string file, string text, HashSet<string> areas)
        {
            var uses = new List<Use>();
            foreach (Match match in StringLiteral.Matches(text))
            {
                var key = match.Groups[1].Value;
                if (areas.Contains(key.Substring(0, key.IndexOf('.'))))
                    uses.Add(new Use { File = file, Line = LineOf(text, match.Index), Key = key, Literal = true, ArgumentsUnknown = true });
            }
            return uses;
        }

        private static List<Use> Scan(string file, string text)
        {
            var uses = new List<Use>();
            foreach (Match match in Call.Matches(text))
            {
                // A call whose key is a variable says so in a comment on its line.
                var lineStart = text.LastIndexOf('\n', match.Index) + 1;
                var lineEnd = text.IndexOf('\n', match.Index);
                if ((lineEnd < 0 ? text.Substring(lineStart) : text.Substring(lineStart, lineEnd - lineStart)).Contains("i18n:keys"))
                    continue;
                var open = match.Index + match.Length;
                var arguments = Split(text, open);
                var keyIndex = match.Value.StartsWith("LocBindings.Text(", StringComparison.Ordinal) || match.Value.StartsWith("LocBindings.TextUpper", StringComparison.Ordinal) ||
                               match.Value.StartsWith("LocBindings.Tooltip", StringComparison.Ordinal) || match.Value.StartsWith("LocBindings.WithTooltip", StringComparison.Ordinal) ? 1 : 0;
                var use = new Use { File = file, Line = LineOf(text, match.Index) };
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
