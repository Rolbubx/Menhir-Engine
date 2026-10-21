using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using Menhir.Translator.TypeSystem;

namespace Menhir.Translator.CodeGen;

/// <summary>
/// For a game's scripts: the <c>m_scripts</c> table the player (Engine/Player/src/mh_scripts.c) runs
/// them with. Each class deriving from the script base type (non-abstract, not generic, with a
/// public parameterless constructor) gets a factory and the fields a scene can set (the public
/// instance fields of the kinds the editor's inspector edits, except [HideInInspector] ones), the
/// same as the editor's ScriptFields.
/// </summary>
internal sealed class ScriptGlue(Il2cTranslator translator)
{
    // M_FIELD_* in runtime.h.
    private enum FieldKind { Int, Long, Float, Double, Bool, String, Enum, Vector2, Vector3 }

    private static readonly string[] Callbacks = ["Awake", "Start", "Update", "LateUpdate"];

    private CNames Names => translator.Names;

    public void Write(StringBuilder c, IEnumerable<TypeDesc> types, string scriptBaseName)
    {
        TypeDesc scriptBase = types.FirstOrDefault(t => t.FullName == scriptBaseName)
                              ?? throw new TranslationException($"The script base class {scriptBaseName} is not part of the program.");
        FieldDesc entity = scriptBase.Fields.FirstOrDefault(f => f.Name == "_entity" && f.Type.Primitive == PrimitiveKind.I4)
                           ?? throw new TranslationException($"{scriptBaseName} has no int _entity field.");
        FieldDesc enabled = scriptBase.Fields.FirstOrDefault(f => f.Name == "enabled" && f.Type.Primitive == PrimitiveKind.Boolean)
                            ?? throw new TranslationException($"{scriptBaseName} has no bool enabled field.");
        var slots = new List<int>();
        foreach (string callback in Callbacks)
        {
            MethodDesc method = scriptBase.Methods.FirstOrDefault(m => m.Name == callback && m.IsVirtual && m.ParameterTypes.Count == 0)
                                ?? throw new TranslationException($"{scriptBaseName} has no virtual {callback}().");
            slots.Add(method.VTableSlot);
        }

        List<TypeDesc> classes = types
            .Where(t => t != scriptBase && t.IsSubclassOf(scriptBase) && !t.IsAbstract && !t.ContainsGenericParameters &&
                        !t.IsInstantiation && Constructor(t) != null)
            .OrderBy(ClassName, StringComparer.Ordinal)
            .ToList();

        c.Append("/* ---- Scripts: what the player runs them with (MScriptTable in runtime.h) ---- */\n\n");
        var entries = new List<string>();
        for (int i = 0; i < classes.Count; i++)
        {
            TypeDesc type = classes[i];
            string structName = $"struct {Names.Type(type)}";
            c.Append($"static MObject* m_script_create_{i}(void)\n{{\n");
            if (translator.ClassInitCall(type) is { Length: > 0 } init)
                c.Append($"    {init}\n");
            c.Append($"    MObject* script = m_new_object(&{Names.TypeInfo(type)});\n");
            c.Append($"    {Names.Method(Constructor(type)!)}(({translator.CType(type)})script);\n    return script;\n}}\n");

            var fields = new List<string>();
            foreach ((FieldDesc field, FieldKind kind) in Fields(type, scriptBase))
            {
                string enumType = kind == FieldKind.Enum ? "&" + Names.TypeInfo(field.Type) : "NULL";
                int size = kind switch
                {
                    FieldKind.Enum => SizeOf(field.Type.EnumUnderlyingType.Primitive),
                    FieldKind.Long or FieldKind.Double => 8,
                    FieldKind.Bool => 1,
                    FieldKind.Vector2 => 8,
                    FieldKind.Vector3 => 12,
                    FieldKind.String => 0,
                    _ => 4,
                };
                fields.Add($"{{ {Il2cTranslator.CString(field.Name)}, offsetof({structName}, {Names.Field(field)}), " +
                           $"{(int)kind}, {size}, {enumType} }}");
            }
            string fieldTable = "NULL";
            if (fields.Count > 0)
            {
                fieldTable = $"m_script_fields_{i}";
                c.Append($"static const MScriptField {fieldTable}[] = {{\n    {string.Join(",\n    ", fields)},\n}};\n");
            }
            entries.Add($"{{ {Il2cTranslator.CString(ClassName(type))}, m_script_create_{i}, {fields.Count}, {fieldTable} }}");
            c.Append('\n');
        }

        if (entries.Count > 0)
            c.Append($"static const MScriptClass m_script_classes[] = {{\n    {string.Join(",\n    ", entries)},\n}};\n\n");
        string baseStruct = $"struct {Names.Type(scriptBase)}";
        c.Append("const MScriptTable m_scripts = {\n");
        c.Append($"    {(entries.Count > 0 ? "m_script_classes" : "NULL")}, {entries.Count},\n");
        c.Append($"    offsetof({baseStruct}, {Names.Field(entity)}), offsetof({baseStruct}, {Names.Field(enabled)}),\n");
        c.Append($"    {{ {string.Join(", ", slots)} }},\n}};\n\n");
    }

