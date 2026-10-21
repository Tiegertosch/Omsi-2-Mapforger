// SPDX-License-Identifier: AGPL-3.0-or-later
// The vertex unscrambling below is ported from openOMSI (crates/omsi-o3d/src/lib.rs, commit
// 23dd0a9e909c57edc22f6c4294c690e739667c9b), Copyright the openOMSI contributors, licensed under the
// GNU Affero General Public License v3.0. See THIRD_PARTY_NOTICES.md and ADR 010.
using System;
using System.Collections.Generic;
using System.IO;

namespace OmsiMapEditor.OMSI
{
    /// <summary>A mesh vertex in the file's Direct3D frame: X right, Y up, Z forward.</summary>
    public struct OmsiMeshVertex
    {
        public float X, Y, Z;
        public float NormalX, NormalY, NormalZ;
        public float U, V;
    }

    /// <summary>A material of an .o3d mesh: its diffuse colour and the texture file name, empty for none.</summary>
    public sealed class OmsiMeshMaterial
    {
        public float DiffuseRed { get; internal set; }
        public float DiffuseGreen { get; internal set; }
        public float DiffuseBlue { get; internal set; }
        public float DiffuseAlpha { get; internal set; }

        /// <summary>File name in the <c>texture</c> folder next to the scenery object, as stored in the mesh.</summary>
        public string Texture { get; internal set; }
    }

    /// <summary>Vertices, triangles and materials of a binary OMSI .o3d mesh. The pivot matrix and bones are skipped.</summary>
    public sealed class OmsiO3dMesh
    {
        public int Version { get; private set; }
        public byte Flags { get; private set; }

        /// <summary>The scrambling key of version 4 and later, or null for older files.</summary>
        public uint? Key { get; private set; }

        /// <summary>True when the stored vertices were scrambled and have been unscrambled while reading.</summary>
        public bool WasScrambled { get; private set; }

        public List<OmsiMeshVertex> Vertices { get; } = new List<OmsiMeshVertex>();

        /// <summary>Vertex indices, three per triangle; triangles with an index beyond the vertices are dropped.</summary>
        public List<int> TriangleIndices { get; } = new List<int>();

        /// <summary>Material index of each triangle, in the order of <see cref="TriangleIndices"/>.</summary>
        public List<int> TriangleMaterials { get; } = new List<int>();

        public List<OmsiMeshMaterial> Materials { get; } = new List<OmsiMeshMaterial>();

        public static OmsiO3dMesh Load(string path)
        {
            return Parse(File.ReadAllBytes(path));
        }

        public static OmsiO3dMesh Parse(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            var reader = new Reader(bytes);
            if (reader.U8() != 0x84 || reader.U8() != 0x19)
                throw new InvalidDataException("Not an OMSI .o3d mesh: the file does not start with 84 19.");

            var mesh = new OmsiO3dMesh { Version = reader.U8() };
            var longIndices = false;
            var alternative = false;
            if (mesh.Version >= 3)
            {
                mesh.Flags = reader.U8();
                longIndices = (mesh.Flags & 1) != 0;
                alternative = (mesh.Flags & 2) != 0;
            }
            uint key = 0;
            if (mesh.Version >= 4)
            {
                key = reader.U32();
                mesh.Key = key;
            }

            // Version 4 and later scramble the vertex records unless the key is 0xFFFFFFFF.
            var scrambled = mesh.Version >= 4 && key != 0xFFFFFFFFu;
            mesh.WasScrambled = scrambled;
            var state = scrambled ? (ushort)Mod((long)key + (mesh.Version - 4) + (alternative ? 0x17D : 0), 0xFDE8) : (ushort)0;
            var wideCount = mesh.Version >= 3;

            while (!reader.AtEnd)
            {
                switch (reader.U8())
                {
                    case 0x17:
                    {
                        var count = wideCount ? (int)reader.U32() : reader.U16();
                        reader.Need((long)count * 32);
                        var count16 = (uint)Mod(count, 0xFDE8);
                        uint mixing = 0;
                        for (var index = 0; index < count; index++)
                        {
                            var vertex = new OmsiMeshVertex
                            {
                                X = reader.F32(), Y = reader.F32(), Z = reader.F32(),
                                NormalX = reader.F32(), NormalY = reader.F32(), NormalZ = reader.F32(),
                                U = reader.F32(), V = reader.F32()
                            };
                            if (scrambled)
                            {
                                if (key == 0)
                                    state = alternative ? (ushort)0x130 : (ushort)0;
                                // 32-bit wrap-around as in the original loader.
                                state = (ushort)(unchecked(state * count16 + count16 * mixing) % 8000u);
                                mixing = FractionByte(vertex.X, vertex.Y, vertex.Z);
                                Unscramble(state, ref vertex);
                            }
                            mesh.Vertices.Add(vertex);
                        }
                        break;
                    }
                    case 0x49:
                    {
                        var count = wideCount ? (int)reader.U32() : reader.U16();
                        var indexSize = longIndices ? 4 : 2;
                        reader.Need((long)count * (3 * indexSize + 2));
                        for (var index = 0; index < count; index++)
                        {
                            var a = longIndices ? (int)reader.U32() : reader.U16();
                            var b = longIndices ? (int)reader.U32() : reader.U16();
                            var c = longIndices ? (int)reader.U32() : reader.U16();
                            var material = reader.U16();
                            mesh.TriangleIndices.Add(a);
                            mesh.TriangleIndices.Add(b);
                            mesh.TriangleIndices.Add(c);
                            mesh.TriangleMaterials.Add(material);
                        }
                        break;
                    }
                    case 0x26:
                    {
                        // Diffuse RGBA, specular RGB, emissive RGB and specular power, then the texture name.
                        var count = reader.U16();
                        for (var index = 0; index < count; index++)
                        {
                            var material = new OmsiMeshMaterial
                            {
                                DiffuseRed = reader.F32(), DiffuseGreen = reader.F32(), DiffuseBlue = reader.F32(), DiffuseAlpha = reader.F32()
                            };
                            reader.Skip(7 * 4);
                            material.Texture = reader.Text(reader.U8());
                            mesh.Materials.Add(material);
                        }
                        break;
                    }
                    case 0x79:
                        reader.Skip(16 * 4);
                        break;
                    case 0x54:
                    {
                        var count = reader.U16();
                        for (var index = 0; index < count; index++)
                        {
                            reader.Skip(reader.U8());
                            var weights = reader.U16();
                            reader.Skip((long)weights * ((longIndices ? 4 : 2) + 4));
                        }
                        break;
                    }
                    // Any other byte is passed over one at a time, as OMSI's loader does.
                }
            }

            mesh.DropInvalidTriangles();
            return mesh;
        }

