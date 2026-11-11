using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Editing the vehicle groups of <c>ailists.cfg</c> (ADR 095): depots with their buses, plain pools, and the tours that name them.</summary>
    public sealed partial class OmsiMapSession
    {
        private const string AiListsFile = "ailists.cfg";

        /// <summary>The map's vehicle groups for editing; an empty list when the map has no ailists.cfg yet.</summary>
        public OmsiAiListFile ReadAiLists()
        {
            var workspace = RequireWorkspace();
            return OmsiAiListFile.Parse(workspace.FileExists(AiListsFile) ? workspace.ReadAllBytes(AiListsFile) : null);
        }

        /// <summary>
        /// Applies an edit to ailists.cfg as one undoable step; a tour file that names <paramref name="renamedFrom"/> is changed to <paramref name="renamedTo"/>
        /// in the same step. Null when nothing changes.
        /// </summary>
        public OmsiTerrainChange EditAiLists(Action<OmsiAiListFile> edit, string renamedFrom = null, string renamedTo = null)
        {
            if (edit == null)
                throw new ArgumentNullException(nameof(edit));
            var workspace = RequireWorkspace();
            var before = workspace.FileExists(AiListsFile) ? workspace.ReadAllBytes(AiListsFile) : null;
            var lists = OmsiAiListFile.Parse(before);
            edit(lists);
            var after = lists.ToBytes();
            var change = new OmsiTerrainChange { Painted = true, TimetableFiles = true };
            if (before == null || !SameBytes(before, after))
                change.Files.Add(new OmsiObjectChange.FileState { RelativePath = AiListsFile, Before = before, After = after });
            if (!string.IsNullOrEmpty(renamedFrom) && !string.Equals(renamedFrom, renamedTo, StringComparison.Ordinal))
                foreach (var line in TimetableLineNames())
                {
                    var path = TimetablePath(line, ".ttl");
                    var lineBefore = workspace.ReadAllBytes(path);
                    var lineFile = OmsiLineFile.Parse(lineBefore);
                    var tours = lineFile.Tours;
                    for (var index = 0; index < tours.Count; index++)
                        if (string.Equals(tours[index].VehicleGroup, renamedFrom, StringComparison.OrdinalIgnoreCase))
                            lineFile.SetTour(index, tours[index].Number, renamedTo);
                    var lineAfter = lineFile.ToBytes();
                    if (!SameBytes(lineBefore, lineAfter))
                        change.Files.Add(new OmsiObjectChange.FileState { RelativePath = path, Before = lineBefore, After = lineAfter });
                }
            if (change.Files.Count == 0)
                return null;
            ApplyFiles(change.Files, false);
            return change;
        }

        /// <summary>The tours that name a vehicle group, as "line tour".</summary>
        public List<string> AiGroupUsers(string group)
        {
            var result = new List<string>();
            foreach (var line in TimetableLineNames())
                foreach (var tour in ReadLineFile(line).Tours)
                    if (string.Equals(tour.VehicleGroup, group, StringComparison.OrdinalIgnoreCase))
                        result.Add(line + " " + tour.Number);
            return result;
        }

        /// <summary>Takes a vehicle group out; refused while a tour still names it.</summary>
        public OmsiTerrainChange RemoveAiGroup(int group)
        {
            var groups = ReadAiLists().Groups;
            if (group < 0 || group >= groups.Count)
                throw new ArgumentOutOfRangeException(nameof(group));
            var users = AiGroupUsers(groups[group].Name);
            if (users.Count > 0)
                throw new InvalidOperationException("The tours " + string.Join(", ", users) + " name the group " + groups[group].Name + ".");
            return EditAiLists(lists => lists.RemoveGroup(group));
        }

        /// <summary>Renames a vehicle group and the tours that name it.</summary>
        public OmsiTerrainChange RenameAiGroup(int group, string name)
        {
            var groups = ReadAiLists().Groups;
            if (group < 0 || group >= groups.Count)
                throw new ArgumentOutOfRangeException(nameof(group));
            var trimmed = (name ?? string.Empty).Trim();
            return EditAiLists(lists => lists.SetName(group, trimmed), groups[group].Name, trimmed);
        }

        /// <summary>The vehicle files of the installation (<c>.bus</c> and <c>.ovh</c>) as OMSI's lists name them, relative to the OMSI folder.</summary>
        public List<string> InstalledVehicleFiles()
        {
            var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(Path.GetDirectoryName(Path.GetFullPath(GlobalConfigPath)));
            var result = new List<string>();
            if (string.IsNullOrEmpty(installation))
                return result;
            var folder = Path.Combine(installation, "Vehicles");
            if (!Directory.Exists(folder))
                return result;
            foreach (var path in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path);
                if (string.Equals(extension, ".bus", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".ovh", StringComparison.OrdinalIgnoreCase))
                    result.Add(path.Substring(installation.Length).TrimStart('\\', '/').Replace('/', '\\'));
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>The names of the depot files (<c>.hof</c>) next to a vehicle file, which a depot of that vehicle can name.</summary>
        public List<string> DepotNamesOfVehicle(string vehicleFile)
        {
            var result = new List<string>();
            var installation = OmsiObjectOutlineCatalog.InstallationRootOfMap(Path.GetDirectoryName(Path.GetFullPath(GlobalConfigPath)));
            if (string.IsNullOrEmpty(installation) || string.IsNullOrWhiteSpace(vehicleFile))
                return result;
            var folder = Path.GetDirectoryName(Path.Combine(installation, vehicleFile.Replace('\\', Path.DirectorySeparatorChar)));
            if (folder == null || !Directory.Exists(folder))
                return result;
            foreach (var path in Directory.EnumerateFiles(folder, "*.hof"))
            {
                try
                {
                    var name = OmsiRecordFile.Parse(File.ReadAllBytes(path), null).Records
                        .Where(record => string.Equals(record.Name, "name", StringComparison.OrdinalIgnoreCase)).Select(record => record.AllValues()).FirstOrDefault();
                    result.Add(name != null && name.Count > 0 && name[0].Length > 0 ? name[0] : Path.GetFileNameWithoutExtension(path));
                }
                catch (IOException)
                {
                    result.Add(Path.GetFileNameWithoutExtension(path));
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
