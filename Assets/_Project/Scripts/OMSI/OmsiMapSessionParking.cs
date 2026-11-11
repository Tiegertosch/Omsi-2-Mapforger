using System;
using System.Collections.Generic;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    public sealed partial class OmsiMapSession
    {
        /// <summary>The parking list of a map: the parked cars its car park objects are filled with.</summary>
        public const string ParkListFileName = "parklist_p.txt";

        /// <summary>
        /// Gives a map without a parking list one, so its car park objects show cars in openOMSI (without a list they stay
        /// empty). A map that has one is left alone. Returns true when a list was written.
        /// </summary>
        public bool EnsureParkList(IList<string> entries)
        {
            if (entries == null || entries.Count == 0)
                return false;
            var workspace = RequireWorkspace();
            if (workspace.FileExists(ParkListFileName))
                return false;
            workspace.Save(ParkListFileName, OmsiTextFile.Encode(string.Join("\r\n", entries) + "\r\n", OmsiTextEncoding.Windows1252));
            return true;
        }
    }
}
