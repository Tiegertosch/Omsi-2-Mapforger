using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Where the compressed full pictures of tiles (<see cref="OmsiSharpCache"/>) are kept: a folder on this computer, not the
    /// project. They are about 7 MB a tile and can always be made again from the picture's JPEG, so they stay out of the project
    /// that is saved, copied or shared, and the folder is held below a size limit: when it is full, the copies that were
    /// used longest ago go first (ADR 062).
    /// </summary>
    public sealed class OmsiSharpStore
    {
        /// <summary>The default limit: about 270 tiles of 3,300 px.</summary>
        public const long DefaultCapBytes = 2L * 1024 * 1024 * 1024;

        private readonly object gate = new object();
        private long? total;

        public OmsiSharpStore(string root, long capBytes)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            CapBytes = Math.Max(0, capBytes);
        }

        /// <summary>The user's local application data, beside the tiles fetched from the web.</summary>
        public static string DefaultRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OMSI MapWorks", "Cache", "Sharp");

        public string Root { get; }
        public long CapBytes { get; }

        /// <summary>Names a project's copies by where its workspace lies, so two projects of one map do not share a tile.</summary>
        public static string ProjectKey(string workspacePath)
        {
            if (workspacePath == null)
                throw new ArgumentNullException(nameof(workspacePath));
            using (var hash = SHA1.Create())
            {
                var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(workspacePath).TrimEnd('\\', '/').ToLowerInvariant()));
                var text = new StringBuilder();
                for (var index = 0; index < 8; index++)
                    text.Append(bytes[index].ToString("x2"));
                return text.ToString();
            }
        }

        /// <summary>The file of a tile's copy, for a project key and the tile's file name (tile_x_y.map).</summary>
        public string FileFor(string projectKey, string tileFileName)
        {
            return Path.Combine(Root, projectKey, Path.GetFileName(tileFileName) + ".sharp");
        }

        /// <summary>A copy's bytes, or null; reading marks it as used just now, so it is the last to go.</summary>
        public byte[] Read(string file)
        {
            if (!File.Exists(file))
                return null;
            var bytes = File.ReadAllBytes(file);
            try
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // Only the order of going is lost.
            }
            return bytes;
        }

        /// <summary>Keeps a copy (next to its place first, so an interrupted write leaves no half file); the oldest go when the limit is passed.</summary>
        public void Write(string file, byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            long before = 0;
            if (File.Exists(file))
                before = new FileInfo(file).Length;
            var temporary = file + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(file))
                File.Delete(file);
            File.Move(temporary, file);
            lock (gate)
            {
                if (total.HasValue)
                    total += bytes.Length - before;
            }
            if (TotalBytes() > CapBytes)
                Prune();
        }

        public void Remove(string file)
        {
            if (!File.Exists(file))
                return;
            var length = new FileInfo(file).Length;
            File.Delete(file);
            lock (gate)
            {
                if (total.HasValue)
                    total -= length;
            }
        }

        /// <summary>Bytes of all copies; counted once, then kept up to date.</summary>
        public long TotalBytes()
        {
            lock (gate)
            {
                if (!total.HasValue)
                    total = Count();
                return total.Value;
            }
        }

        /// <summary>Whether a copy of that many bytes fits without sending another away.</summary>
        public bool HasRoom(long bytes)
        {
            return TotalBytes() + bytes <= CapBytes;
        }

        /// <summary>Deletes the copies used longest ago until the folder is at nine tenths of its limit.</summary>
        public void Prune()
        {
            if (!Directory.Exists(Root))
                return;
            var files = new List<FileInfo>();
            foreach (var path in Directory.GetFiles(Root, "*.sharp", SearchOption.AllDirectories))
                files.Add(new FileInfo(path));
            long sum = 0;
            foreach (var file in files)
                sum += file.Length;
            var goal = CapBytes / 10 * 9;
            files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            foreach (var file in files)
            {
                if (sum <= goal)
                    break;
                try
                {
                    var length = file.Length;
                    file.Delete();
                    sum -= length;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // One that is open stays; the next pass takes it.
                }
            }
            lock (gate)
                total = Math.Max(0, sum);
        }

        /// <summary>Deletes every copy; they are made again when a tile comes near the view.</summary>
        public void Clear()
        {
            if (Directory.Exists(Root))
            {
                foreach (var path in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        // Left for the next time.
                    }
                }
                foreach (var folder in Directory.GetDirectories(Root))
                {
                    try
                    {
                        Directory.Delete(folder, false);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                    {
                        // Not empty: something stayed.
                    }
                }
            }
            lock (gate)
                total = null;
        }

        private long Count()
        {
            long sum = 0;
            if (Directory.Exists(Root))
                foreach (var path in Directory.GetFiles(Root, "*.sharp", SearchOption.AllDirectories))
                    sum += new FileInfo(path).Length;
            return sum;
        }
    }
}
