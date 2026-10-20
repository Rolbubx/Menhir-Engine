using System.Reflection;
using Menhir.Editor.Scenes;

namespace Menhir.Editor.Inspection;

/// <summary>
/// Finds the properties the inspector shows for a component type: the saved properties
/// (<see cref="ComponentTypes.GetSerializedProperties"/>) not marked [HideInInspector],
/// in declaration order.
/// </summary>
public static class InspectableProperties
{
    private static readonly Dictionary<Type, IReadOnlyList<PropertyInfo>> Cache = new();

    public static IReadOnlyList<PropertyInfo> Get(Type type)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(type, out IReadOnlyList<PropertyInfo>? properties))
            {
                properties = ComponentTypes.GetSerializedProperties(type)
                    .Where(p => p.GetCustomAttribute<HideInInspectorAttribute>() == null)
                    .ToList();
                Cache[type] = properties;
            }
            return properties;
        }
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
