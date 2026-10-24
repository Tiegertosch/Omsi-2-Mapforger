using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>The seasons a new map starts with.</summary>
    public enum OmsiSeasonPreset
    {
        /// <summary>The template's [addseason] list, unchanged.</summary>
        Template,

        /// <summary>Spring, summer, autumn and a winter with snow, as in central Europe.</summary>
        FourWithSnow,

        /// <summary>The same year with a winter without snow.</summary>
        MildWinter,

        /// <summary>Summer all year: no [addseason] at all.</summary>
        SummerOnly
    }

    /// <summary>
    /// What a new map is made with (<see cref="OmsiNewMap.Create(string, string, string, OmsiNewMapOptions, DateTime)"/>).
    /// <see cref="FromTemplate"/> fills it with the template's settings, so a value left alone keeps the template's.
    /// </summary>
    public sealed class OmsiNewMapOptions
    {
        public const int MaximumSize = 9;

        /// <summary>The text OMSI's map list shows; empty writes a short default text.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Tiles per side; the map is <see cref="Size"/> × <see cref="Size"/> tiles around tile 0, 0.</summary>
        public int Size { get; set; } = 1;

        /// <summary>The height of all terrain in metres.</summary>
        public float GroundHeight { get; set; }

        /// <summary>Traffic keeps left: [LHT].</summary>
        public bool LeftHandTraffic { get; set; }

        /// <summary>Trams and trains follow their tracks' switches: [realrail].</summary>
        public bool RealRail { get; set; }

        /// <summary>Factors on the template's daily curves of cars and passengers; 1 keeps them.</summary>
        public double CarTraffic { get; set; } = 1.0;
        public double PassengerTraffic { get; set; } = 1.0;

        /// <summary>The years the timetable may be set in, [years].</summary>
        public int FirstYear { get; set; } = 2000;
        public int LastYear { get; set; } = 2030;

        /// <summary>Added to today's year when the game starts at the real date, [realyearoffset].</summary>
        public int YearOffset { get; set; }

        /// <summary>The money system and ticket pack, as global.cfg names them (<c>Money\DM\DM.cfg</c>).</summary>
        public string MoneySystem { get; set; } = string.Empty;
        public string TicketPack { get; set; } = string.Empty;

        public OmsiSeasonPreset Seasons { get; set; } = OmsiSeasonPreset.Template;

        /// <summary>The texture of the base ground layer, the first [groundtex]; the template's detail texture stays.</summary>
        public string GroundTexture { get; set; } = string.Empty;

        /// <summary>The settings of a template's global.cfg.</summary>
        public static OmsiNewMapOptions FromTemplate(string templateGlobalConfigPath)
        {
            return FromTemplate(OmsiRecordFile.Load(templateGlobalConfigPath, OmsiGlobalConfigLayouts.Instance));
        }

        public static OmsiNewMapOptions FromTemplate(OmsiRecordFile config)
        {
            var options = new OmsiNewMapOptions
            {
                LeftHandTraffic = Find(config, "LHT") != null,
                RealRail = Find(config, "realrail") != null,
                MoneySystem = Find(config, "moneysystem")?.GetField(0)?.Trim() ?? string.Empty,
                TicketPack = Find(config, "ticketpack")?.GetField(0)?.Trim() ?? string.Empty,
                GroundTexture = Find(config, "groundtex")?.GetField(0)?.Trim() ?? string.Empty
            };
            var years = Find(config, "years");
            if (years != null && years.TryGetInt(0, out var first) && years.TryGetInt(1, out var last))
            {
                options.FirstYear = first;
                options.LastYear = last;
            }
            if (Find(config, "realyearoffset") is OmsiRecord offset && offset.TryGetInt(0, out var yearOffset))
                options.YearOffset = yearOffset;
            return options;
        }

        /// <summary>The problem that keeps these options from making a map, or null.</summary>
        public string Check()
        {
            if (Size < 1 || Size > MaximumSize)
                return "The map has 1 to " + MaximumSize + " tiles per side.";
            if (float.IsNaN(GroundHeight) || float.IsInfinity(GroundHeight) || Math.Abs(GroundHeight) > 10000f)
                return "The ground height must be a number between -10000 and 10000 m.";
            if (FirstYear < 1800 || LastYear > 2200 || LastYear < FirstYear)
                return "The years must run forward, between 1800 and 2200.";
            if (Math.Abs(YearOffset) > 500)
                return "The year offset must lie between -500 and 500.";
            if (CarTraffic < 0.0 || CarTraffic > 10.0 || PassengerTraffic < 0.0 || PassengerTraffic > 10.0)
                return "Traffic factors lie between 0 and 10.";
            foreach (var value in new[] { Description, MoneySystem, TicketPack, GroundTexture })
                if (value != null && value.IndexOf('[') >= 0 && value.IndexOf(']') > value.IndexOf('['))
                    return "Names and texts must not contain a section name in brackets.";
            return null;
        }

        internal static OmsiRecord Find(OmsiRecordFile config, string name)
        {
            return config.Records.Find(record => string.Equals(record.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>What an OMSI installation offers a new map: money systems, ticket packs and ground textures.</summary>
    public static class OmsiNewMapContent
    {
        private static readonly string[] TextureExtensions = { ".bmp", ".jpg", ".tga", ".dds" };

        /// <summary>Words of ground textures: grass, meadows, fields, soil, sand and gravel, in German and English.</summary>
        private static readonly string[] GroundWords = { "gras", "wiese", "meadow", "feld", "field", "acker", "erde", "soil", "sand", "kies", "gravel", "dirt", "moos", "laub" };

        /// <summary>The files <c>top\folder\*.extension</c> of the installation, as global.cfg names them, sorted.</summary>
        public static List<string> Files(string omsiRoot, string top, string extension)
        {
            var result = new List<string>();
            var folder = Path.Combine(omsiRoot, top);
            if (!Directory.Exists(folder))
                return result;
            foreach (var subfolder in Directory.GetDirectories(folder))
                foreach (var file in Directory.GetFiles(subfolder, "*" + extension))
                    if (string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
                        result.Add(top + "\\" + Path.GetFileName(subfolder) + "\\" + Path.GetFileName(file));
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// Textures for the base ground: the template's ground layers first, then the textures in the installation's
        /// Texture folder that look like ground, without detail and noise textures. A .dds next to a texture of the same
        /// name counts once, as OMSI prefers it anyway.
        /// </summary>
        public static List<string> GroundTextures(string omsiRoot, OmsiRecordFile template)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string relative)
            {
                var key = Path.ChangeExtension(relative.Replace('/', '\\'), null);
                if (seen.Add(key))
                    result.Add(relative);
            }
            if (template != null)
                foreach (var record in template.Records)
                    if (string.Equals(record.Name, "groundtex", StringComparison.OrdinalIgnoreCase) && record.GetField(0) is string texture &&
                        texture.Trim().Length > 0)
                        Add(texture.Trim());
            var folder = Path.Combine(omsiRoot, "Texture");
            if (!Directory.Exists(folder))
                return result;
            var found = new List<string>();
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                var lower = name.ToLowerInvariant();
                if (Array.IndexOf(TextureExtensions, Path.GetExtension(lower)) < 0 || lower.Contains("_det") || lower.Contains("noise"))
                    continue;
                foreach (var word in GroundWords)
                    if (lower.Contains(word))
                    {
                        found.Add("Texture\\" + name);
                        break;
                    }
            }
            // The other formats first, so a texture known as .bmp is not listed by its .dds.
            found.Sort((a, b) =>
            {
                var aDds = a.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);
                var bDds = b.EndsWith(".dds", StringComparison.OrdinalIgnoreCase);
                return aDds != bDds ? (aDds ? 1 : -1) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var texture in found)
                Add(texture);
            return result;
        }

        /// <summary>The OMSI folder of a map's global.cfg: the folder above its maps folder.</summary>
        public static string InstallationOf(string globalConfigPath)
        {
            var mapFolder = Path.GetDirectoryName(Path.GetFullPath(globalConfigPath));
            var mapsFolder = mapFolder == null ? null : Path.GetDirectoryName(mapFolder);
            return mapsFolder == null ? null : Path.GetDirectoryName(mapsFolder);
        }

        internal static string Number(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }
    }
}
