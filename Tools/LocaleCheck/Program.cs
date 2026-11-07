using System;
using System.IO;
using OmsiMapEditor.Core.Localization;

/// <summary>
/// Checks the language files under a folder against the English reference, as the program's own validator does:
/// <c>LocaleCheck &lt;locales folder&gt; [--strict]</c>. Exit code 0 when there is no error (with --strict also no warning),
/// 1 when there is one, 2 when it was called wrongly.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var strict = Array.IndexOf(args, "--strict") >= 0;
        var folders = Array.FindAll(args, argument => !argument.StartsWith("--", StringComparison.Ordinal));
        if (folders.Length != 1)
        {
            Console.Error.WriteLine("Usage: LocaleCheck <locales folder> [--strict]");
            return 2;
        }
        if (!Directory.Exists(folders[0]))
        {
            Console.Error.WriteLine("The folder " + folders[0] + " does not exist.");
            return 2;
        }

        var report = LocaleValidator.Validate(folders[0]);
        foreach (var issue in report.Issues)
            Console.WriteLine(issue);
        Console.WriteLine(report.LanguageCount + " language(s), " + report.KeyCount + " key(s) in the reference, " + report.ErrorCount + " error(s), " +
                          report.WarningCount + " warning(s).");
        return report.ErrorCount > 0 || strict && report.WarningCount > 0 ? 1 : 0;
    }
}
