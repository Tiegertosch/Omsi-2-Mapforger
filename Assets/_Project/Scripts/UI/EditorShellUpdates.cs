using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Help > Check for updates... (ADR 054): asks GitHub for the latest release, at most once a day by itself when the
    /// player starts, and installs a newer one: download beside the program, check, unpack, then MapWorks quits and a
    /// PowerShell script swaps the program files and starts the new version with the open project or map.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        private const string UpdatesAtStartKey = "Mapforge.Updates.CheckAtStart";
        private const string UpdatesLastCheckKey = "Mapforge.Updates.LastCheck";
        private const string UpdatesSkippedKey = "Mapforge.Updates.Skipped";
        private const string ProgramExe = "OMSIMapWorks.exe";

        /// <summary>How long a check by itself at start counts.</summary>
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(20);

        private VisualElement updateDialog;
        private UpdateDialog updateForm;
        private string updateSource = MapWorksUpdates.LatestReleaseUrl;
        private MapWorksRelease offeredRelease;
        private CancellationTokenSource updateCancellation;
        private bool updateInstallWhenFound;
        private bool? runInBackgroundBefore;

        private static string UserAgent => "OMSI-MapWorks/" + Application.version;

        /// <summary>The folder of the running player, which an update replaces.</summary>
        private static string ProgramFolder => Path.GetDirectoryName(Application.dataPath);

        private void InitializeUpdates(VisualElement root, VisualElement helpMenu)
        {
            updateDialog = root.Q<VisualElement>("update-dialog");
            if (updateDialog != null)
            {
                updateForm = new UpdateDialog();
                updateForm.SetCheckAtStart(PlayerPrefs.GetInt(UpdatesAtStartKey, 1) != 0);
                updateForm.CloseRequested += CloseUpdates;
                updateForm.CheckAtStartChanged += value => PlayerPrefs.SetInt(UpdatesAtStartKey, value ? 1 : 0);
                updateForm.SkipRequested += () =>
                {
                    if (offeredRelease != null)
                        PlayerPrefs.SetString(UpdatesSkippedKey, offeredRelease.Tag);
                    CloseUpdates();
                };
                updateForm.OpenPageRequested += () =>
                {
                    Application.OpenURL(offeredRelease?.PageUrl ?? "https://github.com/Tiegertosch/Omsi-2-Mapforger/releases");
                    CloseUpdates();
                };
                updateForm.InstallRequested += InstallUpdate;
                updateDialog.Add(updateForm);
            }
            var menuItem = root.Q<Button>("check-updates-menu-item");
            if (menuItem != null)
                menuItem.clicked += () =>
                {
                    HidePopup(helpMenu);
                    CheckForUpdates(true);
                };
        }

        /// <summary>
        /// At start: reports how an update went, clears what one left behind, and asks GitHub by itself unless that is off,
        /// happened in the last <see cref="UpdateCheckInterval"/>, or this run is a check from the command line.
        /// -updateSource names another address for the latest release, -checkUpdates asks at once, -updateInstall then
        /// installs a newer release.
        /// </summary>
        private void StartUpdates(string[] args, bool automated, bool quiet)
        {
            string Argument(string name)
            {
                var index = Array.IndexOf(args, name);
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            }

            if (Argument("-updateSource") is string source)
                updateSource = source;
            updateInstallWhenFound = Array.IndexOf(args, "-updateInstall") >= 0;
            if (!Application.isEditor)
                ClearUpdateLeftovers();

            if (Argument("-updatedFrom") is string from)
            {
                SetStatusT("updates.status.updated", ("previous", from), ("current", Application.version));
                Debug.Log("Update: " + statusLabel?.text);
            }
            if (Argument("-updateFailed") is string log)
            {
                var reason = LastLine(log);
                if (reason != null)
                    SetStatusT("updates.status.failed_reason", ("reason", reason), ("version", Application.version));
                else
                    SetStatusT("updates.status.failed", ("version", Application.version));
                Debug.LogWarning("Update: " + statusLabel?.text);
            }

            if (Array.IndexOf(args, "-checkUpdates") >= 0)
                CheckForUpdates(true);
            else if (!automated && !quiet && !Application.isEditor && PlayerPrefs.GetInt(UpdatesAtStartKey, 1) != 0 && UpdateCheckDue())
                CheckForUpdates(false);
        }

        /// <summary>Asks GitHub for the latest release; <paramref name="manual"/> shows every outcome, otherwise only a new release.</summary>
        private void CheckForUpdates(bool manual)
        {
            if (updateForm == null || updateCancellation != null)
                return;
            if (manual)
            {
                updateForm.ShowChecking();
                updateDialog.style.display = DisplayStyle.Flex;
            }
            var cancellation = new CancellationTokenSource();
            updateCancellation = cancellation;
            KeepRunning(true);
            var task = MapWorksUpdates.FetchLatestAsync(updateSource, UserAgent, cancellation.Token);
            IVisualElementScheduledItem poll = null;
            poll = updateDialog.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                    return;
                poll.Pause();
                KeepRunning(false);
                if (updateCancellation != cancellation)
                    return;
                updateCancellation = null;
                cancellation.Dispose();
                if (task.IsCanceled)
                    return;
                if (task.IsFaulted)
                {
                    var message = Cause(task.Exception);
                    Debug.LogWarning(task.Exception);
                    Debug.LogWarning("Update: the check failed: " + message);
                    if (manual)
                        updateForm.ShowFailed(Loc.T("updates.title.check"), Loc.T("updates.check_failed", ("reason", message)), false);
                    return;
                }
                PlayerPrefs.SetString(UpdatesLastCheckKey, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                var release = task.Result;
                if (release == null)
                {
                    Debug.Log("Update: there is no release yet.");
                    if (manual)
                        updateForm.ShowNoRelease();
                    return;
                }
                if (!MapWorksUpdates.IsNewer(release, Application.version))
                {
                    Debug.Log("Update: " + Application.version + " is the newest version (latest release " + release.Tag + ").");
                    if (manual)
                        updateForm.ShowCurrent(Application.version);
                    return;
                }
                if (!manual && release.Tag == PlayerPrefs.GetString(UpdatesSkippedKey, string.Empty))
                {
                    Debug.Log("Update: " + release.Tag + " is skipped.");
                    return;
                }
                // At start the window waits for other dialogs, such as the crash report.
                if (!manual && (IsShown(bugReportDialog) || IsShown(newMapDialog)))
                    return;
                offeredRelease = release;
                var problem = InstallProblem(release);
                Debug.Log("Update: " + release.Tag + " is available" + (problem != null ? "; " + problem : "."));
                updateForm.ShowAvailable(release, Application.version, problem);
                updateDialog.style.display = DisplayStyle.Flex;
                if (updateInstallWhenFound && problem == null)
                    InstallUpdate();
            }).Every(100);
        }

        /// <summary>Why MapWorks cannot install the release itself, or null when it can.</summary>
        private static string InstallProblem(MapWorksRelease release)
        {
            if (Application.isEditor)
                return Loc.T("updates.problem.editor");
            if (release.AssetUrl == null)
                return Loc.T("updates.problem.no_asset", ("asset", MapWorksUpdates.AssetSuffix.TrimStart('-')));
            try
            {
                var probe = Path.Combine(ProgramFolder, ".mapworks-write-test");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return Loc.T("updates.problem.not_writable", ("folder", ProgramFolder));
            }
        }

        /// <summary>Downloads, checks and unpacks the offered release beside the program, then hands over to the script and quits.</summary>
        private void InstallUpdate()
        {
            var release = offeredRelease;
            if (release == null || updateCancellation != null)
                return;
            var version = release.Version;
            var staging = Path.Combine(ProgramFolder, MapWorksUpdates.StagingPrefix + version);
            var zip = Path.Combine(staging, release.AssetName);
            var cancellation = new CancellationTokenSource();
            updateCancellation = cancellation;
            KeepRunning(true);
            long done = 0;
            long total = release.AssetSize;
            updateForm.ShowDownloading(version, 0, total);
            Debug.Log("Update: downloading " + release.AssetUrl);
            var task = Task.Run(async () =>
            {
                Directory.CreateDirectory(staging);
                await MapWorksUpdates.DownloadAsync(release, zip, UserAgent, (bytes, size) =>
                {
                    Interlocked.Exchange(ref done, bytes);
                    Interlocked.Exchange(ref total, size);
                }, cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                return MapWorksUpdates.Unpack(zip, Path.Combine(staging, "new"), ProgramExe);
            });
            IVisualElementScheduledItem poll = null;
            poll = updateDialog.schedule.Execute(() =>
            {
                if (!task.IsCompleted)
                {
                    updateForm.ShowDownloading(version, Interlocked.Read(ref done), Interlocked.Read(ref total));
                    return;
                }
                poll.Pause();
                KeepRunning(false);
                var cancelled = updateCancellation != cancellation;
                updateCancellation = null;
                cancellation.Dispose();
                if (cancelled || task.IsCanceled)
                {
                    DeleteFolder(staging);
                    return;
                }
                if (task.IsFaulted)
                {
                    DeleteFolder(staging);
                    var message = Cause(task.Exception);
                    Debug.LogWarning(task.Exception);
                    Debug.LogWarning("Update: the download failed: " + message);
                    updateForm.ShowFailed(Loc.T("updates.install_failed.title", ("version", version)), Loc.T("updates.install_failed.stays", ("reason", message)), true);
                    return;
                }
                StartInstallScript(release, staging, task.Result);
            }).Every(100);
        }

        /// <summary>Writes the script beside the unpacked files, starts it hidden and quits, so it can swap the program files.</summary>
        private void StartInstallScript(MapWorksRelease release, string staging, string newProgram)
        {
            var restart = new List<string> { "-updatedFrom", Application.version };
            if (session?.ProjectFilePath != null)
                restart.AddRange(new[] { "-openProject", session.ProjectFilePath });
            else if (currentSummary?.GlobalConfigPath != null)
                restart.AddRange(new[] { "-openMap", currentSummary.GlobalConfigPath });
            var log = Path.Combine(Application.persistentDataPath, "update.log");
            var script = Path.Combine(staging, "install.ps1");
            try
            {
                // With a byte order mark, so Windows PowerShell reads paths beyond ASCII right.
                File.WriteAllText(script, MapWorksUpdates.InstallScript(Process.GetCurrentProcess().Id, ProgramFolder, newProgram, staging, ProgramExe, log,
                    Application.version, release.Version, restart), new UTF8Encoding(true));
                Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File " + MapWorksUpdates.CommandLineArgument(script))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // Not in the staging folder, which the script removes at the end.
                    WorkingDirectory = ProgramFolder
                });
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is System.ComponentModel.Win32Exception || exception is InvalidOperationException)
            {
                DeleteFolder(staging);
                Debug.LogWarning("Update: the installer could not be started: " + exception.Message);
                updateForm.ShowFailed(Loc.T("updates.install_failed.title", ("version", release.Version)), Loc.T("updates.installer_failed", ("reason", exception.Message)), true);
                return;
            }
            updateForm.ShowInstalling(release.Version);
            Debug.Log("Update: installing " + release.Tag + " from " + newProgram + "; MapWorks quits.");
            // A frame for the window to show, then quit so the script can replace the files, in the background too.
            KeepRunning(true);
            updateDialog.schedule.Execute(Application.Quit).StartingIn(300);
        }

        /// <summary>
        /// Lets the player run on while it checks or downloads with its window in the background; without it Unity pauses
        /// the player, and with it the update, as soon as another window has the focus.
        /// </summary>
        private void KeepRunning(bool on)
        {
            if (on && runInBackgroundBefore == null)
            {
                runInBackgroundBefore = Application.runInBackground;
                Application.runInBackground = true;
            }
            else if (!on && runInBackgroundBefore != null)
            {
                Application.runInBackground = runInBackgroundBefore.Value;
                runInBackgroundBefore = null;
            }
        }

        private void CloseUpdates()
        {
            updateCancellation?.Cancel();
            updateCancellation = null;
            HidePopup(updateDialog);
        }

        private static bool UpdateCheckDue()
        {
            return !DateTime.TryParse(PlayerPrefs.GetString(UpdatesLastCheckKey, string.Empty), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last) ||
                   DateTime.UtcNow - last.ToUniversalTime() > UpdateCheckInterval;
        }

        /// <summary>Removes the unpacked update and the replaced files a finished or failed update left in the program folder.</summary>
        private static void ClearUpdateLeftovers()
        {
            try
            {
                foreach (var folder in Directory.GetDirectories(ProgramFolder, MapWorksUpdates.StagingPrefix + "*"))
                    DeleteFolder(folder);
                foreach (var entry in Directory.GetFileSystemEntries(ProgramFolder, "*" + MapWorksUpdates.OldSuffix))
                {
                    if (Directory.Exists(entry))
                        DeleteFolder(entry);
                    else
                        File.Delete(entry);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Update: leftovers of an update could not be removed: " + exception.Message);
            }
        }

        private static void DeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Update: " + folder + " could not be removed: " + exception.Message);
            }
        }

        /// <summary>The innermost message of a failed task, which names the cause rather than "an error occurred".</summary>
        private static string Cause(Exception exception)
        {
            if (exception == null)
                return Loc.T("updates.unknown_error");
            while (exception.InnerException != null)
                exception = exception.InnerException;
            return exception.Message;
        }

        private static string LastLine(string file)
        {
            try
            {
                var lines = File.ReadAllLines(file);
                for (var index = lines.Length - 1; index >= 0; index--)
                    if (lines[index].Contains("Failed:"))
                        return lines[index].Substring(lines[index].IndexOf("Failed:", StringComparison.Ordinal) + 7).Trim();
                return null;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                return null;
            }
        }
    }
}
