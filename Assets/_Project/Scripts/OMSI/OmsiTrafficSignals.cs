using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// How a car signal head shows a <c>TrafficLightPhase</c> value, after openOMSI's <c>TrafficLightController::aspect</c>
    /// and the stock <c>ampel1.osc</c>/<c>ampel1_ddr.osc</c>. Other heads read the same value their own way (the
    /// pedestrian head <c>ampel2.osc</c> is green for 6..8 and red otherwise, the bus head <c>ampel3.osc</c> shows its
    /// arrow for 6..8, the warning head <c>ampel4.osc</c> flashes unless 12), so the value itself is what is kept.
    /// </summary>
    public enum OmsiSignalAspect
    {
        Red,
        RedYellow,
        Green,
        GreenYellow,
        Yellow,
        Dark
    }

    public static class OmsiSignalAspects
    {
        /// <summary>The values the stock programs write for each aspect: 0 red, 3 red-yellow, 6 green, 8 green-yellow, 9 yellow, 12 dark.</summary>
        public static int StateOf(OmsiSignalAspect aspect)
        {
            switch (aspect)
            {
                case OmsiSignalAspect.Red: return 0;
                case OmsiSignalAspect.RedYellow: return 3;
                case OmsiSignalAspect.Green: return 6;
                case OmsiSignalAspect.GreenYellow: return 8;
                case OmsiSignalAspect.Yellow: return 9;
                default: return 12;
            }
        }

        public static OmsiSignalAspect Of(int state)
        {
            if (state >= 0 && state <= 2)
                return OmsiSignalAspect.Red;
            if (state >= 3 && state <= 5)
                return OmsiSignalAspect.RedYellow;
            if (state == 6 || state == 7)
                return OmsiSignalAspect.Green;
            if (state == 8)
                return OmsiSignalAspect.GreenYellow;
            if (state >= 9 && state <= 11)
                return OmsiSignalAspect.Yellow;
            return OmsiSignalAspect.Dark;
        }

        /// <summary>Whether vehicles may go on this value, as openOMSI decides (green or dark).</summary>
        public static bool AllowsGo(int state)
        {
            var aspect = Of(state);
            return aspect == OmsiSignalAspect.Green || aspect == OmsiSignalAspect.Dark;
        }
    }

    /// <summary>One <c>[phase]</c> of a light: the value its heads read and how many seconds it lasts.</summary>
    public sealed class OmsiSignalPhase
    {
        public OmsiSignalPhase(int state, double seconds)
        {
            State = state;
            Seconds = seconds;
        }

        public int State { get; set; }

        /// <summary>Seconds; a last phase of 0 lasts until the cycle starts again.</summary>
        public double Seconds { get; set; }

        public OmsiSignalPhase Copy()
        {
            return new OmsiSignalPhase(State, Seconds);
        }
    }

    /// <summary>
    /// One <c>[traffic_light]</c> of a crossing's program: a signal group in the sense of traffic engineering. Every head
    /// that names its index and every path bound to it with <c>[use_traffic_light]</c> follows its phases.
    /// </summary>
    public sealed class OmsiSignalLight
    {
        public string Name { get; set; } = string.Empty;
        public List<OmsiSignalPhase> Phases { get; } = new List<OmsiSignalPhase>();

        /// <summary><c>[approachdist]</c> after the light: how far before its stop line a vehicle asks for it (m), or null.</summary>
        public double? ApproachMeters { get; set; }

        public OmsiSignalLight Copy()
        {
            var copy = new OmsiSignalLight { Name = Name, ApproachMeters = ApproachMeters };
            copy.Phases.AddRange(Phases.Select(phase => phase.Copy()));
            return copy;
        }
    }

    /// <summary>
    /// A <c>[traffic_light_stop]</c> (no <see cref="JumpTo"/>) or <c>[traffic_light_jump]</c>: when the cycle clock reaches
    /// <see cref="Seconds"/>, it waits there or goes on at <see cref="JumpTo"/> while the condition holds. With
    /// <see cref="IfNoRequest"/> the condition is that nobody asks at <see cref="Light"/>, otherwise that somebody does.
    /// </summary>
    public sealed class OmsiSignalHold
    {
        public int Light { get; set; }
        public double Seconds { get; set; }
        public bool IfNoRequest { get; set; }
        public double? JumpTo { get; set; }

        public OmsiSignalHold Copy()
        {
            return new OmsiSignalHold { Light = Light, Seconds = Seconds, IfNoRequest = IfNoRequest, JumpTo = JumpTo };
        }
    }

    /// <summary>
    /// The traffic light program of a crossing object type, as its .sco holds it (ADR 085): the lights with their phases
    /// in order from the start of the cycle, the cycle length, the stop and jump points, and which light each path obeys.
    /// It belongs to the object type, so every placed crossing of that type runs it. The reading follows openOMSI
    /// (<c>omsi-scenery/src/sco.rs</c>, <c>omsi-sim/src/traffic/lights.rs</c>).
    /// </summary>
    public sealed class OmsiSignalProgram
    {
        /// <summary>The cycle length if no <c>[traffic_lights_group]</c> gives one: the longest light, at least a second.</summary>
        private const double ShortestCycle = 1.0;

        /// <summary><c>[traffic_lights_group]</c>: the cycle length in seconds; 0 means the longest light; null without the section.</summary>
        public double? CycleSeconds { get; set; }

        public List<OmsiSignalLight> Lights { get; } = new List<OmsiSignalLight>();
        public List<OmsiSignalHold> Holds { get; } = new List<OmsiSignalHold>();

        /// <summary>Per <c>[path]</c>/<c>[path_2]</c> of the object, in file order: the light it obeys, or -1.</summary>
        public List<int> PathLights { get; } = new List<int>();

        /// <summary>True when the object has a program of its own: at least one light.</summary>
        public bool HasProgram => Lights.Count > 0;

        /// <summary>The length of one cycle in seconds, as openOMSI runs it.</summary>
        public double CycleLength
        {
            get
            {
                if (CycleSeconds.HasValue && CycleSeconds.Value > 0)
                    return CycleSeconds.Value;
                var longest = 0.0;
                foreach (var light in Lights)
                {
                    var sum = 0.0;
                    foreach (var phase in light.Phases)
                        sum += phase.Seconds;
                    longest = Math.Max(longest, sum);
                }
                return Math.Max(ShortestCycle, longest);
            }
        }

        /// <summary>The value light <paramref name="light"/> shows at <paramref name="seconds"/> into the cycle (any time wraps round).</summary>
        public int StateAt(int light, double seconds)
        {
            if (light < 0 || light >= Lights.Count || Lights[light].Phases.Count == 0)
                return OmsiSignalAspects.StateOf(OmsiSignalAspect.Green);
            var phases = Lights[light].Phases;
            var cycle = CycleLength;
            var time = ((seconds % cycle) + cycle) % cycle;
            foreach (var phase in phases)
            {
                if (time < phase.Seconds)
                    return phase.State;
                time -= phase.Seconds;
            }
            // After the last phase its value holds until the cycle starts again.
            return phases[phases.Count - 1].State;
        }

        /// <summary>The second at which phase <paramref name="phase"/> of a light starts, from the start of the cycle.</summary>
        public double PhaseStart(int light, int phase)
        {
            var phases = Lights[light].Phases;
            var start = 0.0;
            for (var index = 0; index < phase && index < phases.Count; index++)
                start += phases[index].Seconds;
            return start;
        }

        public OmsiSignalProgram Copy()
        {
            var copy = new OmsiSignalProgram { CycleSeconds = CycleSeconds };
            copy.Lights.AddRange(Lights.Select(light => light.Copy()));
            copy.Holds.AddRange(Holds.Select(hold => hold.Copy()));
            copy.PathLights.AddRange(PathLights);
            return copy;
        }

        /// <summary>The program of a .sco, read with <see cref="OmsiSceneryRecordLayouts"/>.</summary>
        public static OmsiSignalProgram Read(byte[] scoBytes)
        {
            return Read(OmsiRecordFile.Parse(scoBytes, OmsiSceneryRecordLayouts.Instance));
        }

        /// <summary>
        /// The program of a parsed .sco. Paths are counted at every <c>[path]</c> and <c>[path_2]</c> header, as openOMSI
        /// counts them; <c>[phase]</c> and <c>[approachdist]</c> belong to the light before them.
        /// </summary>
        public static OmsiSignalProgram Read(OmsiRecordFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            var program = new OmsiSignalProgram();
            foreach (var record in file.Records)
            {
                var values = record.Fields.Select(line => line.Text.Trim()).ToList();
                switch (record.Name.ToLowerInvariant())
                {
                    case "traffic_lights_group":
                        program.CycleSeconds = Number(values, 0);
                        break;
                    case "traffic_light":
                        program.Lights.Add(new OmsiSignalLight { Name = values.Count > 0 ? values[0] : string.Empty });
                        break;
                    case "phase":
                        if (program.Lights.Count > 0)
                            program.Lights[program.Lights.Count - 1].Phases.Add(new OmsiSignalPhase((int)Number(values, 0), Number(values, 1)));
                        break;
                    case "approachdist":
                        if (program.Lights.Count > 0)
                            program.Lights[program.Lights.Count - 1].ApproachMeters = Number(values, 0);
                        break;
                    case "traffic_light_stop":
                        program.Holds.Add(new OmsiSignalHold
                        {
                            Light = Math.Max(0, (int)Number(values, 0)), Seconds = Number(values, 1), IfNoRequest = Number(values, 2) > 0.5
                        });
                        break;
                    case "traffic_light_jump":
                        program.Holds.Add(new OmsiSignalHold
                        {
                            Light = Math.Max(0, (int)Number(values, 0)), Seconds = Number(values, 1), IfNoRequest = Number(values, 2) > 0.5,
                            JumpTo = Number(values, 3)
                        });
                        break;
                    case "path":
                    case "path_2":
                        program.PathLights.Add(-1);
                        break;
                    case "use_traffic_light":
                        if (program.PathLights.Count > 0)
                            program.PathLights[program.PathLights.Count - 1] = (int)Number(values, 0);
                        break;
                }
            }
            return program;
        }

        /// <summary>
        /// Writes this program into a parsed .sco with as few changed lines as possible: a value that did not change keeps
        /// its text; while the lights, phases and points keep their number, only fields change; otherwise the block of
        /// program sections is written anew where the old one began (or before the first path). Path bindings change
        /// in their <c>[use_traffic_light]</c> records, which are added after their path or taken away. Every other line
        /// stays as it was.
        /// </summary>
        public void WriteTo(OmsiRecordFile file)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            var old = Read(file);
            if (old.PathLights.Count != PathLights.Count)
                throw new ArgumentException(
                    $"The program binds {PathLights.Count} paths, but the object has {old.PathLights.Count}.", nameof(file));
            Validate();
            if (SameShape(old))
                WriteFields(file);
            else
                WriteBlock(file);
            WritePathLights(file);
        }

        /// <summary>Checks what cannot be written: a light without a name line, a negative or non-finite time, a binding to no light.</summary>
        public void Validate()
        {
            foreach (var light in Lights)
            {
                if (light.Name == null || light.Name.IndexOf('\n') >= 0 || light.Name.IndexOf('\r') >= 0 || light.Name.Trim().StartsWith("[", StringComparison.Ordinal))
                    throw new ArgumentException("A light needs a name on one line that does not start with '['.");
                foreach (var phase in light.Phases)
                    if (!IsTime(phase.Seconds))
                        throw new ArgumentException($"Light '{light.Name}' has a phase of {phase.Seconds} s.");
                if (light.ApproachMeters.HasValue && !IsTime(light.ApproachMeters.Value))
                    throw new ArgumentException($"Light '{light.Name}' has an approach distance of {light.ApproachMeters} m.");
            }
            if (CycleSeconds.HasValue && !IsTime(CycleSeconds.Value))
                throw new ArgumentException($"The cycle cannot last {CycleSeconds} s.");
            foreach (var hold in Holds)
                if (hold.Light < 0 || hold.Light >= Lights.Count || !IsTime(hold.Seconds) || hold.JumpTo.HasValue && !IsTime(hold.JumpTo.Value))
                    throw new ArgumentException($"A stop or jump point names light {hold.Light} at {hold.Seconds} s, which the program cannot hold.");
            foreach (var light in PathLights)
                if (light < -1 || light >= Lights.Count)
                    throw new ArgumentException($"A path obeys light {light}, which the program does not have.");
        }

        private static bool IsTime(double value)
        {
            return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private bool SameShape(OmsiSignalProgram old)
        {
            if (old.CycleSeconds.HasValue != CycleSeconds.HasValue || old.Lights.Count != Lights.Count || old.Holds.Count != Holds.Count)
                return false;
            for (var index = 0; index < Lights.Count; index++)
                if (old.Lights[index].Phases.Count != Lights[index].Phases.Count ||
                    old.Lights[index].ApproachMeters.HasValue != Lights[index].ApproachMeters.HasValue)
                    return false;
            // The stop and jump points must still stand in the same order of kinds.
            for (var index = 0; index < Holds.Count; index++)
                if (old.Holds[index].JumpTo.HasValue != Holds[index].JumpTo.HasValue)
                    return false;
            return true;
        }

        private void WriteFields(OmsiRecordFile file)
        {
            var light = -1;
            var phase = 0;
            var stop = 0;
            var jump = 0;
            var stops = Holds.Where(hold => !hold.JumpTo.HasValue).ToList();
            var jumps = Holds.Where(hold => hold.JumpTo.HasValue).ToList();
            foreach (var record in file.Records)
            {
                switch (record.Name.ToLowerInvariant())
                {
                    case "traffic_lights_group":
                        SetNumber(record, 0, CycleSeconds ?? 0);
                        break;
                    case "traffic_light":
                        light++;
                        phase = 0;
                        if (!string.Equals(record.GetField(0)?.Trim(), Lights[light].Name.Trim(), StringComparison.Ordinal))
                            record.SetField(0, Lights[light].Name);
                        break;
                    case "phase":
                        if (light >= 0)
                        {
                            SetInteger(record, 0, Lights[light].Phases[phase].State);
                            SetNumber(record, 1, Lights[light].Phases[phase].Seconds);
                            phase++;
                        }
                        break;
                    case "approachdist":
                        if (light >= 0)
                            SetNumber(record, 0, Lights[light].ApproachMeters ?? 0);
                        break;
                    case "traffic_light_stop":
                        WriteHold(record, stops[stop++]);
                        break;
                    case "traffic_light_jump":
                        WriteHold(record, jumps[jump++]);
                        break;
                }
            }
        }

        private static void WriteHold(OmsiRecord record, OmsiSignalHold hold)
        {
            SetInteger(record, 0, hold.Light);
            SetNumber(record, 1, hold.Seconds);
            SetInteger(record, 2, hold.IfNoRequest ? 1 : 0);
            if (hold.JumpTo.HasValue)
                SetNumber(record, 3, hold.JumpTo.Value);
        }

        private static readonly HashSet<string> ProgramSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "traffic_lights_group", "traffic_light", "phase", "approachdist", "traffic_light_stop", "traffic_light_jump"
        };

        private void WriteBlock(OmsiRecordFile file)
        {
            var at = file.Records.FindIndex(record => ProgramSections.Contains(record.Name));
            if (at < 0)
                at = file.Records.FindIndex(record => IsPath(record.Name));
            if (at < 0)
                at = file.Records.Count;
            var before = file.Records.Take(at).Count(record => ProgramSections.Contains(record.Name));
            foreach (var record in file.Records.Where(record => ProgramSections.Contains(record.Name)).ToList())
                file.RemoveRecord(record);
            at -= before;
            foreach (var record in ProgramRecords(file))
                file.InsertRecord(at++, record);
        }

        /// <summary>The program's sections in the order OMSI's own crossings write them.</summary>
        private IEnumerable<OmsiRecord> ProgramRecords(OmsiRecordFile file)
        {
            if (!HasProgram && !CycleSeconds.HasValue)
                yield break;
            yield return file.CreateRecord("traffic_lights_group", new[] { OmsiNumberFormat.Format(CycleSeconds ?? 0) });
            foreach (var light in Lights)
            {
                yield return file.CreateRecord("traffic_light", new[] { light.Name });
                foreach (var phase in light.Phases)
                    yield return file.CreateRecord("phase", new[] { phase.State.ToString(CultureInfo.InvariantCulture), OmsiNumberFormat.Format(phase.Seconds) });
                if (light.ApproachMeters.HasValue)
                    yield return file.CreateRecord("approachdist", new[] { OmsiNumberFormat.Format(light.ApproachMeters.Value) });
            }
            foreach (var hold in Holds.Where(hold => !hold.JumpTo.HasValue))
                yield return file.CreateRecord("traffic_light_stop", new[]
                {
                    hold.Light.ToString(CultureInfo.InvariantCulture), OmsiNumberFormat.Format(hold.Seconds), hold.IfNoRequest ? "1" : "0"
                });
            foreach (var hold in Holds.Where(hold => hold.JumpTo.HasValue))
                yield return file.CreateRecord("traffic_light_jump", new[]
                {
                    hold.Light.ToString(CultureInfo.InvariantCulture), OmsiNumberFormat.Format(hold.Seconds), hold.IfNoRequest ? "1" : "0",
                    OmsiNumberFormat.Format(hold.JumpTo.Value)
                });
        }

        private void WritePathLights(OmsiRecordFile file)
        {
            var path = -1;
            for (var index = 0; index < file.Records.Count; index++)
            {
                var record = file.Records[index];
                if (!IsPath(record.Name))
                    continue;
                path++;
                var wanted = PathLights[path];
                // Its [use_traffic_light], if any, stands among the records of this path, before the next path.
                var end = index + 1;
                while (end < file.Records.Count && !IsPath(file.Records[end].Name))
                    end++;
                var use = file.Records.Skip(index + 1).Take(end - index - 1)
                    .FirstOrDefault(other => string.Equals(other.Name, "use_traffic_light", StringComparison.OrdinalIgnoreCase));
                if (wanted < 0)
                {
                    if (use != null)
                    {
                        file.RemoveRecord(use);
                        end--;
                    }
                }
                else if (use != null)
                {
                    SetInteger(use, 0, wanted);
                }
                else
                {
                    file.InsertRecord(index + 1, file.CreateRecord("use_traffic_light", new[] { wanted.ToString(CultureInfo.InvariantCulture) }));
                    end++;
                }
                index = end - 1;
            }
        }

        private static bool IsPath(string name)
        {
            return string.Equals(name, "path", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "path_2", StringComparison.OrdinalIgnoreCase);
        }

        private static double Number(IReadOnlyList<string> values, int index)
        {
            return index < values.Count && double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        /// <summary>Writes a number unless the field already holds the same value, so OMSI's long decimals stay as they are.</summary>
        private static void SetNumber(OmsiRecord record, int index, double value)
        {
            var text = record.GetField(index);
            if (text != null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var old) &&
                (old == value || (float)old == (float)value))
                return;
            record.SetField(index, OmsiNumberFormat.Format(value));
        }

        private static void SetInteger(OmsiRecord record, int index, int value)
        {
            var text = record.GetField(index);
            if (text != null && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var old) && old == value)
                return;
            record.SetField(index, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// A signal head placed in the map: an object or attached object whose type has <c>[trafficlight]</c> and whose
    /// <c>[varparent]</c> names a crossing. It shows the light its first label gives as a whole number of 0 or more;
    /// without one, openOMSI lets it follow light 0 (<see cref="LightIndex"/> is then null).
    /// </summary>
    public sealed class OmsiSignalHead
    {
        public OmsiTileSummary Tile { get; internal set; }

        /// <summary>The placed object, or null for an attached one.</summary>
        public OmsiObjectRecord Object { get; internal set; }

        /// <summary>The attached object, or null for a placed one.</summary>
        public OmsiAttachedObjectRecord Attached { get; internal set; }

        public int Id => Object?.Id ?? Attached.Id;
        public string AssetPath => Object?.AssetPath ?? Attached.AssetPath;
        public int? LightIndex { get; internal set; }

        /// <summary>The light index a head's labels name, as openOMSI reads it, or null.</summary>
        public static int? LightOf(IReadOnlyList<string> labels)
        {
            if (labels == null || labels.Count == 0)
                return null;
            return int.TryParse(labels[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 ? index : (int?)null;
        }

        /// <summary>
        /// The heads of a placed crossing: every object and attached object of its tile and the eight around it (a
        /// [varparent] reaches no further, ADR 012) whose type is a signal head and whose [varparent] is the crossing.
        /// </summary>
        /// <param name="isSignalHead">Tells whether an object file is a signal head ([trafficlight]); callers keep the answers.</param>
        public static List<OmsiSignalHead> Of(OmsiMapSummary summary, OmsiTileSummary crossingTile, int crossingId, Func<string, bool> isSignalHead)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (crossingTile == null)
                throw new ArgumentNullException(nameof(crossingTile));
            if (isSignalHead == null)
                throw new ArgumentNullException(nameof(isSignalHead));
            var heads = new List<OmsiSignalHead>();
            foreach (var tile in summary.Tiles)
            {
                if (Math.Abs(tile.X - crossingTile.X) > 1 || Math.Abs(tile.Y - crossingTile.Y) > 1)
                    continue;
                foreach (var item in tile.Objects)
                    if (item.VarParentId == crossingId && isSignalHead(item.AssetPath))
                        heads.Add(new OmsiSignalHead { Tile = tile, Object = item, LightIndex = LightOf(item.Labels) });
                foreach (var item in tile.AttachedObjects)
                    if (item.VarParentId == crossingId && isSignalHead(item.AssetPath))
                        heads.Add(new OmsiSignalHead { Tile = tile, Attached = item, LightIndex = LightOf(item.Labels) });
            }
            return heads;
        }
    }

    /// <summary>
    /// Field counts of the .sco sections a traffic light program uses and of its paths (openOMSI's <c>sco.rs</c>), so the
    /// program can be read and written positionally. Every other section keeps its lines as free text.
    /// </summary>
    public sealed class OmsiSceneryRecordLayouts : IOmsiRecordLayouts
    {
        public static readonly OmsiSceneryRecordLayouts Instance = new OmsiSceneryRecordLayouts();

        private static readonly Dictionary<string, int> FieldCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "traffic_lights_group", 1 },
            { "traffic_light", 1 },
            { "phase", 2 },
            { "approachdist", 1 },
            { "traffic_light_stop", 3 },
            { "traffic_light_jump", 4 },
            { "use_traffic_light", 1 },
            { "path", 12 },
            { "path_2", 14 }
        };

        public int? GetFieldCount(string name, int version, IReadOnlyList<OmsiLine> lines, int firstField, out bool isComplete)
        {
            isComplete = true;
            return FieldCounts.TryGetValue(name, out var count) ? count : (int?)null;
        }
    }
}
