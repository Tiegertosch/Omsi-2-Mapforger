using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OmsiMapEditor.Setup
{
    /// <summary>
    /// What the setup does, without its window (ADR 055): MapWorks for the current user into a folder of its own, its
    /// Start menu and desktop shortcuts, and its entry in Windows' list of apps; and all of it away again.
    /// </summary>
    internal static class SetupInstallation
    {
        public const string AppName = "OMSI MapWorks";
        public const string ExeName = "OMSIMapWorks.exe";
        public const string UninstallerName = "Uninstall OMSI MapWorks.exe";
        private const string PayloadResource = "Payload.zip";
        private const string ZipRoot = "OMSI MapWorks/";
        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OMSI MapWorks";
        private const string ProjectUrl = "https://github.com/Tiegertosch/Omsi-2-Mapforger";
        private const int MaximumPathLength = 259;

        /// <summary>Whether this program carries MapWorks to install; the uninstaller in the program folder does not.</summary>
        public static bool HasPayload
        {
            get
            {
                using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
                    return payload != null;
            }
        }

        /// <summary>The version this setup installs, as BuildRelease wrote it.</summary>
        public static string Version
        {
            get
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Version.txt"))
                {
                    if (stream == null)
                        return string.Empty;
                    using (var reader = new StreamReader(stream))
                        return reader.ReadToEnd().Trim();
                }
            }
        }

        /// <summary>Where a setup installed MapWorks before, or null.</summary>
        public static string InstalledFolder
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                {
                    var folder = key == null ? null : key.GetValue("InstallLocation") as string;
                    return string.IsNullOrEmpty(folder) ? null : folder;
                }
            }
        }

        /// <summary>The version a setup or an update installed before, or null.</summary>
        public static string InstalledVersion
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey))
                    return key == null ? null : key.GetValue("DisplayVersion") as string;
            }
        }

        /// <summary>The folder offered first: the one installed before, else Programs in the user's local app data.</summary>
        public static string DefaultFolder
        {
            get
            {
                return InstalledFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
            }
        }

        /// <summary>
        /// The folder MapWorks goes into for the one the user chose: that folder when it is empty, missing or MapWorks
        /// already, else a folder "OMSI MapWorks" in it, so uninstalling never removes anything else.
        /// </summary>
        /// <exception cref="ArgumentException">The text is no folder.</exception>
        public static string ResolveFolder(string chosen)
        {
            if (string.IsNullOrWhiteSpace(chosen))
                throw new ArgumentException("Choose a folder to install into.");
            var folder = Path.GetFullPath(chosen.Trim()).TrimEnd('\\');
            if (folder.EndsWith(":", StringComparison.Ordinal) || string.Equals(Path.GetPathRoot(folder + "\\").TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase))
                return Path.Combine(folder + "\\", AppName);
            if (Directory.Exists(folder) && !File.Exists(Path.Combine(folder, ExeName)) && Directory.EnumerateFileSystemEntries(folder).Any())
                return Path.Combine(folder, AppName);
            return folder;
        }

        /// <summary>Why MapWorks cannot go into the folder now, or null when it can.</summary>
        public static string CheckFolder(string folder)
        {
            var longest = PayloadFiles().Select(name => name.Length).DefaultIfEmpty(0).Max();
            if (folder.Length + 1 + longest > MaximumPathLength)
                return "This folder is too deep for MapWorks' files; Windows allows " + MaximumPathLength + " characters. Choose a shorter one.";
            if (IsRunning(folder))
                return "OMSI MapWorks is running from this folder. Close it first.";
            return null;
        }

        /// <summary>
        /// Puts MapWorks into the folder, replacing a MapWorks there, adds the shortcuts asked for and the entry in
        /// Windows' list of apps; <paramref name="progress"/> gets the share done and the file at hand.
        /// </summary>
        public static void Install(string folder, bool startMenu, bool desktop, Action<double, string> progress)
        {
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                var entries = archive.Entries.Where(entry => entry.FullName.StartsWith(ZipRoot, StringComparison.Ordinal) && entry.FullName.Length > ZipRoot.Length).ToList();
                if (!entries.Any(entry => entry.FullName == ZipRoot + ExeName))
                    throw new InvalidDataException("This setup holds no " + ExeName + ".");
                var total = Math.Max(1L, entries.Sum(entry => entry.Length));

                // A MapWorks there before goes first, so no file of an older version stays behind.
                if (File.Exists(Path.Combine(folder, ExeName)))
                {
                    progress(0, "Removing the version installed before");
                    foreach (var entry in Directory.GetFileSystemEntries(folder))
                        Delete(entry);
                }
                Directory.CreateDirectory(folder);
                var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
                long done = 0;
                foreach (var entry in entries)
                {
                    var relative = entry.FullName.Substring(ZipRoot.Length);
                    var target = Path.GetFullPath(Path.Combine(root, relative));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The setup holds a file outside its folder: " + entry.FullName);
                    if (relative.EndsWith("/", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                    done += entry.Length;
                    progress((double)done / total, relative);
                }
                Register(folder, total);
            }
            var exe = Path.Combine(folder, ExeName);
            if (startMenu)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"), exe);
            if (desktop)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"), exe);
            progress(1, string.Empty);
        }

        /// <summary>Why the uninstaller in <paramref name="folder"/> cannot remove MapWorks, or null when it can.</summary>
        public static string CheckUninstall(string folder)
        {
            if (!SamePath(InstalledFolder, folder))
                return "This copy of MapWorks was not installed with its setup, so the uninstaller leaves it alone. Delete its folder yourself.";
            if (IsRunning(folder))
                return "OMSI MapWorks is running. Close it first.";
            return null;
        }

        /// <summary>
        /// Removes the shortcuts, the entry in Windows' list of apps and every file in the folder but the running
        /// uninstaller, which a hidden command removes with the folder once it has quit.
        /// </summary>
        public static void Uninstall(string folder, Action<double, string> progress)
        {
            foreach (var shortcut in new[]
                     {
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"),
                         Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk")
                     })
                if (File.Exists(shortcut))
                    File.Delete(shortcut);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);

            var self = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            var entries = Directory.GetFileSystemEntries(folder).Where(entry => !SamePath(entry, self)).ToList();
            for (var index = 0; index < entries.Count; index++)
            {
                progress((double)index / Math.Max(1, entries.Count), Path.GetFileName(entries[index]));
                Delete(entries[index]);
            }
            progress(1, string.Empty);
        }

        /// <summary>Removes the folder with the uninstaller in it a few seconds after this process has quit.</summary>
        public static void RemoveAfterExit(string folder)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"" + folder.TrimEnd('\\') + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath()
            });
        }

        public static void Start(string folder)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(folder, ExeName)) { WorkingDirectory = folder, UseShellExecute = false });
        }

        /// <summary>The paths of the payload's files inside the program folder.</summary>
        private static List<string> PayloadFiles()
        {
            using (var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (payload == null)
                    return new List<string>();
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                    return archive.Entries.Where(entry => entry.FullName.StartsWith(ZipRoot, StringComparison.Ordinal))
                        .Select(entry => entry.FullName.Substring(ZipRoot.Length)).ToList();
            }
        }

        private static void Register(string folder, long bytes)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                var uninstaller = Path.Combine(folder, UninstallerName);
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", AppName);
                key.SetValue("DisplayIcon", Path.Combine(folder, ExeName));
                key.SetValue("InstallLocation", folder);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                key.SetValue("UninstallString", "\"" + uninstaller + "\"");
                key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" --silent");
                key.SetValue("URLInfoAbout", ProjectUrl);
                key.SetValue("HelpLink", ProjectUrl + "/wiki");
                key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, bytes / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>A shortcut through Windows' own WScript.Shell, so the setup needs no library for it.</summary>
        private static void CreateShortcut(string path, string target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var shellType = Type.GetTypeFromProgID("WScript.Shell", true);
            var shell = Activator.CreateInstance(shellType);
            try
            {
                var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                try
                {
                    var linkType = link.GetType();
                    linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                    linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(target) });
                    linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                    linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Edit maps for OMSI 2" });
                    linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(link);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static bool IsRunning(string folder)
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
                using (process)
                    try
                    {
                        if (SamePath(Path.GetDirectoryName(process.MainModule.FileName), folder))
                            return true;
                    }
                    catch (Win32Exception)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
            return false;
        }

        private static void Delete(string entry)
        {
            if (Directory.Exists(entry))
                Directory.Delete(entry, true);
            else if (File.Exists(entry))
                File.Delete(entry);
        }

        private static bool SamePath(string first, string second)
        {
            if (first == null || second == null)
                return false;
            return string.Equals(Path.GetFullPath(first).TrimEnd('\\'), Path.GetFullPath(second).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
