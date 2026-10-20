using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Vertex positions and triangles of a DirectX text mesh (<c>xof 0303txt</c>), in the file's Direct3D frame.
    /// Frame transforms are applied, child before parent, so every mesh lands in the object frame.
    /// </summary>
    public sealed class OmsiXMesh
    {
        public List<float[]> Positions { get; } = new List<float[]>();
        public List<int> TriangleIndices { get; } = new List<int>();

        public static OmsiXMesh Load(string path)
        {
            return Parse(File.ReadAllBytes(path));
        }

        public static OmsiXMesh Parse(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));
            var header = Encoding.ASCII.GetString(bytes, 0, Math.Min(16, bytes.Length));
            if (!header.StartsWith("xof ", StringComparison.Ordinal))
                throw new InvalidDataException("Not a DirectX .x file.");
            if (header.Length < 12 || header.Substring(8, 4) != "txt ")
                throw new InvalidDataException("Only text .x files are supported; this one is " + header.Substring(8, Math.Min(4, header.Length - 8)).Trim() + ".");

            var mesh = new OmsiXMesh();
            var tokens = new Tokenizer(Encoding.ASCII.GetString(bytes, 16, bytes.Length - 16));
            while (tokens.Peek() != null)
                mesh.ReadObject(tokens, Identity());
            return mesh;
        }

        private void ReadObject(Tokenizer tokens, double[] parentMatrix)
        {
            var type = tokens.Next();
            if (type == "{")
            {
                // A reference such as "{ MaterialName }".
                SkipBlock(tokens);
                return;
            }
            if (type == "}")
                return;
            // An optional name, and for templates a GUID, may precede the opening brace.
            while (tokens.Peek() != null && tokens.Peek() != "{" && tokens.Peek() != "}")
                tokens.Next();
            if (tokens.Next() != "{")
                return;

            if (type == "Frame")
                ReadFrame(tokens, parentMatrix);
            else if (type == "Mesh")
                ReadMesh(tokens, parentMatrix);
            else
                SkipBlock(tokens);
        }

        private void ReadFrame(Tokenizer tokens, double[] parentMatrix)
        {
            var local = Identity();
            var world = parentMatrix;
            while (tokens.Peek() != null && tokens.Peek() != "}")
            {
                if (tokens.Peek() == "FrameTransformMatrix")
                {
                    tokens.Next();
                    tokens.Expect("{");
                    for (var index = 0; index < 16; index++)
                        local[index] = tokens.Number();
                    SkipBlock(tokens);
                    world = Multiply(local, parentMatrix);
                }
                else
                {
                    ReadObject(tokens, world);
                }
            }
            tokens.Next();
        }

        private void ReadMesh(Tokenizer tokens, double[] matrix)
        {
            var first = Positions.Count;
            var vertexCount = (int)tokens.Number();
            for (var index = 0; index < vertexCount; index++)
            {
                var x = tokens.Number();
                var y = tokens.Number();
                var z = tokens.Number();
                // Row vector times matrix; translation in elements 12 to 14.
                Positions.Add(new[]
                {
                    (float)(x * matrix[0] + y * matrix[4] + z * matrix[8] + matrix[12]),
                    (float)(x * matrix[1] + y * matrix[5] + z * matrix[9] + matrix[13]),
                    (float)(x * matrix[2] + y * matrix[6] + z * matrix[10] + matrix[14])
                });
            }

            var faceCount = (int)tokens.Number();
            for (var face = 0; face < faceCount; face++)
            {
                var corners = (int)tokens.Number();
                var indices = new int[corners];
                for (var corner = 0; corner < corners; corner++)
                    indices[corner] = (int)tokens.Number();
                // Faces may have more than three corners; split them into a fan.
                for (var corner = 1; corner + 1 < corners; corner++)
                {
                    if (indices[0] < vertexCount && indices[corner] < vertexCount && indices[corner + 1] < vertexCount)
                    {
                        TriangleIndices.Add(first + indices[0]);
                        TriangleIndices.Add(first + indices[corner]);
                        TriangleIndices.Add(first + indices[corner + 1]);
                    }
                }
            }

            // Normals, texture coordinates and materials follow as nested blocks.
            SkipBlock(tokens);
        }

        /// <summary>Skips to the brace that closes the current block, including nested blocks.</summary>
        private static void SkipBlock(Tokenizer tokens)
        {
            var depth = 1;
            while (depth > 0)
            {
                var token = tokens.Next();
                if (token == null)
                    return;
                if (token == "{")
                    depth++;
                else if (token == "}")
                    depth--;
            }
        }

        private static double[] Identity()
        {
            return new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        }

        /// <summary>Row-vector convention: applying <paramref name="first"/> and then <paramref name="second"/>.</summary>
        private static double[] Multiply(double[] first, double[] second)
        {
            var result = new double[16];
            for (var row = 0; row < 4; row++)
                for (var column = 0; column < 4; column++)
                    for (var k = 0; k < 4; k++)
                        result[row * 4 + column] += first[row * 4 + k] * second[k * 4 + column];
            return result;
        }

        /// <summary>Splits the text into words, numbers and braces; semicolons, commas and comments separate tokens.</summary>
        private sealed class Tokenizer
        {
            private readonly string text;
            private int position;
            private string peeked;

            public Tokenizer(string text)
            {
                this.text = text;
            }

            public string Peek()
            {
                return peeked ?? (peeked = Read());
            }

            public string Next()
            {
                var token = Peek();
                peeked = null;
                return token;
            }

            public void Expect(string expected)
            {
                var token = Next();
                if (token != expected)
                    throw new InvalidDataException("Expected '" + expected + "' in the .x file but found '" + token + "'.");
            }

            public double Number()
            {
                var token = Next();
                if (token == null || !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new InvalidDataException("Expected a number in the .x file but found '" + token + "'.");
                return value;
            }

            private string Read()
            {
                while (position < text.Length)
                {
                    var character = text[position];
                    if (char.IsWhiteSpace(character) || character == ';' || character == ',')
                    {
                        position++;
                    }
                    else if (character == '/' && position + 1 < text.Length && text[position + 1] == '/' || character == '#')
                    {
                        while (position < text.Length && text[position] != '\n')
                            position++;
                    }
                    else if (character == '{' || character == '}')
                    {
                        position++;
                        return character.ToString();
                    }
                    else if (character == '"')
                    {
                        var end = text.IndexOf('"', position + 1);
                        var token = end < 0 ? text.Substring(position) : text.Substring(position, end - position + 1);
                        position = end < 0 ? text.Length : end + 1;
                        return token;
                    }
                    else
                    {
                        var start = position;
                        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != ';' &&
                               text[position] != ',' && text[position] != '{' && text[position] != '}')
                            position++;
                        return text.Substring(start, position - start);
                    }
                }
                return null;
            }
        }
    }
}
