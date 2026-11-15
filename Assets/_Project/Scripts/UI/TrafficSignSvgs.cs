using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The signs the owner draws as SVG files (ADR 099). A file <c>Assets/_Project/UI/Resources/TrafficSigns/&lt;name&gt;.svg.txt</c> (Unity
    /// imports a <c>.svg</c> without the vector graphics package as no text, so the file carries a <c>.txt</c> after it and loads as a
    /// <c>TextAsset</c> named <c>TrafficSigns/&lt;name&gt;.svg</c>) replaces the built-in drawing of that sign everywhere: in the tiles, under the
    /// pointer, as the signs on the map and in the tab icons. Without the file, or with one that does not read, the built-in Painter2D drawing
    /// is used, so the tool never has a hole. The names:
    /// <list type="bullet">
    /// <item><c>priority-road</c>, <c>yield</c>, <c>overtaking-ban</c>: the signs of the first tab.</item>
    /// <item><c>speed-&lt;kmh&gt;</c> (such as <c>speed-50</c>): a whole speed sign, used for that speed first.</item>
    /// <item><c>speed-blank</c> and <c>digit-0</c> to <c>digit-9</c>: the ring and the digits that make any other speed (custom speeds, up to 250).</item>
    /// <item><c>tab-rules</c>, <c>tab-speed</c>: the icons of the two tabs; <c>currentColor</c> takes the colour of the tab (orange when it is open).</item>
    /// </list>
    /// What the files may hold is listed at <see cref="SvgSign"/>.
    /// </summary>
    internal static class TrafficSignSvgs
    {
        /// <summary>The folder inside a Resources folder.</summary>
        public const string Folder = "TrafficSigns/";

        public const string PriorityRoad = "priority-road";
        public const string Yield = "yield";
        public const string OvertakingBan = "overtaking-ban";
        public const string SpeedBlank = "speed-blank";
        public const string TabRules = "tab-rules";
        public const string TabSpeed = "tab-speed";

        /// <summary>How high a digit is drawn on a speed sign made of the ring and digits, in radii of the sign; its viewBox height is this high.</summary>
        public const float DigitHeight = 0.8f;

        /// <summary>The widest a row of digits gets, in radii of the sign; a longer row (such as 100) is narrowed to fit.</summary>
        public const float DigitsMaxWidth = 1.25f;

        /// <summary>The space between two digits, in radii of the sign.</summary>
        public const float DigitGap = 0.06f;

        private static readonly string[] DigitNames = { "digit-0", "digit-1", "digit-2", "digit-3", "digit-4", "digit-5", "digit-6", "digit-7", "digit-8", "digit-9" };

        // Parsed once per name; a name without a usable file is kept as null, so a redraw of hundreds of signs asks the files nothing.
        private static readonly Dictionary<string, SvgSign> Cache = new Dictionary<string, SvgSign>(StringComparer.Ordinal);
        private static readonly Dictionary<int, string> SpeedNames = new Dictionary<int, string>();

        /// <summary>
        /// Where the text of a file comes from, by name without extension; null for none. The default reads the TextAsset of the Resources
        /// folder; tests replace it.
        /// </summary>
        public static Func<string, string> Source { get; set; } = ReadResource;

        /// <summary>Forgets what was read, so changed files or a changed <see cref="Source"/> are read again.</summary>
        public static void Reset()
        {
            Cache.Clear();
            SpeedNames.Clear();
        }

        private static string ReadResource(string name)
        {
            var asset = Resources.Load<TextAsset>(Folder + name + ".svg");
            return asset != null ? asset.text : null;
        }

        /// <summary>The sign of a file, or null when there is none or it does not read (said once in the log, with the reason).</summary>
        public static SvgSign Get(string name)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;
            SvgSign sign = null;
            try
            {
                var text = Source(name);
                if (!string.IsNullOrEmpty(text) && !SvgSign.TryParse(text, out sign, out var error))
                    Debug.LogWarning("Traffic sign " + name + ".svg is not used: " + error + ". The built-in drawing stays.");
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                // A file that cannot be read must not stop a redraw.
                Debug.LogWarning("Traffic sign " + name + ".svg could not be read: " + exception.Message + ". The built-in drawing stays.");
                sign = null;
            }
            Cache[name] = sign;
            return sign;
        }

        /// <summary>The name of the whole sign for a speed, such as <c>speed-50</c>.</summary>
        public static string SpeedName(int kmh)
        {
            if (!SpeedNames.TryGetValue(kmh, out var name))
            {
                name = "speed-" + kmh.ToString(CultureInfo.InvariantCulture);
                SpeedNames[kmh] = name;
            }
            return name;
        }

        /// <summary>
        /// Draws a sign from its file when there is one and returns true; false leaves the drawing to the built-in one. A sign is as wide and
        /// as high as <paramref name="radius"/> times two, its viewBox fitted into that square.
        /// </summary>
        public static bool TryDraw(Painter2D painter, TrafficSignKind kind, int kmh, Vector2 centre, float radius)
        {
            switch (kind)
            {
                case TrafficSignKind.PriorityRoad:
                    return DrawNamed(painter, PriorityRoad, centre, radius);
                case TrafficSignKind.Yield:
                    return DrawNamed(painter, Yield, centre, radius);
                case TrafficSignKind.OvertakingBan:
                    return DrawNamed(painter, OvertakingBan, centre, radius);
                case TrafficSignKind.Speed:
                    return DrawSpeed(painter, kmh, centre, radius);
                default:
                    return false;
            }
        }

        private static bool DrawNamed(Painter2D painter, string name, Vector2 centre, float radius)
        {
            var sign = Get(name);
            if (sign == null)
                return false;
            sign.Draw(painter, centre, radius * 2f, Color.black);
            return true;
        }

        private static bool DrawSpeed(Painter2D painter, int kmh, Vector2 centre, float radius)
        {
            if (kmh > 0)
            {
                var whole = Get(SpeedName(kmh));
                if (whole != null)
                {
                    whole.Draw(painter, centre, radius * 2f, Color.black);
                    return true;
                }
            }
            var blank = Get(SpeedBlank);
            if (blank == null)
                return false;
            if (kmh <= 0)
            {
                // No number yet (the custom tile before one is typed): the empty ring.
                blank.Draw(painter, centre, radius * 2f, Color.black);
                return true;
            }
            if (kmh > 999)
                return false;
            var count = kmh >= 100 ? 3 : kmh >= 10 ? 2 : 1;
            // All the digits must be there before anything is drawn, or a speed would show a ring with a hole in its number.
            var width = 0f;
            var divisor = count == 3 ? 100 : count == 2 ? 10 : 1;
            for (var index = 0; index < count; index++, divisor /= 10)
            {
                var digit = Get(DigitNames[kmh / divisor % 10]);
                if (digit == null)
                    return false;
                width += digit.ViewBox.width * (DigitHeight * radius / digit.ViewBox.height) + (index > 0 ? DigitGap * radius : 0f);
            }
            blank.Draw(painter, centre, radius * 2f, Color.black);
            var narrow = Mathf.Min(1f, DigitsMaxWidth * radius / width);
            var left = centre.x - width * narrow / 2f;
            divisor = count == 3 ? 100 : count == 2 ? 10 : 1;
            for (var index = 0; index < count; index++, divisor /= 10)
            {
                var digit = Get(DigitNames[kmh / divisor % 10]);
                var scale = DigitHeight * radius / digit.ViewBox.height * narrow;
                var digitWidth = digit.ViewBox.width * scale;
                digit.DrawScaled(painter, new Vector2(left + digitWidth / 2f, centre.y), scale, Color.black);
                left += digitWidth + DigitGap * radius * narrow;
            }
            return true;
        }
    }
}
