using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OmsiMapEditor.OMSI
{
    /// <summary>
    /// Triangles with one texture, in the object frame OMSI models use: x right, y up, z forward. Triangles wind as in
    /// OMSI's own models: seen from the side they face, (b - a) × (c - a) points towards the viewer in this frame.
    /// </summary>
    public sealed class OmsiGeneratedMesh
    {
        /// <summary>The texture file name, looked up in the <c>texture</c> folder next to the .sco.</summary>
        public string Texture { get; set; }

        public List<double> Positions { get; } = new List<double>();
        public List<double> Uvs { get; } = new List<double>();
        public List<int> Triangles { get; } = new List<int>();
    }

    /// <summary>Writes text .x models as OMSI reads them: one mesh, a material per texture, face normals and texture coordinates.</summary>
    public static class OmsiXWriter
    {
        public static string Write(IReadOnlyList<OmsiGeneratedMesh> meshes)
        {
            if (meshes == null)
                throw new ArgumentNullException(nameof(meshes));
            var text = new StringBuilder();
            text.Append("xof 0302txt 0032\n\n");
            text.Append("// Written by OMSI MapWorks.\n\n");
            text.Append("Mesh MapforgeMesh {\n");

            var vertices = 0;
            var faces = 0;
            foreach (var mesh in meshes)
            {
                vertices += mesh.Positions.Count / 3;
                faces += mesh.Triangles.Count / 3;
            }
            text.Append(vertices).Append(";\n");
            var written = 0;
            foreach (var mesh in meshes)
                for (var index = 0; index < mesh.Positions.Count; index += 3)
                {
                    written++;
                    text.Append(Number(mesh.Positions[index])).Append(';').Append(Number(mesh.Positions[index + 1])).Append(';')
                        .Append(Number(mesh.Positions[index + 2])).Append(';').Append(written == vertices ? ";\n" : ",\n");
                }
            text.Append(faces).Append(";\n");
            var offset = 0;
            var face = 0;
            foreach (var mesh in meshes)
            {
                for (var index = 0; index < mesh.Triangles.Count; index += 3)
                {
                    face++;
                    text.Append("3;").Append(offset + mesh.Triangles[index]).Append(',').Append(offset + mesh.Triangles[index + 1]).Append(',')
                        .Append(offset + mesh.Triangles[index + 2]).Append(face == faces ? ";;\n" : ";,\n");
                }
                offset += mesh.Positions.Count / 3;
            }

            text.Append("MeshMaterialList {\n").Append(meshes.Count).Append(";\n").Append(faces).Append(";\n");
            face = 0;
            for (var material = 0; material < meshes.Count; material++)
                for (var index = 0; index < meshes[material].Triangles.Count; index += 3)
                {
                    face++;
                    text.Append(material).Append(face == faces ? ";;\n" : ",\n");
                }
            for (var material = 0; material < meshes.Count; material++)
            {
                text.Append("Material Material").Append(material).Append(" {\n");
                text.Append("1.000000;1.000000;1.000000;1.000000;;\n0.000000;\n0.000000;0.000000;0.000000;;\n0.000000;0.000000;0.000000;;\n");
                if (!string.IsNullOrEmpty(meshes[material].Texture))
                    text.Append("TextureFilename {\"").Append(meshes[material].Texture).Append("\";}\n");
                text.Append("}\n");
            }
            text.Append("}\n");

            // One normal per face, from its winding.
            text.Append("MeshNormals {\n").Append(faces).Append(";\n");
            face = 0;
            foreach (var mesh in meshes)
                for (var index = 0; index < mesh.Triangles.Count; index += 3)
                {
                    face++;
                    Normal(mesh, index, out var x, out var y, out var z);
                    text.Append(Number(x)).Append(';').Append(Number(y)).Append(';').Append(Number(z)).Append(face == faces ? ";;\n" : ";,\n");
                }
            text.Append(faces).Append(";\n");
            for (face = 0; face < faces; face++)
                text.Append("3;").Append(face).Append(',').Append(face).Append(',').Append(face).Append(face + 1 == faces ? ";;\n" : ";,\n");
            text.Append("}\n");

            text.Append("MeshTextureCoords {\n").Append(vertices).Append(";\n");
            written = 0;
            foreach (var mesh in meshes)
                for (var index = 0; index < mesh.Positions.Count / 3; index++)
                {
                    written++;
                    var u = index * 2 < mesh.Uvs.Count ? mesh.Uvs[index * 2] : 0.0;
                    var v = index * 2 + 1 < mesh.Uvs.Count ? mesh.Uvs[index * 2 + 1] : 0.0;
                    text.Append(Number(u)).Append(';').Append(Number(v)).Append(written == vertices ? ";;\n" : ";,\n");
                }
            text.Append("}\n");
            text.Append("}\n");
            return text.ToString();
        }

        private static void Normal(OmsiGeneratedMesh mesh, int index, out double x, out double y, out double z)
        {
            double[] Point(int vertex) => new[] { mesh.Positions[vertex * 3], mesh.Positions[vertex * 3 + 1], mesh.Positions[vertex * 3 + 2] };
            var a = Point(mesh.Triangles[index]);
            var b = Point(mesh.Triangles[index + 1]);
            var c = Point(mesh.Triangles[index + 2]);
            double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
            double vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
            // (b - a) × (c - a), which points out of the faces of OMSI's own models.
            x = uy * vz - uz * vy;
            y = uz * vx - ux * vz;
            z = ux * vy - uy * vx;
            var length = Math.Sqrt(x * x + y * y + z * z);
            if (length < 1e-12)
            {
                x = 0.0;
                y = 1.0;
                z = 0.0;
                return;
            }
            x /= length;
            y /= length;
            z /= length;
        }

        internal static string Number(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A scenery object made by the editor: its .sco text, its model and the textures it needs from the installation.</summary>
    public sealed class OmsiGeneratedObject
    {
        public string Sco { get; internal set; }
        public string Model { get; internal set; }

        /// <summary>Texture files to copy next to the object, as paths relative to the installation.</summary>
        public List<string> Textures { get; } = new List<string>();

        /// <summary>Files the editor makes for the object, such as the plain textures of road markings, by their path in its folder.</summary>
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Makes junction objects. For the first test (stage 4b) a straight piece in a road's profile: its surfaces, a
    /// connection point at each end suggesting that profile, and the profile's lanes as paths, so vehicles and
    /// pedestrians drive and walk through it as along the road.
    /// </summary>
    public static class OmsiJunctionGenerator
    {
        public static OmsiGeneratedObject StraightPiece(string profilePath, OmsiSplineProfile profile, double lengthMeters, string friendlyName)
        {
            if (string.IsNullOrWhiteSpace(profilePath))
                throw new ArgumentException("A spline profile (.sli) is required.", nameof(profilePath));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (!(lengthMeters >= 1.0))
                throw new ArgumentOutOfRangeException(nameof(lengthMeters), "The piece must be at least 1 m long.");
            var half = lengthMeters / 2.0;
            var generated = new OmsiGeneratedObject();

            // The surfaces of a straight spline running forward through the origin are the piece's model.
            var spline = new OmsiSplineRecord { LocalX = 0.0, LocalY = -half, HeadingDegrees = 0.0, LengthMeters = lengthMeters };
            var meshes = new List<OmsiGeneratedMesh>();
            foreach (var surface in OmsiSplineSurface.Build(spline, profile, true))
            {
                var texture = profile.Textures[surface.TextureIndex].Path;
                var mesh = new OmsiGeneratedMesh { Texture = Path.GetFileName(texture) };
                mesh.Positions.AddRange(surface.Positions);
                mesh.Uvs.AddRange(surface.Uvs);
                mesh.Triangles.AddRange(surface.Triangles);
                meshes.Add(mesh);
                if (!generated.Textures.Contains(texture))
                    generated.Textures.Add(texture);
            }
            generated.Model = OmsiXWriter.Write(meshes);

            var sco = new StringBuilder();
            sco.Append("Made by OMSI MapWorks: a straight test piece in the profile ").Append(profilePath.Trim()).Append(".\r\n\r\n");
            Section(sco, "friendlyname", friendlyName);
            Section(sco, "groups", "1", "Mapforge");
            Section(sco, "rendertype", "surface");
            Section(sco, "fixed");
            Section(sco, "surface");
            foreach (var end in new[] { -1.0, 1.0 })
                Section(sco, "splinehelper", profilePath.Trim(), "0", "0", OmsiXWriter.Number(end * half), end < 0 ? "180" : "0", "0", "0");
            foreach (var lane in profile.Lanes)
            {
                // A lane against the spline runs backwards from the far end; one used both ways is written once.
                var backwards = lane.Direction == 1;
                Section(sco, "path", OmsiXWriter.Number(lane.X), OmsiXWriter.Number(backwards ? half : -half), OmsiXWriter.Number(lane.Height),
                    backwards ? "180" : "0", "0", OmsiXWriter.Number(lengthMeters), "0", "0", lane.Kind.ToString(CultureInfo.InvariantCulture),
                    OmsiXWriter.Number(lane.Width), lane.Direction == 2 ? "2" : "0", "0");
            }
            Section(sco, "mesh", "model.x");
            Section(sco, "collision_mesh", "model.x");
            generated.Sco = sco.ToString();
            return generated;
        }

        /// <summary>
        /// A junction of its arms (stage 4c): carriageway, rounded kerbs and pavements from the arms' profiles, a
        /// connection point at the end of each arm suggesting its profile, and lanes for every way through.
        /// </summary>
        /// <param name="roadTexture">A plain carriageway texture relative to the installation, or null for the first arm's.</param>
        /// <param name="markings">Road markings to build in (ADR 044), by the anchors of the arms; null or empty for none.</param>
        public static OmsiGeneratedObject Junction(IEnumerable<OmsiJunctionArm> arms, string roadTexture, string friendlyName,
            IList<OmsiMarkingLine> markings = null)
        {
            var layout = OmsiJunctionLayout.Build(arms, roadTexture);
            var meshes = new List<OmsiGeneratedMesh>(layout.Meshes);
            if (markings != null && markings.Count > 0)
                OmsiJunctionMarkings.AddMeshes(OmsiJunctionMarkings.Anchors(layout), markings, meshes);
            var generated = new OmsiGeneratedObject { Model = OmsiXWriter.Write(meshes) };
            generated.Textures.AddRange(layout.Textures.Values);
            foreach (var mesh in meshes)
                if (string.Equals(mesh.Texture, OmsiJunctionMarkings.WhiteTexture, StringComparison.OrdinalIgnoreCase))
                    generated.Files["texture\\" + OmsiJunctionMarkings.WhiteTexture] = OmsiJunctionMarkings.PlainBitmap(240, 240, 235);
                else if (string.Equals(mesh.Texture, OmsiJunctionMarkings.YellowTexture, StringComparison.OrdinalIgnoreCase))
                    generated.Files["texture\\" + OmsiJunctionMarkings.YellowTexture] = OmsiJunctionMarkings.PlainBitmap(245, 190, 30);

            var sco = new StringBuilder();
            sco.Append("Made by OMSI MapWorks: a junction of ").Append(layout.Arms.Count).Append(" arms.\r\n");
            // What the [splinehelper] sections cannot say, in their order, so the junction can be made again (OmsiGeneratedJunction).
            sco.Append(OmsiGeneratedJunction.ArmsPrefix);
            for (var index = 0; index < layout.Arms.Count; index++)
            {
                var arm = layout.Arms[index];
                sco.Append(index == 0 ? " " : "; ").Append(OmsiXWriter.Number(arm.HeadingDegrees));
                if (arm.Main)
                    sco.Append(" main");
                if (arm.Mirrored)
                    sco.Append(" mirrored");
            }
            sco.Append("\r\n");
            if (markings != null && markings.Count > 0)
                sco.Append(OmsiJunctionMarkings.Write(markings)).Append("\r\n");
            sco.Append("\r\n");
            Section(sco, "friendlyname", friendlyName);
            Section(sco, "groups", "1", "Mapforge");
            Section(sco, "rendertype", "surface");
            Section(sco, "fixed");
            Section(sco, "surface");
            foreach (var arm in layout.Arms)
            {
                var radians = arm.HeadingDegrees * Math.PI / 180.0;
                Section(sco, "splinehelper", arm.ProfilePath.Trim(), OmsiXWriter.Number(Math.Sin(radians) * arm.Length), "0",
                    OmsiXWriter.Number(Math.Cos(radians) * arm.Length), OmsiXWriter.Number(arm.HeadingDegrees), "0", "0");
            }
            foreach (var path in layout.Paths)
                Section(sco, "path", OmsiXWriter.Number(path.X), OmsiXWriter.Number(path.Y), OmsiXWriter.Number(path.Height),
                    OmsiXWriter.Number(path.HeadingDegrees), OmsiXWriter.Number(path.RadiusMeters), OmsiXWriter.Number(path.LengthMeters), "0", "0",
                    path.Kind.ToString(CultureInfo.InvariantCulture), OmsiXWriter.Number(path.Width), path.Direction.ToString(CultureInfo.InvariantCulture),
                    path.Blinker.ToString(CultureInfo.InvariantCulture));
            Section(sco, "mesh", "model.x");
            Section(sco, "collision_mesh", "model.x");
            generated.Sco = sco.ToString();
            return generated;
        }

        private static void Section(StringBuilder text, string name, params string[] lines)
        {
            text.Append('[').Append(name).Append("]\r\n");
            foreach (var line in lines)
                text.Append(line).Append("\r\n");
            text.Append("\r\n");
        }
    }

    /// <summary>
    /// Stores objects the editor makes under <c>Sceneryobjects\Mapforge\&lt;project&gt;</c> of the installation, as map packages
    /// keep their own objects (ADR 026): each in its own folder with its model and copies of its textures. Existing files
    /// are never overwritten; a name in use gets a number.
    /// </summary>
    public static class OmsiGeneratedObjectStore
    {
        public const string Folder = @"Sceneryobjects\Mapforge";

        /// <returns>The object's path relative to the installation, as tile files name objects.</returns>
        public static string Write(string installationRoot, string projectName, string objectName, OmsiGeneratedObject generated)
        {
            if (string.IsNullOrWhiteSpace(installationRoot))
                throw new ArgumentException("The OMSI installation folder is required.", nameof(installationRoot));
            if (generated == null)
                throw new ArgumentNullException(nameof(generated));
            var project = SafeName(projectName, "Project");
            var name = SafeName(objectName, "Junction");
            var projectFolder = Path.Combine(installationRoot, Folder, project);
            var folderName = name;
            for (var number = 2; Directory.Exists(Path.Combine(projectFolder, folderName)); number++)
                folderName = name + "_" + number;
            var folder = Path.Combine(projectFolder, folderName);
            Directory.CreateDirectory(Path.Combine(folder, "model"));
            Directory.CreateDirectory(Path.Combine(folder, "texture"));

            File.WriteAllText(Path.Combine(folder, "model", "model.x"), generated.Model, new UTF8Encoding(false));
            foreach (var texture in generated.Textures)
            {
                // OMSI prefers a .dds of the same name; a .cfg sets wetness and puddles, a .surf the kind of surface.
                foreach (var source in new[] { texture, Path.ChangeExtension(texture, ".dds"), texture + ".cfg", texture + ".surf" })
                {
                    var from = OmsiRelativePath.Resolve(installationRoot, source);
                    var to = Path.Combine(folder, "texture", Path.GetFileName(from));
                    if (File.Exists(from) && !File.Exists(to))
                        File.Copy(from, to);
                }
            }
            foreach (var file in generated.Files)
                File.WriteAllBytes(Path.Combine(folder, file.Key.Replace('\\', Path.DirectorySeparatorChar)), file.Value);
            var scoName = folderName + ".sco";
            File.WriteAllBytes(Path.Combine(folder, scoName), OmsiTextFile.Encode(generated.Sco, OmsiTextEncoding.Windows1252));
            return Folder + "\\" + project + "\\" + folderName + "\\" + scoName;
        }

        /// <summary>A file and folder name from free text: letters, digits, spaces, '-' and '_' only.</summary>
        public static string SafeName(string text, string fallback)
        {
            var result = new StringBuilder();
            foreach (var character in text ?? string.Empty)
                result.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == ' ' ? character : '_');
            var name = result.ToString().Trim();
            return name.Length == 0 ? fallback : name;
        }
    }
}
