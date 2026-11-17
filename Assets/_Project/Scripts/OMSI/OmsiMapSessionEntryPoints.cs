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
            RequireEntryPointRegistration("make an entry point");

            var change = PlaceObject(summary, tile, OmsiNewMap.EntryPointObject, x, y, 0.0, headingDegrees, 0.0, 0.0, new[] { name });
            try
            {
                RegisterEntryPoint(change, tile, name, x, y, groundHeight, headingDegrees);
            }
            catch (Exception failure)
            {
                // The object must not stay without its line: that would be an entry point global.cfg does not list.
                RollBackAfter(failure, () => Undo(summary, change));
                throw;
            }
            return change;
        }

        /// <summary>
        /// Whether this map lists its entry points in global.cfg and the base map is edited, so a new entry point object can be registered; false
        /// in a chrono variant and in a map without an [entrypoints] list. <see cref="AddEntryPoint"/> and pasting entry point objects refuse otherwise.
        /// </summary>
        public bool CanRegisterEntryPoints(out string reason)
        {
            reason = null;
            if (EditedVariant != null)
            {
                reason = "Entry points name tiles of the base map: end editing the chrono variant to make an entry point.";
                return false;
            }
            if (!CanEdit)
            {
                reason = "This map cannot be changed.";
                return false;
            }
            if (FindEntryList(OmsiRecordFile.Parse(RequireWorkspace().ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance)) == null)
            {
                reason = "This map's global.cfg has no [entrypoints] list; openOMSI would then use the entry point objects of its tiles alone.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// A name for a new entry point that no entry point of the map has yet: <paramref name="wanted"/> itself when it is free, otherwise it
        /// with the next number (“Start 2”). The editor offers it when asking for the name of an entry point put down from the object library.
        /// </summary>
        public string UniqueEntryName(OmsiMapSummary summary, string wanted)
        {
            var baseName = (wanted ?? string.Empty).Trim();
            if (baseName.Length == 0 || baseName.StartsWith("[", StringComparison.Ordinal) || baseName.IndexOf('\r') >= 0 || baseName.IndexOf('\n') >= 0)
                baseName = "Start";
            var taken = new HashSet<string>(ReadEntryPoints(summary).Select(entry => entry.Name), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(baseName))
                return baseName;
            for (var number = 2; ; number++)
            {
                var candidate = baseName + " " + number.ToString(CultureInfo.InvariantCulture);
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        private void RequireEntryPointRegistration(string action)
        {
            RequireBaseMap(action);
            RequireWorkspace();
            if (!CanRegisterEntryPoints(out var reason))
                throw new InvalidOperationException(reason);
        }

        /// <summary>Writes the [entrypoints] line for an entry point object that <paramref name="change"/> just put into <paramref name="tile"/>, as part of that change.</summary>
        private void RegisterEntryPoint(OmsiObjectChange change, OmsiTileSummary tile, string name, double x, double y, double groundHeight, double headingDegrees)
        {
            var workspace = RequireWorkspace();
            var configState = change.Files.Find(file => string.Equals(file.RelativePath, GlobalConfigFileName, StringComparison.OrdinalIgnoreCase));
            var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
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
            // PlaceObject always records global.cfg (the next ID) in its change; a change of an object's type does not.
            if (configState != null)
                configState.After = config.ToBytes();
            else
                AddFile(change, GlobalConfigFileName, configBefore, config.ToBytes());
        }

        /// <summary>
        /// Gives an object another object file (<see cref="SetElementType"/>) without leaving an unregistered entry point behind (ADR 079): an object
        /// that becomes an <c>[entrypoint]</c> object gets its line in [entrypoints] under <paramref name="entryName"/> (also the object's first label),
        /// from its place and heading; an object that stops being one loses its line, except the map's last. Everything is one undo step, and a failure
        /// leaves the files as they were. An entry point that stays one keeps its line. Null when the file is the same.
        /// </summary>
        /// <param name="isEntryPoint">Whether an object file is an <c>[entrypoint]</c> object; without it only the editor's own entry point object counts.</param>
        /// <param name="entryName">The name of the new entry point; only used, and then required, when an object that is no entry point becomes one.</param>
        public OmsiObjectChange SetElementType(OmsiMapSummary summary, OmsiTileSummary tile, object element, string assetPath, Func<string, bool> isEntryPoint, string entryName)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (string.IsNullOrWhiteSpace(assetPath))
                throw new ArgumentException("An object file is required.", nameof(assetPath));
            assetPath = assetPath.Trim();
            isEntryPoint = isEntryPoint ?? (asset => string.Equals(asset, OmsiNewMap.EntryPointObject, StringComparison.OrdinalIgnoreCase));
            var item = element as OmsiObjectRecord;
            var currentPath = item?.AssetPath ?? (element as OmsiAttachedObjectRecord)?.AssetPath;
            if (currentPath == null || string.Equals(assetPath, currentPath, StringComparison.Ordinal))
                return SetElementType(tile, element, assetPath);

            var becomes = isEntryPoint(assetPath);
            var registered = item == null ? null : EntryPointOf(summary, tile, item);
            if (registered == null && !becomes)
                return SetElementType(tile, element, assetPath);
            if (registered != null && becomes)
                return SetElementType(tile, element, assetPath);

            var workspace = RequireWorkspace();
            if (registered != null)
            {
                // An entry point that stops being one: its line goes with the type, but the map keeps one entry point at least.
                RequireBaseMap("change the type of an entry point");
                var configBefore = workspace.ReadAllBytes(GlobalConfigFileName);
                var config = OmsiRecordFile.Parse(configBefore, OmsiGlobalConfigLayouts.Instance);
                var list = FindEntryList(config);
                var field = list == null ? -1 : FieldOfEntry(list, registered.TileIndex, registered.ObjectId);
                if (field < 0)
                    throw new InvalidOperationException("The entry point is no longer in global.cfg.");
                if ((list.Fields.Count - 1) / EntryFieldCount <= 1)
                    throw new InvalidOperationException("A map needs at least one entry point; make another before changing the type of this one.");
                var change = SetElementType(tile, element, assetPath);
                if (change == null)
                    return null;
                try
                {
                    ReplaceEntryList(config, existing => existing.Take(field - 1).Concat(existing.Skip(field - 1 + EntryFieldCount)));
                    workspace.Save(GlobalConfigFileName, config);
                    SetConfigState(change, configBefore, config.ToBytes());
                }
                catch (Exception failure)
                {
                    RollBackAfter(failure, () => Undo(summary, change));
                    throw;
                }
                return change;
            }

            // An object that becomes an entry point: it needs the list, the base map, a name and a place on the terrain.
            if (item == null)
                throw new InvalidOperationException("An entry point stands on the terrain, not on an object: an attached object cannot become one.");
            RequireEntryPointRegistration("make an entry point");
            entryName = CheckEntryName(entryName);
            var labels = new List<string>(item.Labels);
            if (labels.Count == 0)
                labels.Add(entryName);
            else
                labels[0] = entryName;
            var ground = item.Height;
            var world = OmsiMapGeometry.TileLocalToWorld(tile.X, tile.Y, item.LocalX, item.LocalY, summary.TileSizeMeters);
            if (new OmsiTerrainSampler(summary, Source).TryGetHeight(world.X, world.Y, out var found))
                ground = found;
            var heading = item.HeadingDegrees;
            var x = item.LocalX;
            var y = item.LocalY;
            var made = ChangeElement(tile, element, assetPath, labels);
            if (made == null)
                return null;
            try
            {
                RegisterEntryPoint(made, tile, entryName, x, y, ground, heading);
            }
            catch (Exception failure)
            {
                RollBackAfter(failure, () => Undo(summary, made));
                throw;
            }
            return made;
        }

        /// <summary>
        /// Why changing this object's type to <paramref name="assetPath"/> cannot be done: it would make an entry point where none can be registered
        /// (attached object, chrono variant, map without an [entrypoints] list), or take away the map's last one. Null when the type may change.
        /// </summary>
        public string EntryPointTypeProblem(OmsiMapSummary summary, OmsiTileSummary tile, object element, string assetPath, Func<string, bool> isEntryPoint)
        {
            isEntryPoint = isEntryPoint ?? (asset => string.Equals(asset, OmsiNewMap.EntryPointObject, StringComparison.OrdinalIgnoreCase));
            var item = element as OmsiObjectRecord;
            var currentPath = item?.AssetPath ?? (element as OmsiAttachedObjectRecord)?.AssetPath;
            if (currentPath == null || string.Equals(assetPath, currentPath, StringComparison.Ordinal))
                return null;
            var becomes = isEntryPoint(assetPath);
            var registered = item == null ? null : EntryPointOf(summary, tile, item);
            if (registered != null && !becomes && EditedVariant != null)
                return "Entry points name tiles of the base map: end editing the chrono variant to change the type of an entry point.";
            if (registered == null && becomes)
            {
                if (item == null)
                    return "An entry point stands on the terrain, not on an object: an attached object cannot become one.";
                return CanRegisterEntryPoints(out var reason) ? null : reason;
            }
            if (registered != null && !becomes && ReadEntryPoints(summary).Count <= 1)
                return "A map needs at least one entry point; make another before changing the type of this one.";
            return null;
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
