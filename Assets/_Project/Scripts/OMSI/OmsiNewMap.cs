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
    /// money system, ticket pack, years, ground textures, seasons and traffic density, each of which
    /// <see cref="OmsiNewMapOptions"/> can change. The new map has a square of tiles around tile 0, 0 with flat terrain,
    /// one entry point named "Start" in the middle of tile 0, 0, no timetable and a fresh ID counter.
    /// OMSI hangs and crashes on loading a map without any entry point, so the new map has one from the start. Tiles, timetables, chrono changes, signal routes and the last situation of the template are not
    /// taken over, since they describe the template's places. The map is built in a temporary sibling folder and only
    /// renamed at the end, and an existing folder is never touched.
    /// </summary>
    public static class OmsiNewMap
    {
        public const int TileVersion = 14;
        public const string FirstTileFile = "tile_0_0.map";

        /// <summary>The object every entry point of the installed maps stands on; part of the base game.</summary>
        public const string EntryPointObject = @"Sceneryobjects\Generic\entrypoint_bus.sco";

        public const string EntryPointName = "Start";

        /// <summary>The entry point object is the first and only element; IDs continue after it.</summary>
        private const int EntryPointId = 1;

        private const string TemporarySuffix = ".mapforge-new-tmp";

        /// <summary>Records of global.cfg that belong to the template's places and are left out.</summary>
        private static readonly string[] DroppedRecords = { "map", "entrypoints", "backgroundimage", "worldcoordinates" };

        private const string DefaultDescription = "A new map, created with OMSI MapWorks.";

        /// <summary>Hour and density of a day as in Grundorf, for templates without a curve of their own.</summary>
        private static readonly double[,] StandardCarCurve =
        {
            { 0, 0.1 }, { 4, 0 }, { 6, 1 }, { 7, 1.5 }, { 8, 1 }, { 10, 0.5 }, { 15, 0.6 }, { 16, 1 }, { 17, 1.5 }, { 20, 0.4 }, { 24, 0.1 }
        };

        private static readonly double[,] StandardPassengerCurve =
        {
            { 0, 0.2 }, { 4, 0 }, { 6, 1 }, { 7, 1.2 }, { 8, 1 }, { 10, 0.6 }, { 15, 0.8 }, { 16, 1.2 }, { 17, 1.2 }, { 20, 0.8 }, { 24, 0.2 }
        };

        /// <summary>Kind, first and last day of the year of each season with snow in winter, as the installed maps have them.</summary>
        private static readonly int[,] SnowyYear = { { 4, 0, 38 }, { 3, 38, 75 }, { 1, 76, 134 }, { 2, 260, 323 }, { 3, 324, 370 } };

        private static readonly int[,] MildYear = { { 3, 0, 75 }, { 1, 76, 134 }, { 2, 260, 323 }, { 3, 324, 370 } };

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
            return Create(templateGlobalConfigPath, folderName, friendlyName, OmsiNewMapOptions.FromTemplate(templateGlobalConfigPath), createdAt);
        }

        /// <summary>
        /// Creates the map folder <paramref name="folderName"/> next to the template's map folder, with the display
        /// name <paramref name="friendlyName"/> in OMSI and the settings of <paramref name="options"/>.
        /// </summary>
        public static OmsiNewMapReport Create(string templateGlobalConfigPath, string folderName, string friendlyName, OmsiNewMapOptions options,
            DateTime createdAt)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            var optionsProblem = options.Check();
            if (optionsProblem != null)
                throw new ArgumentException(optionsProblem, nameof(options));
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
                File.WriteAllBytes(Path.Combine(temporary, "global.cfg"), CreateGlobalConfig(config, folderName, friendlyName.Trim(), options, createdAt));
                foreach (var tile in Tiles(options.Size))
                {
                    var file = TileFile(tile[0], tile[1]);
                    File.WriteAllBytes(Path.Combine(temporary, file), file == FirstTileFile ? CreateFirstTile(createdAt) : CreateEmptyTile(createdAt));
                    File.WriteAllBytes(Path.Combine(temporary, file + ".terrain"), CreateFlatTerrain(options.GroundHeight));
                }
                File.WriteAllBytes(Path.Combine(temporary, "signalroutes.cfg"), OmsiTextFile.Encode(
                    "-----------------------\r\nSignal Routes File\r\n-----------------------\r\n\r\nCreated with OMSI MapWorks\r\nDate: " +
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
        /// The template's global.cfg with the new names, a fresh ID counter, the map camera over the new tile, the entry
        /// point "Start" on the entry point object of the first tile, no background image or world coordinates, and the
        /// single tile 0, 0. Unknown sections stay as they are.
        /// </summary>
        public static byte[] CreateGlobalConfig(OmsiRecordFile template, string name, string friendlyName, DateTime createdAt)
        {
            return CreateGlobalConfig(template, name, friendlyName, OmsiNewMapOptions.FromTemplate(template), createdAt);
        }

        /// <summary>
        /// The template's global.cfg as <see cref="CreateGlobalConfig(OmsiRecordFile, string, string, DateTime)"/> makes it,
        /// with the settings of <paramref name="options"/> and its tiles; settings equal to the template's stay as written.
        /// </summary>
        public static byte[] CreateGlobalConfig(OmsiRecordFile template, string name, string friendlyName, OmsiNewMapOptions options, DateTime createdAt)
        {
            var config = template;
            config.Preamble.Clear();
            config.Preamble.Add(new OmsiLine("File created with OMSI MapWorks on " + createdAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                config.DefaultLineEnding));
            config.Preamble.Add(new OmsiLine(string.Empty, config.DefaultLineEnding));
            foreach (var dropped in DroppedRecords)
                config.Records.RemoveAll(record => string.Equals(record.Name, dropped, StringComparison.OrdinalIgnoreCase));

            SetSingleField(config, "name", name, 0);
            SetSingleField(config, "friendlyname", friendlyName, 1);
            SetDescription(config, string.IsNullOrWhiteSpace(options.Description) ? DefaultDescription : options.Description.Trim());
            SetSingleField(config, "NextIDCode", (EntryPointId + 1).ToString(CultureInfo.InvariantCulture), IndexAfter(config, "version"));

            var camera = config.Records.Find(record => string.Equals(record.Name, "mapcam", StringComparison.OrdinalIgnoreCase));
            if (camera != null && camera.Fields.Count >= 5)
            {
                // Tile 0, 0, looking at its middle from 50 m above; the three camera angles keep the template's values.
                camera.SetField(0, "0");
                camera.SetField(1, "0");
                camera.SetField(2, "150");
                camera.SetField(3, Height(options.GroundHeight + 50f));
                camera.SetField(4, "150");
            }
            ApplySettings(config, options);

            // Object number 0 of tile index 0, the position of the object, heading 0 as the quaternion 0, 0, 0, 1.
            config.InsertRecord(config.Records.Count, config.CreateRecord("entrypoints", new[]
            {
                "1", "0", EntryPointId.ToString(CultureInfo.InvariantCulture), "0", "150.000", options.GroundHeight.ToString("0.000", CultureInfo.InvariantCulture),
                "150.000", "0.000000", "0.000000", "0.000000", "1.000000", "0", EntryPointName
            }));
            // Tile 0, 0 first: the entry point names it by its index 0.
            foreach (var tile in Tiles(options.Size))
                config.InsertRecord(config.Records.Count, config.CreateRecord("map", new[]
                {
                    tile[0].ToString(CultureInfo.InvariantCulture), tile[1].ToString(CultureInfo.InvariantCulture), TileFile(tile[0], tile[1])
                }));
            return config.ToBytes();
        }

        /// <summary>
        /// The tiles of a map <paramref name="size"/> tiles wide around tile 0, 0, tile 0, 0 first and the others row by
        /// row from the south-west; an even size reaches one tile further north and east.
        /// </summary>
        public static List<int[]> Tiles(int size)
        {
            var tiles = new List<int[]> { new[] { 0, 0 } };
            var first = -((size - 1) / 2);
            for (var y = first; y < first + size; y++)
                for (var x = first; x < first + size; x++)
                    if (x != 0 || y != 0)
                        tiles.Add(new[] { x, y });
            return tiles;
        }

        private static string TileFile(int x, int y)
        {
            return "tile_" + x.ToString(CultureInfo.InvariantCulture) + "_" + y.ToString(CultureInfo.InvariantCulture) + ".map";
        }

        private static string Height(float metres)
        {
            return OmsiNumberFormat.Format(metres);
        }

        /// <summary>Writes the settings that differ from the template; equal ones keep the template's lines.</summary>
        private static void ApplySettings(OmsiRecordFile config, OmsiNewMapOptions options)
        {
            var template = OmsiNewMapOptions.FromTemplate(config);
            SetFlag(config, "LHT", options.LeftHandTraffic, "realrail");
            SetFlag(config, "realrail", options.RealRail, "dynhelperactive");
            if (options.MoneySystem.Length > 0 && !string.Equals(options.MoneySystem, template.MoneySystem, StringComparison.OrdinalIgnoreCase))
                SetSingleField(config, "moneysystem", options.MoneySystem, IndexAfter(config, "mapcam"));
            if (options.TicketPack.Length > 0 && !string.Equals(options.TicketPack, template.TicketPack, StringComparison.OrdinalIgnoreCase))
                SetSingleField(config, "ticketpack", options.TicketPack, IndexAfter(config, "moneysystem"));
            if (options.FirstYear != template.FirstYear || options.LastYear != template.LastYear)
            {
                var years = OmsiNewMapOptions.Find(config, "years");
                if (years != null && years.Fields.Count == 2)
                {
                    years.SetField(0, options.FirstYear.ToString(CultureInfo.InvariantCulture));
                    years.SetField(1, options.LastYear.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    if (years != null)
                        config.RemoveRecord(years);
                    config.InsertRecord(IndexAfterAny(config, "repair_time_min", "ticketpack", "moneysystem", "mapcam"), config.CreateRecord("years", new[]
                    {
                        options.FirstYear.ToString(CultureInfo.InvariantCulture), options.LastYear.ToString(CultureInfo.InvariantCulture)
                    }));
                }
            }
            if (options.YearOffset != template.YearOffset)
            {
                if (options.YearOffset == 0)
                    config.Records.RemoveAll(record => string.Equals(record.Name, "realyearoffset", StringComparison.OrdinalIgnoreCase));
                else
                    SetSingleField(config, "realyearoffset", options.YearOffset.ToString(CultureInfo.InvariantCulture), IndexAfter(config, "years"));
            }
            if (options.GroundTexture.Length > 0 && !string.Equals(options.GroundTexture, template.GroundTexture, StringComparison.OrdinalIgnoreCase))
            {
                var ground = OmsiNewMapOptions.Find(config, "groundtex");
                if (ground != null && ground.Fields.Count == 5)
                    ground.SetField(0, options.GroundTexture);
                else
                    config.InsertRecord(IndexAfter(config, "standarddepot"), config.CreateRecord("groundtex", new[]
                    {
                        options.GroundTexture, @"texture\gras_det.bmp", "0", "1", "60"
                    }));
            }
            if (options.Seasons == OmsiSeasonPreset.FourWithSnow)
                ReplaceSeasons(config, SnowyYear);
            else if (options.Seasons == OmsiSeasonPreset.MildWinter)
                ReplaceSeasons(config, MildYear);
            else if (options.Seasons == OmsiSeasonPreset.SummerOnly)
                ReplaceSeasons(config, new int[0, 3]);
            ScaleCurve(config, "trafficdensity_road", options.CarTraffic, StandardCarCurve);
            ScaleCurve(config, "trafficdensity_passenger", options.PassengerTraffic, StandardPassengerCurve);
        }

        /// <summary>Adds a section without fields after <paramref name="after"/>, or removes it.</summary>
        private static void SetFlag(OmsiRecordFile config, string name, bool on, string after)
        {
            var present = OmsiNewMapOptions.Find(config, name) != null;
            if (on == present)
                return;
            if (!on)
            {
                config.Records.RemoveAll(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase));
                return;
            }
            var index = config.Records.FindIndex(record => string.Equals(record.Name, after, StringComparison.OrdinalIgnoreCase));
            config.InsertRecord(index >= 0 ? index + 1 : IndexAfter(config, "NextIDCode"), config.CreateRecord(name, new string[0]));
        }

        /// <summary>
        /// Multiplies the densities of a daily curve; a template without the curve keeps none at factor 1 and otherwise
        /// gets the standard day, multiplied.
        /// </summary>
        private static void ScaleCurve(OmsiRecordFile config, string name, double factor, double[,] standard)
        {
            if (Math.Abs(factor - 1.0) < 1e-9)
                return;
            var points = config.Records.FindAll(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase));
            if (points.Count == 0)
            {
                var insertAt = config.Records.FindLastIndex(record => record.Name.StartsWith("trafficdensity", StringComparison.OrdinalIgnoreCase) ||
                                                                      string.Equals(record.Name, "addseason", StringComparison.OrdinalIgnoreCase) ||
                                                                      string.Equals(record.Name, "groundtex", StringComparison.OrdinalIgnoreCase)) + 1;
                if (insertAt == 0)
                    insertAt = config.Records.Count;
                for (var point = 0; point < standard.GetLength(0); point++)
                    config.InsertRecord(insertAt + point, config.CreateRecord(name, new[]
                    {
                        OmsiNewMapContent.Number(standard[point, 0]), OmsiNewMapContent.Number(standard[point, 1] * factor)
                    }));
                return;
            }
            foreach (var point in points)
                if (point.Fields.Count == 2 && point.TryGetDouble(1, out var density))
                    point.SetField(1, OmsiNewMapContent.Number(density * factor));
        }

        /// <summary>
        /// Replaces the [addseason] list, with the comment line OMSI's editor writes above each season; comments above
        /// the old seasons go with them.
        /// </summary>
        private static void ReplaceSeasons(OmsiRecordFile config, int[,] seasons)
        {
            var insertAt = -1;
            for (var index = config.Records.Count - 1; index >= 0; index--)
            {
                if (!string.Equals(config.Records[index].Name, "addseason", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (index > 0)
                    config.Records[index - 1].Trailer.RemoveAll(line => IsSeasonComment(line.Text));
                config.Records.RemoveAt(index);
                insertAt = index;
            }
            if (insertAt < 0)
                insertAt = config.Records.FindLastIndex(record => string.Equals(record.Name, "groundtex", StringComparison.OrdinalIgnoreCase)) + 1;
            if (insertAt <= 0)
                insertAt = config.Records.Count;
            for (var season = 0; season < seasons.GetLength(0); season++)
            {
                if (insertAt > 0)
                    config.Records[insertAt - 1].Trailer.Add(new OmsiLine(" " + SeasonComment(seasons[season, 0]), config.DefaultLineEnding));
                config.InsertRecord(insertAt, config.CreateRecord("addseason", new[]
                {
                    seasons[season, 0].ToString(CultureInfo.InvariantCulture), seasons[season, 1].ToString(CultureInfo.InvariantCulture),
                    seasons[season, 2].ToString(CultureInfo.InvariantCulture)
                }));
                insertAt++;
            }
        }

        private static string SeasonComment(int kind)
        {
            switch (kind)
            {
                case 1:
                    return "Spring:";
                case 2:
                    return "Autumn:";
                case 3:
                    return "Winter:";
                case 4:
                    return "Winter (snow):";
                case 5:
                    return "Summer (dry):";
                default:
                    return "Summer:";
            }
        }

        private static bool IsSeasonComment(string text)
        {
            var trimmed = text.Trim();
            return trimmed.EndsWith(":", StringComparison.Ordinal) &&
                   (trimmed.StartsWith("Winter", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Spring", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Autumn", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Summer", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Fall", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The first tile as OMSI writes one, UTF-16 with byte order mark, version and empty terrain sections, with the
        /// entry point object in its middle.
        /// </summary>
        public static byte[] CreateFirstTile(DateTime createdAt)
        {
            var text = TileHeader(createdAt);
            // Detail level, file, ID, x, y, height, heading, pitch, bank and no labels.
            text.Append("[object]\r\n0\r\n").Append(EntryPointObject).Append("\r\n").Append(EntryPointId.ToString(CultureInfo.InvariantCulture))
                .Append("\r\n150\r\n150\r\n0\r\n0\r\n0\r\n0\r\n0\r\n\r\n");
            return OmsiTextFile.Encode(text.ToString(), OmsiTextEncoding.Utf16LittleEndian);
        }

        /// <summary>An empty tile as OMSI writes one, UTF-16 with byte order mark, version and empty terrain sections.</summary>
        public static byte[] CreateEmptyTile(DateTime createdAt)
        {
            return OmsiTextFile.Encode(TileHeader(createdAt).ToString(), OmsiTextEncoding.Utf16LittleEndian);
        }

        private static StringBuilder TileHeader(DateTime createdAt)
        {
            var text = new StringBuilder();
            text.Append("File created with OMSI MapWorks on ").Append(createdAt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture)).Append("\r\n\r\n");
            text.Append("[version]\r\n").Append(TileVersion.ToString(CultureInfo.InvariantCulture)).Append("\r\n\r\n");
            text.Append("[terrain]\r\n\r\n\r\n");
            text.Append("[variable_terrainlightmap]\r\n\r\n");
            text.Append("[variable_terrain]\r\n\r\n");
            return text;
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

        /// <summary>The index after the first of the named records the file has, or 0.</summary>
        private static int IndexAfterAny(OmsiRecordFile config, params string[] names)
        {
            foreach (var name in names)
            {
                var index = config.Records.FindIndex(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    return index + 1;
            }
            return 0;
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
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
                description.Trailer.Add(new OmsiLine(line, ending));
        }
    }
}
