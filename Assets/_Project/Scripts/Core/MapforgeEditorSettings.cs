using System;

namespace OmsiMapEditor.Core
{
    /// <summary>
    /// Versioned application defaults stored in Settings/Resources/editor-settings.json.
    /// Field names match the JSON keys so Unity's JsonUtility can read the file directly.
    /// </summary>
    [Serializable]
    public sealed class MapforgeEditorSettings
    {
        public const int CurrentSchemaVersion = 1;

        /// <summary>Resource name for Resources.Load, without folder or extension.</summary>
        public const string ResourceName = "editor-settings";

        public int schemaVersion = CurrentSchemaVersion;
        public ApplicationSettings application = new ApplicationSettings();
        public InterfaceSettings @interface = new InterfaceSettings();

        [Serializable]
        public sealed class ApplicationSettings
        {
            public string displayName = "OMSI MapWorks";
            public int defaultWindowWidth = 1600;
            public int defaultWindowHeight = 900;
            public int minimumWindowWidth = 1100;
            public int minimumWindowHeight = 700;
            public bool startMaximized;
        }

        [Serializable]
        public sealed class InterfaceSettings
        {
            public string defaultLocale = "en";
            public float scale = 1f;
        }

        /// <summary>Checks the loaded values and throws with a precise message when one is unusable.</summary>
        public void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new FormatException("editor-settings.json uses unsupported schemaVersion " + schemaVersion + ".");
            if (application == null || @interface == null)
                throw new FormatException("editor-settings.json must contain \"application\" and \"interface\" objects.");
            if (string.IsNullOrWhiteSpace(application.displayName))
                throw new FormatException("editor-settings.json: application.displayName must not be empty.");
            if (application.defaultWindowWidth < application.minimumWindowWidth ||
                application.defaultWindowHeight < application.minimumWindowHeight ||
                application.minimumWindowWidth <= 0 || application.minimumWindowHeight <= 0)
                throw new FormatException("editor-settings.json: window sizes must be positive and the default size must not be below the minimum size.");
            if (!(@interface.scale >= 0.5f && @interface.scale <= 3f))
                throw new FormatException("editor-settings.json: interface.scale must be between 0.5 and 3.");
        }
    }
}
