using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;
using UnityEngine.Rendering;

namespace OmsiMapEditor.World
{
    /// <summary>
    /// Preview images of scenery objects for the object menu (ADR 030): each object's model, read like a kind about to be
    /// placed, drawn isometrically from the front left and above into a texture with a transparent background, a few per
    /// frame. The images
    /// are kept as PNG files below the user's data folder, named after the object and its file's time, so they are drawn
    /// once.
    /// </summary>
    public sealed partial class MapObjectRenderer
    {
        public const int PreviewWidth = 128;
        public const int PreviewHeight = 96;

        private const int PreviewsPerFrame = 2;
        private const int PreviewKindsPerRead = 24;
        private const float PreviewElevation = 30f;
        private const float PreviewAzimuth = 225f;

        private readonly List<string> previewQueue = new List<string>();
        private readonly HashSet<string> previewWanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> previewImages = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private RenderTexture previewTarget;

        // Requests of one frame go to the front of the queue in their order, so the tiles shown last are drawn first.
        private int previewRequestFrame = -1;
        private int previewInsertAt;
        private CommandBuffer previewCommands;

        /// <summary>True while asked-for previews are still to be drawn.</summary>
        public bool PreviewsPending => previewQueue.Count > 0;

        /// <summary>A preview is ready; the texture is null when the object has no model to draw.</summary>
        public event Action<string, Texture2D> PreviewReady;

        /// <summary>
        /// Asks for the preview of an object; it arrives through <see cref="PreviewReady"/>, at once when kept from before.
        /// The latest requests are drawn first, such as the tiles of the tab just opened.
        /// </summary>
        public void RequestPreview(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return;
            if (previewImages.TryGetValue(assetPath, out var known))
            {
                PreviewReady?.Invoke(assetPath, known);
                return;
            }
            if (Time.frameCount != previewRequestFrame)
            {
                previewRequestFrame = Time.frameCount;
                previewInsertAt = 0;
            }
            if (!previewWanted.Add(assetPath))
            {
                // Asked again while waiting: to the front with the others of this frame.
                var waiting = previewQueue.IndexOf(assetPath);
                if (waiting >= previewInsertAt)
                {
                    previewQueue.RemoveAt(waiting);
                    previewQueue.Insert(previewInsertAt++, assetPath);
                }
                return;
            }
            var stored = LoadStoredPreview(assetPath);
            if (stored != null)
            {
                previewImages[assetPath] = stored;
                PreviewReady?.Invoke(assetPath, stored);
                return;
            }
            previewQueue.Insert(Math.Min(previewInsertAt++, previewQueue.Count), assetPath);
        }

        /// <summary>Draws queued previews whose kind is read, and reads the kinds of the next ones while nothing else loads.</summary>
        private void UpdatePreviews()
        {
            if (previewQueue.Count == 0 || installationRoot == null || template == null)
                return;
            var idle = loading == null && pending == null;
            var drawn = 0;
            List<string> missing = null;
            for (var index = 0; index < previewQueue.Count && drawn < PreviewsPerFrame; index++)
            {
                var path = previewQueue[index];
                if (kinds.TryGetValue(path, out var kind))
                {
                    Texture2D image = null;
                    try
                    {
                        image = DrawPreview(kind);
                        if (image != null)
                            StorePreview(path, image);
                    }
                    catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
                    {
                        Debug.LogWarning("Could not keep the preview of " + path + ": " + exception.Message);
                    }
                    Finish(index--, path, image);
                    drawn++;
                }
                else if (requestedKinds.Contains(path))
                {
                    // Read once without a model: nothing to draw.
                    if (idle)
                        Finish(index--, path, null);
                }
                else if (idle)
                {
                    missing = missing ?? new List<string>();
                    if (missing.Count < PreviewKindsPerRead)
                        missing.Add(path);
                }
            }
            if (missing == null || !idle)
                return;
            requestedKinds.UnionWith(missing);
            var root = installationRoot;
            loadingGeneration = generation;
            loading = Task.Run(() =>
            {
                var data = new LoadData();
                ReadKinds(missing, root, data);
                return data;
            });
        }

        private void Finish(int index, string path, Texture2D image)
        {
            previewQueue.RemoveAt(index);
            previewImages[path] = image;
            PreviewReady?.Invoke(path, image);
        }

