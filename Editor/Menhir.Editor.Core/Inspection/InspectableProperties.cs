using System.Reflection;

namespace Menhir.Editor.Inspection;

/// <summary>
/// Finds the properties the inspector shows for a component type: public instance
/// properties with a public getter and setter, not marked [HideInInspector],
/// in declaration order.
/// </summary>
public static class InspectableProperties
{
    private static readonly Dictionary<Type, IReadOnlyList<PropertyInfo>> Cache = new();

    public static IReadOnlyList<PropertyInfo> Get(Type type)
    {
        if (Cache.TryGetValue(type, out IReadOnlyList<PropertyInfo>? properties))
            return properties;

        properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Where(p => p.GetCustomAttribute<HideInInspectorAttribute>() == null)
            .OrderBy(p => p.MetadataToken) // declaration order within a type
            .ToList();
        Cache[type] = properties;
        return properties;
    }

    /// <summary>"MeshRenderer" → "Mesh Renderer", "castShadows" → "Cast Shadows".</summary>
    public static string NicifyName(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i == 0)
            {
                builder.Append(char.ToUpperInvariant(c));
                continue;
            }
            bool wordStart = char.IsUpper(c) &&
                             (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
            if (wordStart || (char.IsDigit(c) && !char.IsDigit(name[i - 1])))
                builder.Append(' ');
            builder.Append(c);
        }
        return builder.ToString();
    }
}
