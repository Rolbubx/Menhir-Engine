using System.Reflection;

namespace Menhir.Editor.Scenes;

/// <summary>
/// Registry of the component types scenes can contain, by name. A type is saved under its
/// class name (e.g. "MeshRenderer"), so two registered components cannot share a class name.
/// Built-in components are registered automatically; script assemblies will be added with
/// <see cref="Register(Assembly)"/>.
/// </summary>
public static class ComponentTypes
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, Type> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, IReadOnlyList<PropertyInfo>> SerializedProperties = new();

    static ComponentTypes()
    {
        Register(typeof(Component).Assembly);
    }

    public static IReadOnlyCollection<Type> All
    {
        get
        {
            lock (Lock)
                return ByName.Values.ToList();
        }
    }

    /// <summary>Registers every public, non-abstract component with a parameterless constructor.</summary>
    public static void Register(Assembly assembly)
    {
        IEnumerable<Type> types = assembly.GetExportedTypes().Where(t =>
            t.IsSubclassOf(typeof(Component)) && !t.IsAbstract && t != typeof(MissingComponent) &&
            t.GetConstructor(Type.EmptyTypes) != null);

        lock (Lock)
        {
            foreach (Type type in types)
            {
                // The type's name is saved under "type", next to the properties.
                if (type.GetProperty("Type") is { } clash && clash.GetMethod?.IsPublic == true && clash.SetMethod?.IsPublic == true)
                    throw new InvalidOperationException(
                        $"{type.FullName} cannot have a saved property named Type (scene files use \"type\" for the component's type).");
                if (ByName.TryGetValue(type.Name, out Type? existing) && existing != type)
                    throw new InvalidOperationException(
                        $"Components {existing.FullName} and {type.FullName} have the same name.");
                ByName[type.Name] = type;
            }
        }
    }

    public static string GetName(Type type) => type.Name;

    /// <summary>Creates a component from its saved name, or returns null when the type is unknown.</summary>
    public static Component? Create(string name)
    {
        Type? type;
        lock (Lock)
            ByName.TryGetValue(name, out type);
        return type == null ? null : (Component)Activator.CreateInstance(type)!;
    }

    /// <summary>The properties saved in scene files: public instance properties with a public getter and setter.</summary>
    public static IReadOnlyList<PropertyInfo> GetSerializedProperties(Type type)
    {
        lock (Lock)
        {
            if (!SerializedProperties.TryGetValue(type, out IReadOnlyList<PropertyInfo>? properties))
            {
                properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetMethod?.IsPublic == true && p.SetMethod?.IsPublic == true)
                    .Where(p => p.GetIndexParameters().Length == 0)
                    .OrderBy(p => p.MetadataToken)
                    .ToList();
                SerializedProperties[type] = properties;
            }
            return properties;
        }
    }
}
