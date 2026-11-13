using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OmsiMapEditor.Core;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Help > Report a bug... (ADR 053): a form whose report opens as a filled-in new issue on GitHub in the browser, with
    /// the end of the log in it and the whole log in a folder to drag into the issue. A session that ends without MapWorks
    /// closing normally, by a crash or because it hung and was ended, leaves its marker behind; the next start finds it
    /// and opens the form for that session, with its log and Unity's crash report.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The longest stretch of a log read for a report, from its end.</summary>
        private const int BugReportLogBytes = 8 * 1024 * 1024;

        private VisualElement bugReportDialog;
        private BugReportDialog bugReportForm;
        private string sessionMarker;
        private DateTime crashedSessionStart;
        private bool bugReportDryRun;

        private void InitializeBugReport(VisualElement root, VisualElement helpMenu)
        {
            bugReportDialog = root.Q<VisualElement>("bug-report-dialog");
            RegisterOverlay(bugReportDialog, true);
            if (bugReportDialog != null)
            {
                bugReportForm = new BugReportDialog();
                bugReportForm.Cancelled += () => HidePopup(bugReportDialog);
                bugReportForm.SendRequested += SendBugReport;
                bugReportDialog.Add(bugReportForm);
            }
            var menuItem = root.Q<Button>("report-bug-menu-item");
            if (menuItem != null)
                menuItem.clicked += () =>
                {
                    HidePopup(helpMenu);
                    OpenBugReport(false);
                };
        }

        /// <summary>
        /// Notes that this session runs, until it quits normally, and tells whether an earlier one ended without quitting.
        /// Only the player keeps track; command-line checks that quit by themselves neither leave nor read a marker.
        /// </summary>
        private bool BeginSession(bool automated)
        {
            if (Application.isEditor || automated)
                return false;
            var crashed = false;
            try
            {
                var folder = Path.Combine(Application.persistentDataPath, "Sessions");
                Directory.CreateDirectory(folder);
                var current = Process.GetCurrentProcess();
                foreach (var marker in Directory.GetFiles(folder, "*.txt"))
                {
                    // Another MapWorks still running keeps its marker.
                    if (int.TryParse(Path.GetFileNameWithoutExtension(marker), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && IsRunning(id, current.ProcessName))
                        continue;
                    var started = File.GetLastWriteTime(marker);
                    if (!crashed || started > crashedSessionStart)
                        crashedSessionStart = started;
                    crashed = true;
                    File.Delete(marker);
                }
                sessionMarker = Path.Combine(folder, current.Id.ToString(CultureInfo.InvariantCulture) + ".txt");
                File.WriteAllText(sessionMarker, DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                Application.quitting += EndSession;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidOperationException)
            {
                Debug.LogWarning("Bug report: the session marker could not be kept: " + exception.Message);
            }
            if (crashed)
                Debug.Log("Bug report: the last session ended unexpectedly (started " + crashedSessionStart.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + ").");
            return crashed;
        }

        private void EndSession()
        {
            try
            {
                if (sessionMarker != null && File.Exists(sessionMarker))
                    File.Delete(sessionMarker);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Bug report: the session marker could not be removed: " + exception.Message);
            }
        }

        private static bool IsRunning(int processId, string processName)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                    return !process.HasExited && string.Equals(process.ProcessName, processName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private void OpenBugReport(bool crashed)
        {
            if (bugReportDialog == null || bugReportForm == null)
                return;
            bugReportForm.Open(crashed);
            bugReportDialog.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Writes the logs into a folder of their own under Documents\OMSI MapWorks\Bug reports, opens the filled-in issue
        /// in the browser and the folder in Explorer. A check from the command line only writes and logs.
        /// </summary>
        private void SendBugReport()
        {
            if (bugReportForm == null)
                return;
            var report = new BugReport();
            bugReportForm.FillIn(report);
            if (string.IsNullOrEmpty(report.Title))
                return;
            AddBugReportFacts(report, !report.Crashed);

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string folder = null;
            string logFile = null;
            if (bugReportForm.IncludeLog)
            {
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var logPath = report.Crashed ? PreviousLogPath() : Application.consoleLogPath;
                report.Log = BugReport.Sanitize(ReadLogEnd(logPath), userProfile);
                var crashReport = report.Crashed ? CrashReportPath() : null;
                report.CrashReport = BugReport.Sanitize(ReadLogEnd(crashReport), userProfile);
                try
                {
                    if (report.Log != null || report.CrashReport != null)
                    {
                        folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OMSI MapWorks", "Bug reports", stamp);
                        Directory.CreateDirectory(folder);
                    }
                    if (report.Log != null)
                    {
                        logFile = Path.Combine(folder, "MapWorks-log-" + stamp + ".txt");
                        File.WriteAllText(logFile, report.Log, new UTF8Encoding(false));
                        report.LogFileName = Path.GetFileName(logFile);
                    }
                    if (report.CrashReport != null)
                        File.WriteAllText(Path.Combine(folder, "MapWorks-crash-" + stamp + ".txt"), report.CrashReport, new UTF8Encoding(false));
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Bug report: the log could not be written: " + exception.Message);
                    logFile = null;
                    report.LogFileName = null;
                }
            }

            var url = report.IssueUrl(out var complete);
            if (!complete)
                GUIUtility.systemCopyBuffer = report.Body();
            HidePopup(bugReportDialog);
            SetStatusT("bugreport.status", ("clipboard", complete ? "yes" : "no"), ("hasfile", logFile != null ? "yes" : "no"),
                ("file", logFile != null ? Path.GetFileName(logFile) : string.Empty));
            Debug.Log("Bug report: " + statusLabel?.text + " Address " + url.Length + " characters" + (folder != null ? ", files in " + folder : string.Empty) + ".");
            if (bugReportDryRun)
            {
                Debug.Log("Bug report address: " + url);
                return;
            }
            Application.OpenURL(url);
            if (logFile != null)
                ShowInExplorer(logFile);
        }

        /// <summary>The facts every report carries; the open map only when the report is about this session.</summary>
        private void AddBugReportFacts(BugReport report, bool withMap)
        {
            void Fact(string name, string value) => report.Facts.Add(new KeyValuePair<string, string>(name, value));
            Fact("MapWorks", Application.version + " (Unity " + Application.unityVersion + (Application.isEditor ? ", in the Unity editor" : string.Empty) + ")");
            Fact("Windows", SystemInfo.operatingSystem);
            Fact("Graphics", SystemInfo.graphicsDeviceName + ", " + SystemInfo.graphicsDeviceVersion + ", " + SystemInfo.graphicsMemorySize.ToString(CultureInfo.InvariantCulture) + " MB");
            Fact("Processor", SystemInfo.processorType + ", " + SystemInfo.systemMemorySize.ToString(CultureInfo.InvariantCulture) + " MB memory");
            Fact("Screen", Screen.width.ToString(CultureInfo.InvariantCulture) + " × " + Screen.height.ToString(CultureInfo.InvariantCulture));
            if (!withMap)
                return;
            Fact("Map", currentSummary == null ? "none open"
                : currentSummary.Name + ", " + currentSummary.Tiles.Count.ToString(CultureInfo.InvariantCulture) + " tiles, " + (session != null && session.CanEdit ? "with a project" : "read only")
                  + (chronoDate != null ? ", chrono view" : string.Empty));
        }

        /// <summary>The log of the session before this one, which Unity keeps beside the current log.</summary>
        private static string PreviousLogPath()
        {
            var current = Application.consoleLogPath;
            if (string.IsNullOrEmpty(current))
                return null;
            return Path.Combine(Path.GetDirectoryName(current) ?? string.Empty, Path.GetFileNameWithoutExtension(current) + "-prev" + Path.GetExtension(current));
        }

        /// <summary>Unity's error report of a crash after the crashed session started, or null when it left none.</summary>
        private string CrashReportPath()
        {
            try
            {
                var crashes = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Application.companyName, Application.productName, "Crashes"));
                if (!crashes.Exists)
                    return null;
                var latest = crashes.GetDirectories().Where(directory => directory.LastWriteTime >= crashedSessionStart)
                    .OrderByDescending(directory => directory.LastWriteTime).FirstOrDefault();
                var errorLog = latest == null ? null : Path.Combine(latest.FullName, "error.log");
                return errorLog != null && File.Exists(errorLog) ? errorLog : null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return null;
            }
        }

        /// <summary>The end of a log, read while Unity may still write to it, or null when there is none.</summary>
        private static string ReadLogEnd(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var start = Math.Max(0L, stream.Length - BugReportLogBytes);
                    stream.Seek(start, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var text = reader.ReadToEnd();
                        // Cut in the middle, the first line is only a part of one.
                        if (start > 0)
                        {
                            var firstLine = text.IndexOf('\n');
                            text = firstLine >= 0 ? text.Substring(firstLine + 1) : text;
                        }
                        return text;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Bug report: " + path + " could not be read: " + exception.Message);
                return null;
            }
        }

        private static void ShowInExplorer(string file)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = false });
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                Debug.LogWarning("Bug report: Explorer could not be opened: " + exception.Message);
            }
        }
    }
}
