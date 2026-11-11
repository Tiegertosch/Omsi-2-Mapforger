using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    public sealed partial class OmsiMapSession
    {
        /// <summary>The parking list of a map: the parked cars its car park objects are filled with.</summary>
        public const string ParkListFileName = "parklist_p.txt";

        /// <summary>The cars of the map's parking list, empty when it has none.</summary>
        public List<string> ReadParkList(int index = 0)
        {
            var workspace = RequireWorkspace();
            var file = OmsiParkList.FileName(index);
            return workspace.FileExists(file) ? OmsiParkList.Parse(workspace.ReadAllBytes(file)) : new List<string>();
        }

        /// <summary>True when the map has the parking list of that number.</summary>
        public bool HasParkList(int index = 0)
        {
            return RequireWorkspace().FileExists(OmsiParkList.FileName(index));
        }

        /// <summary>Writes the map's parking list; returns false when it is the list the map has already.</summary>
        public bool WriteParkList(IList<string> entries, int index = 0)
        {
            var workspace = RequireWorkspace();
            var file = OmsiParkList.FileName(index);
            var text = entries.Count == 0 ? string.Empty : string.Join("\r\n", entries) + "\r\n";
            var bytes = OmsiTextFile.Encode(text, OmsiTextEncoding.Windows1252);
            if (workspace.FileExists(file) && OmsiParkList.Parse(workspace.ReadAllBytes(file)).SequenceEqual(entries, StringComparer.OrdinalIgnoreCase))
                return false;
            workspace.Save(file, bytes);
            return true;
        }

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
