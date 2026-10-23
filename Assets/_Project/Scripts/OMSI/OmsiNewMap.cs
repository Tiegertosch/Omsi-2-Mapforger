using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The map folder <see cref="OmsiNewMap.Create"/> wrote.</summary>
    public sealed class OmsiNewMapReport
    {
        public string MapFolder { get; internal set; }
        public string GlobalConfigPath { get; internal set; }

        /// <summary>Helper files taken over from the template, relative to the new map folder.</summary>
        public List<string> CopiedFiles { get; } = new List<string>();
    }

    /// <summary>
    /// Creates a new OMSI map next to an existing one that serves as the template: its helper files (AI lists, drivers,
    /// humans, parked cars, registrations, holidays and the water textures) and the settings of its global.cfg, such as
    /// money system, ticket pack, years, ground textures, seasons and traffic density. The new map has one tile at 0, 0
    /// with flat terrain at height 0, no entry points, no timetable and a fresh ID counter, like a map that OMSI's own
    /// editor starts. Tiles, timetables, chrono changes, signal routes and the last situation of the template are not
    /// taken over, since they describe the template's places. The map is built in a temporary sibling folder and only
    /// renamed at the end, and an existing folder is never touched.
    /// </summary>
    public static class OmsiNewMap
    {
        public const int TileVersion = 14;
        public const string FirstTileFile = "tile_0_0.map";

        private const string TemporarySuffix = ".mapforge-new-tmp";

        /// <summary>Records of global.cfg that belong to the template's places and are left out.</summary>
        private static readonly string[] DroppedRecords = { "map", "entrypoints", "backgroundimage", "worldcoordinates" };

        /// <summary>True when a helper file of the template map folder is taken over.</summary>
        public static bool IsHelperFile(string fileName)
        {
            var name = fileName.ToLowerInvariant();
            return !(name == "global.cfg" || name.EndsWith(".dsc", StringComparison.Ordinal) || name.StartsWith("tile_", StringComparison.Ordinal) ||
                     name.StartsWith("laststn.", StringComparison.Ordinal) || name.StartsWith("signalroutes", StringComparison.Ordinal) ||
                     name.StartsWith("old_", StringComparison.Ordinal) || name == "picture.jpg" || name == OmsiMapExporter.MarkerFileName);
        }

        /// <summary>The problem with a folder name for a new map, or null when it can be used.</summary>
        public static string CheckFolderName(string mapsFolder, string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
                return "Enter a folder name for the new map.";
            if (folderName.Trim() != folderName || folderName.EndsWith(".", StringComparison.Ordinal))
                return "The folder name must not begin or end with a space or end with a dot.";
            if (folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "The folder name contains characters Windows does not allow.";
            if (Directory.Exists(Path.Combine(mapsFolder, folderName)) || File.Exists(Path.Combine(mapsFolder, folderName)))
                return "A map folder with this name already exists: " + Path.Combine(mapsFolder, folderName);
            return null;
        }

        /// <summary>
        /// Creates the map folder <paramref name="folderName"/> next to the template's map folder, with the display
        /// name <paramref name="friendlyName"/> in OMSI.
        /// </summary>
        public static OmsiNewMapReport Create(string templateGlobalConfigPath, string folderName, string friendlyName, DateTime createdAt)
        {
            if (string.IsNullOrWhiteSpace(friendlyName))
                throw new ArgumentException("A display name for the new map is required.", nameof(friendlyName));
            var templateFolder = Path.GetDirectoryName(Path.GetFullPath(templateGlobalConfigPath));
            if (templateFolder == null || !File.Exists(templateGlobalConfigPath))
                throw new FileNotFoundException("The template map's global.cfg was not found.", templateGlobalConfigPath);
            var mapsFolder = Path.GetDirectoryName(templateFolder);
            if (mapsFolder == null)
                throw new InvalidOperationException("The template map must lie in a maps folder.");
            var problem = CheckFolderName(mapsFolder, folderName);
            if (problem != null)
                throw new InvalidOperationException(problem);

            var target = Path.Combine(mapsFolder, folderName);
            var temporary = target + TemporarySuffix;
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, true);

            var report = new OmsiNewMapReport { MapFolder = target, GlobalConfigPath = Path.Combine(target, "global.cfg") };
            try
            {
                Directory.CreateDirectory(temporary);
                foreach (var file in Directory.GetFiles(templateFolder))
                {
                    var name = Path.GetFileName(file);
                    if (!IsHelperFile(name))
                        continue;
                    File.Copy(file, Path.Combine(temporary, name));
                    report.CopiedFiles.Add(name);
                }
                // The texture folder holds the water textures; its subfolders hold per-tile images of the template.
                var textures = Path.Combine(templateFolder, "texture");
                if (Directory.Exists(textures))
                {
                    Directory.CreateDirectory(Path.Combine(temporary, "texture"));
                    foreach (var file in Directory.GetFiles(textures))
                    {
                        var relative = Path.Combine("texture", Path.GetFileName(file));
                        File.Copy(file, Path.Combine(temporary, relative));
                        report.CopiedFiles.Add(relative);
                    }
                }
                Directory.CreateDirectory(Path.Combine(temporary, "TTData"));

                var config = OmsiRecordFile.Load(templateGlobalConfigPath, OmsiGlobalConfigLayouts.Instance);
                File.WriteAllBytes(Path.Combine(temporary, "global.cfg"), CreateGlobalConfig(config, folderName, friendlyName.Trim(), createdAt));
                File.WriteAllBytes(Path.Combine(temporary, FirstTileFile), CreateEmptyTile(createdAt));
                File.WriteAllBytes(Path.Combine(temporary, FirstTileFile + ".terrain"), CreateFlatTerrain(0f));
                File.WriteAllBytes(Path.Combine(temporary, "signalroutes.cfg"), OmsiTextFile.Encode(
                    "-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nCreated with OMSI Map Editor\r\nDate: " +
                    createdAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) + "\r\n\r\n", OmsiTextEncoding.Windows1252));

                Directory.Move(temporary, target);
            }
            catch
            {
                if (Directory.Exists(temporary))
                    Directory.Delete(temporary, true);
                throw;
            }
            return report;
        }

        /// <summary>
        /// The template's global.cfg with the new names, a fresh ID counter, the map camera over the new tile, no entry
        /// points, background image or world coordinates, and the single tile 0, 0. Unknown sections stay as they are.
        /// </summary>
        public static byte[] CreateGlobalConfig(OmsiRecordFile template, string name, string friendlyName, DateTime createdAt)
        {
            var config = template;
            config.Preamble.Clear();
            config.Preamble.Add(new OmsiLine("File created with OMSI Map Editor on " + createdAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                config.DefaultLineEnding));
            config.Preamble.Add(new OmsiLine(string.Empty, config.DefaultLineEnding));
            foreach (var dropped in DroppedRecords)
                config.Records.RemoveAll(record => string.Equals(record.Name, dropped, StringComparison.OrdinalIgnoreCase));

            SetSingleField(config, "name", name, 0);
            SetSingleField(config, "friendlyname", friendlyName, 1);
            SetDescription(config, "A new map, created with OMSI Map Editor.");
            SetSingleField(config, "NextIDCode", "1", IndexAfter(config, "version"));

            var camera = config.Records.Find(record => string.Equals(record.Name, "mapcam", StringComparison.OrdinalIgnoreCase));
            if (camera != null && camera.Fields.Count >= 5)
            {
                // Tile 0, 0, looking at its middle from 50 m above; the three camera angles keep the template's values.
                camera.SetField(0, "0");
                camera.SetField(1, "0");
                camera.SetField(2, "150");
                camera.SetField(3, "50");
                camera.SetField(4, "150");
            }

            config.InsertRecord(config.Records.Count, config.CreateRecord("map", new[] { "0", "0", FirstTileFile }));
            return config.ToBytes();
        }

        /// <summary>An empty tile as OMSI writes one: UTF-16 with byte order mark, version, empty terrain sections.</summary>
        public static byte[] CreateEmptyTile(DateTime createdAt)
        {
            var text = new StringBuilder();
            text.Append("File created with OMSI Map Editor on ").Append(createdAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)).Append("\r\n\r\n");
            text.Append("[version]\r\n").Append(TileVersion.ToString(CultureInfo.InvariantCulture)).Append("\r\n\r\n");
            text.Append("[terrain]\r\n\r\n\r\n");
            text.Append("[variable_terrainlightmap]\r\n\r\n");
            text.Append("[variable_terrain]\r\n\r\n");
            return OmsiTextFile.Encode(text.ToString(), OmsiTextEncoding.Utf16LittleEndian);
        }

        /// <summary>A terrain file with every one of the 61 × 61 heights at <paramref name="height"/> metres.</summary>
        public static byte[] CreateFlatTerrain(float height)
        {
            var count = OmsiTerrain.Resolution * OmsiTerrain.Resolution;
            var bytes = new byte[4 + count * 4];
            Buffer.BlockCopy(BitConverter.GetBytes(60), 0, bytes, 0, 4);
            var value = BitConverter.GetBytes(height);
            for (var index = 0; index < count; index++)
                Buffer.BlockCopy(value, 0, bytes, 4 + index * 4, 4);
            return bytes;
        }

        private static int IndexAfter(OmsiRecordFile config, string name)
        {
            var index = config.Records.FindIndex(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? 0 : index + 1;
        }

        /// <summary>Sets a one-field record, or inserts it at <paramref name="insertAt"/> when the template has none.</summary>
        private static void SetSingleField(OmsiRecordFile config, string name, string value, int insertAt)
        {
            var record = config.Records.Find(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (record != null && record.Fields.Count == 1)
            {
                record.SetField(0, value);
                return;
            }
            if (record != null)
                config.RemoveRecord(record);
            config.InsertRecord(Math.Min(insertAt, config.Records.Count), config.CreateRecord(name, new[] { value }));
        }

        /// <summary>Replaces the free text between [description] and [end], or adds both after [friendlyname].</summary>
        private static void SetDescription(OmsiRecordFile config, string text)
        {
            var index = config.Records.FindIndex(record => string.Equals(record.Name, "description", StringComparison.OrdinalIgnoreCase));
            var ending = config.DefaultLineEnding;
            if (index < 0 || index + 1 >= config.Records.Count ||
                !string.Equals(config.Records[index + 1].Name, "end", StringComparison.OrdinalIgnoreCase))
            {
                if (index >= 0)
                    config.Records.RemoveAt(index);
                index = IndexAfter(config, "friendlyname");
                config.InsertRecord(index, config.CreateRecord("description", new string[0], 0));
                config.InsertRecord(index + 1, config.CreateRecord("end", new string[0]));
            }
            var description = config.Records[index];
            description.Trailer.Clear();
            description.Trailer.Add(new OmsiLine(text, ending));
        }
    }
}
