using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Unity keeps PlayerPrefs under the company and product name, so renaming the program from "OMSI Map Editor" to
    /// "OMSI MapWorks" would leave recent files, favourites and DLC folders behind. Copies them over once, before the
    /// first scene reads them.
    /// </summary>
    internal static class PreferencesMigration
    {
        private const string MigratedKey = "Mapforge.PreferencesFromOmsiMapEditor";
        private const string EarlierName = @"OMSI Map Editor\OMSI Map Editor";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CopyFromEarlierName()
        {
            if (PlayerPrefs.HasKey(MigratedKey))
                return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var root = Application.isEditor ? @"Software\Unity\UnityEditor\" : @"Software\";
            var target = root + Application.companyName + @"\" + Application.productName;
            if (!string.Equals(target, root + EarlierName, StringComparison.OrdinalIgnoreCase))
            {
                var error = CopyTree(root + EarlierName, target);
                if (error == FileNotFound)
                    Debug.Log("No preferences of OMSI Map Editor to take over.");
                else if (error != 0)
                {
                    Debug.LogWarning("Could not take over the preferences of OMSI Map Editor, Windows error " + error + ".");
                    return;
                }
                else
                    Debug.Log("Took over the preferences of OMSI Map Editor.");
            }
#endif
            PlayerPrefs.SetInt(MigratedKey, 1);
            PlayerPrefs.Save();
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int FileNotFound = 2;
        private const int KeyRead = 0x20019;
        private const int KeyAllAccess = 0xF003F;
        private static readonly IntPtr CurrentUser = new IntPtr(unchecked((int)0x80000001));

        private static int CopyTree(string sourcePath, string targetPath)
        {
            var error = RegOpenKeyExW(CurrentUser, sourcePath, 0, KeyRead, out var source);
            if (error != 0)
                return error;
            try
            {
                error = RegCreateKeyExW(CurrentUser, targetPath, 0, null, 0, KeyAllAccess, IntPtr.Zero, out var target, out _);
                if (error != 0)
                    return error;
                try
                {
                    return RegCopyTreeW(source, null, target);
                }
                finally
                {
                    RegCloseKey(target);
                }
            }
            finally
            {
                RegCloseKey(source);
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int RegOpenKeyExW(IntPtr key, string subKey, int options, int access, out IntPtr result);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int RegCreateKeyExW(IntPtr key, string subKey, int reserved, string keyClass, int options, int access,
            IntPtr security, out IntPtr result, out int disposition);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int RegCopyTreeW(IntPtr source, string subKey, IntPtr target);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        private static extern int RegCloseKey(IntPtr key);
#endif
    }
}
