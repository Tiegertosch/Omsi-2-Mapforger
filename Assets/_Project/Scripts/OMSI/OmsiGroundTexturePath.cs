using System;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>Why a file chosen as a ground texture cannot be taken over.</summary>
    public enum OmsiGroundTexturePathProblem
    {
        None,
        NoInstallation,
        OutsideInstallation,
        UnsupportedType
    }

    /// <summary>
    /// The rule for the texture files of a [groundtex] (ADR 091): the file must lie inside the OMSI installation, because the map
    /// stores it relative to it and the file is never copied, and it must be a format the editor reads (BMP, TGA, DDS).
    /// </summary>
    public static class OmsiGroundTexturePath
    {
        /// <summary>The extensions of the picture files a ground texture can be, with the dot.</summary>
        public static readonly string[] Extensions = { ".bmp", ".tga", ".dds" };

        /// <summary>
        /// The folder the file dialog starts in: the folder of the last chosen texture when it still exists, otherwise the installation's
        /// Texture folder when that exists, otherwise null (the system's choice).
        /// </summary>
        public static string ChooseStartFolder(string lastFolder, string installationRoot, Func<string, bool> directoryExists)
        {
            if (!string.IsNullOrEmpty(lastFolder) && directoryExists(lastFolder))
                return lastFolder;
            if (string.IsNullOrEmpty(installationRoot))
                return null;
            var textureFolder = Path.Combine(installationRoot, "Texture");
            return directoryExists(textureFolder) ? textureFolder : null;
        }

        /// <summary>
        /// The path as the map stores it, relative to the installation with backslashes (for example <c>texture\gras.bmp</c>; the
        /// installation's Texture folder is written lower case as OMSI's own files do), or false with the reason.
        /// </summary>
        public static bool TryMakeRelative(string installationRoot, string chosenPath, out string relativePath, out OmsiGroundTexturePathProblem problem)
        {
            relativePath = null;
            if (string.IsNullOrWhiteSpace(installationRoot))
            {
                problem = OmsiGroundTexturePathProblem.NoInstallation;
                return false;
            }
            string full;
            try
            {
                if (string.IsNullOrWhiteSpace(chosenPath))
                {
                    problem = OmsiGroundTexturePathProblem.OutsideInstallation;
                    return false;
                }
                full = Path.GetFullPath(chosenPath.Trim());
                installationRoot = Path.GetFullPath(installationRoot);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            {
                problem = OmsiGroundTexturePathProblem.OutsideInstallation;
                return false;
            }
            var root = installationRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!OmsiRelativePath.IsSameOrInside(full, root) || string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
            {
                problem = OmsiGroundTexturePathProblem.OutsideInstallation;
                return false;
            }
            if (Array.IndexOf(Extensions, Path.GetExtension(full).ToLowerInvariant()) < 0)
            {
                problem = OmsiGroundTexturePathProblem.UnsupportedType;
                return false;
            }
            var parts = full.Substring(root.Length + 1).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length > 1 && string.Equals(parts[0], "texture", StringComparison.OrdinalIgnoreCase))
                parts[0] = "texture";
            relativePath = string.Join("\\", parts);
            problem = OmsiGroundTexturePathProblem.None;
            return true;
        }
    }
}
