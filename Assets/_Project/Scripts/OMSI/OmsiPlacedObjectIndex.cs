using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmsiMapEditor.OMSI
{
    /// <summary>How the list of placed objects is arranged.</summary>
    public enum OmsiPlacedObjectGrouping
    {
        /// <summary>One group per object file, most used first.</summary>
        ByType,

        /// <summary>One group per tile that holds objects.</summary>
        ByTile,

        /// <summary>No groups: every object by file name, then ID.</summary>
        Flat
    }

    public enum OmsiPlacedObjectRowKind
    {
        Group,
        Object
    }

    /// <summary>One placed object: the tile it is written in and its record, both shared with the map summary.</summary>
    public struct OmsiPlacedObjectEntry
    {
        public OmsiTileSummary Tile;
        public OmsiObjectRecord Record;

        /// <summary>Index into <see cref="OmsiPlacedObjectIndex.Types"/>.</summary>
        public int TypeIndex;

        /// <summary>Index into <see cref="OmsiPlacedObjectIndex.Tiles"/>.</summary>
        public int TileIndex;
    }

    /// <summary>An object file the map uses, with every spelling of its path counted together.</summary>
    public sealed class OmsiPlacedObjectType
    {
        /// <summary>The path as the first object of this type writes it, relative to the installation.</summary>
        public string AssetPath { get; internal set; }

        /// <summary>The path in lower case with backslashes: the same for every spelling, usable as a stable key.</summary>
        public string Key { get; internal set; }

        /// <summary>The file name without its extension, such as <c>bench</c>.</summary>
        public string FileName { get; internal set; }

        /// <summary>The folders before the file name, such as <c>Sceneryobjects\Generic</c>; empty without folders.</summary>
        public string Folder { get; internal set; }

        public int Count { get; internal set; }

        /// <summary>Where the file comes from; null until a resolver told it.</summary>
        public OmsiContentOrigin? Origin { get; internal set; }

        internal string LowerFileName;
    }

    /// <summary>What a filter lets through: words that must all match, and the origins to keep.</summary>
    public sealed class OmsiPlacedObjectFilter
    {
        public static readonly OmsiPlacedObjectFilter None = new OmsiPlacedObjectFilter(null, null);

        /// <param name="text">
        /// Words that must all match one object: a part of its path (file name or folder), the start of its ID or its
        /// tile as <c>x,y</c>. Null or blank matches everything.
        /// </param>
        /// <param name="origins">The origins to keep; null keeps all. Types without a known origin pass only without it.</param>
        public OmsiPlacedObjectFilter(string text, ICollection<OmsiContentOrigin> origins)
        {
            Text = text ?? string.Empty;
            Origins = origins;
            Words = SplitWords(Text);
        }

        public string Text { get; }
        public ICollection<OmsiContentOrigin> Origins { get; }

        /// <summary>True when the filter has words; groups with matches then open by themselves.</summary>
        public bool HasText => Words.Length > 0;

        internal string[] Words { get; }

        private static string[] SplitWords(string text)
        {
            // "2, -1" is one tile, not two words.
            var compact = text.Trim().ToLowerInvariant().Replace('/', '\\');
            while (compact.Contains(", ") || compact.Contains(" ,"))
                compact = compact.Replace(", ", ",").Replace(" ,", ",");
            return compact.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    /// <summary>One line of the flattened list: a group heading or one object.</summary>
    public struct OmsiPlacedObjectRow
    {
        public OmsiPlacedObjectRowKind Kind;

        /// <summary>The type index for <see cref="OmsiPlacedObjectGrouping.ByType"/>, the tile index for ByTile; −1 when flat.</summary>
        public int Group;

        /// <summary>The entry of an object row; −1 for a group row.</summary>
        public int Entry;

        /// <summary>For a group row the number of its objects that pass the filter.</summary>
        public int Count;

        /// <summary>For a group row whether its objects follow.</summary>
        public bool Expanded;
    }

    /// <summary>The rows for a list, with the totals a summary line needs.</summary>
    public sealed class OmsiPlacedObjectRows
    {
        internal OmsiPlacedObjectRows(List<OmsiPlacedObjectRow> rows, int matches, int groups)
        {
            Rows = rows;
            MatchCount = matches;
            GroupCount = groups;
        }

        public List<OmsiPlacedObjectRow> Rows { get; }

        /// <summary>Objects that pass the filter, also those in closed groups.</summary>
        public int MatchCount { get; }

        /// <summary>Groups with at least one match.</summary>
        public int GroupCount { get; }
    }

    /// <summary>
    /// A map-wide index of the placed objects ([object] records of the tiles, the base map or a chrono overlay) for a
    /// list: by object file, by tile or flat, with a search and an origin filter, flattened into rows for a virtual
    /// list. It copies no records; build it again after the summary changed. Attached objects ([attachObj]) and the
    /// objects of rows along splines ([splineAttachement]) are not [object] records and are not in it.
    /// </summary>
    public sealed class OmsiPlacedObjectIndex
    {
        private readonly List<OmsiPlacedObjectEntry> entries = new List<OmsiPlacedObjectEntry>();
        private readonly List<OmsiPlacedObjectType> types = new List<OmsiPlacedObjectType>();
        private readonly List<OmsiTileSummary> tiles = new List<OmsiTileSummary>();
        private int[] byType;
        private int[] byTile;
        private int[] flat;
        private int[] typeOrder;
        private int[] tileOrder;
        private Dictionary<OmsiObjectRecord, int> entryOfRecord;

        private OmsiPlacedObjectIndex()
        {
        }

        public IReadOnlyList<OmsiPlacedObjectEntry> Entries => entries;

        /// <summary>The object files, most used first, then by file name.</summary>
        public IReadOnlyList<OmsiPlacedObjectType> Types => types;

        /// <summary>The tiles that hold at least one object, by X, then Y.</summary>
        public IReadOnlyList<OmsiTileSummary> Tiles => tiles;

        public int ObjectCount => entries.Count;

        /// <summary>True after a resolver told the origins of the types.</summary>
        public bool HasOrigins { get; private set; }

        /// <param name="mapTiles">The tiles of the summary to list, such as <c>summary.Tiles</c>.</param>
        /// <param name="originOf">
        /// Tells the origin of a path relative to the installation, such as <c>OmsiContentOrigins.Of</c>; called once per
        /// type. Null while the installation is still being read; <see cref="ResolveOrigins"/> adds them later.
        /// </param>
        public static OmsiPlacedObjectIndex Build(IReadOnlyList<OmsiTileSummary> mapTiles, Func<string, OmsiContentOrigin> originOf = null)
        {
            if (mapTiles == null)
                throw new ArgumentNullException(nameof(mapTiles));
            var index = new OmsiPlacedObjectIndex();
            index.Fill(mapTiles);
            if (originOf != null)
                index.ResolveOrigins(originOf);
            return index;
        }

        /// <summary>Sets the origin of every type; once per type, not per object.</summary>
        public void ResolveOrigins(Func<string, OmsiContentOrigin> originOf)
        {
            if (originOf == null)
                throw new ArgumentNullException(nameof(originOf));
            foreach (var type in types)
                type.Origin = originOf(type.AssetPath);
            HasOrigins = true;
        }

        /// <summary>The key that names a group across rebuilds: the type's <see cref="OmsiPlacedObjectType.Key"/> or the tile as <c>x,y</c>.</summary>
        public string GroupKey(OmsiPlacedObjectGrouping grouping, int group)
        {
            switch (grouping)
            {
                case OmsiPlacedObjectGrouping.ByType:
                    return types[group].Key;
                case OmsiPlacedObjectGrouping.ByTile:
                    return TileKey(tiles[group]);
                default:
                    return string.Empty;
            }
        }

        public static string TileKey(OmsiTileSummary tile)
        {
            return tile.X.ToString(CultureInfo.InvariantCulture) + "," + tile.Y.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The entry of a record of this index, or −1 when it is not in it (such as after it was deleted).</summary>
        public int EntryOf(OmsiObjectRecord record)
        {
            if (record == null)
                return -1;
            if (entryOfRecord == null)
            {
                entryOfRecord = new Dictionary<OmsiObjectRecord, int>(entries.Count);
                for (var i = 0; i < entries.Count; i++)
                    entryOfRecord[entries[i].Record] = i;
            }
            return entryOfRecord.TryGetValue(record, out var entry) ? entry : -1;
        }

        /// <summary>
        /// The rows of the list: per group a heading and, when it is open, its matching objects. Groups without matches
        /// are left out. With search words every group with matches is open; without them a group is open when
        /// <paramref name="expandedGroups"/> holds its <see cref="GroupKey"/>.
        /// </summary>
        public OmsiPlacedObjectRows BuildRows(OmsiPlacedObjectFilter filter, OmsiPlacedObjectGrouping grouping, ICollection<string> expandedGroups = null)
        {
            filter = filter ?? OmsiPlacedObjectFilter.None;
            var typePasses = TypePasses(filter, out var typeMatchesAllWords);
            var rows = new List<OmsiPlacedObjectRow>();
            var matches = 0;
            var groups = 0;
            if (grouping == OmsiPlacedObjectGrouping.Flat)
            {
                foreach (var entry in Order(grouping))
                    if (Matches(entry, filter, typePasses, typeMatchesAllWords))
                        rows.Add(new OmsiPlacedObjectRow { Kind = OmsiPlacedObjectRowKind.Object, Group = -1, Entry = entry });
                return new OmsiPlacedObjectRows(rows, rows.Count, 0);
            }

            var order = Order(grouping);
            var groupOrder = grouping == OmsiPlacedObjectGrouping.ByType ? typeOrder : tileOrder;
            var position = 0;
            for (var g = 0; g < groupOrder.Length; g++)
            {
                var group = groupOrder[g];
                var start = position;
                while (position < order.Length && GroupOf(order[position], grouping) == group)
                    position++;
                if (grouping == OmsiPlacedObjectGrouping.ByType && !typePasses[group])
                    continue;
                var headingAt = rows.Count;
                var count = 0;
                var expanded = filter.HasText || expandedGroups != null && expandedGroups.Contains(GroupKey(grouping, group));
                rows.Add(new OmsiPlacedObjectRow { Kind = OmsiPlacedObjectRowKind.Group, Group = group, Entry = -1, Expanded = expanded });
                for (var i = start; i < position; i++)
                {
                    var entry = order[i];
                    if (!Matches(entry, filter, typePasses, typeMatchesAllWords))
                        continue;
                    count++;
                    if (expanded)
                        rows.Add(new OmsiPlacedObjectRow { Kind = OmsiPlacedObjectRowKind.Object, Group = group, Entry = entry });
                }
                if (count == 0)
                {
                    rows.RemoveRange(headingAt, rows.Count - headingAt);
                    continue;
                }
                var heading = rows[headingAt];
                heading.Count = count;
                rows[headingAt] = heading;
                matches += count;
                groups++;
            }
            return new OmsiPlacedObjectRows(rows, matches, groups);
        }

        private void Fill(IReadOnlyList<OmsiTileSummary> mapTiles)
        {
            // Paths as written go through an Ordinal lookup (Mono's OrdinalIgnoreCase costs about 1.5 µs per lookup, Ordinal
            // about 0.6 µs; the reader gives every record its own string, so the same-object check before it only helps
            // objects placed in the session). Spellings that differ only in case or slashes meet in a second lookup by key,
            // once per spelling. Berlin-Spandau on Unity's Mono: about 21 ms for 29,450 objects.
            var typeOfPath = new Dictionary<string, int>(StringComparer.Ordinal);
            var typeOfKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var tileIndexOf = new Dictionary<OmsiTileSummary, int>();
            var orderedTiles = new List<OmsiTileSummary>();
            foreach (var tile in mapTiles)
                if (tile != null && tile.Objects.Count > 0)
                    orderedTiles.Add(tile);
            orderedTiles.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            for (var i = 0; i < orderedTiles.Count; i++)
            {
                tiles.Add(orderedTiles[i]);
                tileIndexOf[orderedTiles[i]] = i;
            }

            var unordered = new List<OmsiPlacedObjectType>();
            string lastPath = null;
            var lastType = -1;
            foreach (var tile in mapTiles)
            {
                if (tile == null || tile.Objects.Count == 0)
                    continue;
                var tileIndex = tileIndexOf[tile];
                foreach (var record in tile.Objects)
                {
                    var path = record.AssetPath ?? string.Empty;
                    if (!ReferenceEquals(path, lastPath))
                    {
                        if (!typeOfPath.TryGetValue(path, out lastType))
                        {
                            var key = path.Replace('/', '\\').ToLowerInvariant();
                            if (!typeOfKey.TryGetValue(key, out lastType))
                            {
                                lastType = unordered.Count;
                                unordered.Add(NewType(path, key));
                                typeOfKey[key] = lastType;
                            }
                            typeOfPath[path] = lastType;
                        }
                        lastPath = path;
                    }
                    unordered[lastType].Count++;
                    entries.Add(new OmsiPlacedObjectEntry { Tile = tile, Record = record, TypeIndex = lastType, TileIndex = tileIndex });
                }
            }

            // Most used first, then by name; the type indices of the entries follow the new order.
            var order = new int[unordered.Count];
            for (var i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                var x = unordered[a];
                var y = unordered[b];
                if (x.Count != y.Count)
                    return y.Count.CompareTo(x.Count);
                var byName = string.CompareOrdinal(x.LowerFileName, y.LowerFileName);
                return byName != 0 ? byName : string.CompareOrdinal(x.Key, y.Key);
            });
            var renumber = new int[order.Length];
            for (var i = 0; i < order.Length; i++)
            {
                types.Add(unordered[order[i]]);
                renumber[order[i]] = i;
            }
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                entry.TypeIndex = renumber[entry.TypeIndex];
                entries[i] = entry;
            }
            typeOrder = Sequence(types.Count);
            tileOrder = Sequence(tiles.Count);
        }

        private static OmsiPlacedObjectType NewType(string path, string key)
        {
            var display = path.Replace('/', '\\');
            var slash = display.LastIndexOf('\\');
            var name = slash >= 0 ? display.Substring(slash + 1) : display;
            var dot = name.LastIndexOf('.');
            if (dot > 0)
                name = name.Substring(0, dot);
            return new OmsiPlacedObjectType
            {
                AssetPath = path,
                Key = key,
                FileName = name,
                Folder = slash >= 0 ? display.Substring(0, slash) : string.Empty,
                LowerFileName = name.ToLowerInvariant()
            };
        }

        private static int[] Sequence(int count)
        {
            var sequence = new int[count];
            for (var i = 0; i < count; i++)
                sequence[i] = i;
            return sequence;
        }

        private int GroupOf(int entry, OmsiPlacedObjectGrouping grouping)
        {
            return grouping == OmsiPlacedObjectGrouping.ByType ? entries[entry].TypeIndex : entries[entry].TileIndex;
        }

        /// <summary>The entries in list order for a grouping, sorted once and kept.</summary>
        private int[] Order(OmsiPlacedObjectGrouping grouping)
        {
            switch (grouping)
            {
                case OmsiPlacedObjectGrouping.ByType:
                    // Type, then tile, then ID.
                    return byType ?? (byType = Sorted(e => (long)e.TypeIndex * tiles.Count + e.TileIndex, e => e.Record.Id, null));
                case OmsiPlacedObjectGrouping.ByTile:
                    // Tile, then type name, then ID.
                    return byTile ?? (byTile = Sorted(e => (long)e.TileIndex * types.Count + NameRanks()[e.TypeIndex], e => e.Record.Id, null));
                default:
                    // Type name, then ID, then tile.
                    return flat ?? (flat = Sorted(e => NameRanks()[e.TypeIndex], e => e.Record.Id, e => e.TileIndex));
            }
        }

        private int[] nameRanks;

        /// <summary>Per type its place when the types go by file name (ties by count order).</summary>
        private int[] NameRanks()
        {
            if (nameRanks != null)
                return nameRanks;
            var byName = Sequence(types.Count);
            Array.Sort(byName, (a, b) =>
            {
                var result = string.CompareOrdinal(types[a].LowerFileName, types[b].LowerFileName);
                return result != 0 ? result : a.CompareTo(b);
            });
            nameRanks = new int[types.Count];
            for (var i = 0; i < byName.Length; i++)
                nameRanks[byName[i]] = i;
            return nameRanks;
        }

        /// <summary>
        /// A stable sort of the entry indices by a group rank, then the ID, then <paramref name="last"/>, then file order.
        /// Sorting 64-bit keys instead of comparing entries keeps it fast on Mono.
        /// </summary>
        private int[] Sorted(Func<OmsiPlacedObjectEntry, long> rank, Func<OmsiPlacedObjectEntry, int> id, Func<OmsiPlacedObjectEntry, int> last)
        {
            var order = Sequence(entries.Count);
            var keys = new long[entries.Count];
            for (var i = 0; i < keys.Length; i++)
                keys[i] = (rank(entries[i]) << 32) | (uint)(id(entries[i]) ^ int.MinValue);
            Array.Sort(keys, order);
            // Equal keys (the same ID twice in one group) go by the last criterion, then file order.
            for (var start = 0; start < keys.Length;)
            {
                var end = start + 1;
                while (end < keys.Length && keys[end] == keys[start])
                    end++;
                if (end - start > 1)
                    Array.Sort(order, start, end - start, Comparer<int>.Create((a, b) =>
                    {
                        var result = last == null ? 0 : last(entries[a]).CompareTo(last(entries[b]));
                        return result != 0 ? result : a.CompareTo(b);
                    }));
                start = end;
            }
            return order;
        }

        /// <summary>Per type whether its origin passes, and per type and word whether its path holds the word.</summary>
        private bool[] TypePasses(OmsiPlacedObjectFilter filter, out bool[][] pathHoldsWord)
        {
            var passes = new bool[types.Count];
            pathHoldsWord = new bool[types.Count][];
            var words = filter.Words;
            for (var t = 0; t < types.Count; t++)
            {
                var type = types[t];
                passes[t] = filter.Origins == null || type.Origin.HasValue && filter.Origins.Contains(type.Origin.Value);
                var holds = new bool[words.Length];
                for (var w = 0; w < words.Length; w++)
                    holds[w] = type.Key.IndexOf(words[w], StringComparison.Ordinal) >= 0;
                pathHoldsWord[t] = holds;
            }
            return passes;
        }

        private bool Matches(int entryIndex, OmsiPlacedObjectFilter filter, bool[] typePasses, bool[][] pathHoldsWord)
        {
            var entry = entries[entryIndex];
            if (!typePasses[entry.TypeIndex])
                return false;
            var words = filter.Words;
            var holds = pathHoldsWord[entry.TypeIndex];
            for (var w = 0; w < words.Length; w++)
            {
                if (holds[w])
                    continue;
                if (!IdStartsWith(entry.Record.Id, words[w]) && !IsTile(entry.Tile, words[w]))
                    return false;
            }
            return true;
        }

        /// <summary>True when the word is digits that the ID starts with, compared without making a string.</summary>
        internal static bool IdStartsWith(int id, string word)
        {
            if (word.Length == 0 || word.Length > 10 || id < 0)
                return false;
            long prefix = 0;
            foreach (var character in word)
            {
                if (character < '0' || character > '9')
                    return false;
                prefix = prefix * 10 + (character - '0');
            }
            if (word.Length > 1 && word[0] == '0')
                return false;
            long value = id;
            var digits = value == 0 ? 1 : 0;
            for (var rest = value; rest > 0; rest /= 10)
                digits++;
            if (digits < word.Length)
                return false;
            for (var i = word.Length; i < digits; i++)
                value /= 10;
            return value == prefix;
        }

        /// <summary>True when the word is the tile as <c>x,y</c>.</summary>
        internal static bool IsTile(OmsiTileSummary tile, string word)
        {
            var comma = word.IndexOf(',');
            if (comma <= 0 || comma == word.Length - 1)
                return false;
            return TryParseInt(word, 0, comma, out var x) && TryParseInt(word, comma + 1, word.Length, out var y) && x == tile.X && y == tile.Y;
        }

        private static bool TryParseInt(string text, int start, int end, out int value)
        {
            value = 0;
            var negative = start < end && text[start] == '-';
            if (negative)
                start++;
            if (start >= end || end - start > 9)
                return false;
            for (var i = start; i < end; i++)
            {
                var character = text[i];
                if (character < '0' || character > '9')
                    return false;
                value = value * 10 + (character - '0');
            }
            if (negative)
                value = -value;
            return true;
        }
    }
}
