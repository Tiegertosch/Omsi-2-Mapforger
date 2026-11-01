using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using OmsiMapEditor.Core;

namespace OmsiMapEditor.Tests
{
    public sealed class BugReportTests
    {
        [Test]
        public void IssueUrlCarriesTitleBodyAndLabel()
        {
            var report = new BugReport { Title = "Arms jump & turn", WhatHappened = "The arm jumped." };
            report.Facts.Add(new KeyValuePair<string, string>("MapWorks", "1.0"));

            var url = report.IssueUrl(out var complete);

            Assert.IsTrue(complete);
            StringAssert.StartsWith(BugReport.NewIssueUrl + "?labels=bug&title=Arms%20jump%20%26%20turn&body=", url);
            var body = Uri.UnescapeDataString(url.Substring(url.IndexOf("&body=", StringComparison.Ordinal) + 6));
            Assert.AreEqual("### What happened\nThe arm jumped.\n\n### System\n- **MapWorks:** 1.0\n", body);
        }

        [Test]
        public void LongLogGivesWayLineByLineFromItsStart()
        {
            var log = new StringBuilder();
            for (var line = 0; line < 2000; line++)
                log.Append("Line ").Append(line).Append(" of the log\n");
            var report = new BugReport { Title = "Crash", Log = log.ToString(), LogFileName = "MapWorks-log.txt" };

            var url = report.IssueUrl(out var complete, 4000);

            Assert.IsTrue(complete);
            Assert.LessOrEqual(url.Length, 4000);
            var body = Uri.UnescapeDataString(url.Substring(url.IndexOf("&body=", StringComparison.Ordinal) + 6));
            StringAssert.Contains("Line 1999 of the log\n```", body);
            StringAssert.DoesNotContain("Line 0 of", body);
            StringAssert.Contains("<!-- Drag MapWorks-log.txt", body);
            // The excerpt starts with a whole line.
            StringAssert.IsMatch("The last lines:\n```text\nLine \\d+ of the log\n", body);
        }

        [Test]
        public void TooLongOwnTextAsksToPaste()
        {
            var report = new BugReport { Title = "Long", WhatHappened = new string('ä', 5000) };

            var url = report.IssueUrl(out var complete);

            Assert.IsFalse(complete);
            Assert.LessOrEqual(url.Length, BugReport.MaximumUrlLength);
            StringAssert.EndsWith(Uri.EscapeDataString(BugReport.PasteNotice), url);
            StringAssert.Contains(new string('ä', 5000), report.Body());
        }

        [Test]
        public void CrashReportComesBeforeTheLog()
        {
            var report = new BugReport { Title = "Crash: on start", Crashed = true, CrashReport = "Access violation\nat 0x0", Log = "Last line" };

            var body = report.Body();

            Assert.AreEqual("### What happened\nMapWorks closed unexpectedly.\n\n" +
                            "### Crash report\n```text\nAccess violation\nat 0x0\n```\n\n" +
                            "### Log\nThe last lines:\n```text\nLast line\n```\n", body);
        }

        [Test]
        public void FenceIsLongerThanBackticksInTheLog()
        {
            var report = new BugReport { Title = "Fence", Log = "a ``` b" };

            StringAssert.Contains("````text\na ``` b\n````", report.Body());
        }

        [Test]
        public void SanitizeHidesUserFolderAndKeys()
        {
            var text = @"Loading C:\Users\Jane Doe\Documents\map.omproj and c:/users/jane doe/x.png from https://api.maptiler.com/tiles/1.jpg?key=abc123&size=2 or ?access_token=pk.xyz";

            var clean = BugReport.Sanitize(text, @"C:\Users\Jane Doe");

            Assert.AreEqual(@"Loading %USERPROFILE%\Documents\map.omproj and %USERPROFILE%/x.png from https://api.maptiler.com/tiles/1.jpg?key=***&size=2 or ?access_token=***", clean);
        }

        [Test]
        public void CompactDropsUnitySourceLinesAndBlankLines()
        {
            var log = "Loaded map.\n(Filename: C:\\build\\output\\Runtime\\Export\\Debug.cs Line: 35)\n\r\nNext\r\n";

            Assert.AreEqual("Loaded map.\nNext\n", BugReport.Compact(log));
        }

        [Test]
        public void TailAndHeadKeepWholeLines()
        {
            Assert.AreEqual("three\nfour", BugReport.Tail("one\ntwo\nthree\nfour\n", 11));
            Assert.AreEqual("one\ntwo", BugReport.Head("one\ntwo\nthree\nfour", 9));
            Assert.AreEqual(string.Empty, BugReport.Tail("one", 0));
            Assert.AreEqual("abc", BugReport.Tail("abc", 5));
        }
    }
}
