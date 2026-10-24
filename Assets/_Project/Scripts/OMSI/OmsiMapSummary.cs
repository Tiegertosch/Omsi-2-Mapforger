using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Read-only overview of an OMSI map and its referenced map tiles.</summary>
    public sealed class OmsiMapSummary
    {
        /// <summary>Tile edge of classic maps without [worldcoordinates].</summary>
        public const double ClassicTileSizeMeters = 300.0;

        public string Name { get; internal set; }
        public string FriendlyName { get; internal set; }
        public string Description { get; internal set; }
        public string RootPath { get; internal set; }
        public string GlobalConfigPath { get; internal set; }
        public List<OmsiTileSummary> Tiles { get; } = new List<OmsiTileSummary>();
        public List<string> Warnings { get; } = new List<string>();
        /// <summary>
        /// The edge of the tiles in metres: 300 for classic maps; for [worldcoordinates] maps the edge of their middle row
        /// of Web Mercator tiles (<see cref="OmsiWorldTiles.GridEdgeMeters"/>), about 371.9 m for Berlin-Spandau.
        /// </summary>
        public double TileSizeMeters { get; internal set; } = ClassicTileSizeMeters;

        /// <summary>True when global.cfg contains [worldcoordinates]: the tiles are Web Mercator tiles (<see cref="OmsiWorldTiles"/>).</summary>
        public bool UsesWorldCoordinates { get; internal set; }

        /// <summary>The [NextIDCode] value of global.cfg, or null when it is missing.</summary>
        public int? NextIdCode { get; internal set; }

        /// <summary>Highest object, attachment or spline ID in the base tiles; chrono tiles are not read here.</summary>
        public int HighestBaseTileId { get; internal set; }

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

        /// <summary>[attachObj] records: objects hanging at an attachment point of an earlier object of this tile.</summary>
        public List<OmsiAttachedObjectRecord> AttachedObjects { get; } = new List<OmsiAttachedObjectRecord>();

        /// <summary>[splineAttachement] records and their repeaters, in file order.</summary>
        public List<OmsiSplineAttachmentRecord> SplineAttachments { get; } = new List<OmsiSplineAttachmentRecord>();
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

        /// <summary>
        /// Tilt about the object's right axis; positive lowers its front. OMSI turns an object by bank, then pitch, then
        /// heading, like Direct3D's yaw-pitch-roll.
        /// </summary>
        public double PitchDegrees { get; internal set; }

        /// <summary>Tilt about the object's forward axis; positive raises its right side.</summary>
        public double BankDegrees { get; internal set; }
    }

    /// <summary>An object attached to an attachment point ([new_attachment]) of its parent object.</summary>
    public sealed class OmsiAttachedObjectRecord
    {
        public string AssetPath { get; internal set; }
        public int Id { get; internal set; }

        /// <summary>The parent's ID; the parent is written earlier in the same tile.</summary>
        public int ParentId { get; internal set; }

        /// <summary>Index of the parent's [new_attachment] point.</summary>
        public int AttachPointIndex { get; internal set; }

        public double HeadingDegrees { get; internal set; }

        /// <summary>Pitch and bank as for <see cref="OmsiObjectRecord"/>, relative to the attachment point.</summary>
        public double PitchDegrees { get; internal set; }

        public double BankDegrees { get; internal set; }
    }

    /// <summary>A row of objects along a spline chain: lamps, posts, signs or a single bus stop.</summary>
    public sealed class OmsiSplineAttachmentRecord
    {
        public string AssetPath { get; internal set; }
        public int Id { get; internal set; }

        /// <summary>The spline in this tile's spline order, [spline] and [spline_h] counted together.</summary>
        public int SplineIndex { get; internal set; }

        /// <summary>Distance to the right of the spline.</summary>
        public double LateralOffset { get; internal set; }

        /// <summary>Height above the spline.</summary>
        public double Height { get; internal set; }

        /// <summary>Distance of the first object from the start of the spline.</summary>
        public double StartDistance { get; internal set; }

        /// <summary>Heading relative to the spline's direction.</summary>
        public double HeadingDegrees { get; internal set; }

        /// <summary>Distance between objects; 0 places one object.</summary>
        public double Interval { get; internal set; }

        /// <summary>Objects follow while their distance from the first does not exceed this.</summary>
        public double Range { get; internal set; }

        /// <summary>True for [splineAttachement_repeater], which continues a row on another tile.</summary>
        public bool IsRepeater { get; internal set; }
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

        /// <summary>Absolute height at the end: the start height plus the height difference of the record.</summary>
        public double EndHeight { get; internal set; }

        /// <summary>Gradient in percent at the start and the end; it changes evenly in between (fields 11 and 12).</summary>
        public double GradientStartPercent { get; internal set; }

        public double GradientEndPercent { get; internal set; }

        /// <summary>
        /// The height after <paramref name="distance"/> metres: the start height plus the gradient running evenly from
        /// start to end, corrected evenly so the end has <see cref="EndHeight"/>, as [spline_h] states it directly.
        /// </summary>
        public double HeightAt(double distance)
        {
            if (!(LengthMeters > 0.0))
                return Height;
            return Height + Rise(distance) + (EndHeight - Height - Rise(LengthMeters)) * distance / LengthMeters;
        }

        /// <summary>The gradient in percent after <paramref name="distance"/> metres, with the same correction as <see cref="HeightAt"/>.</summary>
        public double GradientAtPercent(double distance)
        {
            if (!(LengthMeters > 0.0))
                return 0.0;
            var gradient = GradientStartPercent + (GradientEndPercent - GradientStartPercent) * distance / LengthMeters;
            return gradient + (EndHeight - Height - Rise(LengthMeters)) / LengthMeters * 100.0;
        }

        private double Rise(double distance)
        {
            var gradient = GradientStartPercent + (GradientEndPercent - GradientStartPercent) * distance / LengthMeters;
            return distance * (GradientStartPercent + gradient) / 200.0;
        }

        public double HeadingDegrees { get; internal set; }
        public double LengthMeters { get; internal set; }

        /// <summary>Constant curve radius; 0 is straight and a positive radius turns right (clockwise in plan view).</summary>
        public double RadiusMeters { get; internal set; }

        /// <summary>
        /// True when the record comes from a [spline_h] section. It shares the leading fields of [spline] and inserts
        /// the height difference between start and end as field 13; cant, skew and alignment move back by one.
        /// </summary>
        public bool IsSplineH { get; internal set; }

        /// <summary>True when the record ends with "mirror": the cross-section is turned over.</summary>
        public bool IsMirrored { get; internal set; }

        /// <summary>
        /// True when [spline_terrain_align] or [spline_terrain_align_2] follows the spline: the terrain was fitted to it, as
        /// OMSI's editor marks such roads, and OMSI cuts the terrain away under it and closes the cut up to the road edge
        /// (seen in OMSI by the user on 24 October 2026).
        /// </summary>
        public bool AlignsTerrain { get; internal set; }
    }
}
