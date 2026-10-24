using System;
using System.Collections.Generic;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Typed access to one record of a map tile. Getters parse the current field text; setters write a single field
    /// in OMSI's number format and leave every other line, including free text, untouched.
    /// </summary>
    public abstract class OmsiTileRecordView
    {
        protected OmsiTileRecordView(OmsiRecord record)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        public OmsiRecord Record { get; }

        protected int GetInt(int index)
        {
            if (!Record.TryGetInt(index, out var value))
                throw new FormatException("Field " + index + " of [" + Record.Name + "] is not an integer: '" + Record.GetField(index) + "'.");
            return value;
        }

        protected double GetDouble(int index)
        {
            if (!Record.TryGetDouble(index, out var value))
                throw new FormatException("Field " + index + " of [" + Record.Name + "] is not a number: '" + Record.GetField(index) + "'.");
            return value;
        }

        protected void SetInt(int index, int value)
        {
            Record.SetField(index, OmsiNumberFormat.Format(value));
        }

        protected void SetDouble(int index, double value)
        {
            Record.SetField(index, OmsiNumberFormat.Format(value));
        }

        /// <summary>Counted text lines such as object labels: the count field, then that many lines.</summary>
        protected IReadOnlyList<string> GetCountedLines(int countIndex)
        {
            var count = GetInt(countIndex);
            var lines = new string[count];
            for (var index = 0; index < count; index++)
                lines[index] = Record.Fields[countIndex + 1 + index].Text;
            return lines;
        }

        protected void SetCountedLine(int countIndex, int lineIndex, string text)
        {
            var count = GetInt(countIndex);
            if (lineIndex < 0 || lineIndex >= count)
                throw new ArgumentOutOfRangeException(nameof(lineIndex), lineIndex, "The record has " + count + " text line(s).");
            Record.SetField(countIndex + 1 + lineIndex, text);
        }
    }

    /// <summary>An [object] record (tile version 9 and later).</summary>
    public sealed class OmsiObjectView : OmsiTileRecordView
    {
        internal OmsiObjectView(OmsiRecord record) : base(record) { }

        public int DetailLevel { get => GetInt(0); set => SetInt(0, value); }
        public string AssetPath { get => Record.GetField(1); set => Record.SetField(1, value); }
        public int Id { get => GetInt(2); set => SetInt(2, value); }
        public double X { get => GetDouble(3); set => SetDouble(3, value); }
        public double Y { get => GetDouble(4); set => SetDouble(4, value); }

        /// <summary>Height; relative to the terrain unless the object is absolute, such as a crossing with a [splinehelper].</summary>
        public double Z { get => GetDouble(5); set => SetDouble(5, value); }

        public double HeadingDegrees { get => GetDouble(6); set => SetDouble(6, value); }
        public double PitchDegrees { get => GetDouble(7); set => SetDouble(7, value); }
        public double BankDegrees { get => GetDouble(8); set => SetDouble(8, value); }

        /// <summary>Labels filling the object's string variables in order; empty labels are kept.</summary>
        public IReadOnlyList<string> Labels => GetCountedLines(9);

        public void SetLabel(int index, string text)
        {
            SetCountedLine(9, index, text);
        }

        /// <summary>Replaces all labels, changing their count if needed.</summary>
        public void SetLabels(IList<string> labels)
        {
            Record.ReplaceCountedLines(9, labels);
        }
    }

    /// <summary>An [attachObj] record (tile version 12 and later).</summary>
    public sealed class OmsiAttachedObjectView : OmsiTileRecordView
    {
        internal OmsiAttachedObjectView(OmsiRecord record) : base(record) { }

        public int DetailLevel { get => GetInt(0); set => SetInt(0, value); }
        public string AssetPath { get => Record.GetField(1); set => Record.SetField(1, value); }
        public int Id { get => GetInt(2); set => SetInt(2, value); }

        /// <summary>The parent record; it must be written earlier in the same tile.</summary>
        public int ParentId { get => GetInt(3); set => SetInt(3, value); }

        public int ParentInstance { get => GetInt(4); set => SetInt(4, value); }
        public int AttachPointIndex { get => GetInt(5); set => SetInt(5, value); }
        public double HeadingDegrees { get => GetDouble(6); set => SetDouble(6, value); }
        public double PitchDegrees { get => GetDouble(7); set => SetDouble(7, value); }
        public double BankDegrees { get => GetDouble(8); set => SetDouble(8, value); }
        public IReadOnlyList<string> Labels => GetCountedLines(9);

        public void SetLabel(int index, string text)
        {
            SetCountedLine(9, index, text);
        }

        public void SetLabels(IList<string> labels)
        {
            Record.ReplaceCountedLines(9, labels);
        }
    }

    /// <summary>
    /// A [splineAttachement] or [splineAttachement_repeater] record. A repeater has two extra leading fields and
    /// continues its master attachment on another tile.
    /// </summary>
    public sealed class OmsiSplineAttachmentView : OmsiTileRecordView
    {
        private readonly int offset;

        internal OmsiSplineAttachmentView(OmsiRecord record, bool isRepeater) : base(record)
        {
            IsRepeater = isRepeater;
            offset = isRepeater ? 2 : 0;
        }

        public bool IsRepeater { get; }
        public int DetailLevel { get => GetInt(0); set => SetInt(0, value); }

        /// <summary>Index of the master attachment's tile in global.cfg's [map] list; repeaters only.</summary>
        public int MasterTileIndex => IsRepeater ? GetInt(1) : throw new InvalidOperationException("Only repeaters have a master tile.");

        /// <summary>Number of the first object of this continuation; repeaters only.</summary>
        public int FirstObjectNumber => IsRepeater ? GetInt(2) : throw new InvalidOperationException("Only repeaters have a first object number.");

        public string AssetPath { get => Record.GetField(1 + offset); set => Record.SetField(1 + offset, value); }
        public int Id { get => GetInt(2 + offset); set => SetInt(2 + offset, value); }

        /// <summary>Index of the spline in this tile's spline order, counting [spline] and [spline_h] together.</summary>
        public int SplineIndex { get => GetInt(3 + offset); set => SetInt(3 + offset, value); }

        public double LateralOffset { get => GetDouble(4 + offset); set => SetDouble(4 + offset, value); }
        public double Height { get => GetDouble(5 + offset); set => SetDouble(5 + offset, value); }
        public double StartDistance { get => GetDouble(6 + offset); set => SetDouble(6 + offset, value); }
        public double HeadingDegrees { get => GetDouble(7 + offset); set => SetDouble(7 + offset, value); }
        public double PitchDegrees { get => GetDouble(8 + offset); set => SetDouble(8 + offset, value); }
        public double BankDegrees { get => GetDouble(9 + offset); set => SetDouble(9 + offset, value); }
        public double Interval { get => GetDouble(10 + offset); set => SetDouble(10 + offset, value); }
        public double Range { get => GetDouble(11 + offset); set => SetDouble(11 + offset, value); }
        public int TiltFlag { get => GetInt(12 + offset); set => SetInt(12 + offset, value); }
        public IReadOnlyList<string> Strings => GetCountedLines(13 + offset);
    }

    /// <summary>A [spline] (version 11 and later) or [spline_h] (version 14) record.</summary>
    public sealed class OmsiSplineView : OmsiTileRecordView
    {
        private readonly int cantIndex;
        private readonly int? skewIndex;
        private readonly int alignmentIndex;

        internal OmsiSplineView(OmsiRecord record, int version) : base(record)
        {
            IsSplineH = string.Equals(record.Name, "spline_h", StringComparison.OrdinalIgnoreCase);
            var shift = IsSplineH ? 1 : 0;
            cantIndex = 13 + shift;
            if (version >= 14)
            {
                skewIndex = 15 + shift;
                alignmentIndex = 17 + shift;
            }
            else
            {
                alignmentIndex = 15;
            }
        }

        public bool IsSplineH { get; }
        public int DetailLevel { get => GetInt(0); set => SetInt(0, value); }
        public string AssetPath { get => Record.GetField(1); set => Record.SetField(1, value); }
        public int Id { get => GetInt(2); set => SetInt(2, value); }
        public int PreviousId { get => GetInt(3); set => SetInt(3, value); }
        public int NextId { get => GetInt(4); set => SetInt(4, value); }
        public double X { get => GetDouble(5); set => SetDouble(5, value); }

        /// <summary>Absolute height of the start point.</summary>
        public double Height { get => GetDouble(6); set => SetDouble(6, value); }

        public double Y { get => GetDouble(7); set => SetDouble(7, value); }
        public double HeadingDegrees { get => GetDouble(8); set => SetDouble(8, value); }
        public double LengthMeters { get => GetDouble(9); set => SetDouble(9, value); }

        /// <summary>0 is straight; a positive radius turns right.</summary>
        public double RadiusMeters { get => GetDouble(10); set => SetDouble(10, value); }

        public double GradientStartPercent { get => GetDouble(11); set => SetDouble(11, value); }
        public double GradientEndPercent { get => GetDouble(12); set => SetDouble(12, value); }

        /// <summary>Height change from start to end; only [spline_h] stores it, a [spline] derives it from the gradients.</summary>
        public double HeightDifference
        {
            get => IsSplineH ? GetDouble(13) : LengthMeters * (GradientStartPercent + GradientEndPercent) / 200.0;
            set
            {
                if (!IsSplineH)
                    throw new InvalidOperationException("A [spline] has no height difference field; change its gradients instead.");
                SetDouble(13, value);
            }
        }

        public double CantStart { get => GetDouble(cantIndex); set => SetDouble(cantIndex, value); }
        public double CantEnd { get => GetDouble(cantIndex + 1); set => SetDouble(cantIndex + 1, value); }

        /// <summary>Skew exists from tile version 14; null before, and setting it there fails.</summary>
        public double? SkewStart
        {
            get => skewIndex.HasValue ? GetDouble(skewIndex.Value) : (double?)null;
            set => SetDouble(SkewIndex(), value ?? 0.0);
        }

        public double? SkewEnd
        {
            get => skewIndex.HasValue ? GetDouble(skewIndex.Value + 1) : (double?)null;
            set => SetDouble(SkewIndex() + 1, value ?? 0.0);
        }

        private int SkewIndex()
        {
            if (!skewIndex.HasValue)
                throw new InvalidOperationException("Splines of tiles before version 14 have no skew.");
            return skewIndex.Value;
        }

        public double AlignmentLength { get => GetDouble(alignmentIndex); set => SetDouble(alignmentIndex, value); }

        /// <summary>True when the optional mirror line follows; every path then lies at the negated offset.</summary>
        public bool IsMirrored
        {
            get => Record.Fields.Count > alignmentIndex + 1 &&
                   string.Equals(Record.GetField(alignmentIndex + 1), "mirror", StringComparison.Ordinal);
            set
            {
                if (value == IsMirrored)
                    return;
                if (value)
                    Record.InsertField(alignmentIndex + 1, "mirror");
                else
                    Record.RemoveField(alignmentIndex + 1);
            }
        }

        /// <summary>Height at the end point.</summary>
        public double EndHeight => Height + HeightDifference;
    }

    /// <summary>Creates typed views for the records of a map tile file.</summary>
    public static class OmsiTileRecords
    {
        /// <summary>
        /// Returns a view for a complete record with a verified layout, or null for other records such as rules,
        /// chrono commands or sections of unverified versions.
        /// </summary>
        public static OmsiTileRecordView Create(OmsiRecordFile file, OmsiRecord record)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (record == null || !record.HasKnownLayout || !record.IsComplete)
                return null;

            switch (record.Name.ToLowerInvariant())
            {
                case "object":
                    return new OmsiObjectView(record);
                case "attachobj":
                    return new OmsiAttachedObjectView(record);
                case "splineattachement":
                    return new OmsiSplineAttachmentView(record, false);
                case "splineattachement_repeater":
                    return new OmsiSplineAttachmentView(record, true);
                case "spline":
                case "spline_h":
                    return new OmsiSplineView(record, file.Version);
                default:
                    return null;
            }
        }

        public static IEnumerable<T> Of<T>(OmsiRecordFile file) where T : OmsiTileRecordView
        {
            foreach (var record in file.Records)
                if (Create(file, record) is T view)
                    yield return view;
        }

        /// <summary>
        /// IDs this tile defines: objects, attached objects, spline attachments and splines. Repeaters reuse the ID
        /// of their master attachment and are not counted.
        /// </summary>
        public static IEnumerable<int> DefinedIds(OmsiRecordFile file)
        {
            foreach (var record in file.Records)
            {
                switch (Create(file, record))
                {
                    case OmsiObjectView item:
                        yield return item.Id;
                        break;
                    case OmsiAttachedObjectView attached:
                        yield return attached.Id;
                        break;
                    case OmsiSplineAttachmentView attachment when !attachment.IsRepeater:
                        yield return attachment.Id;
                        break;
                    case OmsiSplineView spline:
                        yield return spline.Id;
                        break;
                }
            }
        }
    }
}
