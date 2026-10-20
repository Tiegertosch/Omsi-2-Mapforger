using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Read-only overview of an OMSI map and its referenced map tiles.</summary>
    public sealed class OmsiMapSummary
    {
        /// <summary>Tile edge of classic maps without [worldcoordinates].</summary>
        public const double ClassicTileSizeMeters = 300.0;

        /// <summary>
        /// Tile edge of [worldcoordinates] maps, measured from Berlin-Spandau's cross-tile spline links.
        /// OMSI derives these tiles from geographic coordinates, so the true edge varies slightly with latitude;
        /// this constant keeps the measured links within about 0.35 m.
        /// </summary>
        public const double WorldCoordinateTileSizeMeters = 371.9;

        public string Name { get; internal set; }
        public string FriendlyName { get; internal set; }
        public string Description { get; internal set; }
        public string RootPath { get; internal set; }
        public string GlobalConfigPath { get; internal set; }
        public List<OmsiTileSummary> Tiles { get; } = new List<OmsiTileSummary>();
        public List<string> Warnings { get; } = new List<string>();
        public double TileSizeMeters { get; internal set; } = ClassicTileSizeMeters;

        /// <summary>True when global.cfg contains [worldcoordinates] and the wider tile grid applies.</summary>
        public bool UsesWorldCoordinates { get; internal set; }

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

        /// <summary>ID of the spline connected to this spline's start, or 0 when the start is open.</summary>
        public int PreviousId { get; internal set; }

        /// <summary>ID of the spline connected to this spline's end, or 0 when the end is open.</summary>
        public int NextId { get; internal set; }
        public double LocalX { get; internal set; }
        public double LocalY { get; internal set; }
        public double Height { get; internal set; }
        public double HeadingDegrees { get; internal set; }
        public double LengthMeters { get; internal set; }

        /// <summary>Constant curve radius; 0 is straight and a positive radius turns right (clockwise in plan view).</summary>
        public double RadiusMeters { get; internal set; }

        /// <summary>
        /// True when the record comes from a [spline_h] section. It shares the leading fields of [spline] and has at
        /// least one more field at the end whose meaning is not confirmed yet.
        /// </summary>
        public bool IsSplineH { get; internal set; }
    }
}
