using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Read-only overview of an OMSI map and its referenced map tiles.</summary>
    public sealed class OmsiMapSummary
    {
        public string Name { get; internal set; }
        public string FriendlyName { get; internal set; }
        public string Description { get; internal set; }
        public string RootPath { get; internal set; }
        public string GlobalConfigPath { get; internal set; }
        public List<OmsiTileSummary> Tiles { get; } = new List<OmsiTileSummary>();
        public List<string> Warnings { get; } = new List<string>();
        public double TileSizeMeters { get; internal set; } = 300.0;

        public int ObjectCount { get; internal set; }
        public int SplineCount { get; internal set; }
        public int AttachmentCount { get; internal set; }
        public int TerrainTileCount { get; internal set; }
    }

    /// <summary>Counts recognizable records in one OMSI map tile.</summary>
    public sealed class OmsiTileSummary
    {
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public string FileName { get; internal set; }
        public int ObjectCount { get; internal set; }
        public int SplineCount { get; internal set; }
        public int AttachmentCount { get; internal set; }
        public bool IsAvailable { get; internal set; }
        public List<OmsiObjectRecord> Objects { get; } = new List<OmsiObjectRecord>();
        public List<OmsiSplineRecord> Splines { get; } = new List<OmsiSplineRecord>();
        public float TerrainMinHeight { get; internal set; }
        public float TerrainMaxHeight { get; internal set; }
        public bool HasTerrain { get; internal set; }
    }

    /// <summary>Placement values needed for a top-down map overview.</summary>
    public sealed class OmsiObjectRecord
    {
        public string AssetPath { get; internal set; }
        public int Id { get; internal set; }
        public double LocalX { get; internal set; }
        public double LocalY { get; internal set; }
        public double Height { get; internal set; }
        public double HeadingDegrees { get; internal set; }
    }

    /// <summary>Plan-view curve parameters for one map spline.</summary>
    public sealed class OmsiSplineRecord
    {
        public string AssetPath { get; internal set; }
        public int Id { get; internal set; }
        public double LocalX { get; internal set; }
        public double LocalY { get; internal set; }
        public double Height { get; internal set; }
        public double HeadingDegrees { get; internal set; }
        public double LengthMeters { get; internal set; }
        public double RadiusMeters { get; internal set; }
    }
}
