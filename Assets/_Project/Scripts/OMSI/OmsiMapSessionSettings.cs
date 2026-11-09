using System;
using System.Linq;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// The map's own settings in global.cfg, changed after the map was made (ADR 076): display name and description, the side traffic keeps
    /// to, rail, years, money system, ticket pack, ground texture, seasons and how busy cars and passengers are.
    /// </summary>
    public sealed partial class OmsiMapSession
    {
        /// <summary>
        /// Writes new settings into global.cfg of the project copy, keeping everything they do not name, and gives <paramref name="summary"/> the
        /// new name and description. <see cref="OmsiNewMapOptions.CarTraffic"/> and <see cref="OmsiNewMapOptions.PassengerTraffic"/> multiply the
        /// daily curves as they are now; 1 leaves them. Size and ground height are not used. Returns false when the file already said so.
        /// </summary>
        public bool UpdateMapSettings(OmsiMapSummary summary, string friendlyName, OmsiNewMapOptions options)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(friendlyName))
                throw new ArgumentException("A display name for the map is required.", nameof(friendlyName));
            var problem = options.Check();
            if (problem != null)
                throw new ArgumentException(problem, nameof(options));
            if (friendlyName.IndexOf('[') >= 0 && friendlyName.IndexOf(']') > friendlyName.IndexOf('['))
                throw new ArgumentException("Names and texts must not contain a section name in brackets.", nameof(friendlyName));

            var workspace = RequireWorkspace();
            var before = workspace.ReadAllBytes(GlobalConfigFileName);
            var config = OmsiRecordFile.Parse(before, OmsiGlobalConfigLayouts.Instance);
            var description = (options.Description ?? string.Empty).Trim();
            var changesDescription = !string.Equals(Normalize(description), Normalize(summary.Description), StringComparison.Ordinal);
            OmsiNewMap.EditGlobalConfig(config, friendlyName.Trim(), changesDescription ? description : null, options);
            var after = config.ToBytes();
            var same = before.SequenceEqual(after);
            if (!same)
                workspace.Save(GlobalConfigFileName, after);
            summary.FriendlyName = friendlyName.Trim();
            summary.Description = description;
            return !same;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Trim().Replace("\r\n", "\n");
        }
    }
}
