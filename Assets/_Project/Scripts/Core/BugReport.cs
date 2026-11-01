using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OmsiMapEditor.Core
{
    /// <summary>
    /// A bug report for the project's GitHub issues (ADR 053): what the user wrote, facts about the computer and the map,
    /// and the end of the log. GitHub takes it as a filled-in new-issue form in the browser, so MapWorks needs no token;
    /// the whole log lies ready as a file the user drags into the issue.
    /// </summary>
    public sealed class BugReport
    {
        public const string NewIssueUrl = "https://github.com/Tiegertosch/Omsi-2-Mapforger/issues/new";

        /// <summary>
        /// The longest address handed to the browser. GitHub fails from about 7,000 characters on (500 on the way to its
        /// sign-in, 414 from 9,000), measured on 1 November 2026.
        /// </summary>
        public const int MaximumUrlLength = 6000;

        /// <summary>What the body says instead of the report when even the user's own text is too long for the address.</summary>
        public const string PasteNotice = "_The report was too long to fill in here. MapWorks copied it to the clipboard: select this text and paste it (Ctrl+V)._";

        private static readonly Regex Secret = new Regex(@"(?i)\b(key|apikey|api_key|access_token|token)=[^&\s""'<>]+", RegexOptions.Compiled);

        public string Title = string.Empty;
        public string WhatHappened = string.Empty;
        public string Steps = string.Empty;
        public string Expected = string.Empty;

        /// <summary>MapWorks closed unexpectedly in the session the report is about.</summary>
        public bool Crashed;

        /// <summary>Version, Windows, graphics, map and the like, in the order they are listed.</summary>
        public readonly List<KeyValuePair<string, string>> Facts = new List<KeyValuePair<string, string>>();

        /// <summary>The log the report is about, already cleaned with <see cref="Sanitize"/>, or null without one.</summary>
        public string Log;

        /// <summary>The name the whole log has in the folder MapWorks opens, or null when it lies nowhere.</summary>
        public string LogFileName;

        /// <summary>Unity's error report of a crash, or null.</summary>
        public string CrashReport;

        /// <summary>
        /// The new-issue address with title and body, at most <paramref name="maximumLength"/> characters long. The log
        /// gives way first, line by line from its start; when the user's own text alone is too long, the body only says
        /// to paste the report, which the caller then puts on the clipboard (<paramref name="complete"/> is false).
        /// </summary>
        public string IssueUrl(out bool complete, int maximumLength = MaximumUrlLength)
        {
            var compactLog = Log == null ? null : Compact(Log);
            var shortest = Url(Body(0, compactLog));
            if (shortest.Length > maximumLength)
            {
                complete = false;
                return Url(PasteNotice);
            }
            // The longest excerpt that fits; every character takes at least one in the address.
            var fits = 0;
            var tooLong = Math.Min((compactLog?.Length ?? 0) + (CrashReport?.Length ?? 0), maximumLength) + 1;
            while (tooLong - fits > 1)
            {
                var middle = fits + (tooLong - fits) / 2;
                if (Url(Body(middle, compactLog)).Length <= maximumLength)
                    fits = middle;
                else
                    tooLong = middle;
            }
            complete = true;
            return Url(Body(fits, compactLog));
        }

        /// <summary>The whole body in GitHub's Markdown, with the log as long as it is.</summary>
        public string Body()
        {
            return Body(int.MaxValue, Log == null ? null : Compact(Log));
        }

        /// <summary>The body with at most <paramref name="excerptCharacters"/> characters of crash report and log.</summary>
        private string Body(int excerptCharacters, string compactLog)
        {
            var body = new StringBuilder();
            Section(body, "What happened", Crashed ? Join("MapWorks closed unexpectedly.", WhatHappened) : WhatHappened);
            Section(body, "Steps to reproduce", Steps);
            Section(body, "Expected", Expected);
            if (Facts.Count > 0)
            {
                body.Append("### System\n");
                foreach (var fact in Facts)
                    body.Append("- **").Append(fact.Key).Append(":** ").Append(fact.Value).Append('\n');
                body.Append('\n');
            }

            // The crash report takes up to a third, the log the rest.
            var crashCharacters = CrashReport == null ? 0 : Math.Min(CrashReport.Length, compactLog == null ? excerptCharacters : excerptCharacters / 3);
            if (crashCharacters > 0)
            {
                body.Append("### Crash report\n");
                Fence(body, Head(CrashReport, crashCharacters));
            }
            if (Log != null || LogFileName != null)
            {
                body.Append("### Log\n");
                if (LogFileName != null)
                    body.Append("<!-- Drag ").Append(LogFileName).Append(" from the folder MapWorks opened into this box to attach the whole log. -->\n");
                var excerpt = compactLog == null ? string.Empty : Tail(compactLog, Math.Max(0, excerptCharacters - crashCharacters));
                if (excerpt.Length > 0)
                {
                    body.Append("The last lines:\n");
                    Fence(body, excerpt);
                }
            }
            return body.ToString().TrimEnd('\n') + "\n";
        }

        /// <summary>
        /// The text with the user's profile folder written as %USERPROFILE%, so the report does not show the Windows user
        /// name, and keys and tokens in addresses blanked out.
        /// </summary>
        public static string Sanitize(string text, string userProfile)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            if (!string.IsNullOrEmpty(userProfile))
            {
                var profile = userProfile.TrimEnd('\\', '/');
                foreach (var variant in new[] { profile, profile.Replace('\\', '/'), profile.Replace('/', '\\') })
                    if (variant.Length > 3)
                        text = Regex.Replace(text, Regex.Escape(variant), "%USERPROFILE%", RegexOptions.IgnoreCase);
            }
            return Secret.Replace(text, "$1=***");
        }

        /// <summary>The end of the text in whole lines, at most <paramref name="maximumCharacters"/> long.</summary>
        public static string Tail(string text, int maximumCharacters)
        {
            if (string.IsNullOrEmpty(text) || maximumCharacters <= 0)
                return string.Empty;
            text = text.TrimEnd('\r', '\n');
            if (text.Length <= maximumCharacters)
                return text;
            var start = text.Length - maximumCharacters;
            var lineStart = text.IndexOf('\n', start - 1);
            return lineStart >= 0 && lineStart + 1 < text.Length ? text.Substring(lineStart + 1) : text.Substring(start);
        }

        /// <summary>The start of the text in whole lines, at most <paramref name="maximumCharacters"/> long.</summary>
        public static string Head(string text, int maximumCharacters)
        {
            if (string.IsNullOrEmpty(text) || maximumCharacters <= 0)
                return string.Empty;
            text = text.TrimEnd('\r', '\n');
            if (text.Length <= maximumCharacters)
                return text;
            var lineEnd = text.LastIndexOf('\n', maximumCharacters);
            return lineEnd > 0 ? text.Substring(0, lineEnd).TrimEnd('\r') : text.Substring(0, maximumCharacters);
        }

        /// <summary>The log without the source lines and blank lines Unity puts after every message.</summary>
        public static string Compact(string log)
        {
            var lines = log.Replace("\r\n", "\n").Split('\n');
            var compact = new StringBuilder(log.Length);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("(Filename:", StringComparison.Ordinal))
                    continue;
                compact.Append(line).Append('\n');
            }
            return compact.ToString();
        }

        private string Url(string body)
        {
            return NewIssueUrl + "?labels=bug&title=" + Uri.EscapeDataString(Title.Trim()) + "&body=" + Uri.EscapeDataString(body);
        }

        private static void Section(StringBuilder body, string heading, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            body.Append("### ").Append(heading).Append('\n').Append(text.Trim().Replace("\r\n", "\n")).Append("\n\n");
        }

        private static void Fence(StringBuilder body, string text)
        {
            // A fence longer than any run of backticks in the text, so the text cannot end it.
            var fence = "```";
            while (text.Contains(fence))
                fence += "`";
            body.Append(fence).Append("text\n").Append(text.Replace("\r\n", "\n")).Append('\n').Append(fence).Append("\n\n");
        }

        private static string Join(string first, string second)
        {
            return string.IsNullOrWhiteSpace(second) ? first : first + "\n\n" + second.Trim();
        }
    }
}
