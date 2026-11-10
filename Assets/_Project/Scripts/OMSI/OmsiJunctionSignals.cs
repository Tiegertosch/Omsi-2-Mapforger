using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The traffic light program of a junction MapWorks made (ADR 085): its lights, cycle and stop points as the .sco holds
    /// them, and which light each way through obeys, by <see cref="OmsiLaneConnections.Key"/>. The junction's .sco is made
    /// anew whenever it changes (markings, lanes, a turned arm, another road type) and its paths are numbered anew; the
    /// ways through are what stays, so the bindings are kept by them in a line <c>MapWorks signals: 0.1>2.2=0 …</c> and
    /// turned into <c>[use_traffic_light]</c> records on the first arc of each way every time the .sco is written.
    /// </summary>
    public sealed class OmsiJunctionSignals
    {
        internal const string Prefix = "MapWorks signals:";

        /// <summary>Lights, cycle and stop points; its path bindings are not used (they follow from <see cref="WayLights"/>).</summary>
        public OmsiSignalProgram Program { get; private set; } = new OmsiSignalProgram();

        /// <summary>The light each way through obeys, by its key; ways not listed obey none.</summary>
        public Dictionary<string, int> WayLights { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Takes the lights, cycle and stop points of an edited program; the bindings stay those of <see cref="WayLights"/>.</summary>
        public void SetProgram(OmsiSignalProgram program)
        {
            if (program == null)
                throw new ArgumentNullException(nameof(program));
            Program = program.Copy();
            Program.PathLights.Clear();
            foreach (var key in WayLights.Keys.ToList())
                if (WayLights[key] >= Program.Lights.Count)
                    WayLights.Remove(key);
        }

        public OmsiJunctionSignals Copy()
        {
            var copy = new OmsiJunctionSignals { Program = Program.Copy() };
            copy.Program.PathLights.Clear();
            foreach (var pair in WayLights)
                copy.WayLights[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>The signals in a junction's .sco, or null when it has no program.</summary>
        public static OmsiJunctionSignals Read(byte[] scoBytes)
        {
            if (scoBytes == null)
                throw new ArgumentNullException(nameof(scoBytes));
            var program = OmsiSignalProgram.Read(scoBytes);
            if (!program.HasProgram)
                return null;
            var signals = new OmsiJunctionSignals { Program = program };
            foreach (var raw in OmsiTextFile.Decode(scoBytes).Split('\n'))
            {
                var text = raw.Trim();
                if (!text.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;
                foreach (var word in text.Substring(Prefix.Length).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var equals = word.LastIndexOf('=');
                    if (equals > 0 && word.IndexOf('>') > 0 &&
                        int.TryParse(word.Substring(equals + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var light) &&
                        light >= 0 && light < program.Lights.Count)
                        signals.WayLights[word.Substring(0, equals)] = light;
                }
                break;
            }
            signals.Program.PathLights.Clear();
            return signals;
        }

        /// <summary>The line that keeps the bindings, such as <c>MapWorks signals: 0.1>2.2=0 1.1>3.2=1</c>.</summary>
        public string Line()
        {
            var text = new StringBuilder(Prefix);
            foreach (var pair in WayLights.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                text.Append(' ').Append(pair.Key).Append('=').Append(pair.Value.ToString(CultureInfo.InvariantCulture));
            return text.ToString();
        }

        /// <summary>
        /// Writes the program into the text of a junction's .sco made from <paramref name="layout"/>: its sections before
        /// the paths and a <c>[use_traffic_light]</c> after the first arc of each bound way. Returns the bound ways the
        /// junction no longer has (they are left out and dropped from <see cref="WayLights"/>).
        /// </summary>
        internal List<string> WriteInto(ref string sco, OmsiJunctionLayout layout)
        {
            var lost = WayLights.Keys.Where(key => !layout.WaysThrough.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToList();
            foreach (var key in lost)
                WayLights.Remove(key);
            var file = OmsiRecordFile.Parse(OmsiTextFile.Encode(sco, OmsiTextEncoding.Windows1252), OmsiSceneryRecordLayouts.Instance);
            var program = Program.Copy();
            program.PathLights.Clear();
            foreach (var path in layout.Paths)
                program.PathLights.Add(path.StartsWay && path.WayThrough != null && WayLights.TryGetValue(path.WayThrough, out var light) ? light : -1);
            program.WriteTo(file);
            sco = OmsiTextFile.Decode(file.ToBytes());
            return lost;
        }
    }
}
