using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A release of MapWorks on GitHub, as its latest-release answer describes it.</summary>
    public sealed class MapWorksRelease
    {
        public string Tag;
        public string Name;
        public string Notes;
        public string PageUrl;
        public DateTime? Published;

        /// <summary>The Windows download, `OMSI-MapWorks-&lt;version&gt;-win64.zip`, or null when the release has none.</summary>
        public string AssetName;
        public string AssetUrl;
        public long AssetSize;

        /// <summary>The download's SHA-256 in lower-case hex, when GitHub gives one.</summary>
        public string AssetSha256;

        /// <summary>The version the tag names, such as 1.2.0 for v1.2.</summary>
        public string Version => MapWorksUpdates.TryParseVersion(Tag, out var version) ? MapWorksUpdates.Format(version) : Tag;
    }

    /// <summary>
    /// Updates from the releases of the GitHub repository (ADR 054): the latest release, whether it is newer than the
    /// running version, its Windows zip downloaded and checked, unpacked beside the program, and the script that swaps
    /// the program files once MapWorks has quit and starts it again. Without Unity, so it can be tested.
    /// </summary>
    public static class MapWorksUpdates
    {
        public const string LatestReleaseUrl = "https://api.github.com/repos/Tiegertosch/Omsi-2-Mapforger/releases/latest";
        public const string AssetSuffix = "-win64.zip";

        /// <summary>
        /// The folder beside the program that holds an unpacked update until the script swaps it in; short, since Windows
        /// allows paths of 259 characters and the player's deepest files are long already.
        /// </summary>
        public const string StagingPrefix = ".mwu-";

        /// <summary>The longest path Windows allows without long-path support.</summary>
        public const int MaximumPathLength = 259;

        /// <summary>What the script appends to the names of replaced program files until the new ones run.</summary>
        public const string OldSuffix = ".mapworks-old";

        private const int BufferSize = 81920;

        /// <summary>
        /// Reads a version such as "v1.2", "1.2.3" or "1.2.3-beta": a leading v and anything from a dash or plus on are left
        /// out, missing parts count as 0.
        /// </summary>
        public static bool TryParseVersion(string text, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim();
            if (text[0] == 'v' || text[0] == 'V')
                text = text.Substring(1);
            var end = text.IndexOfAny(new[] { '-', '+', ' ' });
            if (end >= 0)
                text = text.Substring(0, end);
            var parts = text.Split('.');
            if (parts.Length == 0 || parts.Length > 4)
                return false;
            var numbers = new int[4];
            for (var index = 0; index < parts.Length; index++)
                if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
                    return false;
            version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
            return true;
        }

        /// <summary>The version as three parts, or four when the fourth is not 0.</summary>
        public static string Format(Version version)
        {
            return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
        }

        /// <summary>Whether the release is newer than the running version; an unreadable version is never newer.</summary>
        public static bool IsNewer(MapWorksRelease release, string currentVersion)
        {
            return release != null && TryParseVersion(release.Tag, out var offered) && TryParseVersion(currentVersion, out var current) && offered > current;
        }

        /// <summary>Reads GitHub's answer for one release.</summary>
        /// <exception cref="InvalidDataException">The answer is not a release.</exception>
        public static MapWorksRelease ParseRelease(string json)
        {
            if (!(OmsiJson.Parse(json) is Dictionary<string, object> answer) || !(answer.TryGetValue("tag_name", out var tag) && tag is string tagName))
                throw new InvalidDataException("The answer is not a GitHub release.");
            var release = new MapWorksRelease
            {
                Tag = tagName,
                Name = Text(answer, "name") ?? tagName,
                Notes = Text(answer, "body") ?? string.Empty,
                PageUrl = Text(answer, "html_url"),
            };
            if (DateTime.TryParse(Text(answer, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var published))
                release.Published = published;
            if (answer.TryGetValue("assets", out var assets) && assets is List<object> list)
                foreach (var item in list)
                {
                    if (!(item is Dictionary<string, object> asset))
                        continue;
                    var name = Text(asset, "name");
                    if (name == null || !name.EndsWith(AssetSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    release.AssetName = name;
                    release.AssetUrl = Text(asset, "browser_download_url");
                    release.AssetSize = asset.TryGetValue("size", out var size) && size is double bytes ? (long)bytes : 0;
                    var digest = Text(asset, "digest");
                    if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        release.AssetSha256 = digest.Substring(7).ToLowerInvariant();
                    break;
                }
            return release;
        }

        /// <summary>
        /// The latest release at <paramref name="url"/> (GitHub's API unless a check names another), or null when the
        /// repository has none yet.
        /// </summary>
        /// <exception cref="HttpRequestException">GitHub could not be reached or answered with an error.</exception>
        public static async Task<MapWorksRelease> FetchLatestAsync(string url, string userAgent, CancellationToken cancellation)
        {
            CheckAddress(url);
            using (var client = Client(userAgent, 30))
                // A second try after a second, for a connection the network dropped.
                for (var attempt = 1; ; attempt++)
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                        {
                            request.Headers.Accept.ParseAdd("application/vnd.github+json");
                            using (var response = await client.SendAsync(request, cancellation).ConfigureAwait(false))
                            {
                                if (response.StatusCode == HttpStatusCode.NotFound)
                                    return null;
                                if (!response.IsSuccessStatusCode)
                                    throw new HttpRequestException("GitHub answered " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                                return ParseRelease(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                            }
                        }
                    }
                    catch (HttpRequestException) when (attempt == 1 && !cancellation.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellation).ConfigureAwait(false);
                    }
        }

        /// <summary>
        /// Downloads the release's zip to <paramref name="file"/> and checks its size and, when GitHub gives it, its
        /// SHA-256; <paramref name="progress"/> gets the bytes so far and the whole size (0 when unknown).
        /// </summary>
        /// <exception cref="InvalidDataException">The download is not the file GitHub describes.</exception>
        public static async Task DownloadAsync(MapWorksRelease release, string file, string userAgent, Action<long, long> progress, CancellationToken cancellation)
        {
            if (release?.AssetUrl == null)
                throw new InvalidOperationException("The release has no Windows download.");
            CheckAddress(release.AssetUrl);
            Directory.CreateDirectory(Path.GetDirectoryName(file) ?? ".");
            var part = file + ".part";
            using (var client = Client(userAgent, 600))
            using (var response = await client.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException("The download answered " + (int)response.StatusCode + " " + response.ReasonPhrase + ".");
                var total = response.Content.Headers.ContentLength ?? release.AssetSize;
                using (var hash = SHA256.Create())
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                {
                    var buffer = new byte[BufferSize];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
                        hash.TransformBlock(buffer, 0, read, null, 0);
                        done += read;
                        progress?.Invoke(done, total);
                    }
                    hash.TransformFinalBlock(buffer, 0, 0);
                    if (release.AssetSize > 0 && done != release.AssetSize)
                        throw new InvalidDataException("The download has " + done + " bytes instead of " + release.AssetSize + ".");
                    var sha256 = Hex(hash.Hash);
                    if (release.AssetSha256 != null && sha256 != release.AssetSha256)
                        throw new InvalidDataException("The download's checksum does not match the release.");
                }
            }
            if (File.Exists(file))
                File.Delete(file);
            File.Move(part, file);
        }

        /// <summary>
        /// Unpacks the zip into <paramref name="folder"/>, emptied first, and returns the folder in it that holds the
        /// program: the one with UnityPlayer.dll, <paramref name="exeName"/> and its _Data folder.
        /// </summary>
        /// <exception cref="InvalidDataException">The zip leaves its folder or holds no MapWorks.</exception>
        public static string Unpack(string zip, string folder, string exeName)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var entry in archive.Entries)
                {
                    // Measured before Path.GetFullPath, which refuses such paths with a less helpful message.
                    var length = root.Length + entry.FullName.Length;
                    if (length > MaximumPathLength)
                        throw new PathTooLongException("MapWorks lies in a folder too deep to update it: unpacking needs paths of " + length +
                                                       " characters, Windows allows " + MaximumPathLength + ". Move MapWorks to a shorter folder, such as D:\\OMSI MapWorks.");
                    var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    // No entry may leave the folder ("zip slip").
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The download holds a file outside its folder: " + entry.FullName);
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target) ?? root);
                    entry.ExtractToFile(target, true);
                }
            var program = FindProgram(folder, exeName, 2);
            if (program == null)
                throw new InvalidDataException("The download holds no " + exeName + " with its UnityPlayer.dll and " + Path.GetFileNameWithoutExtension(exeName) + "_Data folder.");
            return program;
        }

        /// <summary>
        /// The PowerShell script that waits for MapWorks (process <paramref name="processId"/>) to quit, renames each file
        /// and folder of the program that the update brings to *.mapworks-old, moves the new ones in from
        /// <paramref name="newProgram"/>, which lies on the same drive, and starts the new MapWorks with
        /// <paramref name="startArguments"/>. When a step fails it puts the old files back and starts the old MapWorks
        /// with -updateFailed and the log. Other files in the program folder are left alone.
        /// </summary>
        public static string InstallScript(int processId, string programFolder, string newProgram, string stagingFolder, string exeName, string log,
            string oldVersion, string newVersion, IList<string> startArguments)
        {
            string Quote(string text) => "'" + text.Replace("'", "''") + "'";
            // Start-Process of Windows PowerShell 5.1 joins a list without quotes, so the line is quoted here.
            var arguments = new StringBuilder();
            foreach (var argument in startArguments)
                arguments.Append(arguments.Length > 0 ? " " : string.Empty).Append(CommandLineArgument(argument));

            var script = new StringBuilder();
            script.Append("# Swaps in an update of OMSI MapWorks (ADR 054) once MapWorks has quit.\r\n");
            script.Append("$ErrorActionPreference = 'Stop'\r\n");
            script.Append("$program = ").Append(Quote(programFolder)).Append("\r\n");
            script.Append("$source = ").Append(Quote(newProgram)).Append("\r\n");
            script.Append("$staging = ").Append(Quote(stagingFolder)).Append("\r\n");
            script.Append("$exe = Join-Path $program ").Append(Quote(exeName)).Append("\r\n");
            script.Append("$log = ").Append(Quote(log)).Append("\r\n");
            script.Append("$arguments = ").Append(Quote(arguments.ToString())).Append("\r\n");
            script.Append("$old = ").Append(Quote(OldSuffix)).Append("\r\n");
            script.Append("$version = ").Append(Quote(newVersion)).Append("\r\n");
            script.Append(@"function Write-Log($text) { Add-Content -LiteralPath $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $text) -Encoding UTF8 }
function Retry($action) {
    for ($try = 1; ; $try++) {
        try { & $action; return } catch { if ($try -ge 20) { throw }; Start-Sleep -Milliseconds 500 }
    }
}
Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue
Write-Log ('Update from ' + ").Append(Quote(oldVersion)).Append(@" + ' into ' + $program)
try { Wait-Process -Id ").Append(processId.ToString(CultureInfo.InvariantCulture)).Append(@" -Timeout 120 -ErrorAction Stop } catch { }
$renamed = New-Object System.Collections.Generic.List[string]
$placed = New-Object System.Collections.Generic.List[string]
try {
    $items = @(Get-ChildItem -LiteralPath $source -Force)
    foreach ($item in $items) {
        $target = Join-Path $program $item.Name
        if (Test-Path -LiteralPath ($target + $old)) { Retry { Remove-Item -LiteralPath ($target + $old) -Recurse -Force } }
        if (Test-Path -LiteralPath $target) {
            Retry { Rename-Item -LiteralPath $target -NewName ($item.Name + $old) }
            $renamed.Add($target)
        }
    }
    foreach ($item in $items) {
        $target = Join-Path $program $item.Name
        Retry { Move-Item -LiteralPath $item.FullName -Destination $target }
        $placed.Add($target)
    }
    Write-Log ('Swapped ' + $items.Count + ' files and folders.')
    # A MapWorks the setup installed here shows its new version in Windows' list of apps (ADR 055).
    try {
        $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OMSI MapWorks'
        $entry = Get-ItemProperty -LiteralPath $key -ErrorAction Stop
        if ($entry.InstallLocation.TrimEnd('\') -eq $program.TrimEnd('\')) { Set-ItemProperty -LiteralPath $key -Name DisplayVersion -Value $version }
    } catch { }
    foreach ($target in $renamed) { try { Remove-Item -LiteralPath ($target + $old) -Recurse -Force } catch { Write-Log ('Left ' + $target + $old + ': ' + $_) } }
    try { Remove-Item -LiteralPath $staging -Recurse -Force } catch { Write-Log ('Left ' + $staging + ': ' + $_) }
    Write-Log ('Done. Starting ' + $exe + ' ' + $arguments)
    Start-Process -FilePath $exe -WorkingDirectory $program -ArgumentList $arguments
}
catch {
    Write-Log ('Failed: ' + $_)
    foreach ($target in $placed) { try { Remove-Item -LiteralPath $target -Recurse -Force } catch { Write-Log ('Could not remove ' + $target + ': ' + $_) } }
    foreach ($target in $renamed) { try { Rename-Item -LiteralPath ($target + $old) -NewName (Split-Path $target -Leaf) } catch { Write-Log ('Could not restore ' + $target + ': ' + $_) } }
    Write-Log 'The old version is back.'
    Start-Process -FilePath $exe -WorkingDirectory $program -ArgumentList ('-updateFailed ""' + $log + '""')
}
");
            return script.ToString();
        }

        /// <summary>Only HTTPS addresses, or plain HTTP to this computer for a check.</summary>
        /// <exception cref="InvalidOperationException">The address is neither.</exception>
        public static void CheckAddress(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
                throw new InvalidOperationException("Updates come only over HTTPS: " + url);
        }

        /// <summary>
        /// Release notes as plain text for the window: headings without their #, bullets as •, without ** and __ around
        /// words and without the backticks of code.
        /// </summary>
        public static string NotesAsText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
                return string.Empty;
            var text = new StringBuilder();
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                var indent = line.Length - line.TrimStart().Length;
                var content = line.TrimStart();
                if (content.StartsWith("#", StringComparison.Ordinal))
                    content = content.TrimStart('#').TrimStart();
                else if (content.StartsWith("- ", StringComparison.Ordinal) || content.StartsWith("* ", StringComparison.Ordinal))
                    content = "• " + content.Substring(2);
                content = content.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);
                text.Append(' ', indent).Append(content).Append('\n');
            }
            return text.ToString().Trim('\n');
        }

        /// <summary>One argument for a Windows command line, quoted the way the C runtime reads it back.</summary>
        public static string CommandLineArgument(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                return argument;
            var quoted = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                // Backslashes before a quote are doubled, and the quote escaped.
                quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes).Append(character);
                backslashes = 0;
            }
            // Backslashes before the closing quote are doubled too.
            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
        }

        private static string FindProgram(string folder, string exeName, int depth)
        {
            if (File.Exists(Path.Combine(folder, exeName)) && File.Exists(Path.Combine(folder, "UnityPlayer.dll")) &&
                Directory.Exists(Path.Combine(folder, Path.GetFileNameWithoutExtension(exeName) + "_Data")))
                return folder;
            if (depth == 0)
                return null;
            foreach (var child in Directory.GetDirectories(folder))
            {
                var found = FindProgram(child, exeName, depth - 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static HttpClient Client(string userAgent, int seconds)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
            // GitHub's API refuses requests without a user agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            return client;
        }

        private static string Text(Dictionary<string, object> json, string key)
        {
            return json.TryGetValue(key, out var value) ? value as string : null;
        }

        private static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return text.ToString();
        }
    }
}
