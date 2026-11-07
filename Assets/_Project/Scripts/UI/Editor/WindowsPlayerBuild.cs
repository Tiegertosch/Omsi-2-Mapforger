using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.Editor
{
    /// <summary>
    /// Validates and builds the Windows desktop player. Every build checks that no player file is a copy of an OMSI file;
    /// files that were not reviewed for OMSI content (ADR 005) are a warning in a normal build and stop a release build.
    /// </summary>
    public static class WindowsPlayerBuild
    {
        private const string StartupScenePath = "Assets/_Project/Scenes/EditorStartup.unity";
        private const string OutputPath = "Builds/Windows/OMSIMapWorks.exe";
        private const string UninstallerName = "Uninstall OMSI MapWorks.exe";
        private const string InstallerFolder = "Tools/Installer";
        private const string FontsFolder = "Assets/_Project/UI/Resources/Fonts";
        private const string LogoPath = "Assets/_Project/UI/Branding/Logo.png";
        private const string SetupIconPath = "Tools/Icon/MapWorks.ico";

        [MenuItem("OMSI MapWorks/Build/Windows Player")]
        public static void Build()
        {
            Build(false);
        }

        /// <summary>A player to hand out: every player file must be reviewed in its current form.</summary>
        [MenuItem("OMSI MapWorks/Build/Windows Player (Release)")]
        public static void BuildRelease()
        {
            Build(true);
        }

        /// <summary>Records the player files as reviewed, after a person checked that none of them is OMSI content.</summary>
        [MenuItem("OMSI MapWorks/Build/Record Reviewed Player Assets...")]
        public static void RecordReviewedPlayerAssets()
        {
            var paths = OmsiContentBoundary.GetPlayerAssetPaths(EnabledScenes()).ToList();
            var review = OmsiContentBoundary.ReviewPlayerAssets(paths);
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Record reviewed player assets",
                    (review.IsClean && review.RemovedFiles.Count == 0
                        ? "All player files are already reviewed."
                        : "Check these files for OMSI content, converted or not, before recording them:\n\n" + Shorten(review.ToString())),
                    "Record", "Cancel"))
                return;
            var count = OmsiContentBoundary.RecordReviewedPlayerAssets(paths);
            Debug.Log("Recorded " + count + " reviewed player files in " + OmsiContentBoundary.ReviewListPath +
                      (review.IsClean ? "." : ":\n" + review));
        }

        private static string Shorten(string text)
        {
            var lines = text.Split('\n');
            return lines.Length <= 25 ? text : string.Join("\n", lines.Take(25)) + "\n... and " + (lines.Length - 25) + " more";
        }

        private static string[] EnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        private static void Build(bool release)
        {
            var scenes = EnabledScenes();
            if (!scenes.Contains(StartupScenePath))
                throw new InvalidOperationException("The startup scene is missing from the enabled build scenes.");

            if (!OmsiContentBoundary.AreEditorToolsExcludedFromPlayer())
                throw new InvalidOperationException("Player build blocked: editor-only OMSI tools must remain excluded from the player.");

            var playerAssetPaths = OmsiContentBoundary.GetPlayerAssetPaths(scenes).ToList();
            var skippedSourcePaths = new List<string>();
            var copiedOmsiFile = OmsiContentBoundary.FindCopiedInstallFile(playerAssetPaths, skippedSourcePaths);
            foreach (var skippedPath in skippedSourcePaths)
                Debug.LogWarning("OMSI content check could not read and skipped: " + skippedPath);
            if (!string.IsNullOrEmpty(copiedOmsiFile))
            {
                throw new InvalidOperationException("Player build blocked: a project asset is byte-identical to a file in the remembered OMSI installation: " + copiedOmsiFile);
            }

            var review = OmsiContentBoundary.ReviewPlayerAssets(playerAssetPaths);
            if (!review.IsClean)
            {
                var message = (review.NewFiles.Count + review.ChangedFiles.Count) + " player file(s) were not reviewed for OMSI content in their current form; " +
                              "check them and choose OMSI MapWorks > Build > Record Reviewed Player Assets:\n" + review;
                if (release)
                    throw new InvalidOperationException("Release build blocked: " + message);
                Debug.LogWarning(message);
            }
            else if (review.RemovedFiles.Count > 0)
                Debug.Log("Reviewed player files no longer used:\n" + review);

            var startupScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartupScenePath);
            if (startupScene == null)
            {
                throw new InvalidOperationException("The editor startup scene could not be imported.");
            }

            var shell = Resources.Load<VisualTreeAsset>("EditorShell");
            if (shell == null)
            {
                throw new InvalidOperationException("EditorShell.uxml was not imported as a UI Toolkit resource.");
            }

            CheckLanguageFiles(release);

            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Windows player build failed: " + report.summary.result);
            }

            CopyLanguageFiles();
            Debug.Log((release ? "Windows release player built: " : "Windows player built: ") + Path.GetFullPath(OutputPath));
            if (release)
                PackRelease();
        }

        private const string LanguageSourceFolder = "locales";

        /// <summary>
        /// The language files are checked as a pull request is: a damaged or incomplete released language stops a release build and is
        /// a warning in any other (ADR 068). Without the English reference the program would show keys instead of texts, so that
        /// stops every build.
        /// </summary>
        private static void CheckLanguageFiles(bool release)
        {
            var report = LocaleValidator.Validate(LanguageSourceFolder);
            foreach (var issue in report.Issues.Where(item => item.Severity == LocaleIssueSeverity.Warning))
                Debug.LogWarning("Language files: " + issue);
            var errors = report.Issues.Where(item => item.Severity == LocaleIssueSeverity.Error).ToList();
            foreach (var issue in errors)
                Debug.LogError("Language files: " + issue);
            if (!Directory.Exists(Path.Combine(LanguageSourceFolder, TranslationManager.ReferenceLanguage)))
                throw new InvalidOperationException("Player build blocked: the folder locales/" + TranslationManager.ReferenceLanguage + " with the English texts is missing.");
            if (errors.Count > 0 && release)
                throw new InvalidOperationException("Release build blocked: " + errors.Count + " error(s) in the language files; run Tools/LocaleCheck to see them.");
        }

        /// <summary>
        /// Puts the language files into the player's StreamingAssets, next to the data every installation and update carries. They are
        /// plain files, so a translation can be added or changed there without a build.
        /// </summary>
        private static void CopyLanguageFiles()
        {
            var playerFolder = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
            var target = Path.Combine(playerFolder, Path.GetFileNameWithoutExtension(OutputPath) + "_Data", "StreamingAssets", LanguageSourceFolder);
            if (Directory.Exists(target))
                Directory.Delete(target, true);
            foreach (var file in Directory.GetFiles(LanguageSourceFolder, "*.json", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(LanguageSourceFolder, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination, true);
            }
        }

        /// <summary>
        /// Packs the release player as Builds/OMSI-MapWorks-&lt;version&gt;-win64.zip, the file a GitHub release carries
        /// for Help > Check for updates (ADR 054), with the program in a folder "OMSI MapWorks" and without Unity's folders
        /// that must not ship; the uninstaller goes into the program first, so updates bring it along. Then wraps the zip
        /// into Builds/OMSI-MapWorks-&lt;version&gt;-Setup.exe, the installer in the editor's design (ADR 055).
        /// </summary>
        private static void PackRelease()
        {
            var version = PlayerSettings.bundleVersion.Trim();
            var playerFolder = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
            CompileSetup(Path.Combine(playerFolder, UninstallerName), null, version);
            var zip = Path.GetFullPath(Path.Combine("Builds", "OMSI-MapWorks-" + version + "-win64.zip"));
            if (File.Exists(zip))
                File.Delete(zip);
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var file in Directory.GetFiles(playerFolder, "*", SearchOption.AllDirectories))
                {
                    var relative = file.Substring(playerFolder.Length + 1);
                    var top = relative.Split(Path.DirectorySeparatorChar)[0];
                    if (top.EndsWith("_DoNotShip", StringComparison.Ordinal) || top.EndsWith("_ButDontShipItWithYourGame", StringComparison.Ordinal))
                        continue;
                    archive.CreateEntryFromFile(file, "OMSI MapWorks/" + relative.Replace(Path.DirectorySeparatorChar, '/'), System.IO.Compression.CompressionLevel.Optimal);
                }
            string sha256;
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(zip))
                sha256 = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            Debug.Log("Release zip: " + zip + " (" + new FileInfo(zip).Length + " bytes, SHA-256 " + sha256 + "). Publish it on GitHub as a release with the tag v" +
                      version + "; older versions of MapWorks then offer it under Help > Check for updates.");

            var setup = Path.GetFullPath(Path.Combine("Builds", "OMSI-MapWorks-" + version + "-Setup.exe"));
            CompileSetup(setup, zip, version);
            Debug.Log("Release setup: " + setup + " (" + new FileInfo(setup).Length + " bytes). Attach it to the same release for people who install MapWorks for the first time.");
        }

        /// <summary>
        /// Compiles Tools/Installer with the C# compiler Unity brings along (Data/DotNetSdk) against the .NET Framework 4
        /// of Windows: with <paramref name="payload"/> the setup carrying that zip, without it the uninstaller.
        /// </summary>
        private static void CompileSetup(string output, string payload, string version)
        {
            var sdk = Path.Combine(EditorApplication.applicationContentsPath, "DotNetSdk");
            var dotnet = Path.Combine(sdk, "dotnet.exe");
            var csc = Directory.Exists(Path.Combine(sdk, "sdk"))
                ? Directory.GetFiles(Path.Combine(sdk, "sdk"), "csc.dll", SearchOption.AllDirectories).FirstOrDefault(path => path.Replace('\\', '/').Contains("/Roslyn/bincore/"))
                : null;
            if (!File.Exists(dotnet) || csc == null)
                throw new InvalidOperationException("Release build blocked: Unity's C# compiler was not found under " + sdk + ", so the setup cannot be built.");
            var framework = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319");
            if (!File.Exists(Path.Combine(framework, "System.Windows.Forms.dll")))
                throw new InvalidOperationException("Release build blocked: the .NET Framework 4 of Windows was not found under " + framework + ".");

            var versionFile = Path.GetFullPath(Path.Combine("Temp", "MapWorksSetupVersion.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(versionFile));
            File.WriteAllText(versionFile, version);
            string Quote(string text) => "\"" + text + "\"";
            var arguments = new List<string>
            {
                "exec", Quote(csc), "-nologo", "-noconfig", "-nostdlib", "-target:winexe", "-optimize+", "-platform:anycpu", "-langversion:7.3",
                "-out:" + Quote(output), "-win32manifest:" + Quote(Path.GetFullPath(Path.Combine(InstallerFolder, "app.manifest"))),
                "-win32icon:" + Quote(Path.GetFullPath(SetupIconPath)),
                "-resource:" + Quote(Path.GetFullPath(Path.Combine(FontsFolder, "Inter-Regular.ttf")) + ",Inter-Regular.ttf"),
                "-resource:" + Quote(Path.GetFullPath(Path.Combine(FontsFolder, "Inter-SemiBold.ttf")) + ",Inter-SemiBold.ttf"),
                "-resource:" + Quote(Path.GetFullPath(LogoPath) + ",Logo.png"),
                "-resource:" + Quote(versionFile + ",Version.txt")
            };
            foreach (var assembly in new[] { "mscorlib", "System", "System.Core", "System.Drawing", "System.Windows.Forms", "System.IO.Compression", "System.IO.Compression.FileSystem" })
                arguments.Add("-r:" + Quote(Path.Combine(framework, assembly + ".dll")));
            if (payload != null)
                arguments.Add("-resource:" + Quote(payload + ",Payload.zip"));
            foreach (var source in Directory.GetFiles(InstallerFolder, "*.cs"))
                arguments.Add(Quote(Path.GetFullPath(source)));

            var start = new System.Diagnostics.ProcessStartInfo(dotnet, string.Join(" ", arguments))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                // The compiler writes its errors to the standard output.
                RedirectStandardOutput = true
            };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                var messages = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Release build blocked: the setup did not compile:\n" + messages);
            }
        }
    }
}
