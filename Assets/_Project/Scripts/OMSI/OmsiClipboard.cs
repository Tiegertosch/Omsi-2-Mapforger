using System;
using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>An object hung on an object being copied: its point, turn, labels, and what hangs on it in turn.</summary>
    public sealed class OmsiPasteAttached
    {
        public string AssetPath { get; set; }
        public int PointIndex { get; set; }
        public double HeadingDegrees { get; set; }
        public double PitchDegrees { get; set; }
        public double BankDegrees { get; set; }
        public IReadOnlyList<string> Labels { get; set; } = new string[0];
        public List<OmsiPasteAttached> Children { get; } = new List<OmsiPasteAttached>();
    }

    /// <summary>
    /// An object that was copied, by value: nothing in it points into the map, so a later edit, deletion or reload cannot
    /// change it. World metres east and north of the map's origin.
    /// </summary>
    public sealed class OmsiPasteItem
    {
        public string AssetPath { get; set; }
        public double WorldX { get; set; }
        public double WorldY { get; set; }

        /// <summary>The written height: above the ground for most objects, absolute for junctions and [absheight] objects.</summary>
        public double Height { get; set; }

        /// <summary>The terrain height under the original, so an object of absolute height keeps its distance from the ground; null when unknown.</summary>
        public double? SourceGround { get; set; }

        public double HeadingDegrees { get; set; }
        public double PitchDegrees { get; set; }
        public double BankDegrees { get; set; }
        public IReadOnlyList<string> Labels { get; set; } = new string[0];

        /// <summary>The object's [rule] records, as written (a [kill_rule] is not copied).</summary>
        public List<OmsiPathRule> Rules { get; } = new List<OmsiPathRule>();

        public List<OmsiPasteAttached> Attached { get; } = new List<OmsiPasteAttached>();
    }

    /// <summary>A copied object put at a new place: the world position and the heading it will have.</summary>
    public sealed class OmsiPasteTarget
    {
        public OmsiPasteItem Item { get; set; }
        public double WorldX { get; set; }
        public double WorldY { get; set; }
        public double HeadingDegrees { get; set; }
    }

    /// <summary>Objects copied to be put down again, with the middle of their positions as the point they are held by.</summary>
    public sealed class OmsiClipboardData
    {
        /// <summary>Objects made by MapWorks (junctions under Sceneryobjects\Mapforge) are not copied: copies would share one object file.</summary>
        public const string GeneratedFolder = @"Sceneryobjects\Mapforge\";

        public List<OmsiPasteItem> Items { get; } = new List<OmsiPasteItem>();

        /// <summary>The middle of the bounding box of the positions: what the pointer holds while the copies are placed, and what a turn is about.</summary>
        public double CentreX { get; private set; }

        public double CentreY { get; private set; }

        /// <summary>How many selected objects were left out because MapWorks made their files.</summary>
        public int GeneratedLeftOut { get; private set; }

        public int AttachedCount
        {
            get
            {
                var count = 0;
                void Walk(List<OmsiPasteAttached> list)
                {
                    foreach (var item in list)
                    {
                        count++;
                        Walk(item.Children);
                    }
                }
                foreach (var item in Items)
                    Walk(item.Attached);
                return count;
            }
        }

        /// <summary>Whether an object file is one that MapWorks generated for a junction (its copies would share it).</summary>
        public static bool IsGenerated(string assetPath)
        {
            return assetPath != null && assetPath.Replace('/', '\\').StartsWith(GeneratedFolder, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Takes a copy of objects with their labels, pitch and bank, rules and the objects hung on them.</summary>
        /// <param name="groundAt">The terrain height at a world position, or null where there is none; may be null.</param>
        public static OmsiClipboardData Capture(OmsiMapSummary summary, IEnumerable<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>> objects,
            Func<double, double, double?> groundAt = null)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (objects == null)
                throw new ArgumentNullException(nameof(objects));
            var data = new OmsiClipboardData();
            var size = summary.TileSizeMeters;
            foreach (var pair in objects)
            {
                var tile = pair.Key;
                var record = pair.Value;
                if (IsGenerated(record.AssetPath))
                {
                    data.GeneratedLeftOut++;
                    continue;
                }
                var x = tile.X * size + record.LocalX;
                var y = tile.Y * size + record.LocalY;
                var item = new OmsiPasteItem
                {
                    AssetPath = record.AssetPath, WorldX = x, WorldY = y, Height = record.Height, SourceGround = groundAt?.Invoke(x, y),
                    HeadingDegrees = record.HeadingDegrees, PitchDegrees = record.PitchDegrees, BankDegrees = record.BankDegrees,
                    Labels = record.Labels.ToArray()
                };
                foreach (var rule in record.Rules)
                    if (!rule.Kills)
                        item.Rules.Add(new OmsiPathRule(rule.PathIndex, rule.Name, rule.Value, rule.Extra));
                item.Attached.AddRange(AttachedTo(tile, record.Id, 0));
                data.Items.Add(item);
            }
            if (data.Items.Count > 0)
            {
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var item in data.Items)
                {
                    minX = Math.Min(minX, item.WorldX);
                    minY = Math.Min(minY, item.WorldY);
                    maxX = Math.Max(maxX, item.WorldX);
                    maxY = Math.Max(maxY, item.WorldY);
                }
                data.CentreX = (minX + maxX) * 0.5;
                data.CentreY = (minY + maxY) * 0.5;
            }
            return data;
        }

        private static List<OmsiPasteAttached> AttachedTo(OmsiTileSummary tile, int parentId, int depth)
        {
            var list = new List<OmsiPasteAttached>();
            // A tile is not deeper than a handful of levels; the limit only guards against a loop of bad data.
            if (depth > 16)
                return list;
            foreach (var attached in tile.AttachedObjects)
            {
                if (attached.ParentId != parentId)
                    continue;
                var copy = new OmsiPasteAttached
                {
                    AssetPath = attached.AssetPath, PointIndex = attached.AttachPointIndex, HeadingDegrees = attached.HeadingDegrees,
                    PitchDegrees = attached.PitchDegrees, BankDegrees = attached.BankDegrees, Labels = attached.Labels.ToArray()
                };
                copy.Children.AddRange(AttachedTo(tile, attached.Id, depth + 1));
                list.Add(copy);
            }
            return list;
        }

        /// <summary>
        /// The copies with the middle of the group put at a world position and the group turned clockwise by an angle about it, the
        /// way the Rotate tool turns a selection: positions swing about the middle, headings turn with it.
        /// </summary>
        public List<OmsiPasteTarget> PlaceAt(double centreX, double centreY, double rotationDegrees)
        {
            var radians = rotationDegrees * Math.PI / 180.0;
            var sin = Math.Sin(radians);
            var cos = Math.Cos(radians);
            var targets = new List<OmsiPasteTarget>(Items.Count);
            foreach (var item in Items)
            {
                var x = item.WorldX - CentreX;
                var y = item.WorldY - CentreY;
                targets.Add(new OmsiPasteTarget
                {
                    Item = item,
                    WorldX = centreX + x * cos + y * sin,
                    WorldY = centreY - x * sin + y * cos,
                    HeadingDegrees = ((item.HeadingDegrees + rotationDegrees) % 360.0 + 360.0) % 360.0
                });
            }
            return targets;
        }
    }
}
