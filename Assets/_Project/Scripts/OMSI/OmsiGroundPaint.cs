using System;

namespace OmsiMapEditor.OMSI
{
    public enum OmsiPaintMode
    {
        /// <summary>Lays the chosen texture on, and takes the textures painted over it away under the brush.</summary>
        Paint,

        /// <summary>Takes the chosen texture away, so the one beneath shows.</summary>
        Erase,

        /// <summary>Blurs the chosen texture's edge, for a soft transition.</summary>
        Soften
    }

    /// <summary>One position of the brush: centre and radius in metres, how much of the radius is solid, and how strong it paints.</summary>
    public struct OmsiPaintDab
    {
        public double X;
        public double Y;
        public double Radius;

        /// <summary>0 to 1: the share of the radius that paints fully; outside it the coverage falls off smoothly to the edge.</summary>
        public double Hardness;

        /// <summary>0 to 1: how much of a pixel the brush covers at most.</summary>
        public double Strength;
    }

    /// <summary>
    /// The brush of ground painting (ADR 091). A mask holds the coverage of one texture; every operation only moves a pixel towards a target
    /// (paint takes the larger value, erase the smaller), so a brush held in place does no more than one dab, and a stroke does not depend on
    /// how often it is sampled.
    /// </summary>
    public static class OmsiGroundPaint
    {
        /// <summary>The share of the brush at a distance from its centre: 1 inside the solid part, then a smooth fall to 0 at the radius.</summary>
        public static double Falloff(double distance, double radius, double hardness)
        {
            if (radius <= 0.0 || distance >= radius)
                return 0.0;
            var solid = Math.Max(0.0, Math.Min(0.999, hardness)) * radius;
            if (distance <= solid)
                return 1.0;
            var t = (distance - solid) / (radius - solid);
            var s = 1.0 - t;
            return s * s * (3.0 - 2.0 * s);
        }

        /// <summary>Whether the circle of a dab reaches the square tile whose south-west corner is given.</summary>
        public static bool Reaches(double originX, double originY, double tileSize, OmsiPaintDab dab)
        {
            return dab.X + dab.Radius > originX && dab.X - dab.Radius < originX + tileSize && dab.Y + dab.Radius > originY &&
                   dab.Y - dab.Radius < originY + tileSize;
        }

        /// <summary>Raises the coverage under the brush to its strength; false when no pixel changed.</summary>
        public static bool Raise(OmsiGroundMask mask, double originX, double originY, double tileSize, OmsiPaintDab dab)
        {
            return Apply(mask, originX, originY, tileSize, dab, (current, share) =>
            {
                var target = (byte)Math.Round(255.0 * share * dab.Strength);
                return target > current ? target : current;
            });
        }

        /// <summary>
        /// Lowers the coverage under the brush: a pixel keeps at most what the brush leaves it, 1 minus its share (times the strength).
        /// </summary>
        public static bool Lower(OmsiGroundMask mask, double originX, double originY, double tileSize, OmsiPaintDab dab)
        {
            return Apply(mask, originX, originY, tileSize, dab, (current, share) =>
            {
                var limit = (byte)Math.Round(255.0 * (1.0 - share * dab.Strength));
                return limit < current ? limit : current;
            });
        }

        /// <summary>Blends each pixel under the brush towards the average of its neighbours.</summary>
        public static bool Soften(OmsiGroundMask mask, double originX, double originY, double tileSize, OmsiPaintDab dab)
        {
            var size = mask.Size;
            var source = (byte[])mask.Alpha.Clone();
            return Apply(mask, originX, originY, tileSize, dab, (current, share) => current, (px, py, current, share) =>
            {
                var sum = 0;
                var count = 0;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var x = px + dx;
                        var y = py + dy;
                        if (x < 0 || y < 0 || x >= size || y >= size)
                            continue;
                        sum += source[y * size + x];
                        count++;
                    }
                var average = sum / (double)count;
                var weight = Math.Min(1.0, share * dab.Strength);
                return (byte)Math.Round(current + (average - current) * weight);
            });
        }

        /// <summary>Sets every pixel of the mask to a coverage.</summary>
        public static bool Fill(OmsiGroundMask mask, byte coverage)
        {
            var changed = false;
            for (var index = 0; index < mask.Alpha.Length; index++)
                if (mask.Alpha[index] != coverage)
                {
                    mask.Alpha[index] = coverage;
                    changed = true;
                }
            return changed;
        }

        /// <summary>The coverage of the pixel in a column and a row, as a share of 0 to 1.</summary>
        public static double CoverageAt(OmsiGroundMask mask, int column, int row)
        {
            return mask.Alpha[row * mask.Size + column] / 255.0;
        }

        private static bool Apply(OmsiGroundMask mask, double originX, double originY, double tileSize, OmsiPaintDab dab, Func<byte, double, byte> change,
            Func<int, int, byte, double, byte> perPixel = null)
        {
            if (mask == null || dab.Radius <= 0.0 || tileSize <= 0.0 || !Reaches(originX, originY, tileSize, dab))
                return false;
            var size = mask.Size;
            var pixel = tileSize / size;
            // The columns count from the west edge, the rows from the north edge; a pixel's centre is half a pixel in.
            var firstColumn = Math.Max(0, (int)Math.Floor((dab.X - dab.Radius - originX) / pixel) - 1);
            var lastColumn = Math.Min(size - 1, (int)Math.Ceiling((dab.X + dab.Radius - originX) / pixel) + 1);
            var firstRow = Math.Max(0, (int)Math.Floor((originY + tileSize - dab.Y - dab.Radius) / pixel) - 1);
            var lastRow = Math.Min(size - 1, (int)Math.Ceiling((originY + tileSize - dab.Y + dab.Radius) / pixel) + 1);
            var changed = false;
            for (var row = firstRow; row <= lastRow; row++)
            {
                var y = originY + tileSize - (row + 0.5) * pixel;
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var x = originX + (column + 0.5) * pixel;
                    var share = Falloff(Math.Sqrt((x - dab.X) * (x - dab.X) + (y - dab.Y) * (y - dab.Y)), dab.Radius, dab.Hardness);
                    if (share <= 0.0)
                        continue;
                    var index = row * size + column;
                    var current = mask.Alpha[index];
                    var next = perPixel != null ? perPixel(column, row, current, share) : change(current, share);
                    if (next == current)
                        continue;
                    mask.Alpha[index] = next;
                    changed = true;
                }
            }
            return changed;
        }
    }
}
