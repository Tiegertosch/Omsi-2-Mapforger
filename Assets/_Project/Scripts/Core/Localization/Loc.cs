namespace OmsiMapEditor.Core.Localization
{
    /// <summary>
    /// The program's one <see cref="TranslationManager"/>, set once at start; interface code writes <c>Loc.T("menu.file")</c>.
    /// Without a manager, such as in a test that does not need texts, a key shows as <c>[key]</c>.
    /// </summary>
    public static class Loc
    {
        public static TranslationManager Manager { get; set; }

        public static string T(string key)
        {
            return Manager != null ? Manager.T(key) : "[" + key + "]";
        }

        public static string T(string key, params (string Name, object Value)[] arguments)
        {
            return Manager != null ? Manager.T(key, arguments) : "[" + key + "]";
        }
    }
}
