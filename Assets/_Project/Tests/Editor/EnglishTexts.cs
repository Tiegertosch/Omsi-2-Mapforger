using System;
using System.IO;
using OmsiMapEditor.Core.Localization;

namespace OmsiMapEditor.Tests
{
    /// <summary>
    /// Puts the English texts of the shipped language files in place for a test that reads texts the program writes (the map check),
    /// and gives the earlier state back when disposed.
    /// </summary>
    internal sealed class EnglishTexts : IDisposable
    {
        private readonly TranslationManager previous;

        private EnglishTexts()
        {
            previous = Loc.Manager;
            var folder = Directory.GetCurrentDirectory();
            for (var depth = 0; depth < 6 && folder != null; depth++, folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(Path.Combine(folder, "locales", "en-US")))
                {
                    var manager = new TranslationManager(Path.Combine(folder, "locales"));
                    manager.SetLanguage("en-US");
                    Loc.Manager = manager;
                    return;
                }
            throw new DirectoryNotFoundException("locales/en-US was not found from " + Directory.GetCurrentDirectory());
        }

        public static EnglishTexts Use()
        {
            return new EnglishTexts();
        }

        public void Dispose()
        {
            Loc.Manager = previous;
        }
    }
}
