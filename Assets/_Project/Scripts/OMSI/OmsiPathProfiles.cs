using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Path types of other widths and, for vehicles, of both directions, made from OMSI's invisible street and sidewalk (ADR 089):
    /// the path tool draws splines of such a profile, whose single [path] has the width and direction the profile gives it. A new
    /// profile is a copy of the chosen one with the width of its lane (and the edges of its helper strip) and the direction
    /// changed, written below Splines\Mapforge\&lt;project&gt;.
    /// </summary>
    public static class OmsiPathProfiles
    {
        public const double MinimumWidth = 0.5;
        public const double MaximumWidth = 12.0;

        private static readonly Regex TitleSuffix = new Regex(@"\s*\(\d+(\.\d+)? m(, both ways)?\)$", RegexOptions.CultureInvariant);

        private static readonly Regex Variant = new Regex(@"^(?<base>.*?)(_\d+(_\d+)?m)?(_2way)?$", RegexOptions.CultureInvariant);

        /// <summary>The name of a profile file without the width and direction a path type of this kind adds to it.</summary>
        public static string BaseName(string fileNameWithoutExtension)
        {
            var match = Variant.Match(fileNameWithoutExtension ?? string.Empty);
            return match.Success && match.Groups["base"].Length > 0 ? match.Groups["base"].Value : fileNameWithoutExtension;
        }

        /// <summary>The name of the path type of a base name, such as <c>invis_street_2_5m_2way</c>.</summary>
        public static string VariantName(string baseName, double width, bool bothWays)
        {
            var text = Math.Round(width, 2).ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_');
            return baseName + "_" + text + "m" + (bothWays ? "_2way" : string.Empty);
        }

        /// <summary>The width of the one lane of a path profile, or null when it has none.</summary>
        public static double? WidthOf(OmsiSplineProfile profile)
        {
            var lanes = profile?.Lanes.FindAll(lane => lane.Kind != OmsiSplineProfile.UnreadableLane);
            return lanes != null && lanes.Count == 1 ? (double?)lanes[0].Width : null;
        }

        /// <summary>True when the one lane of a path profile runs both ways.</summary>
        public static bool IsBothWays(OmsiSplineProfile profile)
        {
            var lanes = profile?.Lanes.FindAll(lane => lane.Kind != OmsiSplineProfile.UnreadableLane);
            return lanes != null && lanes.Count == 1 && lanes[0].Direction == 2;
        }

        /// <summary>
        /// The text of a path profile with another width: the lane's width, the edges of the helper strip (as wide as the lane) and,
        /// for a lane of vehicles, its direction (0 along the spline, 2 both ways). A lane of people keeps the direction it has.
        /// </summary>
        public static string Compose(string baseText, double width, bool bothWays)
        {
            var lines = baseText.Replace("\r\n", "\n").Split('\n');
            var result = new List<string>(lines);
            var half = width / 2.0;
            var titled = false;
            for (var index = 0; index < result.Count; index++)
            {
                var line = result[index].Trim();
                if (!titled && line.Length > 0)
                {
                    // A type made before has its width in the title already.
                    result[index] = TitleSuffix.Replace(result[index].TrimEnd(), string.Empty) + " (" + width.ToString("0.##", CultureInfo.InvariantCulture) + " m" + (bothWays ? ", both ways" : string.Empty) + ")";
                    titled = true;
                }
                else if (line.Equals("[profilepnt]", StringComparison.OrdinalIgnoreCase) && index + 1 < result.Count)
                {
                    // The strip's two points lie at the left and right edge: the first has a negative x, the second a positive one.
                    if (double.TryParse(result[index + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                        result[index + 1] = (x < 0.0 ? -half : half).ToString("0.###", CultureInfo.InvariantCulture);
                }
                else if (line.Equals("[path]", StringComparison.OrdinalIgnoreCase) && index + 5 < result.Count)
                {
                    // kind, x, height, width, direction
                    result[index + 4] = width.ToString("0.###", CultureInfo.InvariantCulture);
                    if (result[index + 1].Trim() == "0")
                        result[index + 5] = bothWays ? "2" : "0";
                }
            }
            return string.Join("\r\n", result);
        }

        /// <summary>
        /// The path type of the profile <paramref name="baseAssetPath"/> with a width and direction, for a project: the profile itself when
        /// it already has them, a file written before below Splines\Mapforge\&lt;project&gt;, or a new one.
        /// </summary>
        /// <returns>The profile's path relative to the installation, as tile files name it.</returns>
        public static string Create(string installationRoot, string projectName, string baseAssetPath, double width, bool bothWays)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            width = Math.Max(MinimumWidth, Math.Min(MaximumWidth, width));
            var file = OmsiRelativePath.Resolve(installationRoot, baseAssetPath);
            if (!File.Exists(file))
                throw new FileNotFoundException("The path profile is missing.", file);
            var baseText = OmsiTextFile.Decode(File.ReadAllBytes(file));
            var profile = OmsiSplineProfile.Parse(OmsiTextFile.Encode(baseText, OmsiTextEncoding.Windows1252), baseAssetPath);
            var kindIsVehicle = profile.Lanes.Exists(lane => lane.Kind == 0);
            var direction = kindIsVehicle && bothWays;
            if (Math.Abs((WidthOf(profile) ?? 0.0) - width) < 1e-6 && direction == IsBothWays(profile))
                return baseAssetPath;
            var project = OmsiGeneratedObjectStore.SafeName(projectName, "Project");
            var folder = Path.Combine(installationRoot, OmsiRoadTypeStore.Folder, project);
            var name = VariantName(BaseName(Path.GetFileNameWithoutExtension(file)), width, direction);
            var target = Path.Combine(folder, name + ".sli");
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(target, OmsiTextFile.Encode(Compose(baseText, width, direction), OmsiTextEncoding.Windows1252));
            }
            return OmsiRoadTypeStore.Folder + "\\" + project + "\\" + name + ".sli";
        }
    }
}