        private void DropInvalidTriangles()
        {
            for (var index = TriangleIndices.Count - 3; index >= 0; index -= 3)
            {
                if (TriangleIndices[index] >= Vertices.Count || TriangleIndices[index + 1] >= Vertices.Count ||
                    TriangleIndices[index + 2] >= Vertices.Count)
                {
                    TriangleIndices.RemoveRange(index, 3);
                    TriangleMaterials.RemoveAt(index / 3);
                }
            }
        }

        private static long Mod(long value, long modulus)
        {
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        /// <summary>Mixing byte from the raw, still scrambled position; it feeds the state of the next vertex.</summary>
        private static uint FractionByte(float x, float y, float z)
        {
            double Fraction(float value) => value - Math.Truncate((double)value);
            var product = Math.Abs(Fraction(x) * Fraction(y) * Fraction(z)) * 600.0;
            return (uint)Mod((long)Math.Truncate(product), 256);
        }

        private static void Unscramble(ushort state, ref OmsiMeshVertex vertex)
        {
            if (state < 1000)
                Swap(ref vertex.X, ref vertex.Y);
            else if (state < 3000)
                Swap(ref vertex.X, ref vertex.Z);
            else if (state > 7000)
                Swap(ref vertex.Y, ref vertex.Z);

            if ((state & 3) == 0)
                vertex.NormalX = -vertex.NormalX;
            if (state % 6 == 0)
                vertex.NormalY = -vertex.NormalY;
            if (state % 7 == 0)
                vertex.NormalZ = -vertex.NormalZ;

            // OMSI's own loader has an unreachable "state > 6500" branch after "state > 4500"; it is kept out.
            if (state < 600)
                Swap(ref vertex.NormalY, ref vertex.NormalZ);
            else if (state > 4500)
                Swap(ref vertex.NormalX, ref vertex.NormalY);

            if (state % 5 == 0)
            {
                var m = (float)(state % 100);
                vertex.U -= m * m / 10000f;
            }
            if (state % 3 == 0)
            {
                var m = (float)(state % 50);
                vertex.V -= m * m / 2500f;
            }
        }

        private static void Swap(ref float first, ref float second)
        {
            var temporary = first;
            first = second;
            second = temporary;
        }

        private sealed class Reader
        {
            private readonly byte[] bytes;
            private int position;

            public Reader(byte[] bytes)
            {
                this.bytes = bytes;
            }

            public bool AtEnd => position >= bytes.Length;

            public void Need(long count)
            {
                if (count < 0 || position + count > bytes.Length)
                    throw new InvalidDataException("The .o3d file ends inside a section.");
            }

            public void Skip(long count)
            {
                Need(count);
                position += (int)count;
            }

            public byte U8()
            {
                Need(1);
                return bytes[position++];
            }

            public ushort U16()
            {
                Need(2);
                var value = BitConverter.ToUInt16(bytes, position);
                position += 2;
                return value;
            }

            public uint U32()
            {
                Need(4);
                var value = BitConverter.ToUInt32(bytes, position);
                position += 4;
                return value;
            }

            /// <summary>A Windows-1252 string of the given length.</summary>
            public string Text(int length)
            {
                Need(length);
                var text = new char[length];
                for (var index = 0; index < length; index++)
                    text[index] = OmsiTextFile.Windows1252Char(bytes[position + index]);
                position += length;
                return new string(text);
            }

            public float F32()
            {
                Need(4);
                var value = BitConverter.ToSingle(bytes, position);
                position += 4;
                return value;
            }
        }
    }
}
