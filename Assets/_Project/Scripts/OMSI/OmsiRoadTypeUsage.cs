using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// How much of the open map uses a road type (owner's decision of 18 November 2026, point 1): the splines that have it as their
    /// profile, called pieces, and the junctions the editor made with it on an arm. Only the open map is counted, not the other maps of the
    /// installation; <see cref="OmsiRoadTypeStore.UsedBy"/> searches those, in the background, when a road type is to be deleted.
    /// </summary>
    public sealed class OmsiRoadTypeUsage
    {
        public OmsiRoadTypeUsage(int pieces, List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>> junctions)
        {
            Pieces = pieces;
            Junctions = junctions ?? new List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>>();
        }

        /// <summary>The splines of the map whose profile is the road type.</summary>
        public int Pieces { get; }

        /// <summary>The junctions of the map the editor made with the road type on an arm, by tile.</summary>
        public List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>> Junctions { get; }

        public int JunctionCount => Junctions.Count;

        public bool IsUnused => Pieces == 0 && Junctions.Count == 0;

        /// <summary>Counts what in the map uses the road type; the junction files are read from the installation (each file once).</summary>
        /// <param name="installationRoot">The OMSI installation, where the made junctions' files lie; null counts no junctions.</param>
        public static OmsiRoadTypeUsage Of(OmsiMapSummary map, string installationRoot, string assetPath)
        {
            if (map == null || string.IsNullOrWhiteSpace(assetPath))
                return new OmsiRoadTypeUsage(0, null);
            var pieces = 0;
            var junctions = new List<KeyValuePair<OmsiTileSummary, OmsiObjectRecord>>();
            var known = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var tile in map.Tiles)
            {
                foreach (var spline in tile.Splines)
                    if (OmsiWholeRoad.SameAsset(spline.AssetPath, assetPath))
                        pieces++;
                if (installationRoot == null)
                    continue;
                foreach (var record in tile.Objects)
                {
                    if (!OmsiGeneratedJunction.IsMadeHere(record.AssetPath))
                        continue;
                    if (!known.TryGetValue(record.AssetPath, out var uses))
                    {
                        var made = OmsiGeneratedJunction.Read(installationRoot, record.AssetPath, null);
                        uses = made != null && made.Arms.Exists(arm => OmsiWholeRoad.SameAsset(arm.ProfilePath, assetPath));
                        known[record.AssetPath] = uses;
                    }
                    if (uses)
                        junctions.Add(new KeyValuePair<OmsiTileSummary, OmsiObjectRecord>(tile, record));
                }
            }
            return new OmsiRoadTypeUsage(pieces, junctions);
        }
    }
}
