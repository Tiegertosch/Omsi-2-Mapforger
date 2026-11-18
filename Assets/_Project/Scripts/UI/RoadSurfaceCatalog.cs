using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The surfaces the road type window offers (the textures of the installation's spline profiles, ADR 049) and their small previews, which the
    /// cross-section and the surface picker both draw. The list and the previews are read on worker threads; <see cref="Pump"/>, called from the
    /// interface thread every moment, turns what was read into textures and reports it by <see cref="PreviewsChanged"/>.
    /// </summary>
    internal sealed class RoadSurfaceCatalog
    {
        private const int PreviewSize = 64;

        private readonly Dictionary<string, Texture2D> previews = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> previewRequested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object previewLock = new object();
        private readonly List<KeyValuePair<string, OmsiImage>> previewsDone = new List<KeyValuePair<string, OmsiImage>>();
        private HashSet<string> excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Task<List<string>> list;

        /// <summary>New previews were turned into textures, so what draws them should draw again.</summary>
        public event Action PreviewsChanged;

        public string InstallationRoot { get; private set; }

        /// <summary>The list was asked for and is read (or failed); false before it was asked for.</summary>
        public bool Started => list != null;

        public bool Ready => list != null && list.IsCompleted;

        /// <summary>The texture paths of the installation, most used first; empty while reading or when reading failed.</summary>
        public IReadOnlyList<string> Paths => list != null && list.IsCompleted && !list.IsFaulted ? list.Result : (IReadOnlyList<string>)Array.Empty<string>();

        public bool Failed => list != null && list.IsFaulted;

        public void SetInstallation(string root)
        {
            if (string.Equals(root, InstallationRoot, StringComparison.OrdinalIgnoreCase))
                return;
            InstallationRoot = root;
            list = null;
            foreach (var texture in previews.Values)
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            previews.Clear();
            lock (previewLock)
            {
                previewRequested.Clear();
                previewsDone.Clear();
            }
        }

        /// <summary>The folders of <c>Splines</c> the user left out (ADR 084); a change reads the list again the next time it is asked for.</summary>
        public void SetExcludedFolders(ICollection<string> folders)
        {
            var next = new HashSet<string>(folders ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (next.SetEquals(excludedFolders))
                return;
            excludedFolders = next;
            list = null;
        }

        /// <summary>Starts reading the list of surfaces, unless it is read or being read.</summary>
        public void Start()
        {
            if (list != null || InstallationRoot == null)
                return;
            var root = InstallationRoot;
            var excluded = excludedFolders;
            list = Task.Run(() => OmsiRoadTypeStore.SurfaceTextures(root, excluded));
        }

        /// <summary>The preview of a texture, or null while it is read on a worker thread or when it cannot be read.</summary>
        public Texture2D Preview(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (previews.TryGetValue(path, out var texture))
                return texture;
            if (InstallationRoot == null)
                return null;
            lock (previewLock)
                if (!previewRequested.Add(path))
                    return null;
            var root = InstallationRoot;
            Task.Run(() =>
            {
                OmsiImage image = null;
                try
                {
                    var file = OmsiRelativePath.Resolve(root, path);
                    var bytes = File.Exists(file) ? File.ReadAllBytes(file) : File.ReadAllBytes(Path.ChangeExtension(file, ".dds"));
                    if (OmsiImageReader.TryDecode(bytes, out var decoded))
                        image = decoded.Reduce(PreviewSize);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is ArgumentException)
                {
                    // Without a preview the tile stays plain.
                }
                lock (previewLock)
                    previewsDone.Add(new KeyValuePair<string, OmsiImage>(path, image));
            });
            return null;
        }

        /// <summary>Turns previews read on the worker threads into textures. True when there were some.</summary>
        public bool Pump()
        {
            List<KeyValuePair<string, OmsiImage>> done;
            lock (previewLock)
            {
                if (previewsDone.Count == 0)
                    return false;
                done = new List<KeyValuePair<string, OmsiImage>>(previewsDone);
                previewsDone.Clear();
            }
            foreach (var pair in done)
            {
                Texture2D texture = null;
                var image = pair.Value;
                if (image != null)
                {
                    var rgba = new byte[image.Width * image.Height * 4];
                    for (var row = 0; row < image.Height; row++)
                        for (var column = 0; column < image.Width; column++)
                        {
                            // OMSI images run from the top row down, Unity's from the bottom up.
                            var from = (row * image.Width + column) * image.Channels;
                            var to = ((image.Height - 1 - row) * image.Width + column) * 4;
                            for (var channel = 0; channel < 4; channel++)
                                rgba[to + channel] = image.Channels == 4 ? image.Pixels[from + channel] : channel == 3 ? (byte)255 : image.Pixels[from];
                        }
                    texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
                    texture.LoadRawTextureData(rgba);
                    texture.Apply(false, true);
                }
                previews[pair.Key] = texture;
            }
            PreviewsChanged?.Invoke();
            return true;
        }
    }
}
