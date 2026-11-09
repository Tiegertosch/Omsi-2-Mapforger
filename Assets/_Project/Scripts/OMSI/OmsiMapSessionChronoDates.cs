using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The days a chrono variant applies on: <c>[startdate]</c> and <c>[enddate]</c> in its <c>Chrono.cfg</c>, written as year, month and day
    /// run together (<c>20261109</c>), as the stock variants write them.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Gives the edited variant a first and last day (<paramref name="end"/> null keeps it applying), replacing what its Chrono.cfg said;
        /// the rest of the file stays as it is. Returns false when the file already said so. The variant's <see cref="OmsiChronoVariant.Start"/>
        /// and <see cref="OmsiChronoVariant.End"/> follow.
        /// </summary>
        public bool SetChronoDates(OmsiChronoVariant variant, DateTime start, DateTime? end)
        {
            if (variant == null)
                throw new ArgumentNullException(nameof(variant));
            if (EditedVariant == null || !string.Equals(EditedVariant.Name, variant.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Edit " + variant.Title + " to change the days it applies on.");
            if (end.HasValue && end.Value.Date < start.Date)
                throw new ArgumentException("The variant must end on or after its first day.", nameof(end));

            var workspace = RequireWorkspace();
            var path = Path.Combine(variant.Folder, "Chrono.cfg");
            var before = workspace.ReadAllBytes(path);
            var file = OmsiRecordFile.Parse(before, null);
            var first = file.Records.FirstOrDefault(record => string.Equals(record.Name, "startdate", StringComparison.OrdinalIgnoreCase));
            var last = file.Records.FirstOrDefault(record => string.Equals(record.Name, "enddate", StringComparison.OrdinalIgnoreCase));
            // The records of Chrono.cfg have no known layout, so a day is not set in place: a new record takes the old one's place.
            first = Replace(file, first, "startdate", Date(start), 0);
            if (!end.HasValue)
            {
                if (last != null)
                    file.RemoveRecord(last);
            }
            else
                Replace(file, last, "enddate", Date(end.Value), file.Records.IndexOf(first) + 1);

            var after = file.ToBytes();
            var same = before.SequenceEqual(after);
            if (!same)
                workspace.Save(path, after);
            foreach (var target in new[] { variant, EditedVariant })
            {
                target.Start = start.Date;
                target.End = end?.Date;
            }
            return !same;
        }

        /// <summary>Puts a record with one value where <paramref name="old"/> stood, keeping the blank lines after it; at <paramref name="insertAt"/> when there was none.</summary>
        private static OmsiRecord Replace(OmsiRecordFile file, OmsiRecord old, string name, string value, int insertAt)
        {
            var at = insertAt;
            var blankLines = 1;
            if (old != null)
            {
                at = file.Records.IndexOf(old);
                blankLines = 0;
                for (var index = old.Trailer.Count - 1; index >= 0 && old.Trailer[index].Text.Length == 0; index--)
                    blankLines++;
                file.RemoveRecord(old);
            }
            var record = file.CreateRecord(name, new[] { value }, blankLines);
            file.InsertRecord(at, record);
            return record;
        }
    }
}
