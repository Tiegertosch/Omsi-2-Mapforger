using System;

namespace OmsiMapEditor.Core
{
    /// <summary>Versioned project metadata that points to an external OMSI map source.</summary>
    [Serializable]
    public sealed class MapforgeProject
    {
        public const int CurrentFormatVersion = 1;

        public int formatVersion = CurrentFormatVersion;
        public string projectName;
        public string globalConfigPath;
    }
}
