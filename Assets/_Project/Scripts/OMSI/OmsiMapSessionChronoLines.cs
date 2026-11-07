using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The lines a chrono variant switches off (ADR 067): <c>[deactivate_lines]</c> in its <c>Chrono.cfg</c>, the number of lines and then
    /// one line name per line, each the name of a <c>.ttl</c> file, as the stock variants write it.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Makes <paramref name="lines"/> the lines the edited variant switches off, replacing what its Chrono.cfg said; the rest of the file
        /// stays as it is. Returns false when the file already said so. The variant's <see cref="OmsiChronoVariant.DeactivatedLines"/> follow.
        /// </summary>
        public bool SetDeactivatedLines(OmsiChronoVariant variant, IEnumerable<string> lines)
        {
            if (variant == null)
                throw new ArgumentNullException(nameof(variant));
            if (lines == null)
                throw new ArgumentNullException(nameof(lines));
            if (EditedVariant == null || !string.Equals(EditedVariant.Name, variant.Name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Edit " + variant.Title + " to change the lines it switches off.");
            var names = new List<string>();
            foreach (var name in lines)
            {
                if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\r') >= 0 || name.IndexOf('\n') >= 0)
                    throw new ArgumentException("A line name must be one line of text.", nameof(lines));
                if (!names.Exists(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)))
                    names.Add(name);
            }

            var workspace = RequireWorkspace();
            var path = System.IO.Path.Combine(variant.Folder, "Chrono.cfg");
            var before = workspace.ReadAllBytes(path);
            var file = OmsiRecordFile.Parse(before, null);
            // The new block takes the place of the old one, so a file that already says so stays byte for byte as it was.
            var existing = file.Records.Where(record => string.Equals(record.Name, "deactivate_lines", StringComparison.OrdinalIgnoreCase)).ToList();
            var at = existing.Count > 0 ? file.Records.IndexOf(existing[0]) : file.Records.Count;
            // The stock files end right after the last line name; a block in the middle has its blank line.
            var blankLines = 1;
            if (existing.Count > 0)
            {
                blankLines = 0;
                var trailer = existing[0].Trailer;
                for (var index = trailer.Count - 1; index >= 0 && trailer[index].Text.Length == 0; index--)
                    blankLines++;
            }
            foreach (var record in existing)
                file.RemoveRecord(record);
            if (names.Count > 0)
            {
                var fields = new List<string> { names.Count.ToString(CultureInfo.InvariantCulture) };
                fields.AddRange(names);
                file.InsertRecord(at, file.CreateRecord("deactivate_lines", fields, blankLines));
            }
            var after = file.ToBytes();
            var same = before.SequenceEqual(after);
            if (!same)
                workspace.Save(path, after);
            foreach (var target in new[] { variant, EditedVariant })
            {
                target.DeactivatedLines.Clear();
                target.DeactivatedLines.AddRange(names);
            }
            return !same;
        }
    }
}