        /// <summary>
        /// Draws a kind isometrically from the front left and above, framed by its bounds: trees as their crossed planes, other models with
        /// their opaque and then their see-through materials, on a transparent background.
        /// </summary>
        private Texture2D DrawPreview(KindData kind)
        {
            var model = kind.Model;
            Mesh mesh;
            Material[] materials;
            Material[] blended = null;
            Matrix4x4 matrix;
            Bounds bounds;
            if (model.IsTree)
            {
                var crossed = kind.CrossMaterial != null;
                mesh = crossed ? treeCross : kind.Mesh;
                materials = crossed ? new[] { kind.CrossMaterial } : kind.Materials;
                matrix = crossed ? CrossMatrix(model, Matrix4x4.identity) : InstanceMatrix(model, Matrix4x4.identity);
                bounds = LocalBounds(kind);
            }
            else
            {
                mesh = kind.Mesh;
                materials = kind.Materials;
                blended = kind.BlendedMaterials;
                matrix = Matrix4x4.identity;
                bounds = mesh != null ? mesh.bounds : default;
            }
            if (mesh == null || materials == null)
                return null;

            if (previewTarget == null)
            {
                previewTarget = new RenderTexture(PreviewWidth, PreviewHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Object previews" };
                previewTarget.Create();
                previewCommands = new CommandBuffer { name = "Object preview" };
            }

            // Isometric from the front left (the model looks along +z) and 30° above: an orthographic view down onto the
            // object, framed by its bounding sphere. A positive angle about x tilts the view down.
            var centre = bounds.center;
            var radius = Mathf.Max(0.25f, bounds.extents.magnitude);
            var rotation = Quaternion.Euler(PreviewElevation, PreviewAzimuth, 0f);
            var direction = rotation * Vector3.forward;
            var distance = radius * 3f;
            var position = centre - direction * distance;
            var view = Matrix4x4.TRS(position, rotation, new Vector3(1f, 1f, -1f)).inverse;
            var aspect = (float)PreviewWidth / PreviewHeight;
            var halfHeight = FramedHalfHeight(bounds, rotation, aspect);
            var projection = Matrix4x4.Ortho(-halfHeight * aspect, halfHeight * aspect, -halfHeight, halfHeight, distance - radius * 1.5f, distance + radius * 1.5f);

            var commands = previewCommands;
            commands.Clear();
            commands.SetRenderTarget(previewTarget);
            commands.ClearRenderTarget(true, true, new Color(0f, 0f, 0f, 0f));
            commands.SetViewProjectionMatrices(view, projection);
            var instance = new[] { matrix };
            foreach (var set in new[] { materials, blended })
            {
                if (set == null)
                    continue;
                for (var submesh = 0; submesh < set.Length && submesh < mesh.subMeshCount; submesh++)
                    if (set[submesh] != null)
                        commands.DrawMeshInstanced(mesh, submesh, set[submesh], 0, instance);
            }
            Graphics.ExecuteCommandBuffer(commands);

            var previous = RenderTexture.active;
            RenderTexture.active = previewTarget;
            var image = new Texture2D(PreviewWidth, PreviewHeight, TextureFormat.RGBA32, false) { name = kind.AssetPath, wrapMode = TextureWrapMode.Clamp };
            image.ReadPixels(new Rect(0, 0, PreviewWidth, PreviewHeight), 0, 0, false);
            image.Apply(false, false);
            RenderTexture.active = previous;
            return image;
        }

        /// <summary>
        /// Half the height of an orthographic view that holds the corners of the bounds seen along the rotation, with a small
        /// margin, so flat objects fill the image as well as tall ones.
        /// </summary>
        private static float FramedHalfHeight(Bounds bounds, Quaternion rotation, float aspect)
        {
            var inverse = Quaternion.Inverse(rotation);
            float halfWidth = 0f, halfHeight = 0f;
            for (var corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -bounds.extents.x : bounds.extents.x, (corner & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                    (corner & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
                var seen = inverse * offset;
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(seen.x));
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(seen.y));
            }
            return Mathf.Max(0.1f, Mathf.Max(halfHeight, halfWidth / aspect) * 1.06f);
        }

        /// <summary>Where the preview of an object is kept: named after the installation, the object and the time of its file.</summary>
        private string PreviewFile(string assetPath)
        {
            try
            {
                var scoPath = OmsiRelativePath.Resolve(installationRoot, assetPath);
                var stamp = File.Exists(scoPath) ? File.GetLastWriteTimeUtc(scoPath).Ticks : 0L;
                using (var md5 = MD5.Create())
                {
                    var key = Encoding.UTF8.GetBytes((installationRoot + "|" + assetPath).ToLowerInvariant() + "|" + stamp + "|2");
                    var name = BitConverter.ToString(md5.ComputeHash(key)).Replace("-", string.Empty).ToLowerInvariant();
                    return Path.Combine(Application.persistentDataPath, "ObjectPreviews", name + ".png");
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private Texture2D LoadStoredPreview(string assetPath)
        {
            if (installationRoot == null)
                return null;
            var file = PreviewFile(assetPath);
            if (file == null || !File.Exists(file))
                return null;
            try
            {
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = assetPath, wrapMode = TextureWrapMode.Clamp };
                if (image.LoadImage(File.ReadAllBytes(file), false))
                    return image;
                Destroy(image);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Could not read the kept preview of " + assetPath + ": " + exception.Message);
            }
            return null;
        }

        private void StorePreview(string assetPath, Texture2D image)
        {
            var file = PreviewFile(assetPath);
            if (file == null)
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, image.EncodeToPNG());
        }

        private void ReleasePreviews()
        {
            foreach (var image in previewImages.Values)
                if (image != null)
                    Destroy(image);
            previewImages.Clear();
            previewQueue.Clear();
            previewWanted.Clear();
            if (previewTarget != null)
            {
                previewTarget.Release();
                Destroy(previewTarget);
                previewTarget = null;
            }
            previewCommands?.Release();
            previewCommands = null;
        }
    }
}