    /// <summary>The name the editor saves: like .NET's Type.FullName ("Game.Player", nested "Game.Outer+Inner").</summary>
    private static string ClassName(TypeDesc type) =>
        type.DeclaringType != null ? ClassName(type.DeclaringType) + "+" + type.Name
        : type.Namespace.Length > 0 ? type.Namespace + "." + type.Name : type.Name;

    private static MethodDesc? Constructor(TypeDesc type) =>
        type.Methods.FirstOrDefault(m => m.IsConstructor && m.ParameterTypes.Count == 0 &&
                                         (m.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public);

    private IEnumerable<(FieldDesc Field, FieldKind Kind)> Fields(TypeDesc type, TypeDesc scriptBase)
    {
        // Base classes first, like the editor's field order; only public fields of the script's own classes.
        foreach (TypeDesc owner in type.BaseTypesAndSelf().TakeWhile(t => t != scriptBase).Reverse())
        {
            foreach (FieldDesc field in owner.Fields)
            {
                if (field.IsStatic || field.IsLiteral || (field.Attributes & FieldAttributes.InitOnly) != 0 ||
                    (field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public ||
                    HasAttribute(field, "MenhirEngine.HideInInspectorAttribute") || KindOf(field.Type) is not { } kind)
                    continue;
                yield return (field, kind);
            }
        }
    }

    private static FieldKind? KindOf(TypeDesc type)
    {
        if (type.IsEnum)
            return FieldKind.Enum;
        switch (type.Primitive)
        {
            case PrimitiveKind.I4: return FieldKind.Int;
            case PrimitiveKind.I8: return FieldKind.Long;
            case PrimitiveKind.R4: return FieldKind.Float;
            case PrimitiveKind.R8: return FieldKind.Double;
            case PrimitiveKind.Boolean: return FieldKind.Bool;
        }
        if (type.FullName == "System.String")
            return FieldKind.String;
        if (type.FullName == "MenhirEngine.Vector2" && FloatFields(type, "x", "y"))
            return FieldKind.Vector2;
        if (type.FullName == "MenhirEngine.Vector3" && FloatFields(type, "x", "y", "z"))
            return FieldKind.Vector3;
        return null;
    }

    /// <summary>The vector's instance fields are exactly these floats, in order (the player copies them as an array).</summary>
    private static bool FloatFields(TypeDesc type, params string[] names)
    {
        List<FieldDesc> fields = type.Fields.Where(f => !f.IsStatic).ToList();
        return fields.Count == names.Length &&
               fields.Select((f, i) => f.Name == names[i] && f.Type.Primitive == PrimitiveKind.R4).All(ok => ok);
    }

    private static int SizeOf(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.I1 or PrimitiveKind.U1 or PrimitiveKind.Boolean => 1,
        PrimitiveKind.I2 or PrimitiveKind.U2 or PrimitiveKind.Char => 2,
        PrimitiveKind.I8 or PrimitiveKind.U8 => 8,
        _ => 4,
    };

    private bool HasAttribute(FieldDesc field, string attributeName)
    {
        MetadataReader reader = field.Owner.Module!.Reader;
        foreach (CustomAttributeHandle handle in reader.GetFieldDefinition(field.Handle).GetCustomAttributes())
        {
            EntityHandle constructor = reader.GetCustomAttribute(handle).Constructor;
            EntityHandle parent = constructor.Kind == HandleKind.MemberReference
                ? reader.GetMemberReference((MemberReferenceHandle)constructor).Parent
                : reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType();
            if (translator.Context.ResolveType(field.Owner.Module, parent).FullName == attributeName)
                return true;
        }
        return false;
    }
}
