using System.Collections.Generic;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    public sealed partial class OmsiMapSession
    {
        /// <summary>The daily density curve of cars (<paramref name="passengers"/> false) or of passengers, as the map's global.cfg has it.</summary>
        public List<KeyValuePair<double, double>> ReadTrafficCurve(bool passengers)
        {
            var config = OmsiRecordFile.Parse(RequireWorkspace().ReadAllBytes(GlobalConfigFileName), OmsiGlobalConfigLayouts.Instance);
            return OmsiTrafficCurve.Read(config, passengers ? OmsiTrafficCurve.PassengerSection : OmsiTrafficCurve.CarSection);
        }

        /// <summary>
        /// Writes the daily density curves of the map; a curve given as null stays as it is. Returns false when global.cfg is
        /// unchanged by it.
        /// </summary>
        public bool WriteTrafficCurves(IList<KeyValuePair<double, double>> cars, IList<KeyValuePair<double, double>> passengers)
        {
            var workspace = RequireWorkspace();
            var before = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(before, OmsiGlobalConfigLayouts.Instance);
            if (cars != null)
                OmsiTrafficCurve.Write(config, OmsiTrafficCurve.CarSection, cars);
            if (passengers != null)
                OmsiTrafficCurve.Write(config, OmsiTrafficCurve.PassengerSection, passengers);
            var after = config.ToBytes();
            if (before.SequenceEqual(after))
                return false;
            workspace.Save(GlobalConfigFileName, after);
            return true;
        }
    }
}
