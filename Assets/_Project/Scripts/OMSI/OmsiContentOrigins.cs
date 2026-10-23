using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Where a file of the OMSI installation comes from.</summary>
    public enum OmsiContentOrigin
    {
        /// <summary>Part of OMSI 2 itself: every player has it.</summary>
        BaseGame,

        /// <summary>Part of a paid add-on: maps for everyone should not need it.</summary>
        Dlc,

        /// <summary>In no Steam package: freeware, own work or add-ons bought outside Steam.</summary>
        UserContent,

        /// <summary>Not known, because Steam's file lists were not found and the file is outside the base game folders.</summary>
        Unknown
    }

    /// <summary>
    /// Tells for each file of an OMSI installation whether it belongs to the base game, a DLC or neither. Steam keeps a
    /// manifest of every installed depot with the names of its files (<c>Steam\depotcache\depot_manifest.manifest</c>);
    /// the app manifest <c>appmanifest_252530.acf</c> names the installed depots and marks DLC depots with
    /// <c>dlcappid</c>. Files in no depot are user content. Without Steam's lists the base game folders of OMSI 2 count
    /// as base game and everything else as unknown.
    /// </summary>
    public sealed class OmsiContentOrigins
    {
        public const string OmsiAppId = "252530";

        /// <summary>The Sceneryobjects folders that OMSI 2 itself installs, for installations without Steam's lists.</summary>
        public static readonly string[] BaseGameObjectFolders =
        {
            "Agriculture", "Animals", "Buildings_DDR_RUE", "Buildings_JHY", "Buildings_MC", "Buildings_RUE", "Generic", "Kreuz_MC",
            "Kreuz_RUE", "Railroad_MC", "Railroad_RUE", "Ruede", "Static_Vehicles_RUE", "Streetobjects_MC", "Streetobjects_RUE",
            "Streetobjects_RW1HH", "Trees_MC", "Verkehrszeichen_MC", "Verkehrszeichen_RUE"
        };

        private readonly HashSet<string> baseFiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> dlcFiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> baseFolders = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> markedFolders = new List<string>();

        private OmsiContentOrigins()
        {
            foreach (var folder in BaseGameObjectFolders)
                baseFolders.Add(Normalize("Sceneryobjects\\" + folder));
        }

        /// <summary>True when Steam's file lists were read; otherwise only the base game folders are known.</summary>
        public bool FromSteam { get; private set; }

        /// <summary>A short account of what was read, for the status bar and the log.</summary>
        public string Description { get; private set; } = string.Empty;

        /// <summary>Without any lists: base game folders count as base game, the rest as unknown.</summary>
        public static OmsiContentOrigins FoldersOnly(string reason)
        {
            return new OmsiContentOrigins { Description = reason };
        }

        /// <summary>
        /// Reads Steam's lists for an installation in <c>steamapps\common\OMSI 2</c>. <paramref name="steamRoots"/> are
        /// folders that may hold Steam's <c>depotcache</c>; the library of the installation is always tried first.
        /// Missing or unreadable lists give <see cref="FoldersOnly"/>.
        /// </summary>
        public static OmsiContentOrigins ForInstallation(string installationRoot, IEnumerable<string> steamRoots)
        {
            var steamApps = Directory.GetParent(Path.GetFullPath(installationRoot))?.Parent;
            var appManifest = steamApps == null ? null : Path.Combine(steamApps.FullName, "appmanifest_" + OmsiAppId + ".acf");
            if (appManifest == null || !File.Exists(appManifest))
                return FoldersOnly("No Steam app manifest next to the installation; base game folders are assumed.");

            List<SteamDepot> depots;
            try
            {
                depots = SteamAppManifest.InstalledDepots(File.ReadAllText(appManifest, Encoding.UTF8));
            }
            catch (FormatException exception)
            {
                return FoldersOnly("The Steam app manifest could not be read (" + exception.Message + "); base game folders are assumed.");
            }

            var cacheFolders = new List<string> { Path.Combine(steamApps.FullName, "depotcache") };
            if (steamApps.Parent != null)
                cacheFolders.Add(Path.Combine(steamApps.Parent.FullName, "depotcache"));
            if (steamRoots != null)
                foreach (var root in steamRoots)
                    if (!string.IsNullOrEmpty(root))
                        cacheFolders.Add(Path.Combine(root, "depotcache"));

            var origins = new OmsiContentOrigins();
            var read = 0;
            var dlcDepots = 0;
            var missing = new List<string>();
            foreach (var depot in depots)
            {
                var name = depot.DepotId + "_" + depot.ManifestId + ".manifest";
                var path = cacheFolders.Find(folder => File.Exists(Path.Combine(folder, name)));
                if (path == null)
                {
                    missing.Add(depot.DepotId);
                    continue;
                }
                List<string> files;
                bool encrypted;
                try
                {
                    files = SteamDepotManifest.FileNames(File.ReadAllBytes(Path.Combine(path, name)), out encrypted);
                }
                catch (Exception exception) when (exception is IOException || exception is FormatException ||
                                                  exception is UnauthorizedAccessException)
                {
                    missing.Add(depot.DepotId);
                    continue;
                }
                if (encrypted)
                {
                    missing.Add(depot.DepotId);
                    continue;
                }
                var target = depot.IsDlc ? origins.dlcFiles : origins.baseFiles;
                foreach (var file in files)
                    target.Add(Normalize(file));
                read++;
                if (depot.IsDlc)
                    dlcDepots++;
            }

            // Without the base game's list nothing can be told apart reliably.
            if (origins.baseFiles.Count == 0)
                return FoldersOnly("Steam's file list of OMSI 2 was not found; base game folders are assumed.");
            origins.FromSteam = true;
            origins.Description = "Steam file lists: " + origins.baseFiles.Count + " base game files, " + dlcDepots + " DLC package(s)" +
                                  (missing.Count > 0 ? "; no list for depot(s) " + string.Join(", ", missing) : string.Empty) + ".";
            return origins;
        }

        /// <summary>
        /// Where Steam is usually installed: under the program folders and at the root of each fixed drive. Steam's own
        /// folder holds the depotcache, also for libraries on other drives.
        /// </summary>
        public static List<string> LikelySteamRoots()
        {
            var roots = new List<string>();
            foreach (var variable in new[] { "ProgramFiles(x86)", "ProgramFiles" })
            {
                var folder = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(folder))
                    roots.Add(Path.Combine(folder, "Steam"));
            }
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed)
                        continue;
                    roots.Add(Path.Combine(drive.Name, "Steam"));
                    roots.Add(Path.Combine(drive.Name, "Program Files (x86)", "Steam"));
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Without the drive list the program folders still count.
            }
            return roots;
        }

        /// <summary>
        /// Folders the user marked as DLC, relative to the installation such as <c>Sceneryobjects\SomeAddon</c>: add-ons
        /// bought outside Steam look like user content otherwise. Files below a marked folder count as DLC unless the base
        /// game ships them. Replaces the earlier marks.
        /// </summary>
        public void SetMarkedFolders(IEnumerable<string> folders)
        {
            markedFolders.Clear();
            if (folders == null)
                return;
            foreach (var folder in folders)
                if (!string.IsNullOrWhiteSpace(folder))
                    markedFolders.Add(Normalize(folder).TrimEnd('\\') + "\\");
        }

        public bool HasMarkedFolders => markedFolders.Count > 0;

        /// <summary>True when the file lies below a folder the user marked as DLC.</summary>
        public bool IsMarkedByHand(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || markedFolders.Count == 0)
                return false;
            var key = Normalize(relativePath);
            foreach (var folder in markedFolders)
                if (key.StartsWith(folder, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>The origin of a file given relative to the installation, such as <c>Sceneryobjects\Generic\bench.sco</c>.</summary>
        public OmsiContentOrigin Of(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return OmsiContentOrigin.Unknown;
            var key = Normalize(relativePath);
            OmsiContentOrigin origin;
            if (FromSteam)
            {
                // A file that the base game ships is there for everyone, even if a DLC ships it too.
                if (baseFiles.Contains(key))
                    return OmsiContentOrigin.BaseGame;
                origin = dlcFiles.Contains(key) ? OmsiContentOrigin.Dlc : OmsiContentOrigin.UserContent;
            }
            else
            {
                var separator = key.IndexOf('\\');
                var second = separator < 0 ? -1 : key.IndexOf('\\', separator + 1);
                if (second > 0 && baseFolders.Contains(key.Substring(0, second)))
                    return OmsiContentOrigin.BaseGame;
                origin = OmsiContentOrigin.Unknown;
            }
            return IsMarkedByHand(relativePath) ? OmsiContentOrigin.Dlc : origin;
        }

        private static string Normalize(string path)
        {
            return path.Replace('/', '\\').TrimStart('\\').ToLowerInvariant();
        }
    }

    /// <summary>An installed depot of an app: its manifest, and whether it belongs to a DLC.</summary>
    public sealed class SteamDepot
    {
        public string DepotId;
        public string ManifestId;
        public bool IsDlc;
    }

    /// <summary>Reads the installed depots from a Steam app manifest (<c>appmanifest_*.acf</c>, Valve KeyValues text).</summary>
    public static class SteamAppManifest
    {
        public static List<SteamDepot> InstalledDepots(string text)
        {
            var root = KeyValues.Parse(text);
            var depots = new List<SteamDepot>();
            if (!(root.TryGetValue("AppState", out var state) && state is Dictionary<string, object> app) ||
                !(app.TryGetValue("InstalledDepots", out var installed) && installed is Dictionary<string, object> list))
                return depots;
            foreach (var pair in list)
            {
                if (!(pair.Value is Dictionary<string, object> depot) || !(depot.TryGetValue("manifest", out var manifest) && manifest is string id))
                    continue;
                depots.Add(new SteamDepot { DepotId = pair.Key, ManifestId = id, IsDlc = depot.ContainsKey("dlcappid") });
            }
            return depots;
        }

        /// <summary>Valve KeyValues: quoted keys followed by a quoted value or a braced block; keys ignore case.</summary>
        private static class KeyValues
        {
            public static Dictionary<string, object> Parse(string text)
            {
                var position = 0;
                return Block(text, ref position, false);
            }

            private static Dictionary<string, object> Block(string text, ref int position, bool nested)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    var token = Next(text, ref position);
                    if (token == null)
                    {
                        if (nested)
                            throw new FormatException("a block is not closed");
                        return result;
                    }
                    if (token == "}")
                    {
                        if (!nested)
                            throw new FormatException("a block closes that was not opened");
                        return result;
                    }
                    var value = Next(text, ref position) ?? throw new FormatException("the key '" + token + "' has no value");
                    result[token] = value == "{" ? Block(text, ref position, true) : (object)value;
                }
            }

            /// <summary>The next quoted string, brace, or null at the end; comments start with //.</summary>
            private static string Next(string text, ref int position)
            {
                while (position < text.Length)
                {
                    var character = text[position];
                    if (char.IsWhiteSpace(character))
                    {
                        position++;
                        continue;
                    }
                    if (character == '/' && position + 1 < text.Length && text[position + 1] == '/')
                    {
                        while (position < text.Length && text[position] != '\n')
                            position++;
                        continue;
                    }
                    if (character == '{' || character == '}')
                    {
                        position++;
                        return character.ToString();
                    }
                    if (character != '"')
                        throw new FormatException("unexpected '" + character + "'");
                    var builder = new StringBuilder();
                    position++;
                    while (position < text.Length && text[position] != '"')
                    {
                        if (text[position] == '\\' && position + 1 < text.Length)
                            position++;
                        builder.Append(text[position++]);
                    }
                    position++;
                    return builder.ToString();
                }
                return null;
            }
        }
    }

    /// <summary>
    /// Reads the file names from a Steam depot manifest: sections of a magic number, a length and a protocol buffer
    /// message. The payload lists one mapping per file with its name as field 1; the metadata says in field 4 whether
    /// names are encrypted.
    /// </summary>
    public static class SteamDepotManifest
    {
        private const uint PayloadMagic = 0x71F617D0;
        private const uint MetadataMagic = 0x1F4812BE;
        private const uint EndMagic = 0x32C415AB;

        public static List<string> FileNames(byte[] data, out bool namesEncrypted)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var names = new List<string>();
            namesEncrypted = false;
            var position = 0;
            while (position + 8 <= data.Length)
            {
                var magic = BitConverter.ToUInt32(data, position);
                var length = BitConverter.ToInt32(data, position + 4);
                position += 8;
                if (magic == EndMagic || length < 0 || position + length > data.Length)
                    break;
                if (magic == PayloadMagic)
                {
                    foreach (var mapping in Fields(data, position, length))
                        if (mapping.Number == 1 && mapping.IsBytes)
                            foreach (var field in Fields(data, mapping.Offset, mapping.Length))
                                if (field.Number == 1 && field.IsBytes)
                                    names.Add(Encoding.UTF8.GetString(data, field.Offset, field.Length));
                }
                else if (magic == MetadataMagic)
                {
                    foreach (var field in Fields(data, position, length))
                        if (field.Number == 4 && !field.IsBytes)
                            namesEncrypted = field.Value != 0;
                }
                position += length;
            }
            return names;
        }

        private struct Field
        {
            public int Number;
            public bool IsBytes;
            public ulong Value;
            public int Offset;
            public int Length;
        }

        /// <summary>The fields of a protocol buffer message; length-delimited ones point into the data.</summary>
        private static IEnumerable<Field> Fields(byte[] data, int start, int length)
        {
            var position = start;
            var end = start + length;
            while (position < end)
            {
                var key = Varint(data, ref position);
                var field = new Field { Number = (int)(key >> 3) };
                switch ((int)(key & 7))
                {
                    case 0:
                        field.Value = Varint(data, ref position);
                        break;
                    case 1:
                        position += 8;
                        break;
                    case 2:
                        field.IsBytes = true;
                        field.Length = (int)Varint(data, ref position);
                        field.Offset = position;
                        position += field.Length;
                        if (position > end)
                            throw new FormatException("A manifest field runs past its message.");
                        break;
                    case 5:
                        position += 4;
                        break;
                    default:
                        throw new FormatException("Unknown protocol buffer wire type " + (key & 7) + ".");
                }
                yield return field;
            }
        }

        private static ulong Varint(byte[] data, ref int position)
        {
            ulong result = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (position >= data.Length)
                    throw new FormatException("A manifest number runs past the end.");
                var value = data[position++];
                result |= (ulong)(value & 0x7F) << shift;
                if (value < 0x80)
                    return result;
            }
            throw new FormatException("A manifest number is too long.");
        }
    }
}
