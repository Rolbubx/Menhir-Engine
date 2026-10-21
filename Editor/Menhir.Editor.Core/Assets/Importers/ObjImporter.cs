using System.Globalization;
using System.Numerics;
using Menhir.Editor.Rendering;

namespace Menhir.Editor.Assets.Importers;

/// <summary>
/// Wavefront OBJ: <c>v</c>, <c>vn</c> and <c>f</c> lines (polygons are split into triangle fans,
/// negative indices count from the end). Every object and group goes into one mesh; materials and
/// texture coordinates are ignored for now.
/// </summary>
public static class ObjImporter
{
    public static MeshData Import(string text)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var builder = new MeshBuilder();
        int lineNumber = 0;

        foreach (string rawLine in text.Split('\n'))
        {
            lineNumber++;
            string line = rawLine.Trim();
            int comment = line.IndexOf('#');
            if (comment >= 0)
                line = line[..comment].Trim();
            if (line.Length == 0)
                continue;
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v":
                    positions.Add(ReadVector(parts, lineNumber));
                    break;
                case "vn":
                    normals.Add(ReadVector(parts, lineNumber));
                    break;
                case "f":
                    if (parts.Length < 4)
                        throw new InvalidDataException($"Line {lineNumber}: a face needs at least 3 corners.");
                    var corners = new (Vector3, Vector3?)[parts.Length - 1];
                    for (int i = 1; i < parts.Length; i++)
                        corners[i - 1] = ReadCorner(parts[i], positions, normals, lineNumber);
                    for (int i = 1; i + 1 < corners.Length; i++)
                        builder.AddTriangle(corners[0], corners[i], corners[i + 1]);
                    break;
            }
        }

        if (builder.TriangleCount == 0)
            throw new InvalidDataException("The file has no faces.");
        return builder.Build();
    }

    private static Vector3 ReadVector(string[] parts, int lineNumber)
    {
        if (parts.Length < 4)
            throw new InvalidDataException($"Line {lineNumber}: expected 3 numbers.");
        return new Vector3(Number(parts[1], lineNumber), Number(parts[2], lineNumber), Number(parts[3], lineNumber));
    }

    private static float Number(string s, int lineNumber) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : throw new InvalidDataException($"Line {lineNumber}: '{s}' is not a number.");

    /// <summary>"v", "v/vt", "v//vn" or "v/vt/vn" (1-based; negative counts from the last one read).</summary>
    private static (Vector3, Vector3?) ReadCorner(string corner, List<Vector3> positions, List<Vector3> normals, int lineNumber)
    {
        string[] fields = corner.Split('/');
        Vector3 position = positions[Index(fields[0], positions.Count, lineNumber)];
        Vector3? normal = fields.Length >= 3 && fields[2].Length > 0
            ? normals[Index(fields[2], normals.Count, lineNumber)]
            : null;
        return (position, normal);
    }

    private static int Index(string s, int count, int lineNumber)
    {
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index == 0)
            throw new InvalidDataException($"Line {lineNumber}: '{s}' is not a vertex index.");
        int resolved = index > 0 ? index - 1 : count + index;
        if (resolved < 0 || resolved >= count)
            throw new InvalidDataException($"Line {lineNumber}: index {index} refers to a vertex that does not exist.");
        return resolved;
    }
}
