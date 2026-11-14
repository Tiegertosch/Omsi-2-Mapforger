using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>One line of global.cfg's [entrypoints]: where a bus starts, and the object of its tile that stands for it.</summary>
    public sealed class OmsiEntryPoint
    {
        public string Name { get; internal set; }

        /// <summary>The tile's position in the [map] list of global.cfg, which is also its position in <see cref="OmsiMapSummary.Tiles"/>.</summary>
        public int TileIndex { get; internal set; }

        /// <summary>The tile it stands in, or null when the index names no tile.</summary>
        public OmsiTileSummary Tile { get; internal set; }

        public int ObjectId { get; internal set; }

        /// <summary>Position in the tile, x, height and y as the record holds them.</summary>
        public double X { get; internal set; }

        public double Height { get; internal set; }

        public double Y { get; internal set; }

        public double HeadingDegrees { get; internal set; }

        /// <summary>The object the entry point stands on, in <see cref="Tile"/>; null when the tile has none with this ID.</summary>
        public OmsiObjectRecord Object { get; internal set; }
    }

    /// <summary>
    /// Making, renaming and deleting entry points (ADR 079). OMSI and openOMSI look an entry point up by the ID of its object in the tile the record names;
    /// the object's first label is its name in the editor, and the record repeats it. An entry point is therefore an object of
    /// <see cref="OmsiNewMap.EntryPointObject"/> and a line in [entrypoints], and every change here writes both as one undo step.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        private const int EntryFieldCount = 12;

        /// <summary>The entry points of the map as global.cfg lists them now.</summary>
        public List<OmsiEntryPoint> ReadEntryPoints(OmsiMapSummary summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            var config = OmsiRecordFile.Parse(Source.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            var entries = new List<OmsiEntryPoint>();
            var record = FindEntryList(config);
            if (record == null)
                return entries;
            for (var field = 1; field + EntryFieldCount <= record.Fields.Count; field += EntryFieldCount)
            {
                var entry = new OmsiEntryPoint { Name = record.GetField(field + 11) };
                if (!record.TryGetInt(field + 1, out var id) || !record.TryGetInt(field + 10, out var tileIndex))
                    continue;
                entry.ObjectId = id;
                entry.TileIndex = tileIndex;
                entry.Tile = tileIndex >= 0 && tileIndex < summary.Tiles.Count ? summary.Tiles[tileIndex] : null;
                entry.Object = entry.Tile?.Objects.Find(item => item.Id == id);
                record.TryGetDouble(field + 3, out var x);
                record.TryGetDouble(field + 4, out var height);
                record.TryGetDouble(field + 5, out var y);
                entry.X = x;
                entry.Height = height;
                entry.Y = y;
                if (record.TryGetDouble(field + 7, out var qy) && record.TryGetDouble(field + 9, out var qw))
                    entry.HeadingDegrees = OmsiRoadPlanner.Normalize(2.0 * Math.Atan2(qy, qw) * 180.0 / Math.PI);
                entries.Add(entry);
            }
            return entries;
        }

        /// <summary>The entry point that stands on this object of the tile, or null when the object is none.</summary>
        public OmsiEntryPoint EntryPointOf(OmsiMapSummary summary, OmsiTileSummary tile, OmsiObjectRecord item)
        {
            if (summary == null || tile == null || item == null)
                return null;
            var tileIndex = summary.Tiles.IndexOf(tile);
            return ReadEntryPoints(summary).Find(entry => entry.TileIndex == tileIndex && entry.ObjectId == item.Id);
        }

        /// <summary>
        /// Puts a new entry point into a tile: an <see cref="OmsiNewMap.EntryPointObject"/> that carries the name as its label, and the line in
        /// [entrypoints], as one undo step. The object stands on the terrain (height 0); the line holds the height of the ground there.
        /// Several entry points may share a name, as the places for buses at a depot do.
        /// </summary>
        public OmsiObjectChange AddEntryPoint(OmsiMapSummary summary, OmsiTileSummary tile, string name, double x, double y, double groundHeight, double headingDegrees)
        {
            name = CheckEntryName(name);
            RequireBaseMap("make an entry point");
            var workspace = RequireWorkspace();
            if (FindEntryList(OmsiRecordFile.Parse(workspace.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance)) == null)
                throw new InvalidOperationException("This map's global.cfg has no [entrypoints] list; openOMSI would then use the entry point objects of its tiles alone.");

            var change = PlaceObject(summary, tile, OmsiNewMap.EntryPointObject, x, y, 0.0, headingDegrees, 0.0, 0.0, new[] { name });
            var configState = change.Files.Find(file => string.Equals(file.RelativePath, GlobalConfigFileName, StringComparison.OrdinalIgnoreCase));
            var config = OmsiRecordFile.Parse(workspace.ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            var tilePath = OmsiRelativePath.Normalize(tile.FileName);
            var tileFile = OmsiRecordFile.Parse(workspace.ReadAllBytes(tilePath), OmsiTileRecordLayouts.Instance);
            var half = headingDegrees * Math.PI / 360.0;
            var fields = new List<string>
            {
                OmsiNumberFormat.Format(OmsiTileEditing.PlacedIndexOfObject(tileFile, change.Record.Id)), OmsiNumberFormat.Format(change.Record.Id), "0",
                OmsiNumberFormat.Format(x), OmsiNumberFormat.Format(groundHeight), OmsiNumberFormat.Format(y),
                "0", OmsiNumberFormat.Format(Math.Sin(half)), "0", OmsiNumberFormat.Format(Math.Cos(half)),
                OmsiNumberFormat.Format(TileIndexOf(config, tilePath)), name
            };
            ReplaceEntryList(config, existing => existing.Concat(fields));
            workspace.Save(GlobalConfigFileName, config);
            configState.After = config.ToBytes();
            return change;
        }

        /// <summary>
        /// Gives an entry point another name, in its line and as the first label of its object, as one undo step. Null when the name is the same.
        /// </summary>
        public OmsiObjectChange RenameEntryPoint(OmsiMapSummary summary, OmsiEntryPoint entry, string name)
        {
            name = CheckEntryName(name);
            RequireBaseMap("rename an entry point");
            if (entry == null || entry.Tile == null || entry.Object == null)
                throw new ArgumentException("Pass an entry point that stands on an object of its tile.", nameof(entry));
            var workspace = RequireWorkspace();
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            var list = FindEntryList(config);
            var field = FieldOfEntry(list, entry.TileIndex, entry.ObjectId);
            if (field < 0)
                throw new InvalidOperationException("The entry point is no longer in global.cfg.");

            var labels = new List<string>(entry.Object.Labels);
            if (labels.Count == 0)
                labels.Add(name);
            else
                labels[0] = name;
            var change = SetElementLabels(entry.Tile, entry.Object, labels);
            if (list.GetField(field + 11) == name)
                return change;

            list.SetField(field + 11, name);
            workspace.Save(GlobalConfigFileName, config);
            if (change == null)
            {
                var listIndex = entry.Tile.Objects.IndexOf(entry.Object);
                change = new OmsiObjectChange(OmsiObjectChangeKind.Labels, entry.Tile, entry.Object, listIndex < 0 ? entry.Tile.Objects.Count : listIndex)
                {
                    ElementId = entry.ObjectId,
                    ElementBefore = entry.Object,
                    ElementAfter = entry.Object,
                    AssetBefore = entry.Object.AssetPath,
                    AssetAfter = entry.Object.AssetPath,
                    LabelsBefore = entry.Object.Labels,
                    LabelsAfter = entry.Object.Labels
                };
            }
            SetConfigState(change, configBefore, config.ToBytes());
            return change;
        }

        /// <summary>
        /// Takes an entry point away with its object, as one undo step; the indices of the entry points after it in its tile follow. The last
        /// entry point of a map stays, as OMSI hangs on a map without one. A result without a change names what still refers to the object.
        /// </summary>
        public OmsiObjectChangeResult DeleteEntryPoint(OmsiMapSummary summary, OmsiEntryPoint entry)
        {
            RequireBaseMap("delete an entry point");
            if (entry == null || entry.Tile == null || entry.Object == null)
                throw new ArgumentException("Pass an entry point that stands on an object of its tile.", nameof(entry));
            var workspace = RequireWorkspace();
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
            var list = FindEntryList(config);
            var field = FieldOfEntry(list, entry.TileIndex, entry.ObjectId);
            if (field < 0)
                throw new InvalidOperationException("The entry point is no longer in global.cfg.");
            if ((list.Fields.Count - 1) / EntryFieldCount <= 1)
                throw new InvalidOperationException("A map needs at least one entry point; make another before deleting this one.");

            ReplaceEntryList(config, existing => existing.Take(field - 1).Concat(existing.Skip(field - 1 + EntryFieldCount)));
            workspace.Save(GlobalConfigFileName, config);
            OmsiObjectChangeResult result;
            try
            {
                result = DeleteObject(summary, entry.Tile, entry.Object);
            }
            catch (Exception failure)
            {
                // The failure is thrown as it was; when putting global.cfg back fails too, both are reported together.
                RollBackAfter(failure, () => workspace.Save(GlobalConfigFileName, configBefore));
                throw;
            }
            if (result.Change == null)
            {
                RollBackAfter(new InvalidOperationException("The entry point's object cannot be deleted, so the entry point stays."),
                    () => workspace.Save(GlobalConfigFileName, configBefore));
                return result;
            }
            SetConfigState(result.Change, configBefore, workspace.ReadAllBytes(GlobalConfigFileName));
            return result;
        }

        private void RequireBaseMap(string action)
        {
            if (EditedVariant != null)
                throw new InvalidOperationException("Entry points name tiles of the base map: end editing the chrono variant to " + action + ".");
        }

        private static string CheckEntryName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
                throw new ArgumentException("An entry point needs a name.", nameof(name));
            if (name.IndexOf('\r') >= 0 || name.IndexOf('\n') >= 0)
                throw new ArgumentException("A name must be one line.", nameof(name));
            if (name.StartsWith("[", StringComparison.Ordinal))
                throw new ArgumentException("A name must not start with a bracket, which begins a section in OMSI files.", nameof(name));
            return name;
        }

        private static OmsiRecord FindEntryList(OmsiRecordFile config)
        {
            return config.Records.Find(record => string.Equals(record.Name, "entrypoints", StringComparison.OrdinalIgnoreCase) && record.HasKnownLayout);
        }

        /// <summary>The field where the entry point of this object in this tile starts, or -1.</summary>
        private static int FieldOfEntry(OmsiRecord list, int tileIndex, int objectId)
        {
            for (var field = 1; field + EntryFieldCount <= list.Fields.Count; field += EntryFieldCount)
                if (list.TryGetInt(field + 1, out var id) && id == objectId && list.TryGetInt(field + 10, out var tile) && tile == tileIndex)
                    return field;
            return -1;
        }

        /// <summary>Writes the [entrypoints] list again from the fields <paramref name="change"/> returns for the old ones, with the count in front.</summary>
        private static void ReplaceEntryList(OmsiRecordFile config, Func<List<string>, IEnumerable<string>> change)
        {
            var old = FindEntryList(config) ?? throw new InvalidOperationException("global.cfg has no [entrypoints] list.");
            var existing = new List<string>();
            for (var field = 1; field < old.Fields.Count; field++)
                existing.Add(old.GetField(field));
            var fields = change(existing).ToList();
            fields.Insert(0, (fields.Count / EntryFieldCount).ToString(CultureInfo.InvariantCulture));
            var position = config.Records.IndexOf(old);
            var created = config.CreateRecord("entrypoints", fields, old.Trailer.Count);
            config.RemoveRecord(old);
            config.InsertRecord(position, created);
        }

        /// <summary>Makes global.cfg part of the change: the state it had before the first of the writes, and the one after the last.</summary>
        private void SetConfigState(OmsiObjectChange change, byte[] before, byte[] after)
        {
            var state = change.Files.Find(file => string.Equals(file.RelativePath, GlobalConfigFileName, StringComparison.OrdinalIgnoreCase));
            if (state == null)
            {
                AddFile(change, GlobalConfigFileName, before, after);
                return;
            }
            state.Before = before;
            state.After = after;
        }
    }
}
