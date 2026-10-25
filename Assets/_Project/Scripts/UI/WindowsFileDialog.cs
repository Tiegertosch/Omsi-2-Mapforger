using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OmsiMapEditor.UI
{
    /// <summary>Native Windows open and save file pickers for the desktop player and the Windows editor.</summary>
    internal static class WindowsFileDialog
    {
        public const string GlobalConfigFilter = "OMSI map configuration (global.cfg)\0global.cfg\0All files\0*.*\0";
        public const string ProjectFilter = "MapWorks project (*.omproj)\0*.omproj\0";
        public const string ProjectExtension = "omproj";
        public const string PngFilter = "PNG picture (*.png)\0*.png\0All files\0*.*\0";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int OverwritePrompt = 0x00000002;
        private const int NoChangeDirectory = 0x00000008;
        private const int PathMustExist = 0x00000800;
        private const int FileMustExist = 0x00001000;
        private const int ExplorerStyle = 0x00080000;
        private const int MaxFileLength = 32768;
        private const int MaxTitleLength = 1024;

        [StructLayout(LayoutKind.Sequential)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public IntPtr filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDirectory;
            public IntPtr title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public IntPtr defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reservedValue;
            public int flagsEx;
        }

        [DllImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName(IntPtr openFileName);

        [DllImport("comdlg32.dll", EntryPoint = "GetSaveFileNameW", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileName(IntPtr openFileName);

        [DllImport("comdlg32.dll", EntryPoint = "CommDlgExtendedError", ExactSpelling = true)]
        private static extern uint GetDialogError();

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", ExactSpelling = true)]
        private static extern IntPtr GetForegroundWindow();
#endif

        /// <summary>Shows an open dialog. Returns the selected path, or an empty string when cancelled.</summary>
        /// <param name="filter">Pairs of display name and pattern, each terminated by \0.</param>
        public static string OpenFile(string title, string filter)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return Show(false, title, filter, null, null);
#else
            throw new PlatformNotSupportedException("File dialogs are currently available in the Windows player only.");
#endif
        }

        /// <summary>Shows a save dialog with overwrite confirmation. Returns the path, or an empty string when cancelled.</summary>
        public static string SaveFile(string title, string filter, string defaultExtension, string suggestedFileName)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return Show(true, title, filter, defaultExtension, suggestedFileName);
#else
            throw new PlatformNotSupportedException("File dialogs are currently available in the Windows player only.");
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private static string Show(bool save, string title, string filter, string defaultExtension, string suggestedFileName)
        {
            var filterPointer = IntPtr.Zero;
            var filePointer = IntPtr.Zero;
            var fileTitlePointer = IntPtr.Zero;
            var titlePointer = IntPtr.Zero;
            var extensionPointer = IntPtr.Zero;
            var structurePointer = IntPtr.Zero;

            try
            {
                // The filter list is terminated by an additional empty string.
                filterPointer = AllocateZeroedCopy(filter + "\0");
                filePointer = AllocateZeroedCopy(save ? suggestedFileName : null, MaxFileLength);
                fileTitlePointer = AllocateZeroedCopy(null, MaxTitleLength);
                titlePointer = Marshal.StringToHGlobalUni(title ?? string.Empty);
                if (!string.IsNullOrEmpty(defaultExtension))
                    extensionPointer = Marshal.StringToHGlobalUni(defaultExtension);

                var flags = ExplorerStyle | PathMustExist | NoChangeDirectory | (save ? OverwritePrompt : FileMustExist);
                var dialog = new OpenFileName
                {
                    structSize = Marshal.SizeOf(typeof(OpenFileName)),
                    owner = GetForegroundWindow(),
                    filter = filterPointer,
                    filterIndex = 1,
                    file = filePointer,
                    maxFile = MaxFileLength,
                    fileTitle = fileTitlePointer,
                    maxFileTitle = MaxTitleLength,
                    title = titlePointer,
                    defaultExtension = extensionPointer,
                    flags = flags
                };

                structurePointer = Marshal.AllocHGlobal(dialog.structSize);
                Marshal.StructureToPtr(dialog, structurePointer, false);
                var succeeded = save ? GetSaveFileName(structurePointer) : GetOpenFileName(structurePointer);
                if (succeeded)
                    return Marshal.PtrToStringUni(filePointer);

                var error = GetDialogError();
                if (error != 0)
                    throw new InvalidOperationException("The Windows file dialog failed (CommDlgExtendedError=0x" + error.ToString("X8") + ").");
                return string.Empty;
            }
            finally
            {
                if (structurePointer != IntPtr.Zero) Marshal.FreeHGlobal(structurePointer);
                if (extensionPointer != IntPtr.Zero) Marshal.FreeHGlobal(extensionPointer);
                if (titlePointer != IntPtr.Zero) Marshal.FreeHGlobal(titlePointer);
                if (fileTitlePointer != IntPtr.Zero) Marshal.FreeHGlobal(fileTitlePointer);
                if (filePointer != IntPtr.Zero) Marshal.FreeHGlobal(filePointer);
                if (filterPointer != IntPtr.Zero) Marshal.FreeHGlobal(filterPointer);
            }
        }

        /// <summary>Allocates a zero-filled UTF-16 buffer and copies the text into it, truncated to leave a terminator.</summary>
        private static IntPtr AllocateZeroedCopy(string text, int minimumCharacters = 0)
        {
            var textBytes = Encoding.Unicode.GetBytes(text ?? string.Empty);
            var byteCount = Math.Max(minimumCharacters * sizeof(char), textBytes.Length + sizeof(char));
            var pointer = Marshal.AllocHGlobal(byteCount);
            Marshal.Copy(new byte[byteCount], 0, pointer, byteCount);
            Marshal.Copy(textBytes, 0, pointer, Math.Min(textBytes.Length, byteCount - sizeof(char)));
            return pointer;
        }
#endif
    }
}
