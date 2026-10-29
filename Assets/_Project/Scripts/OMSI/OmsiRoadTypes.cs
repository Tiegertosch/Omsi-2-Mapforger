using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>What a lane of a road type is (ADR 049).</summary>
    public enum OmsiRoadLaneKind
    {
        Driving,
        Sidewalk,
        Median,
        Shoulder,
        Bus,
        Bike,
        Parking,

        /// <summary>A tram track laid in the carriageway (ADR 049, stage 4); with <see cref="OmsiRoadLane.Shared"/> road vehicles use it too.</summary>
        Tram
    }

    /// <summary>Where along its width a lane's row of objects stands: by the kerb (or the side towards the road), in the middle or at the far side.</summary>
    public enum OmsiRoadRowPlace
    {
        Kerb,
        Middle,
        Outer
    }

    /// <summary>The way vehicles use a lane: with the spline or against it.</summary>
    public enum OmsiRoadLaneDirection
    {
        Forward,
        Backward
    }

    /// <summary>
    /// The line painted between two lanes. Lines of two strokes name the left one first: <see cref="SolidDashed"/> is solid
    /// on the left and dashed on the right, so traffic on the right may cross it.
    /// </summary>
    public enum OmsiRoadMarking
    {
        None,
        Dashed,
        Solid,
        LongDashed,
        Double,
        SolidDashed,
        DashedSolid,
        Broad,
        BroadDashed
    }

    /// <summary>
    /// One lane of a road type: its kind, its width in metres, the way traffic uses it, the line on its right edge (null
    /// for the one the editor chooses) and its surface (null for the base game's).
    /// </summary>
    public sealed class OmsiRoadLane
    {
        public OmsiRoadLaneKind Kind { get; set; }
        public double Width { get; set; }
        public OmsiRoadLaneDirection Direction { get; set; }

        /// <summary>The line between this lane and the next on its right; null lets the editor choose (<see cref="OmsiRoadType.AutoMarking"/>).</summary>
        public OmsiRoadMarking? RightMarking { get; set; }

        /// <summary>A texture of the installation for the lane's surface, as a path relative to it; null for the base game's.</summary>
        public string Texture { get; set; }

        /// <summary>For a tram lane: road vehicles drive on the track too, as on tracks laid in the street.</summary>
        public bool Shared { get; set; }

        /// <summary>
        /// An object of the installation laid in a row along the lane whenever the road is built, such as a tree or a
        /// street light (ADR 049, stage 4); null for none.
        /// </summary>
        public string RowObject { get; set; }

        /// <summary>Metres between the objects of the row.</summary>
        public double RowInterval { get; set; } = DefaultRowInterval;

        public OmsiRoadRowPlace RowPlace { get; set; }

        /// <summary>Degrees the row's objects are turned by, clockwise, such as 90 for a hedge modelled across.</summary>
        public double RowTurn { get; set; }

        public const double DefaultRowInterval = 20.0;
        public const double MinimumRowInterval = 2.0;

        /// <summary>Sidewalks and medians stand at kerb height; the other lanes lie at road height.</summary>
        public bool Raised => IsRaised(Kind);

        /// <summary>Whether traffic on the lane has a direction: driving lanes, bus lanes and bike lanes.</summary>
        public bool HasDirection => Kind == OmsiRoadLaneKind.Driving || Kind == OmsiRoadLaneKind.Bus || Kind == OmsiRoadLaneKind.Bike || Kind == OmsiRoadLaneKind.Tram;

        /// <summary>Road vehicles drive on it: driving and bus lanes, and tram lanes shared with them.</summary>
        public bool CarriesTraffic => Kind == OmsiRoadLaneKind.Driving || Kind == OmsiRoadLaneKind.Bus || Kind == OmsiRoadLaneKind.Tram && Shared;

        /// <summary>Whether objects may stand in a row on a lane of this kind: not where vehicles, trams or bikes run.</summary>
        public static bool CanHoldRow(OmsiRoadLaneKind kind)
        {
            return kind == OmsiRoadLaneKind.Sidewalk || kind == OmsiRoadLaneKind.Median || kind == OmsiRoadLaneKind.Shoulder || kind == OmsiRoadLaneKind.Parking;
        }

        /// <summary>
        /// The turn that lays a long object such as a hedge along the road: 90° when its footprint is at least 2 m long across
        /// its forward direction and half again as long as it is deep, else 0°.
        /// </summary>
        public static double SuggestedRowTurn(OmsiObjectOutline outline)
        {
            if (outline == null || outline.Points.Length < 3)
                return 0.0;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var point in outline.Points)
            {
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }
            var across = maxX - minX;
            var deep = maxY - minY;
            return across >= 2.0 && across >= 1.5 * deep ? 90.0 : 0.0;
        }

        public static bool IsRaised(OmsiRoadLaneKind kind)
        {
            return kind == OmsiRoadLaneKind.Sidewalk || kind == OmsiRoadLaneKind.Median;
        }

        public OmsiRoadLane Copy()
        {
            return new OmsiRoadLane
            {
                Kind = Kind, Width = Width, Direction = Direction, RightMarking = RightMarking, Texture = Texture, Shared = Shared, RowObject = RowObject,
                RowInterval = RowInterval, RowPlace = RowPlace, RowTurn = RowTurn
            };
        }

        public static double DefaultWidth(OmsiRoadLaneKind kind)
        {
            switch (kind)
            {
                case OmsiRoadLaneKind.Sidewalk: return 3.0;
                case OmsiRoadLaneKind.Median: return 2.0;
                case OmsiRoadLaneKind.Shoulder: return 1.5;
                case OmsiRoadLaneKind.Bike: return 1.85;
                case OmsiRoadLaneKind.Parking: return 2.0;
                case OmsiRoadLaneKind.Tram: return 3.0;
                default: return 3.25;
            }
        }

        public static double MinimumWidth(OmsiRoadLaneKind kind)
        {
            switch (kind)
            {
                case OmsiRoadLaneKind.Driving: return 2.5;
                case OmsiRoadLaneKind.Bus: return 2.75;
                case OmsiRoadLaneKind.Parking: return 1.8;
                case OmsiRoadLaneKind.Tram: return 2.6;
                case OmsiRoadLaneKind.Sidewalk:
                case OmsiRoadLaneKind.Bike: return 1.0;
                default: return 0.5;
            }
        }

        /// <summary>
        /// The widest lane its texture covers from the kerb without repeating. A raised lane with kerbs on both sides is laid
        /// in two halves and may be twice as wide.
        /// </summary>
        public static double MaximumWidth(OmsiRoadLaneKind kind)
        {
            switch (kind)
            {
                case OmsiRoadLaneKind.Sidewalk: return 4.9;
                case OmsiRoadLaneKind.Median: return 6.9;
                case OmsiRoadLaneKind.Shoulder: return 4.0;
                case OmsiRoadLaneKind.Bike: return 3.0;
                case OmsiRoadLaneKind.Parking: return 3.5;
                case OmsiRoadLaneKind.Tram: return 4.0;
                default: return 5.0;
            }
        }

        public static string Word(OmsiRoadLaneKind kind)
        {
            switch (kind)
            {
                case OmsiRoadLaneKind.Sidewalk: return "walk";
                case OmsiRoadLaneKind.Median: return "median";
                case OmsiRoadLaneKind.Shoulder: return "shoulder";
                case OmsiRoadLaneKind.Bus: return "bus";
                case OmsiRoadLaneKind.Bike: return "bike";
                case OmsiRoadLaneKind.Parking: return "parking";
                case OmsiRoadLaneKind.Tram: return "tram";
                default: return "drive";
            }
        }

        public static string Name(OmsiRoadLaneKind kind)
        {
            switch (kind)
            {
                case OmsiRoadLaneKind.Sidewalk: return "Sidewalk";
                case OmsiRoadLaneKind.Median: return "Median";
                case OmsiRoadLaneKind.Shoulder: return "Shoulder";
                case OmsiRoadLaneKind.Bus: return "Bus lane";
                case OmsiRoadLaneKind.Bike: return "Bike lane";
                case OmsiRoadLaneKind.Parking: return "Parking lane";
                case OmsiRoadLaneKind.Tram: return "Tram lane";
                default: return "Driving lane";
            }
        }

        public static string Word(OmsiRoadMarking marking)
        {
            switch (marking)
            {
                case OmsiRoadMarking.Dashed: return "dashed";
                case OmsiRoadMarking.Solid: return "solid";
                case OmsiRoadMarking.LongDashed: return "long";
                case OmsiRoadMarking.Double: return "double";
                case OmsiRoadMarking.SolidDashed: return "solid-dashed";
                case OmsiRoadMarking.DashedSolid: return "dashed-solid";
                case OmsiRoadMarking.Broad: return "broad";
                case OmsiRoadMarking.BroadDashed: return "broad-dashed";
                default: return "none";
            }
        }

        public static string Name(OmsiRoadMarking marking)
        {
            switch (marking)
            {
                case OmsiRoadMarking.Dashed: return "Dashed";
                case OmsiRoadMarking.Solid: return "Solid";
                case OmsiRoadMarking.LongDashed: return "Long dashes";
                case OmsiRoadMarking.Double: return "Double";
                case OmsiRoadMarking.SolidDashed: return "Solid | dashed";
                case OmsiRoadMarking.DashedSolid: return "Dashed | solid";
                case OmsiRoadMarking.Broad: return "Broad";
                case OmsiRoadMarking.BroadDashed: return "Broad dashed";
                default: return "None";
            }
        }
    }

    /// <summary>
    /// A road type as the road builder makes it (ADR 049): its lanes from left to right, the spline in the middle. It is
    /// kept in its .sli as one line, such as <c>walk 3.00 | drive 3.25 back | drive 3.25 right=solid | walk 3.00</c>.
    /// </summary>
    public sealed class OmsiRoadType
    {
        /// <summary>The line in the .sli that holds the lanes; OMSI reads it as a comment.</summary>
        public const string DescriptionPrefix = "MapWorks road type:";

        public const int MaximumLanes = 16;

        public List<OmsiRoadLane> Lanes { get; } = new List<OmsiRoadLane>();

        /// <summary>The width from edge to edge in metres.</summary>
        public double Width
        {
            get
            {
                var width = 0.0;
                foreach (var lane in Lanes)
                    width += lane.Width;
                return width;
            }
        }

        public OmsiRoadType Copy()
        {
            var copy = new OmsiRoadType();
            foreach (var lane in Lanes)
                copy.Lanes.Add(lane.Copy());
            return copy;
        }

        public string Describe()
        {
            var parts = new List<string>();
            foreach (var lane in Lanes)
            {
                var text = new StringBuilder(OmsiRoadLane.Word(lane.Kind)).Append(' ').Append(lane.Width.ToString("0.00", CultureInfo.InvariantCulture));
                if (lane.HasDirection && lane.Direction == OmsiRoadLaneDirection.Backward)
                    text.Append(" back");
                if (lane.RightMarking.HasValue)
                    text.Append(" right=").Append(OmsiRoadLane.Word(lane.RightMarking.Value));
                if (lane.Kind == OmsiRoadLaneKind.Tram && lane.Shared)
                    text.Append(" cars");
                if (!string.IsNullOrEmpty(lane.Texture))
                    text.Append(" tex=\"").Append(lane.Texture).Append('"');
                if (!string.IsNullOrEmpty(lane.RowObject))
                {
                    text.Append(" row=\"").Append(lane.RowObject).Append('"');
                    if (Math.Abs(lane.RowInterval - OmsiRoadLane.DefaultRowInterval) > 1e-9)
                        text.Append(" every=").Append(lane.RowInterval.ToString("0.##", CultureInfo.InvariantCulture));
                    if (lane.RowPlace != OmsiRoadRowPlace.Kerb)
                        text.Append(" at=").Append(lane.RowPlace.ToString().ToLowerInvariant());
                    if (Math.Abs(lane.RowTurn) > 1e-9)
                        text.Append(" turn=").Append(lane.RowTurn.ToString("0.##", CultureInfo.InvariantCulture));
                }
                parts.Add(text.ToString());
            }
            return string.Join(" | ", parts);
        }

        /// <summary>
        /// Reads lanes such as <c>walk 3 row="Sceneryobjects\x\tree.sco" every=15 | drive 3.25 back right=solid | tram 3 cars |
        /// drive 3.25 tex="Splines\x\texture\y.bmp" | walk 3</c>; a lane without a width gets the default.
        /// </summary>
        /// <exception cref="FormatException">A lane names no known kind, line or setting, or a width is not a number.</exception>
        public static OmsiRoadType Parse(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                throw new FormatException("A road type needs at least one lane.");
            var text = description.Trim();
            if (text.StartsWith(DescriptionPrefix, StringComparison.OrdinalIgnoreCase))
                text = text.Substring(DescriptionPrefix.Length);
            var type = new OmsiRoadType();
            foreach (var part in SplitLanes(text))
            {
                var words = Words(part);
                if (words.Count == 0)
                    continue;
                if (!TryKind(words[0], out var kind))
                    throw new FormatException("\"" + words[0] + "\" is no lane; use drive, bus, tram, bike, parking, shoulder, walk or median.");
                var lane = new OmsiRoadLane { Kind = kind, Width = OmsiRoadLane.DefaultWidth(kind) };
                for (var index = 1; index < words.Count; index++)
                {
                    var word = words[index];
                    if (string.Equals(word, "back", StringComparison.OrdinalIgnoreCase))
                        lane.Direction = OmsiRoadLaneDirection.Backward;
                    else if (word.StartsWith("right=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryMarking(word.Substring(6), out var marking))
                            throw new FormatException("\"" + word.Substring(6) + "\" is no line; use none, dashed, long, solid, double, solid-dashed, dashed-solid, broad or broad-dashed.");
                        lane.RightMarking = marking;
                    }
                    else if (word.StartsWith("tex=", StringComparison.OrdinalIgnoreCase))
                        lane.Texture = word.Substring(4).Trim('"').Trim();
                    else if (string.Equals(word, "cars", StringComparison.OrdinalIgnoreCase) && kind == OmsiRoadLaneKind.Tram)
                        lane.Shared = true;
                    else if (word.StartsWith("row=", StringComparison.OrdinalIgnoreCase))
                        lane.RowObject = word.Substring(4).Trim('"').Trim();
                    else if (word.StartsWith("every=", StringComparison.OrdinalIgnoreCase) &&
                             double.TryParse(word.Substring(6).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var every))
                        lane.RowInterval = every;
                    else if (word.StartsWith("turn=", StringComparison.OrdinalIgnoreCase) &&
                             double.TryParse(word.Substring(5).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var turn))
                        lane.RowTurn = turn;
                    else if (word.StartsWith("at=", StringComparison.OrdinalIgnoreCase) && Enum.TryParse(word.Substring(3), true, out OmsiRoadRowPlace place) &&
                             Enum.IsDefined(typeof(OmsiRoadRowPlace), place))
                        lane.RowPlace = place;
                    else if (double.TryParse(word.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var width))
                        lane.Width = width;
                    else
                        throw new FormatException("\"" + word + "\" in \"" + part.Trim() + "\" is neither a width nor back, cars, right=, tex=, row=, every=, at= or turn=.");
                }
                if (string.IsNullOrEmpty(lane.Texture))
                    lane.Texture = null;
                if (string.IsNullOrEmpty(lane.RowObject))
                    lane.RowObject = null;
                type.Lanes.Add(lane);
            }
            if (type.Lanes.Count == 0)
                throw new FormatException("A road type needs at least one lane.");
            return type;
        }

        public static bool TryParse(string description, out OmsiRoadType type)
        {
            try
            {
                type = Parse(description);
                return true;
            }
            catch (FormatException)
            {
                type = null;
                return false;
            }
        }

        // Lanes are split at '|' outside quotes, words at spaces outside quotes.
        private static List<string> SplitLanes(string text)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            foreach (var character in text)
            {
                if (character == '"')
                    quoted = !quoted;
                if (character == '|' && !quoted)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                else
                    current.Append(character);
            }
            parts.Add(current.ToString());
            return parts;
        }

        private static List<string> Words(string part)
        {
            var words = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            foreach (var character in part)
            {
                if (character == '"')
                    quoted = !quoted;
                if (!quoted && (character == ' ' || character == '\t'))
                {
                    if (current.Length > 0)
                        words.Add(current.ToString());
                    current.Clear();
                }
                else
                    current.Append(character);
            }
            if (current.Length > 0)
                words.Add(current.ToString());
            return words;
        }

        private static bool TryKind(string word, out OmsiRoadLaneKind kind)
        {
            foreach (OmsiRoadLaneKind each in Enum.GetValues(typeof(OmsiRoadLaneKind)))
                if (string.Equals(word, OmsiRoadLane.Word(each), StringComparison.OrdinalIgnoreCase))
                {
                    kind = each;
                    return true;
                }
            kind = OmsiRoadLaneKind.Driving;
            return false;
        }

        private static bool TryMarking(string word, out OmsiRoadMarking marking)
        {
            foreach (OmsiRoadMarking each in Enum.GetValues(typeof(OmsiRoadMarking)))
                if (string.Equals(word, OmsiRoadLane.Word(each), StringComparison.OrdinalIgnoreCase))
                {
                    marking = each;
                    return true;
                }
            marking = OmsiRoadMarking.None;
            return false;
        }

        /// <summary>What keeps the road type from being made, such as a lane too wide for its texture; empty when it can be.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            if (Lanes.Count == 0)
                problems.Add("Add at least one lane.");
            if (Lanes.Count > MaximumLanes)
                problems.Add("A road type has at most " + MaximumLanes + " lanes.");
            for (var index = 0; index < Lanes.Count; index++)
            {
                var lane = Lanes[index];
                var maximum = OmsiRoadLane.MaximumWidth(lane.Kind);
                if (KerbOn(index, -1) && KerbOn(index, 1))
                    maximum *= 2.0;
                if (!(lane.Width >= OmsiRoadLane.MinimumWidth(lane.Kind)) || lane.Width > maximum + 1e-9)
                    problems.Add(OmsiRoadLane.Name(lane.Kind) + " " + (index + 1) + " must be " + OmsiRoadLane.MinimumWidth(lane.Kind).ToString("0.0#", CultureInfo.InvariantCulture) +
                                 " to " + maximum.ToString("0.0#", CultureInfo.InvariantCulture) + " m wide.");
                if (lane.RowObject != null && !OmsiRoadLane.CanHoldRow(lane.Kind))
                    problems.Add("Objects cannot stand on " + OmsiRoadLane.Name(lane.Kind).ToLowerInvariant() + " " + (index + 1) +
                                 "; give them to a sidewalk, median, shoulder or parking lane.");
                if (lane.RowObject != null && !(lane.RowInterval >= OmsiRoadLane.MinimumRowInterval))
                    problems.Add("The objects along " + OmsiRoadLane.Name(lane.Kind).ToLowerInvariant() + " " + (index + 1) + " must stand at least " +
                                 OmsiRoadLane.MinimumRowInterval.ToString("0", CultureInfo.InvariantCulture) + " m apart.");
            }
            return problems;
        }

        /// <summary>Whether a raised lane gets a kerb on one side (-1 left, 1 right): where a lane at road height lies next to it.</summary>
        public bool KerbOn(int index, int side)
        {
            var next = index + side;
            return Lanes[index].Raised && next >= 0 && next < Lanes.Count && !Lanes[next].Raised;
        }

        /// <summary>The line between lane <paramref name="index"/> and the next: the one chosen for it, else <see cref="AutoMarking"/>.</summary>
        public OmsiRoadMarking MarkingAfter(int index)
        {
            if (index < 0 || index + 1 >= Lanes.Count)
                return OmsiRoadMarking.None;
            return Lanes[index].RightMarking ?? AutoMarking(index);
        }

        /// <summary>
        /// The line the editor chooses between lane <paramref name="index"/> and the next, as on German roads: dashed between
        /// lanes of one direction, dashed between the two directions unless both have two driving lanes or more, then
        /// solid; a broad line beside bus and bike lanes; solid along a shoulder; none at a kerb or a parking lane.
        /// </summary>
        public OmsiRoadMarking AutoMarking(int index)
        {
            if (index < 0 || index + 1 >= Lanes.Count)
                return OmsiRoadMarking.None;
            var a = Lanes[index];
            var b = Lanes[index + 1];
            bool Traffic(OmsiRoadLane lane) => lane.CarriesTraffic;
            OmsiRoadLaneKind Seen(OmsiRoadLane lane) => lane.Kind == OmsiRoadLaneKind.Tram && lane.Shared ? OmsiRoadLaneKind.Driving : lane.Kind;
            if (Traffic(a) && Traffic(b))
            {
                if (Seen(a) != Seen(b))
                    return OmsiRoadMarking.Broad;
                if (a.Direction == b.Direction)
                    return OmsiRoadMarking.Dashed;
                CountCarriageway(index, out var forward, out var backward);
                return forward >= 2 && backward >= 2 ? OmsiRoadMarking.Solid : OmsiRoadMarking.Dashed;
            }
            if (Traffic(a) && b.Kind == OmsiRoadLaneKind.Bike || a.Kind == OmsiRoadLaneKind.Bike && Traffic(b))
                return OmsiRoadMarking.Broad;
            if (Traffic(a) && b.Kind == OmsiRoadLaneKind.Shoulder || a.Kind == OmsiRoadLaneKind.Shoulder && Traffic(b))
                return OmsiRoadMarking.Solid;
            if (Traffic(a) && b.Kind == OmsiRoadLaneKind.Tram || a.Kind == OmsiRoadLaneKind.Tram && Traffic(b))
                return OmsiRoadMarking.Solid;
            return OmsiRoadMarking.None;
        }

        /// <summary>The driving lanes each way on the carriageway around a lane: the run of lanes at road height it lies in.</summary>
        private void CountCarriageway(int index, out int forward, out int backward)
        {
            forward = 0;
            backward = 0;
            var first = index;
            while (first > 0 && !Lanes[first - 1].Raised)
                first--;
            for (var each = first; each < Lanes.Count && !Lanes[each].Raised; each++)
                if (Lanes[each].Kind == OmsiRoadLaneKind.Driving || Lanes[each].Kind == OmsiRoadLaneKind.Tram && Lanes[each].Shared)
                {
                    if (Lanes[each].Direction == OmsiRoadLaneDirection.Forward)
                        forward++;
                    else
                        backward++;
                }
        }

        /// <summary>
        /// The indexes of the [path]s of bus lanes in the road type's .sli, in the order <see cref="OmsiRoadTypeWriter"/>
        /// writes them: a track's path and then its path for cars, a path per driving and bus lane and per sidewalk. A road
        /// built of it keeps these paths for buses with [rule] no_cars and bus (ADR 049, stage 4 of the AI paths).
        /// </summary>
        public List<int> BusPaths()
        {
            var paths = new List<int>();
            var index = 0;
            foreach (var lane in Lanes)
            {
                if (lane.Kind == OmsiRoadLaneKind.Bus)
                    paths.Add(index);
                if (lane.Kind == OmsiRoadLaneKind.Tram)
                    index += lane.Shared ? 2 : 1;
                else if (lane.Kind == OmsiRoadLaneKind.Driving || lane.Kind == OmsiRoadLaneKind.Bus || lane.Kind == OmsiRoadLaneKind.Sidewalk)
                    index++;
            }
            return paths;
        }

        /// <summary>A lane's row keeps this far from the side it stands by, so trunks and posts clear the kerb.</summary>
        public const double RowInset = 0.75;

        /// <summary>
        /// The rows of objects the lanes ask for, with where they stand across the road: by the kerb, or for a lane without
        /// one on the side towards the road's middle; in the middle; or at the far side, <see cref="RowInset"/> in from it.
        /// Objects right of the spline face along it, those left of it the other way, as the row tool lays them, each turned
        /// by its lane's <see cref="OmsiRoadLane.RowTurn"/>. Lanes vehicles use carry none.
        /// </summary>
        public List<OmsiRoadRow> Rows()
        {
            var rows = new List<OmsiRoadRow>();
            var x0 = -Width / 2.0;
            for (var index = 0; index < Lanes.Count; index++)
            {
                var lane = Lanes[index];
                var x1 = x0 + lane.Width;
                if (lane.RowObject != null && OmsiRoadLane.CanHoldRow(lane.Kind))
                {
                    var kerbLeft = KerbOn(index, -1);
                    var kerbRight = KerbOn(index, 1);
                    var nearLeft = kerbLeft != kerbRight ? kerbLeft : (x0 + x1) / 2.0 > 0.0;
                    var inset = Math.Min(RowInset, lane.Width / 2.0);
                    double x;
                    if (lane.RowPlace == OmsiRoadRowPlace.Middle)
                        x = (x0 + x1) / 2.0;
                    else if (lane.RowPlace == OmsiRoadRowPlace.Kerb == nearLeft)
                        x = x0 + inset;
                    else
                        x = x1 - inset;
                    rows.Add(new OmsiRoadRow
                    {
                        Lane = index, AssetPath = lane.RowObject, Interval = lane.RowInterval, X = x,
                        Height = lane.Raised ? OmsiRoadTypeWriter.RaisedHeight : OmsiRoadTypeWriter.RoadHeight,
                        HeadingDegrees = (((x >= 0.0 ? 0.0 : 180.0) + lane.RowTurn) % 360.0 + 360.0) % 360.0
                    });
                }
                x0 = x1;
            }
            return rows;
        }

        /// <summary>A name from the lanes, such as "Road 2+2 median 21.5m" or "Footpath 3m".</summary>
        public string SuggestName()
        {
            var forward = 0;
            var backward = 0;
            var median = false;
            var bus = false;
            var tram = false;
            foreach (var lane in Lanes)
            {
                tram |= lane.Kind == OmsiRoadLaneKind.Tram;
                if (lane.Kind == OmsiRoadLaneKind.Driving)
                {
                    if (lane.Direction == OmsiRoadLaneDirection.Forward)
                        forward++;
                    else
                        backward++;
                }
                median |= lane.Kind == OmsiRoadLaneKind.Median;
                bus |= lane.Kind == OmsiRoadLaneKind.Bus;
            }
            var width = Width.ToString("0.#", CultureInfo.InvariantCulture) + "m";
            if (forward + backward == 0)
                return (tram ? "Tram track " : bus ? "Bus road " : "Footpath ") + width;
            var lanes = forward > 0 && backward > 0 ? backward + "+" + forward : (forward + backward) + " one-way";
            return "Road " + lanes + (median ? " median" : string.Empty) + (bus ? " bus" : string.Empty) + (tram ? " tram" : string.Empty) + " " + width;
        }

        /// <summary>Starting points for a new road type, by name.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Templates { get; } = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("2 lanes", "walk 3 | drive 3.25 back | drive 3.25 | walk 3"),
            new KeyValuePair<string, string>("4 lanes", "walk 3.5 | drive 3.25 back | drive 3.25 back | drive 3.25 | drive 3.25 | walk 3.5"),
            new KeyValuePair<string, string>("2+2 with median", "walk 3.5 | drive 3.25 back | drive 3.25 back | median 3 | drive 3.25 | drive 3.25 | walk 3.5"),
            new KeyValuePair<string, string>("One-way", "walk 3 | drive 3.25 | drive 3.25 | walk 3"),
            new KeyValuePair<string, string>("Bus lanes", "walk 3 | bus 3.25 back | drive 3.25 back | drive 3.25 | bus 3.25 | walk 3"),
            new KeyValuePair<string, string>("Parking and bikes", "walk 2.5 | parking 2 | bike 1.85 back | drive 3.25 back | drive 3.25 | bike 1.85 | parking 2 | walk 2.5"),
            new KeyValuePair<string, string>("Country road", "shoulder 1 | drive 3.25 back right=long | drive 3.25 | shoulder 1"),
            new KeyValuePair<string, string>("Tram in the street", "walk 3.5 | drive 3.25 back | tram 3 back cars | tram 3 cars | drive 3.25 | walk 3.5"),
            new KeyValuePair<string, string>("Tram in the middle", "walk 3.5 | drive 3.25 back | tram 3 back | tram 3 | drive 3.25 | walk 3.5"),
            new KeyValuePair<string, string>("Footpath", "walk 3")
        };

        /// <summary>
        /// Takes a spline profile apart into lanes, as a start for a road type of one's own (ADR 049, stage 3): the raised
        /// parts beside the carriageway become sidewalks, a raised part between two carriageways a median, each [path] for
        /// vehicles a driving lane reaching halfway to the next, and room left at the carriageway's edges a shoulder.
        /// Lines are left to the editor and surfaces to the base game; rails and lines painted in the textures are not taken.
        /// </summary>
        /// <param name="notes">What was left out or changed, such as a lane made narrower to fit its texture.</param>
        /// <returns>The lanes, or null when the profile has no carriageway or footpath to take apart.</returns>
        public static OmsiRoadType FromProfile(OmsiSplineProfile profile, List<string> notes)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            notes = notes ?? new List<string>();
            var type = new OmsiRoadType();
            var sections = OmsiProfileSections.Of(profile);
            var roadLeft = -sections.RoadLeft;
            var roadRight = sections.RoadRight;
            if (sections.RoadTexture < 0 || roadRight - roadLeft < 2.0 || !profile.Lanes.Exists(lane => lane.Kind == 0))
            {
                // No carriageway, or none vehicles use: a footpath, if people walk on it.
                if (!profile.Lanes.Exists(lane => lane.Kind == 1) || profile.RightEdge - profile.LeftEdge < 0.5)
                    return null;
                type.AddFitted(OmsiRoadLaneKind.Sidewalk, profile.RightEdge - profile.LeftEdge, OmsiRoadLaneDirection.Forward, notes);
                return type.Lanes.Count > 0 ? type : null;
            }

            const double smallest = 0.3;
            if (roadLeft + sections.OuterLeft >= smallest)
                type.AddFitted(OmsiRoadLaneKind.Sidewalk, roadLeft + sections.OuterLeft, OmsiRoadLaneDirection.Forward, notes);
            if (sections.HasMedian)
            {
                type.AddCarriageway(profile, roadLeft, sections.MedianLeft, notes);
                type.AddFitted(OmsiRoadLaneKind.Median, sections.MedianRight - sections.MedianLeft, OmsiRoadLaneDirection.Forward, notes);
                type.AddCarriageway(profile, sections.MedianRight, roadRight, notes);
            }
            else
                type.AddCarriageway(profile, roadLeft, roadRight, notes);
            if (sections.OuterRight - roadRight >= smallest)
                type.AddFitted(OmsiRoadLaneKind.Sidewalk, sections.OuterRight - roadRight, OmsiRoadLaneDirection.Forward, notes);
            if (type.Lanes.Count > MaximumLanes)
            {
                notes.Add("Only the first " + MaximumLanes + " lanes are taken.");
                type.Lanes.RemoveRange(MaximumLanes, type.Lanes.Count - MaximumLanes);
            }
            if (profile.Lanes.Exists(lane => lane.Kind == OmsiSplineCategories.RailLane && (lane.X <= roadLeft || lane.X >= roadRight)))
                notes.Add("Tracks outside the carriageway are left out.");
            return type.Lanes.Exists(lane => lane.Kind == OmsiRoadLaneKind.Driving || lane.Kind == OmsiRoadLaneKind.Tram) ? type : null;
        }

        /// <summary>
        /// The driving lanes of one carriageway from <paramref name="from"/> to <paramref name="to"/> metres right of the
        /// spline: neighbours share the room between them as their [path]s are wide; at the edges a lane keeps the width of its [path], and the rest is a shoulder.
        /// A track becomes a tram lane, shared with cars where a [path] for them runs along it.
        /// </summary>
        private void AddCarriageway(OmsiSplineProfile profile, double from, double to, List<string> notes)
        {
            const double sameTrack = 0.5;
            var rails = profile.Lanes.FindAll(lane => lane.Kind == OmsiSplineCategories.RailLane && lane.X > from && lane.X < to);
            var paths = profile.Lanes.FindAll(lane => lane.Kind == 0 && lane.X > from && lane.X < to &&
                                                      !rails.Exists(rail => Math.Abs(rail.X - lane.X) < sameTrack));
            paths.AddRange(rails);
            paths.Sort((a, b) => a.X.CompareTo(b.X));
            bool SharedTrack(OmsiSplineLane rail) => profile.Lanes.Exists(lane => lane.Kind == 0 && Math.Abs(lane.X - rail.X) < sameTrack);
            if (paths.Count == 0)
            {
                AddFitted(OmsiRoadLaneKind.Shoulder, to - from, OmsiRoadLaneDirection.Forward, notes);
                return;
            }
            double PathWidth(OmsiSplineLane path)
            {
                var kind = path.Kind == OmsiSplineCategories.RailLane ? OmsiRoadLaneKind.Tram : OmsiRoadLaneKind.Driving;
                return path.Width >= OmsiRoadLane.MinimumWidth(kind) ? path.Width : OmsiRoadLane.DefaultWidth(kind);
            }
            // Two neighbours share the room between their middles as their [path]s are wide.
            var edges = new List<double> { from };
            for (var index = 1; index < paths.Count; index++)
            {
                var a = paths[index - 1];
                var b = paths[index];
                edges.Add(a.X + (b.X - a.X) * PathWidth(a) / (PathWidth(a) + PathWidth(b)));
            }
            edges.Add(to);
            const double shoulderFrom = 0.75;
            var leftRoom = paths[0].X - PathWidth(paths[0]) / 2.0 - from;
            if (leftRoom >= shoulderFrom)
            {
                AddFitted(OmsiRoadLaneKind.Shoulder, leftRoom, OmsiRoadLaneDirection.Forward, notes);
                edges[0] = from + leftRoom;
            }
            var rightRoom = to - (paths[paths.Count - 1].X + PathWidth(paths[paths.Count - 1]) / 2.0);
            if (rightRoom >= shoulderFrom)
                edges[edges.Count - 1] = to - rightRoom;
            for (var index = 0; index < paths.Count; index++)
            {
                var rail = paths[index].Kind == OmsiSplineCategories.RailLane;
                AddFitted(rail ? OmsiRoadLaneKind.Tram : OmsiRoadLaneKind.Driving, edges[index + 1] - edges[index],
                    paths[index].Direction == 1 ? OmsiRoadLaneDirection.Backward : OmsiRoadLaneDirection.Forward, notes);
                if (rail)
                    Lanes[Lanes.Count - 1].Shared = SharedTrack(paths[index]);
            }
            if (rightRoom >= shoulderFrom)
                AddFitted(OmsiRoadLaneKind.Shoulder, rightRoom, OmsiRoadLaneDirection.Forward, notes);
        }

        /// <summary>Adds a lane, its width rounded to centimetres and kept within what its kind allows.</summary>
        private void AddFitted(OmsiRoadLaneKind kind, double width, OmsiRoadLaneDirection direction, List<string> notes)
        {
            var rounded = Math.Round(width, 2);
            var fitted = Math.Max(OmsiRoadLane.MinimumWidth(kind), Math.Min(OmsiRoadLane.MaximumWidth(kind), rounded));
            if (Math.Abs(fitted - rounded) > 0.005)
                notes.Add(OmsiRoadLane.Name(kind) + " " + (Lanes.Count + 1) + " is " + fitted.ToString("0.00", CultureInfo.InvariantCulture) + " m wide instead of " +
                          rounded.ToString("0.00", CultureInfo.InvariantCulture) + " m, as its kind allows.");
            Lanes.Add(new OmsiRoadLane { Kind = kind, Width = fitted, Direction = direction });
        }
    }

    /// <summary>A row of objects along a road type (<see cref="OmsiRoadType.Rows"/>), across the road from its spline.</summary>
    public sealed class OmsiRoadRow
    {
        /// <summary>The lane it belongs to, from the left.</summary>
        public int Lane { get; internal set; }

        public string AssetPath { get; internal set; }
        public double Interval { get; internal set; }

        /// <summary>Metres right of the spline; negative to the left.</summary>
        public double X { get; internal set; }

        /// <summary>Over the spline, on the lane's surface.</summary>
        public double Height { get; internal set; }

        public double HeadingDegrees { get; internal set; }
    }

    /// <summary>The text of a road type's .sli and the files it needs next to it.</summary>
    public sealed class OmsiRoadTypeFiles
    {
        public string Sli { get; internal set; }

        /// <summary>Textures of the installation to copy into the <c>texture</c> folder, as paths relative to the installation.</summary>
        public List<string> Textures { get; } = new List<string>();

        /// <summary>Files the editor makes for the road type, such as the road marking textures, by their name in the <c>texture</c> folder.</summary>
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Writes a road type as a .sli as OMSI's StreetCreator does (ADR 049): a strip per lane with the base game's textures
    /// or the one chosen for it, kerb faces where a raised lane meets one at road height, a height profile per level, a
    /// [path] per driving lane, bus lane and sidewalk, and the road markings as strips of their own a little above the
    /// asphalt.
    /// </summary>
    public static class OmsiRoadTypeWriter
    {
        public const string AsphaltTexture = @"Splines\Marcel\texture\str_asphdrk.bmp";
        public const string SidewalkTexture = @"Splines\Marcel\texture\str_side1.bmp";
        public const string MedianTexture = @"Splines\Marcel\texture\str_gruenstreifen.bmp";
        public const string WhiteTexture = "MapWorks_marking_white.bmp";
        public const string DashTexture = "MapWorks_marking_dash.tga";
        public const string HalfDashTexture = "MapWorks_marking_dash_half.tga";
        public const string GrooveTexture = "MapWorks_rail_groove.bmp";

        /// <summary>Standard gauge, between the inner edges of the rail heads.</summary>
        public const double Gauge = 1.435;

        /// <summary>A grooved rail as seen from above: its head and the groove beside it on the inside of the track.</summary>
        public const double RailHeadWidth = 0.065;
        public const double RailGrooveWidth = 0.035;

        /// <summary>The rails lie this far over the carriageway, under the road markings.</summary>
        public const double RailLift = 0.01;

        /// <summary>
        /// [rail_enh] after a track's [path]: rails 200 m long without joints, and track laid in the street more evenly than
        /// the base game's sleeper tracks (Ruede\rail_concrete_01.sli: 2 cm amplitude), 1 cm across and 0.5 cm up and down.
        /// </summary>
        public static readonly string[] StreetTrack = { "200", "0", "30", "0.01", "7", "30", "0.005", "7" };

        public const double RoadHeight = 0.10;
        public const double RaisedHeight = 0.25;
        public const double MarkingLift = 0.02;
        public const double MarkingWidth = 0.12;
        public const double BroadWidth = 0.25;

        /// <summary>A line at a kerb lies this far from it on the carriageway, as an edge line does.</summary>
        public const double KerbInset = 0.2;

        // Dashes of 3 m with gaps of 6 m, as the guide lines in German towns; long dashes 6 m with gaps of 12 m as on
        // country roads; broad dashes 3 m with gaps of 3 m.
        public const double DashRepeat = 9.0;
        public const double LongDashRepeat = 18.0;
        public const double HalfDashRepeat = 6.0;
        private const int DashRows = 128;

        // How the base game's StreetCreator profiles lay the textures (Marcel\str_2spur_12m_Sandstr.sli, Hstr_6spur_Ruhlebener1.sli).
        private const double AsphaltUStart = 0.005;
        private const double AsphaltUPerMeter = 0.99 / 6.0;
        private const double AsphaltVPerMeter = 0.167;
        private const double RaisedVPerMeter = 0.2;

        private sealed class Surface
        {
            public string Texture;
            public double KerbU;
            public double UPerMeter;
            public double FaceU;
        }

        private static readonly Surface SidewalkSurface = new Surface { Texture = SidewalkTexture, KerbU = 0.953, UPerMeter = 0.766 / 4.0, FaceU = 0.995 };
        private static readonly Surface MedianSurface = new Surface { Texture = MedianTexture, KerbU = 0.973, UPerMeter = 0.613 / 4.4, FaceU = 0.995 };

        /// <exception cref="ArgumentException">The road type has problems (<see cref="OmsiRoadType.Problems"/>).</exception>
        public static OmsiRoadTypeFiles Write(OmsiRoadType type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            var problems = type.Problems();
            if (problems.Count > 0)
                throw new ArgumentException(problems[0], nameof(type));

            var files = new OmsiRoadTypeFiles();
            var textures = new List<string>();
            var strips = new StringBuilder();
            var rails = new StringBuilder();
            var heights = new StringBuilder();
            var paths = new StringBuilder();

            int TextureIndex(string texture)
            {
                var index = textures.FindIndex(each => string.Equals(each, texture, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    return index;
                textures.Add(texture);
                return textures.Count - 1;
            }

            var edges = new double[type.Lanes.Count + 1];
            edges[0] = -type.Width / 2.0;
            for (var index = 0; index < type.Lanes.Count; index++)
                edges[index + 1] = edges[index] + type.Lanes[index].Width;

            for (var index = 0; index < type.Lanes.Count; index++)
            {
                var lane = type.Lanes[index];
                var x0 = edges[index];
                var x1 = edges[index + 1];
                strips.Append("\r\n").Append(OmsiRoadLane.Name(lane.Kind)).Append(":\r\n");
                if (!lane.Raised)
                {
                    if (lane.Texture != null)
                        Strip(strips, TextureIndex(lane.Texture), x0, RoadHeight, AsphaltUStart, ChosenVPerMeter(lane.Width), x1, RoadHeight, 1.0 - AsphaltUStart, ChosenVPerMeter(lane.Width));
                    else
                        Strip(strips, TextureIndex(AsphaltTexture), x0, RoadHeight, AsphaltUStart, AsphaltVPerMeter, x1, RoadHeight, AsphaltUStart + lane.Width * AsphaltUPerMeter, AsphaltVPerMeter);
                }
                else
                {
                    var surface = lane.Kind == OmsiRoadLaneKind.Median ? MedianSurface : SidewalkSurface;
                    var kerbTexture = TextureIndex(surface.Texture);
                    var kerbLeft = type.KerbOn(index, -1);
                    var kerbRight = type.KerbOn(index, 1);
                    if (kerbLeft)
                        Strip(strips, kerbTexture, x0, RoadHeight, surface.FaceU, RaisedVPerMeter, x0, RaisedHeight, surface.KerbU, RaisedVPerMeter);
                    if (lane.Texture != null)
                    {
                        // A texture of its own covers the level once across, or each half where kerbs are on both sides.
                        var texture = TextureIndex(lane.Texture);
                        var middle = kerbLeft && kerbRight ? (x0 + x1) / 2.0 : x1;
                        Strip(strips, texture, x0, RaisedHeight, AsphaltUStart, ChosenVPerMeter(middle - x0), middle, RaisedHeight, 1.0 - AsphaltUStart, ChosenVPerMeter(middle - x0));
                        if (middle < x1)
                            Strip(strips, texture, middle, RaisedHeight, 1.0 - AsphaltUStart, ChosenVPerMeter(x1 - middle), x1, RaisedHeight, AsphaltUStart, ChosenVPerMeter(x1 - middle));
                    }
                    else if (kerbLeft && kerbRight)
                    {
                        var middle = (x0 + x1) / 2.0;
                        RaisedStrip(strips, kerbTexture, surface, x0, middle, true);
                        RaisedStrip(strips, kerbTexture, surface, middle, x1, false);
                    }
                    else
                        RaisedStrip(strips, kerbTexture, surface, x0, x1, kerbLeft);
                    if (kerbRight)
                        Strip(strips, kerbTexture, x1, RaisedHeight, surface.KerbU, RaisedVPerMeter, x1, RoadHeight, surface.FaceU, RaisedVPerMeter);
                }

                if (lane.Kind == OmsiRoadLaneKind.Tram)
                {
                    // Grooved rails flush with the road, the groove on the inside of the track; a [path] for trams and,
                    // where cars share the track, one for them.
                    var middle = (x0 + x1) / 2.0;
                    var groove = TextureIndex(GrooveTexture);
                    var height = RoadHeight + RailLift;
                    var inner = Gauge / 2.0;
                    Strip(rails, groove, middle - inner - RailHeadWidth, height, 0.0, RaisedVPerMeter, middle - inner + RailGrooveWidth, height, 1.0, RaisedVPerMeter);
                    Strip(rails, groove, middle + inner - RailGrooveWidth, height, 1.0, RaisedVPerMeter, middle + inner + RailHeadWidth, height, 0.0, RaisedVPerMeter);
                    var direction = lane.Direction == OmsiRoadLaneDirection.Backward ? 1 : 0;
                    Path(paths, OmsiSplineCategories.RailLane, middle, RoadHeight, 0.0, direction);
                    paths.Append("[rail_enh]\r\n").Append(string.Join("\r\n", StreetTrack)).Append("\r\n\r\n");
                    if (lane.Shared)
                        Path(paths, 0, middle, RoadHeight, lane.Width, direction);
                }
                else if (lane.Kind == OmsiRoadLaneKind.Driving || lane.Kind == OmsiRoadLaneKind.Bus)
                    Path(paths, 0, (x0 + x1) / 2.0, RoadHeight, lane.Width, lane.Direction == OmsiRoadLaneDirection.Backward ? 1 : 0);
                else if (lane.Kind == OmsiRoadLaneKind.Sidewalk)
                    Path(paths, 1, (x0 + x1) / 2.0, RaisedHeight, Math.Min(2.0, lane.Width / 2.0), 2);
            }

            // Height profiles: one per run of lanes at the same level.
            for (var start = 0; start < type.Lanes.Count;)
            {
                var end = start;
                while (end + 1 < type.Lanes.Count && type.Lanes[end + 1].Raised == type.Lanes[start].Raised)
                    end++;
                var height = type.Lanes[start].Raised ? RaisedHeight : RoadHeight;
                heights.Append("[heightprofile]\r\n").Append(Number(edges[start])).Append("\r\n").Append(Number(edges[end + 1])).Append("\r\n")
                    .Append(Number(height)).Append("\r\n").Append(Number(height)).Append("\r\n\r\n");
                start = end + 1;
            }

            // The road markings, over the lanes; a line at a kerb moves onto the carriageway as an edge line.
            var markings = new StringBuilder();
            for (var index = 0; index + 1 < type.Lanes.Count; index++)
            {
                var marking = type.MarkingAfter(index);
                var left = type.Lanes[index];
                var right = type.Lanes[index + 1];
                if (marking == OmsiRoadMarking.None || left.Raised && right.Raised)
                    continue;
                var x = edges[index + 1] + (left.Raised ? KerbInset : right.Raised ? -KerbInset : 0.0);
                markings.Append("\r\n").Append(OmsiRoadLane.Name(marking)).Append(" line:\r\n");
                foreach (var stroke in Strokes(marking))
                    Line(markings, stroke.Key, x + stroke.Value, TextureIndex);
            }

            var text = new StringBuilder();
            text.Append("File created with OMSI MapWorks\r\n");
            text.Append(OmsiRoadType.DescriptionPrefix).Append(' ').Append(type.Describe()).Append("\r\n\r\n");
            text.Append("---------------------------\r\n      Height Profiles\r\n---------------------------\r\n\r\n").Append(heights);
            text.Append("---------------------------\r\n         Textures\r\n---------------------------\r\n\r\n");
            foreach (var texture in textures)
            {
                text.Append("[texture]\r\n").Append(System.IO.Path.GetFileName(texture)).Append("\r\n\r\n");
                if (texture == DashTexture || texture == HalfDashTexture)
                    text.Append("[matl_alpha]\r\n1\r\n\r\n");
                if (texture == DashTexture)
                    files.Files[DashTexture] = DashBitmap(1.0 / 3.0);
                else if (texture == HalfDashTexture)
                    files.Files[HalfDashTexture] = DashBitmap(0.5);
                else if (texture == WhiteTexture)
                    files.Files[WhiteTexture] = OmsiJunctionMarkings.PlainBitmap(240, 240, 235);
                else if (texture == GrooveTexture)
                    files.Files[GrooveTexture] = GrooveBitmap();
                else
                    files.Textures.Add(texture);
            }
            text.Append("---------------------------\r\n     Graphical Lanes\r\n---------------------------\r\n").Append(strips);
            if (rails.Length > 0)
                text.Append("\r\nRails:\r\n").Append(rails);
            text.Append(markings);
            text.Append("\r\n---------------------------\r\n          Paths\r\n---------------------------\r\n\r\n").Append(paths);
            files.Sli = text.ToString();
            return files;
        }

        private enum Stroke
        {
            Solid,
            Dashed,
            LongDashed,
            Broad,
            BroadDashed
        }

        /// <summary>The strokes a line is painted with, each with its offset from the boundary, the left one first.</summary>
        private static List<KeyValuePair<Stroke, double>> Strokes(OmsiRoadMarking marking)
        {
            var apart = MarkingWidth;
            var strokes = new List<KeyValuePair<Stroke, double>>();
            switch (marking)
            {
                case OmsiRoadMarking.Dashed:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Dashed, 0.0));
                    break;
                case OmsiRoadMarking.LongDashed:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.LongDashed, 0.0));
                    break;
                case OmsiRoadMarking.Solid:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Solid, 0.0));
                    break;
                case OmsiRoadMarking.Double:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Solid, -apart));
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Solid, apart));
                    break;
                case OmsiRoadMarking.SolidDashed:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Solid, -apart));
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Dashed, apart));
                    break;
                case OmsiRoadMarking.DashedSolid:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Dashed, -apart));
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Solid, apart));
                    break;
                case OmsiRoadMarking.Broad:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.Broad, 0.0));
                    break;
                case OmsiRoadMarking.BroadDashed:
                    strokes.Add(new KeyValuePair<Stroke, double>(Stroke.BroadDashed, 0.0));
                    break;
            }
            return strokes;
        }

        private static void Line(StringBuilder text, Stroke stroke, double x, Func<string, int> textureIndex)
        {
            var broad = stroke == Stroke.Broad || stroke == Stroke.BroadDashed;
            var half = (broad ? BroadWidth : MarkingWidth) / 2.0;
            string texture;
            double vPerMeter;
            switch (stroke)
            {
                case Stroke.Dashed:
                    texture = DashTexture;
                    vPerMeter = 1.0 / DashRepeat;
                    break;
                case Stroke.LongDashed:
                    texture = DashTexture;
                    vPerMeter = 1.0 / LongDashRepeat;
                    break;
                case Stroke.BroadDashed:
                    texture = HalfDashTexture;
                    vPerMeter = 1.0 / HalfDashRepeat;
                    break;
                default:
                    texture = WhiteTexture;
                    vPerMeter = RaisedVPerMeter;
                    break;
            }
            var height = RoadHeight + MarkingLift;
            Strip(text, textureIndex(texture), x - half, height, 0.25, vPerMeter, x + half, height, 0.75, vPerMeter);
        }

        /// <summary>A texture chosen for a lane repeats along the road as often as the lane is wide, so it keeps its proportions.</summary>
        private static double ChosenVPerMeter(double width)
        {
            return 1.0 / Math.Max(1.0, width);
        }

        /// <summary>A raised lane's level strip, its kerb end at the left or the right edge.</summary>
        private static void RaisedStrip(StringBuilder text, int texture, Surface surface, double x0, double x1, bool kerbAtLeft)
        {
            var far = surface.KerbU - (x1 - x0) * surface.UPerMeter;
            Strip(text, texture, x0, RaisedHeight, kerbAtLeft ? surface.KerbU : far, RaisedVPerMeter, x1, RaisedHeight, kerbAtLeft ? far : surface.KerbU, RaisedVPerMeter);
        }

        private static void Strip(StringBuilder text, int texture, double xA, double hA, double uA, double vA, double xB, double hB, double uB, double vB)
        {
            text.Append("\r\n[profile]\r\n").Append(texture).Append("\r\n");
            Point(text, xA, hA, uA, vA);
            Point(text, xB, hB, uB, vB);
        }

        private static void Point(StringBuilder text, double x, double height, double u, double vPerMeter)
        {
            text.Append("\r\n[profilepnt]\r\n").Append(Number(x)).Append("\r\n").Append(Number(height)).Append("\r\n")
                .Append(Number(u)).Append("\r\n").Append(Number(vPerMeter)).Append("\r\n");
        }

        private static void Path(StringBuilder text, int kind, double x, double height, double width, int direction)
        {
            text.Append("[path]\r\n").Append(kind).Append("\r\n").Append(Number(x)).Append("\r\n").Append(Number(height)).Append("\r\n")
                .Append(Number(width)).Append("\r\n").Append(direction).Append("\r\n\r\n");
        }

        private static string Number(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A grooved rail across, as a 24-bit BMP: steel for the head from u 0 and the dark groove up to u 1, the width of each
        /// in proportion, so a strip from the rail's outer edge to the groove's inner edge shows both.
        /// </summary>
        public static byte[] GrooveBitmap()
        {
            const int width = 20;
            const int height = 4;
            var head = (int)Math.Round(width * RailHeadWidth / (RailHeadWidth + RailGrooveWidth));
            var stride = width * 3;
            var bytes = new byte[54 + stride * height];
            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
            BitConverter.GetBytes(54).CopyTo(bytes, 10);
            BitConverter.GetBytes(40).CopyTo(bytes, 14);
            BitConverter.GetBytes(width).CopyTo(bytes, 18);
            BitConverter.GetBytes(height).CopyTo(bytes, 22);
            BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
            BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
            BitConverter.GetBytes(stride * height).CopyTo(bytes, 34);
            for (var row = 0; row < height; row++)
                for (var column = 0; column < width; column++)
                {
                    var at = 54 + row * stride + column * 3;
                    var steel = column < head;
                    bytes[at] = steel ? (byte)188 : (byte)34;
                    bytes[at + 1] = steel ? (byte)184 : (byte)34;
                    bytes[at + 2] = steel ? (byte)180 : (byte)36;
                }
            return bytes;
        }

        /// <summary>A dash pattern as a 32-bit TGA: white, opaque for <paramref name="share"/> of its height and clear for the rest.</summary>
        public static byte[] DashBitmap(double share)
        {
            const int width = 8;
            var on = (int)Math.Round(DashRows * share);
            var bytes = new byte[18 + width * DashRows * 4];
            bytes[2] = 2;
            bytes[12] = width;
            bytes[14] = DashRows & 0xFF;
            bytes[15] = DashRows >> 8;
            bytes[16] = 32;
            // Eight bits of alpha, rows from the top.
            bytes[17] = 0x28;
            for (var row = 0; row < DashRows; row++)
                for (var column = 0; column < width; column++)
                {
                    var at = 18 + (row * width + column) * 4;
                    bytes[at] = 235;
                    bytes[at + 1] = 240;
                    bytes[at + 2] = 240;
                    bytes[at + 3] = row < on ? (byte)255 : (byte)0;
                }
            return bytes;
        }
    }

    /// <summary>
    /// A road type's .sli changed or deleted (ADR 049, stage 3), with its bytes before and after, so undo and redo put
    /// either back. Textures copied for it stay, as other road types may share them.
    /// </summary>
    public sealed class OmsiRoadTypeChange
    {
        internal OmsiRoadTypeChange(string fullPath, string assetPath, byte[] before, byte[] after)
        {
            FullPath = fullPath;
            AssetPath = assetPath;
            Before = before;
            After = after;
        }

        public string FullPath { get; }

        /// <summary>The profile's path relative to the installation, as tile files name it.</summary>
        public string AssetPath { get; }

        public byte[] Before { get; }

        /// <summary>The file afterwards; null when the road type was deleted.</summary>
        public byte[] After { get; }

        public bool Deleted => After == null;

        public void Undo()
        {
            Put(Before);
        }

        public void Redo()
        {
            Put(After);
        }

        /// <summary>Writes the bytes through a file beside it, so a failed write leaves the old file whole; null deletes it.</summary>
        private void Put(byte[] bytes)
        {
            if (bytes == null)
            {
                File.Delete(FullPath);
                return;
            }
            var temporary = FullPath + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(FullPath))
                File.Replace(temporary, FullPath, null);
            else
                File.Move(temporary, FullPath);
        }
    }

    /// <summary>
    /// Keeps the road types the builder makes under <c>Splines\Mapforge\&lt;project&gt;</c> of the installation (ADR 049),
    /// with copies of their textures in the <c>texture</c> folder they share, as the made junctions keep theirs.
    /// </summary>
    public static class OmsiRoadTypeStore
    {
        public const string Folder = @"Splines\Mapforge";

        /// <summary>Whether a spline profile was made by the road builder, by where it lies.</summary>
        public static bool IsMade(string assetPath)
        {
            return assetPath != null && assetPath.Replace('/', '\\').StartsWith(Folder + "\\", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Writes a new road type; a name in use gets a number, so nothing is overwritten. A texture whose name another
        /// texture of the project's road types already has is copied under a name with a number.
        /// </summary>
        /// <returns>The profile's path relative to the installation, as tile files name it.</returns>
        /// <exception cref="FileNotFoundException">A texture the road type needs is missing from the installation.</exception>
        public static string Write(string installationRoot, string projectName, string name, OmsiRoadType type)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            var project = OmsiGeneratedObjectStore.SafeName(projectName, "Project");
            var fileName = OmsiGeneratedObjectStore.SafeName(name, type.SuggestName());
            var folder = Path.Combine(installationRoot, Folder, project);
            var sliName = fileName;
            for (var number = 2; File.Exists(Path.Combine(folder, sliName + ".sli")); number++)
                sliName = fileName + "_" + number;
            var sli = Compose(installationRoot, type, Path.Combine(folder, "texture"));
            File.WriteAllBytes(Path.Combine(folder, sliName + ".sli"), sli);
            return Folder + "\\" + project + "\\" + sliName + ".sli";
        }

        /// <summary>
        /// Writes a road type the builder made anew with other lanes, under the same path, so every spline of it changes
        /// with it (ADR 049, stage 3). The change keeps the file before and after, for undo.
        /// </summary>
        /// <param name="assetPath">The profile's path relative to the installation, under <see cref="Folder"/>.</param>
        /// <exception cref="FileNotFoundException">The profile, or a texture the road type needs, is missing.</exception>
        public static OmsiRoadTypeChange Replace(string installationRoot, string assetPath, OmsiRoadType type)
        {
            var path = MadePath(installationRoot, assetPath);
            var before = File.ReadAllBytes(path);
            var change = new OmsiRoadTypeChange(path, assetPath, before, Compose(installationRoot, type, Path.Combine(Path.GetDirectoryName(path), "texture")));
            change.Redo();
            return change;
        }

        /// <summary>Deletes a road type the builder made; its textures stay, as other road types share them. Undo writes it back.</summary>
        public static OmsiRoadTypeChange Delete(string installationRoot, string assetPath)
        {
            var path = MadePath(installationRoot, assetPath);
            var change = new OmsiRoadTypeChange(path, assetPath, File.ReadAllBytes(path), null);
            change.Redo();
            return change;
        }

        private static string MadePath(string installationRoot, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            if (!IsMade(assetPath))
                throw new ArgumentException(assetPath + " was not made by the road builder; only those can be changed.", nameof(assetPath));
            var path = OmsiRelativePath.Resolve(installationRoot, assetPath);
            if (!File.Exists(path))
                throw new FileNotFoundException(assetPath + " is missing from the installation.", path);
            return path;
        }

        /// <summary>
        /// What still names a road type: the first tile file of a map of the installation, of the project's workspace or of
        /// an object the editor made (such as a junction with the road type on an arm) that names it; null when nothing does.
        /// A file that cannot be read counts as naming it.
        /// </summary>
        /// <param name="workspaceFolder">The project's folder of edited map files, or null.</param>
        public static string UsedBy(string installationRoot, string workspaceFolder, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            var mark = assetPath.Trim().Replace('/', '\\').ToLowerInvariant();
            var searches = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(Path.Combine(installationRoot, "maps"), "*.map"),
                new KeyValuePair<string, string>(Path.Combine(installationRoot, OmsiGeneratedObjectStore.Folder), "*.sco")
            };
            if (!string.IsNullOrWhiteSpace(workspaceFolder))
                searches.Add(new KeyValuePair<string, string>(workspaceFolder, "*.map"));
            foreach (var search in searches)
            {
                if (!Directory.Exists(search.Key))
                    continue;
                foreach (var file in Directory.EnumerateFiles(search.Key, search.Value, SearchOption.AllDirectories))
                {
                    try
                    {
                        if (OmsiTextFile.Decode(File.ReadAllBytes(file)).Replace('/', '\\').ToLowerInvariant().IndexOf(mark, StringComparison.Ordinal) >= 0)
                            return file;
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        return file;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// The bytes of a road type's .sli: the textures it needs copied into <paramref name="textureFolder"/>, the marking
        /// textures made there, and their names in its [texture] lines.
        /// </summary>
        private static byte[] Compose(string installationRoot, OmsiRoadType type, string textureFolder)
        {
            var files = OmsiRoadTypeWriter.Write(type);
            foreach (var texture in files.Textures)
                if (!File.Exists(OmsiRelativePath.Resolve(installationRoot, texture)))
                    throw new FileNotFoundException("The road type needs " + texture + ", which is missing from the installation.", texture);
            Directory.CreateDirectory(textureFolder);

            // The copied textures in the order of their [texture] lines, which the made ones do not interrupt.
            var copied = new Queue<string>();
            foreach (var texture in files.Textures)
                copied.Enqueue(CopyTexture(installationRoot, texture, OmsiRelativePath.Resolve(installationRoot, texture), textureFolder));
            var sli = new StringBuilder();
            var lines = files.Sli.Split(new[] { "\r\n" }, StringSplitOptions.None);
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                    sli.Append("\r\n");
                if (index > 0 && lines[index - 1] == "[texture]" && !files.Files.ContainsKey(lines[index]) && copied.Count > 0)
                    sli.Append(copied.Dequeue());
                else
                    sli.Append(lines[index]);
            }
            foreach (var file in files.Files)
                File.WriteAllBytes(Path.Combine(textureFolder, file.Key), file.Value);
            return OmsiTextFile.Encode(sli.ToString(), OmsiTextEncoding.Windows1252);
        }

        /// <summary>
        /// Copies a texture with its .dds, .cfg and .surf into the folder and returns the name it has there: its own, unless
        /// another texture of that name is there already, then the first free name with a number.
        /// </summary>
        private static string CopyTexture(string installationRoot, string texture, string source, string textureFolder)
        {
            var name = Path.GetFileName(source);
            var extension = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var bytes = File.ReadAllBytes(source);
            for (var number = 1; ; number++)
            {
                var candidate = number == 1 ? name : stem + "_" + number + extension;
                var target = Path.Combine(textureFolder, candidate);
                if (File.Exists(target) && !SameBytes(File.ReadAllBytes(target), bytes))
                    continue;
                // OMSI prefers a .dds of the same name; a .cfg sets wetness and puddles, a .surf the kind of surface.
                var companions = new[]
                {
                    new KeyValuePair<string, string>(texture, candidate),
                    new KeyValuePair<string, string>(System.IO.Path.ChangeExtension(texture, ".dds"), Path.ChangeExtension(candidate, ".dds")),
                    new KeyValuePair<string, string>(texture + ".cfg", candidate + ".cfg"),
                    new KeyValuePair<string, string>(texture + ".surf", candidate + ".surf")
                };
                foreach (var pair in companions)
                {
                    var from = OmsiRelativePath.Resolve(installationRoot, pair.Key);
                    var to = Path.Combine(textureFolder, pair.Value);
                    if (File.Exists(from) && !File.Exists(to))
                        File.Copy(from, to);
                }
                return candidate;
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (var index = 0; index < a.Length; index++)
                if (a[index] != b[index])
                    return false;
            return true;
        }

        /// <summary>The road type a .sli holds in its <see cref="OmsiRoadType.DescriptionPrefix"/> line; null for one the builder did not make.</summary>
        public static OmsiRoadType Read(byte[] sli)
        {
            if (sli == null)
                return null;
            foreach (var line in OmsiTextFile.Decode(sli).Split('\n'))
            {
                var text = line.Trim();
                if (text.StartsWith(OmsiRoadType.DescriptionPrefix, StringComparison.OrdinalIgnoreCase))
                    return OmsiRoadType.TryParse(text, out var type) ? type : null;
                if (text.StartsWith("[", StringComparison.Ordinal))
                    break;
            }
            return null;
        }

        /// <summary>
        /// The surface textures the installation's spline profiles use, as paths relative to it, most used first; for
        /// choosing a lane's surface. Profiles made by the editor and textures that are missing are left out.
        /// </summary>
        public static List<string> SurfaceTextures(string installationRoot)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var splines = Path.Combine(installationRoot, "Splines");
            if (!Directory.Exists(splines))
                return new List<string>();
            foreach (var file in Directory.EnumerateFiles(splines, "*.sli", SearchOption.AllDirectories))
            {
                var relative = file.Substring(installationRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (IsMade(relative))
                    continue;
                OmsiSplineProfile profile;
                try
                {
                    profile = OmsiSplineProfile.Parse(File.ReadAllBytes(file), relative);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is FormatException)
                {
                    continue;
                }
                foreach (var texture in profile.Textures)
                {
                    // Only textures some strip lays flat count as surfaces, not those of wires, fences or kerb faces alone.
                    var index = profile.Textures.IndexOf(texture);
                    if (texture.UsesAlpha || !profile.Strips.Exists(strip => strip.TextureIndex == index && Math.Abs(strip.Points[strip.Points.Count - 1].X - strip.Points[0].X) >= 0.5))
                        continue;
                    counts.TryGetValue(texture.Path, out var count);
                    counts[texture.Path] = count + 1;
                }
            }
            var result = new List<string>();
            foreach (var pair in counts)
                if (File.Exists(OmsiRelativePath.Resolve(installationRoot, pair.Key)))
                    result.Add(pair.Key);
            result.Sort((a, b) => counts[b] != counts[a] ? counts[b].CompareTo(counts[a]) : string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            return result;
        }
    }
}
