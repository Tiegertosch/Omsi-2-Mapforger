using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// The user's own pictures on tiles (ADR 041, stage 2): a scan, a screenshot or an old plan laid over a rectangle of
    /// tiles and lined up by eye, as openMapper does. A tile covered by an own picture shows its piece instead of the
    /// picture from the air; the picture from the air comes back when the own picture goes.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>The largest edge an own picture is kept at in memory, halved until it fits.</summary>
        private const int OwnPictureSize = 4096;

        private readonly Dictionary<string, OmsiImage> ownPictureImages = new Dictionary<string, OmsiImage>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> ownPictureTiles = new HashSet<long>();
        private List<OmsiOwnPicture> ownPictures = new List<OmsiOwnPicture>();
        private string selectedPictureId;
        private float pictureChangedAt = -1f;
        private int pictureCutGeneration;

        /// <summary>The project's own pictures after a map was opened: read, decoded and cut onto their tiles.</summary>
        private void LoadOwnPictures()
        {
            ownPictureImages.Clear();
            ownPictureTiles.Clear();
            selectedPictureId = null;
            pictureChangedAt = -1f;
            pictureCutGeneration++;
            ownPictures = session?.Workspace != null ? session.ReadOwnPictures() : new List<OmsiOwnPicture>();
            ShowOwnPictures();
            if (ownPictures.Count > 0)
                CutOwnPictures(true);
        }

        /// <summary>Whether a tile shows an own picture, so a picture from the air is kept but not laid on it.</summary>
        private bool HasOwnPicture(OmsiTileSummary tile)
        {
            return tile != null && OmsiOwnPictures.On(ownPictures, tile.X, tile.Y) != null;
        }

        private void ShowOwnPictures()
        {
            if (satelliteWindow == null || currentSummary == null)
                return;
            var selected = ownPictures.Find(item => item.Id == selectedPictureId);
            satelliteWindow.ShowPictures(ownPictures, selected, currentSummary.TileSizeMeters, selected != null ? ownPictureImages.TryGetValue(selected.Id, out var image) ? image : null : null);
        }

        /// <summary>Asks for a picture file and lays it over a rectangle of tiles, stretched to it.</summary>
        private void LoadOwnPicture(int west, int south, int east, int north, string path = null)
        {
            if (session?.Workspace == null || currentSummary == null)
                return;
            path = path ?? WindowsFileDialog.OpenFile("A picture for the tiles", WindowsFileDialog.PictureFilter);
            if (string.IsNullOrEmpty(path))
                return;
            var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (Array.IndexOf(OmsiOwnPictures.Extensions, extension) < 0)
            {
                SetStatus("Pictures can be JPEG, PNG, BMP or TGA files.");
                return;
            }
            OmsiImage image;
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
                image = DecodeOwnPicture(bytes);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
            {
                Debug.LogException(exception);
                SetStatus("The picture could not be read: " + exception.Message);
                return;
            }
            if (image == null)
            {
                SetStatus("The picture could not be read: " + Path.GetFileName(path));
                return;
            }
            var picture = new OmsiOwnPicture
            {
                Id = OmsiOwnPictures.NewId(ownPictures, Math.Min(west, east), Math.Min(south, north)), West = Math.Min(west, east), South = Math.Min(south, north),
                East = Math.Max(west, east), North = Math.Max(south, north)
            };
            picture.File = picture.Id + "." + extension;
            var touched = new List<KeyValuePair<int, int>>(picture.Tiles());
            try
            {
                session.SaveOwnPictureFile(picture.File, bytes);
                // A tile shows one picture: one the new picture covers entirely goes.
                foreach (var old in ownPictures.FindAll(item => AllCovered(item, picture)))
                {
                    session.DeleteOwnPictureFile(old.File);
                    ownPictureImages.Remove(old.Id);
                    ownPictures.Remove(old);
                }
                ownPictures.Add(picture);
                session.SaveOwnPictures(ownPictures);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The picture could not be kept in the project: " + exception.Message);
                return;
            }
            ownPictureImages[picture.Id] = image;
            selectedPictureId = picture.Id;
            ShowOwnPictures();
            CutOwnPictures(true, touched);
            SetStatus("The picture " + Path.GetFileName(path) + " lies on " + touched.Count + " tile(s): line it up under Line up the picture.");
        }

        private static bool AllCovered(OmsiOwnPicture old, OmsiOwnPicture by)
        {
            foreach (var tile in old.Tiles())
                if (!by.Covers(tile.Key, tile.Value))
                    return false;
            return true;
        }

        /// <summary>A picture file as RGBA rows from the top: JPEG and PNG through Unity, BMP and TGA by the editor's reader.</summary>
        private static OmsiImage DecodeOwnPicture(byte[] bytes)
        {
            if (OmsiImageReader.TryDecode(bytes, out var image))
                return Rgba(image).Reduce(OwnPictureSize);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes, false))
                    return null;
                var pixels = texture.GetPixels32();
                int width = texture.width, height = texture.height;
                var rgba = new byte[width * height * 4];
                for (var y = 0; y < height; y++)
                {
                    var from = (height - 1 - y) * width;
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = pixels[from + x];
                        var index = (y * width + x) * 4;
                        rgba[index] = pixel.r;
                        rgba[index + 1] = pixel.g;
                        rgba[index + 2] = pixel.b;
                        rgba[index + 3] = pixel.a;
                    }
                }
                return new OmsiImage(width, height, 4, rgba).Reduce(OwnPictureSize);
            }
            finally
            {
                Destroy(texture);
            }
        }

        private static OmsiImage Rgba(OmsiImage image)
        {
            if (image.Channels == 4)
                return image;
            var rgba = new byte[image.Width * image.Height * 4];
            for (var index = 0; index < image.Width * image.Height; index++)
            {
                rgba[index * 4] = rgba[index * 4 + 1] = rgba[index * 4 + 2] = image.Pixels[index];
                rgba[index * 4 + 3] = 255;
            }
            return new OmsiImage(image.Width, image.Height, 4, rgba);
        }

        /// <summary>The selected picture's fit changed: the pieces are cut again once the changes rest a moment.</summary>
        private void ChangePictureFit(OmsiOwnPicture fit)
        {
            var picture = ownPictures.Find(item => item.Id == fit?.Id);
            if (picture == null)
                return;
            picture.Rotation = fit.Rotation;
            picture.MirrorX = fit.MirrorX;
            picture.MirrorY = fit.MirrorY;
            picture.OffsetX = fit.OffsetX;
            picture.OffsetY = fit.OffsetY;
            picture.Scale = Math.Max(0.05, Math.Min(20.0, fit.Scale));
            pictureChangedAt = Time.realtimeSinceStartup;
            pictureCutGeneration++;
            ShowOwnPictures();
        }

        private void RemoveOwnPicture(string id)
        {
            var picture = ownPictures.Find(item => item.Id == id);
            if (picture == null || session?.Workspace == null)
                return;
            ownPictures.Remove(picture);
            ownPictureImages.Remove(picture.Id);
            try
            {
                session.DeleteOwnPictureFile(picture.File);
                session.SaveOwnPictures(ownPictures);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The picture could not be taken out of the project: " + exception.Message);
            }
            if (selectedPictureId == id)
                selectedPictureId = null;
            ShowOwnPictures();
            CutOwnPictures(true, new List<KeyValuePair<int, int>>(picture.Tiles()));
            SetStatus("Took the picture " + picture.File + " away.");
        }

        /// <summary>Saves a fit that came to rest and cuts the pieces again; called each frame.</summary>
        private void TickOwnPictures()
        {
            if (pictureChangedAt < 0f || Time.realtimeSinceStartup - pictureChangedAt < 0.25f)
                return;
            pictureChangedAt = -1f;
            try
            {
                session?.SaveOwnPictures(ownPictures);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogException(exception);
                SetStatus("The picture's fit could not be saved: " + exception.Message);
            }
            var selected = ownPictures.Find(item => item.Id == selectedPictureId);
            if (selected != null)
                CutOwnPictures(false, new List<KeyValuePair<int, int>>(selected.Tiles()));
        }

        /// <summary>
        /// Cuts the pieces of the tiles given (or of all pictures' tiles) in the background and lays them on the ground;
        /// a tile no picture covers any more gets its picture from the air back. Pictures not decoded yet are read first.
        /// </summary>
        private void CutOwnPictures(bool decode, List<KeyValuePair<int, int>> tiles = null)
        {
            if (currentSummary == null || groundRenderer == null || session == null)
                return;
            if (tiles == null)
            {
                tiles = new List<KeyValuePair<int, int>>();
                foreach (var picture in ownPictures)
                    tiles.AddRange(picture.Tiles());
            }
            if (decode)
                foreach (var picture in ownPictures)
                    if (!ownPictureImages.ContainsKey(picture.Id))
                    {
                        var bytes = session.ReadOwnPictureFile(picture.File);
                        OmsiImage image = null;
                        try
                        {
                            image = bytes != null ? DecodeOwnPicture(bytes) : null;
                        }
                        catch (InvalidDataException exception)
                        {
                            Debug.LogWarning(picture.File + ": " + exception.Message);
                        }
                        if (image != null)
                            ownPictureImages[picture.Id] = image;
                        else
                            Debug.LogWarning("The picture " + picture.File + " is missing from the project or cannot be read.");
                    }
            var tileSize = currentSummary.TileSizeMeters;
            var jobs = new List<KeyValuePair<KeyValuePair<int, int>, KeyValuePair<OmsiOwnPicture, OmsiImage>>>();
            var bare = new List<KeyValuePair<int, int>>();
            var seen = new HashSet<long>();
            foreach (var tile in tiles)
            {
                var key = ((long)tile.Key << 32) | (uint)tile.Value;
                if (!seen.Add(key) || currentSummary.Tiles.Find(item => item.X == tile.Key && item.Y == tile.Value && item.IsAvailable) == null)
                    continue;
                var picture = OmsiOwnPictures.On(ownPictures, tile.Key, tile.Value);
                if (picture != null && ownPictureImages.TryGetValue(picture.Id, out var image))
                    jobs.Add(new KeyValuePair<KeyValuePair<int, int>, KeyValuePair<OmsiOwnPicture, OmsiImage>>(tile,
                        new KeyValuePair<OmsiOwnPicture, OmsiImage>(picture.Clone(), image)));
                else if (ownPictureTiles.Contains(key))
                    bare.Add(tile);
            }
            foreach (var tile in bare)
                RestoreAerialPicture(tile.Key, tile.Value);
            if (jobs.Count == 0)
                return;
            var generation = ++pictureCutGeneration;
            Task.Run(() =>
            {
                var pieces = new List<KeyValuePair<KeyValuePair<int, int>, OmsiImage>>();
                foreach (var job in jobs)
                {
                    var picture = job.Value.Key;
                    var size = Math.Min(ImageryPictureSize, picture.PieceSize(tileSize, job.Value.Value.Width));
                    pieces.Add(new KeyValuePair<KeyValuePair<int, int>, OmsiImage>(job.Key, picture.Cut(job.Value.Value, job.Key.Key, job.Key.Value, tileSize, size)));
                }
                return pieces;
            }).ContinueWith(task => aerialMainThread.Enqueue(() =>
            {
                if (generation != pictureCutGeneration || groundRenderer == null)
                    return;
                if (task.IsFaulted)
                {
                    Debug.LogException(task.Exception);
                    SetStatus("The picture could not be cut onto its tiles: " + task.Exception?.GetBaseException().Message);
                    return;
                }
                foreach (var piece in task.Result)
                    LayOwnPiece(piece.Key.Key, piece.Key.Value, piece.Value);
            }));
        }

        /// <summary>A piece as a texture: its rows from the south, as the ground's UVs; compressed with its transparency.</summary>
        private static Texture2D OwnPieceTexture(OmsiImage piece, string name)
        {
            var size = piece.Width;
            var rows = new byte[piece.Pixels.Length];
            var stride = size * 4;
            for (var row = 0; row < size; row++)
                Array.Copy(piece.Pixels, row * stride, rows, (size - 1 - row) * stride, stride);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = name
            };
            texture.SetPixelData(rows, 0);
            texture.Apply(true, false);
            texture.Compress(true);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// Whether a tile's own picture has more detail than its piece shows: then, zoomed in, the piece is cut again at
        /// the picture's own detail.
        /// </summary>
        private bool OwnPictureIsSharper(OmsiTileSummary tile)
        {
            var picture = OmsiOwnPictures.On(ownPictures, tile.X, tile.Y);
            return picture != null && ownPictureImages.TryGetValue(picture.Id, out var image) &&
                   picture.PieceSize(currentSummary.TileSizeMeters, image.Width) > ImageryPictureSize;
        }

        private readonly HashSet<long> ownSharpCutting = new HashSet<long>();

        /// <summary>Cuts a tile's piece at the picture's own detail in the background and shows it in place of the smaller piece.</summary>
        private void CutSharpOwnPiece(OmsiTileSummary tile)
        {
            var key = ((long)tile.X << 32) | (uint)tile.Y;
            var picture = OmsiOwnPictures.On(ownPictures, tile.X, tile.Y)?.Clone();
            if (picture == null || !ownPictureImages.TryGetValue(picture.Id, out var image) || !ownSharpCutting.Add(key))
                return;
            var tileSize = currentSummary.TileSizeMeters;
            var generation = pictureCutGeneration;
            int x = tile.X, y = tile.Y;
            Task.Run(() => picture.Cut(image, x, y, tileSize, picture.PieceSize(tileSize, image.Width))).ContinueWith(task => aerialMainThread.Enqueue(() =>
            {
                ownSharpCutting.Remove(key);
                // The picture was moved or turned meanwhile: its smaller pieces are cut again, this one is out of date.
                if (generation != pictureCutGeneration || groundRenderer == null || task.IsFaulted)
                    return;
                groundRenderer.SetSharpImagery(x, y, OwnPieceTexture(task.Result, "Sharp own picture " + x + ", " + y));
            }));
        }

        private void LayOwnPiece(int tileX, int tileY, OmsiImage piece)
        {
            var texture = OwnPieceTexture(piece, "Own picture " + tileX + ", " + tileY);
            groundRenderer.SetImagery(tileX, tileY, texture);
            var key = ((long)tileX << 32) | (uint)tileY;
            ownPictureTiles.Add(key);
            // The pictures' terms are the user's own: no attribution line for them.
            imagerySources.Remove(key);
            UpdateImageryAttribution();
        }

        /// <summary>A tile no own picture covers any more: its kept picture from the air again, or nothing.</summary>
        private void RestoreAerialPicture(int tileX, int tileY)
        {
            var key = ((long)tileX << 32) | (uint)tileY;
            ownPictureTiles.Remove(key);
            groundRenderer.SetImagery(tileX, tileY, null);
            var tile = currentSummary.Tiles.Find(item => item.X == tileX && item.Y == tileY);
            if (tile != null && session?.Workspace != null && session.Workspace.IsModified(OmsiMapSession.ImageryPath(tile)))
                storedImagery.Enqueue(tile);
        }
    }
}
