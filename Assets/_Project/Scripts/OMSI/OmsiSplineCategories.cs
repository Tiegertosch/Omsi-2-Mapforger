using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What kind of spline a profile makes, for the tabs of the road menu.</summary>
    public enum OmsiSplineCategory
    {
        /// <summary>Roads whose vehicle lanes all run one way, also one carriageway of a dual road.</summary>
        OneWay,

        /// <summary>Roads with one or two vehicle lanes, both ways.</summary>
        TwoLanes,

        /// <summary>Roads with three or four vehicle lanes.</summary>
        FourLanes,

        /// <summary>Roads with five or more vehicle lanes.</summary>
        SixLanes,

        /// <summary>Roads with a median between their carriageways.</summary>
        Median,

        /// <summary>Rails and tram tracks: profiles with a rail path.</summary>
        Rail,

        /// <summary>Footpaths and pavements: profiles with paths for pedestrians only.</summary>
        Path,

        /// <summary>Everything else, such as wires, fences, walls and helpers only the OMSI editor shows.</summary>
        Other
    }

    /// <summary>Sorts spline profiles into the categories of the road menu by their lanes, read from the profile.</summary>
    public static class OmsiSplineCategories
    {
        /// <summary>The [path] type OMSI uses for rails, for AI and user vehicles alike.</summary>
        public const int RailLane = 2;

        public static OmsiSplineCategory Of(OmsiSplineProfile profile)
        {
            if (profile == null || profile.EditorOnly || profile.LineKind != OmsiSplineLineKind.None || profile.Strips.Count == 0)
                return OmsiSplineCategory.Other;
            // Tracks in a street with lanes for cars make a road (ADR 049, stage 4); tracks alone are rails.
            var vehicle = profile.Lanes.FindAll(lane => lane.Kind == 0 && lane.Width > 0.0);
            if (vehicle.Count == 0 && profile.Lanes.Exists(lane => lane.Kind == RailLane))
                return OmsiSplineCategory.Rail;
            if (vehicle.Count == 0)
                return profile.Lanes.Exists(lane => lane.Kind == 1) ? OmsiSplineCategory.Path : OmsiSplineCategory.Other;
            if (OmsiProfileSections.Of(profile).HasMedian)
                return OmsiSplineCategory.Median;
            if (vehicle.TrueForAll(lane => lane.Direction != 2 && lane.Direction == vehicle[0].Direction))
                return OmsiSplineCategory.OneWay;
            return vehicle.Count <= 2 ? OmsiSplineCategory.TwoLanes : vehicle.Count <= 4 ? OmsiSplineCategory.FourLanes : OmsiSplineCategory.SixLanes;
        }

        /// <summary>The name of a category for its tab.</summary>
        public static string Name(OmsiSplineCategory category)
        {
            switch (category)
            {
                case OmsiSplineCategory.OneWay: return "One-way";
                case OmsiSplineCategory.TwoLanes: return "2 lanes";
                case OmsiSplineCategory.FourLanes: return "3-4 lanes";
                case OmsiSplineCategory.SixLanes: return "5+ lanes";
                case OmsiSplineCategory.Median: return "Median";
                case OmsiSplineCategory.Rail: return "Rail";
                case OmsiSplineCategory.Path: return "Paths";
                default: return "Other";
            }
        }
    }

    /// <summary>
    /// Draws a preview of a spline profile: a short straight piece seen from above, running up, with the textures of its
    /// strips and arrows for the direction of its vehicle lanes. The top surface wins where strips overlap; upright strips
    /// such as kerb faces do not show from above.
    /// </summary>
    public static class OmsiSplineThumbnail
    {
        /// <param name="texture">Gives the decoded texture of a profile texture path, or null when it cannot be read.</param>
        /// <returns>RGBA pixels, rows from top to bottom; transparent beside the profile.</returns>
        public static byte[] Render(OmsiSplineProfile profile, Func<string, OmsiImage> texture, int width, int height)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "A preview needs a positive size.");
            var pixels = new byte[width * height * 4];
            var left = profile.LeftEdge;
            var right = profile.RightEdge;
            if (!(right - left > 0.05))
            {
                left = -2.0;
                right = 2.0;
            }
            // Metres per pixel, the same both ways; the profile fills the width less a margin of two pixels each side.
            var scale = (right - left) / Math.Max(1, width - 4);
            var images = new Dictionary<string, OmsiImage>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < width; column++)
            {
                var x = left + (column - 2 + 0.5) * scale;
                // The level piece of a strip with the highest surface at this distance.
                OmsiProfileStrip best = null;
                var bestIndex = -1;
                var bestHeight = double.MinValue;
                foreach (var strip in profile.Strips)
                    for (var index = 0; index + 1 < strip.Points.Count; index++)
                    {
                        var a = strip.Points[index];
                        var b = strip.Points[index + 1];
                        if (Math.Abs(b.X - a.X) < OmsiSplineSurface.MinimumWidth || x < Math.Min(a.X, b.X) || x > Math.Max(a.X, b.X))
                            continue;
                        var share = (x - a.X) / (b.X - a.X);
                        var surface = a.Height + (b.Height - a.Height) * share;
                        if (surface > bestHeight)
                        {
                            bestHeight = surface;
                            best = strip;
                            bestIndex = index;
                        }
                    }
                if (best == null || best.TextureIndex < 0 || best.TextureIndex >= profile.Textures.Count)
                    continue;
                var from = best.Points[bestIndex];
                var to = best.Points[bestIndex + 1];
                var t = (x - from.X) / (to.X - from.X);
                var u = from.U + (to.U - from.U) * t;
                var vPerMeter = from.VPerMeter + (to.VPerMeter - from.VPerMeter) * t;
                var path = profile.Textures[best.TextureIndex].Path;
                if (!images.TryGetValue(path, out var image))
                {
                    image = texture?.Invoke(path);
                    images[path] = image;
                }
                for (var row = 0; row < height; row++)
                {
                    var along = (height - row - 0.5) * scale;
                    var offset = (row * width + column) * 4;
                    if (image == null)
                    {
                        // Without its texture a strip shows in a grey of its own.
                        var grey = (byte)(90 + best.TextureIndex * 37 % 120);
                        pixels[offset] = grey;
                        pixels[offset + 1] = grey;
                        pixels[offset + 2] = grey;
                        pixels[offset + 3] = 255;
                        continue;
                    }
                    Sample(image, u, along * vPerMeter, pixels, offset);
                }
            }
            DrawArrows(profile, pixels, width, height, left, scale);
            return pixels;
        }

        /// <summary>The texel at texture coordinates that repeat, v running down the image as in OMSI.</summary>
        private static void Sample(OmsiImage image, double u, double v, byte[] pixels, int offset)
        {
            var x = (int)Math.Floor((u - Math.Floor(u)) * image.Width);
            var y = (int)Math.Floor((v - Math.Floor(v)) * image.Height);
            x = Math.Max(0, Math.Min(image.Width - 1, x));
            y = Math.Max(0, Math.Min(image.Height - 1, y));
            var source = (y * image.Width + x) * image.Channels;
            if (image.Channels == 1)
            {
                pixels[offset] = image.Pixels[source];
                pixels[offset + 1] = image.Pixels[source];
                pixels[offset + 2] = image.Pixels[source];
                pixels[offset + 3] = 255;
                return;
            }
            pixels[offset] = image.Pixels[source];
            pixels[offset + 1] = image.Pixels[source + 1];
            pixels[offset + 2] = image.Pixels[source + 2];
            pixels[offset + 3] = image.Pixels[source + 3];
        }

        /// <summary>A white arrow head in the middle of each vehicle lane, pointing the way it runs; two for lanes used both ways.</summary>
        private static void DrawArrows(OmsiSplineProfile profile, byte[] pixels, int width, int height, double left, double scale)
        {
            var size = Math.Max(3, Math.Min(width, height) / 12);
            foreach (var lane in profile.Lanes)
            {
                if (lane.Kind != 0 || lane.Width <= 0.0)
                    continue;
                var column = (int)Math.Round((lane.X - left) / scale) + 2;
                if (lane.Direction == 2)
                {
                    Arrow(pixels, width, height, column, height / 3, size, true);
                    Arrow(pixels, width, height, column, height * 2 / 3, size, false);
                }
                else
                    Arrow(pixels, width, height, column, height / 2, size, lane.Direction == 0);
            }
        }

        private static void Arrow(byte[] pixels, int width, int height, int centre, int middle, int size, bool up)
        {
            for (var step = 0; step < size; step++)
            {
                var row = up ? middle - size / 2 + step : middle + size / 2 - step;
                for (var column = centre - step; column <= centre + step; column++)
                {
                    if (row < 0 || row >= height || column < 0 || column >= width)
                        continue;
                    var offset = (row * width + column) * 4;
                    pixels[offset] = 255;
                    pixels[offset + 1] = 255;
                    pixels[offset + 2] = 255;
                    pixels[offset + 3] = 255;
                }
            }
        }
    }
}
