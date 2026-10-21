using System.Text;
using Menhir.Translator.TypeSystem;

namespace Menhir.Translator.CodeGen;

/// <summary>
/// C names for types, methods, fields and statics. Readable and predictable, because the C
/// runtime refers to some of them (System_String__type, internal calls like System_Console__WriteString).
/// </summary>
public sealed class CNames
{
    private readonly Dictionary<TypeDesc, string> _types = new();
    private readonly Dictionary<MethodDesc, string> _methods = new();
    private readonly HashSet<string> _usedTypeNames = new();

    public static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
            builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        return builder.ToString();
    }

    /// <summary>"Namespace_Type" (nested: "Namespace_Outer_Inner"); arrays: "ArrayOf_T".</summary>
    public string Type(TypeDesc type)
    {
        if (_types.TryGetValue(type, out string? name))
            return name;
        name = type.Kind switch
        {
            TypeKind.Array => "ArrayOf_" + Type(type.ElementType!),
            TypeKind.ByRef => "RefTo_" + Type(type.ElementType!),
            TypeKind.Pointer => "PointerTo_" + Type(type.ElementType!),
            TypeKind.Instantiation => Type(type.GenericDefinition!) + "_" + string.Join("_", type.TypeArguments.Select(Type)) + "_",
            _ => Sanitize(type.DeclaringType != null
                ? Type(type.DeclaringType) + "_" + type.Name
                : type.Namespace.Length > 0 ? type.Namespace.Replace('.', '_') + "_" + type.Name : type.Name),
        };
        if (type.IsDefinition)
        {
            // Two assemblies may define the same type name: keep C names unique.
            string unique = name;
            for (int i = 2; !_usedTypeNames.Add(unique); i++)
                unique = $"{name}_{i}";
            name = unique;
        }
        _types[type] = name;
        return name;
    }

    /// <summary>
    /// "Type__Method", with "_n" for overloads (n = position among same-named methods).
    /// Internal calls never get the suffix: the runtime implements them under that exact name.
    /// </summary>
    public string Method(MethodDesc method)
    {
        if (_methods.TryGetValue(method, out string? name))
            return name;
        string baseName = method.IsConstructor ? "ctor" : method.IsStaticConstructor ? "cctor" : Sanitize(method.Name);
        name = Type(method.Owner) + "__" + baseName;
        if (!method.IsInternalCall)
        {
            var overloads = method.Owner.Methods.Where(m => m.Name == method.Name).ToList();
            if (overloads.Count > 1)
                name += "_" + overloads.FindIndex(m => m.Handle == method.Handle);
        }
        if (method.MethodArguments.Count > 0)
            name += "__" + string.Join("_", method.MethodArguments.Select(Type)) + "_";
        _methods[method] = name;
        return name;
    }

    /// <summary>Instance fields are prefixed with the depth of their type in the class hierarchy, so a
    /// field hiding a base class field with the same name keeps its own slot.</summary>
    public string Field(FieldDesc field)
    {
        int depth = field.Owner.IsValueType ? 0 : field.Owner.BaseTypesAndSelf().Count() - 1;
        return $"f{depth}_{Sanitize(field.Name)}";
    }

    public string StaticField(FieldDesc field) => $"{Type(field.Owner)}__s_{Sanitize(field.Name)}";

    public string TypeInfo(TypeDesc type) => Type(type) + "__type";
    public string VTable(TypeDesc type) => Type(type) + "__vtable";
    public string ClassInit(TypeDesc type) => Type(type) + "__cctor_check";
}
