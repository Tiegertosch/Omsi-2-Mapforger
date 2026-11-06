using System;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// A tile's full picture, already compressed for the graphics card, kept in the project beside the picture's JPEG
    /// (Mapforge\Imagery\tile_x_y.map.sharp, never exported). Decoding a 3,000 px JPEG and compressing it takes half a second
    /// on the main thread; reading these bytes and handing them to the graphics card takes a few milliseconds.
    /// The file says which JPEG it was made from, so a picture loaded anew makes it out of date.
    /// </summary>
    public static class OmsiSharpCache
    {
        private const string Magic = "OMSHARP1";

        /// <summary>The cache file of a tile's picture: the picture's path with .sharp in place of .jpg.</summary>
        public static string PathFor(string imageryPath)
        {
            if (imageryPath == null)
                throw new ArgumentNullException(nameof(imageryPath));
            return imageryPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? imageryPath.Substring(0, imageryPath.Length - 4) + ".sharp"
                : imageryPath + ".sharp";
        }

        /// <summary>The bytes of a cache file: a header naming the picture it belongs to, then the compressed texture data (copied once).</summary>
        public static byte[] Write(int width, int height, int mipCount, int format, string source, byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            var name = Encoding.UTF8.GetBytes(source ?? string.Empty);
            var file = new byte[Magic.Length + 4 * 5 + name.Length + 8 + data.Length];
            var at = 0;
            Array.Copy(Encoding.ASCII.GetBytes(Magic), 0, file, at, Magic.Length);
            at += Magic.Length;
            foreach (var value in new[] { width, height, mipCount, format, name.Length })
            {
                Array.Copy(BitConverter.GetBytes(value), 0, file, at, 4);
                at += 4;
            }
            Array.Copy(name, 0, file, at, name.Length);
            at += name.Length;
            Array.Copy(BitConverter.GetBytes((long)data.Length), 0, file, at, 8);
            at += 8;
            Buffer.BlockCopy(data, 0, file, at, data.Length);
            return file;
        }

        /// <summary>
        /// Reads a cache file's header. False when the bytes are not a cache file, are cut short, or were made for another
        /// picture than <paramref name="source"/>; then the texture is made from the picture as before.
        /// </summary>
        public static bool TryRead(byte[] bytes, string source, out int width, out int height, out int mipCount, out int format, out int dataOffset, out int dataLength)
        {
            width = height = mipCount = format = dataOffset = dataLength = 0;
            if (bytes == null || bytes.Length < Magic.Length + 24)
                return false;
            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic)
                        return false;
                    width = reader.ReadInt32();
                    height = reader.ReadInt32();
                    mipCount = reader.ReadInt32();
                    format = reader.ReadInt32();
                    var nameLength = reader.ReadInt32();
                    if (width <= 0 || height <= 0 || mipCount <= 0 || nameLength < 0 || nameLength > 4096 || stream.Position + nameLength + 8 > bytes.Length)
                        return false;
                    var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                    var length = reader.ReadInt64();
                    if (name != (source ?? string.Empty) || length <= 0 || stream.Position + length != bytes.Length)
                        return false;
                    dataOffset = (int)stream.Position;
                    dataLength = (int)length;
                    return true;
                }
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }
    }
}
