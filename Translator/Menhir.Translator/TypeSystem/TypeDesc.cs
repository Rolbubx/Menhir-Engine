using System.Reflection;
using System.Reflection.Metadata;

namespace Menhir.Translator.TypeSystem;

/// <summary>A loaded assembly.</summary>
public sealed class ModuleDesc
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required System.Reflection.PortableExecutable.PEReader PE { get; init; }
    public required MetadataReader Reader { get; init; }
}

public enum TypeKind
{
    /// <summary>A class, interface, struct or enum from metadata.</summary>
    Definition,
    /// <summary>A single-dimension, zero-based array (T[]).</summary>
    Array,
    /// <summary>A managed pointer (ref T).</summary>
    ByRef,
    /// <summary>An unmanaged pointer (T*).</summary>
    Pointer,
}

/// <summary>The built-in types that map straight to C types.</summary>
public enum PrimitiveKind
{
    None,
    Void,
    Boolean,
    Char,
    I1,
    U1,
    I2,
    U2,
    I4,
    U4,
    I8,
    U8,
    I,
    U,
    R4,
    R8,
}

/// <summary>A type: a definition from an assembly, or an array, byref or pointer of another type.</summary>
public sealed class TypeDesc
{
    private List<FieldDesc>? _fields;
    private List<MethodDesc>? _methods;

    internal TypeDesc(TypeSystemContext context, TypeKind kind)
    {
        Context = context;
        Kind = kind;
    }

    public TypeSystemContext Context { get; }
    public TypeKind Kind { get; }

    // Definitions
    public ModuleDesc? Module { get; internal init; }
    public TypeDefinitionHandle Handle { get; internal init; }
    public string Namespace { get; internal init; } = "";
    public string Name { get; internal init; } = "";
    public TypeDesc? DeclaringType { get; internal init; }
    public TypeAttributes Attributes { get; internal init; }
    public PrimitiveKind Primitive { get; internal set; }

    // Arrays, byrefs, pointers
    public TypeDesc? ElementType { get; internal init; }

    public bool IsDefinition => Kind == TypeKind.Definition;
    public bool IsArray => Kind == TypeKind.Array;
    public bool IsByRef => Kind == TypeKind.ByRef;
    public bool IsPointer => Kind == TypeKind.Pointer;
    public bool IsPrimitive => Primitive != PrimitiveKind.None;
    public bool IsVoid => Primitive == PrimitiveKind.Void;
    public bool IsInterface => IsDefinition && (Attributes & TypeAttributes.Interface) != 0;
    public bool IsAbstract => IsDefinition && (Attributes & TypeAttributes.Abstract) != 0;
    public bool IsBeforeFieldInit => (Attributes & TypeAttributes.BeforeFieldInit) != 0;
    public bool IsGenericDefinition => IsDefinition && Module!.Reader.GetTypeDefinition(Handle).GetGenericParameters().Count > 0;

    /// <summary>Structs, enums and primitives (but not System.ValueType / System.Enum themselves).</summary>
    public bool IsValueType
    {
        get
        {
            if (!IsDefinition)
                return false;
            if (IsPrimitive)
                return !IsVoid;
            TypeDesc? baseType = BaseType;
            if (baseType == null || this == Context.ValueTypeType || this == Context.EnumType)
                return false;
            return baseType == Context.ValueTypeType || baseType == Context.EnumType;
        }
    }

    public bool IsEnum => IsDefinition && BaseType == Context.EnumType;

    /// <summary>Held by reference: classes, interfaces, arrays, strings.</summary>
    public bool IsReferenceType => IsArray || (IsDefinition && !IsValueType && !IsVoid);

    public TypeDesc? BaseType
    {
        get
        {
            if (!IsDefinition)
                return IsArray ? Context.ArrayType : null;
            TypeDefinition definition = Module!.Reader.GetTypeDefinition(Handle);
            return definition.BaseType.IsNil ? null : Context.ResolveType(Module, definition.BaseType);
        }
    }

    /// <summary>Full name as .NET shows it: "Namespace.Outer+Inner", or "System.Int32[]".</summary>
    public string FullName => Kind switch
    {
        TypeKind.Array => ElementType!.FullName + "[]",
        TypeKind.ByRef => ElementType!.FullName + "&",
        TypeKind.Pointer => ElementType!.FullName + "*",
        _ => DeclaringType != null
            ? DeclaringType.FullName + "+" + Name
            : Namespace.Length > 0 ? Namespace + "." + Name : Name,
    };

    public IReadOnlyList<FieldDesc> Fields => _fields ??= LoadFields();
    public IReadOnlyList<MethodDesc> Methods => _methods ??= LoadMethods();

    /// <summary>The underlying integer type of an enum.</summary>
    public TypeDesc EnumUnderlyingType => Fields.First(f => !f.IsStatic).Type;

    /// <summary>Size declared with StructLayout(Size = n), or 0.</summary>
    public int ExplicitSize => IsDefinition ? Module!.Reader.GetTypeDefinition(Handle).GetLayout().Size : 0;

    public bool IsExplicitLayout => (Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout;

    public IEnumerable<TypeDesc> BaseTypesAndSelf()
    {
        for (TypeDesc? t = this; t != null; t = t.BaseType)
            yield return t;
    }

    public bool IsSubclassOf(TypeDesc other) => BaseTypesAndSelf().Contains(other);

    public MethodDesc? FindMethod(string name, Func<MethodDesc, bool>? predicate = null) =>
        Methods.FirstOrDefault(m => m.Name == name && (predicate == null || predicate(m)));

    private List<FieldDesc> LoadFields()
    {
        var fields = new List<FieldDesc>();
        if (!IsDefinition)
            return fields;
        MetadataReader reader = Module!.Reader;
        foreach (FieldDefinitionHandle handle in reader.GetTypeDefinition(Handle).GetFields())
            fields.Add(new FieldDesc(this, handle));
        return fields;
    }

    private List<MethodDesc> LoadMethods()
    {
        var methods = new List<MethodDesc>();
        if (!IsDefinition)
            return methods;
        MetadataReader reader = Module!.Reader;
        foreach (MethodDefinitionHandle handle in reader.GetTypeDefinition(Handle).GetMethods())
            methods.Add(new MethodDesc(this, handle));
        return methods;
    }

    public override string ToString() => FullName;
}

public sealed class FieldDesc
{
    private TypeDesc? _type;

    internal FieldDesc(TypeDesc owner, FieldDefinitionHandle handle)
    {
        Owner = owner;
        Handle = handle;
        FieldDefinition definition = owner.Module!.Reader.GetFieldDefinition(handle);
        Name = owner.Module.Reader.GetString(definition.Name);
        Attributes = definition.Attributes;
    }

    public TypeDesc Owner { get; }
    public FieldDefinitionHandle Handle { get; }
    public string Name { get; }
    public FieldAttributes Attributes { get; }

    public bool IsStatic => (Attributes & FieldAttributes.Static) != 0;

    /// <summary>A const: no storage, the compiler inlines its value.</summary>
    public bool IsLiteral => (Attributes & FieldAttributes.Literal) != 0;

    public bool HasRva => (Attributes & FieldAttributes.HasFieldRVA) != 0;

    public TypeDesc Type => _type ??= Owner.Context.DecodeFieldType(this);

    /// <summary>Raw bytes of a field with an RVA (constant array data).</summary>
    public ReadOnlySpan<byte> GetRvaData(int size)
    {
        FieldDefinition definition = Owner.Module!.Reader.GetFieldDefinition(Handle);
        int rva = definition.GetRelativeVirtualAddress();
        return Owner.Module.PE.GetSectionData(rva).GetContent(0, size).AsSpan();
    }

    public override string ToString() => $"{Owner.FullName}::{Name}";
}

public sealed class MethodDesc
{
    private MethodSignature<TypeDesc>? _signature;

    internal MethodDesc(TypeDesc owner, MethodDefinitionHandle handle)
    {
        Owner = owner;
        Handle = handle;
        MethodDefinition definition = owner.Module!.Reader.GetMethodDefinition(handle);
        Name = owner.Module.Reader.GetString(definition.Name);
        Attributes = definition.Attributes;
        ImplAttributes = definition.ImplAttributes;
        BodyRva = definition.RelativeVirtualAddress;
    }

    public TypeDesc Owner { get; }
    public MethodDefinitionHandle Handle { get; }
    public string Name { get; }
    public MethodAttributes Attributes { get; }
    public MethodImplAttributes ImplAttributes { get; }
    public int BodyRva { get; }

    public bool IsStatic => (Attributes & MethodAttributes.Static) != 0;
    public bool IsVirtual => (Attributes & MethodAttributes.Virtual) != 0;
    public bool IsAbstract => (Attributes & MethodAttributes.Abstract) != 0;
    public bool IsNewSlot => (Attributes & MethodAttributes.NewSlot) != 0;
    public bool IsConstructor => Name == ".ctor";
    public bool IsStaticConstructor => Name == ".cctor";
    public bool IsInternalCall => (ImplAttributes & MethodImplAttributes.InternalCall) != 0 ||
                                  (Attributes & MethodAttributes.PinvokeImpl) != 0;
    public bool HasBody => BodyRva != 0;
    public bool IsGeneric => Owner.Module!.Reader.GetMethodDefinition(Handle).GetGenericParameters().Count > 0;

    public MethodSignature<TypeDesc> Signature => _signature ??= Owner.Context.DecodeMethodSignature(this);

    public TypeDesc ReturnType => Signature.ReturnType;
    public IReadOnlyList<TypeDesc> ParameterTypes => Signature.ParameterTypes;

    /// <summary>Slot in the vtable of virtual methods (set by the vtable builder), or -1.</summary>
    public int VTableSlot { get; internal set; } = -1;

    public MethodBodyBlock GetBody() => Owner.Module!.PE.GetMethodBody(BodyRva);

    public bool SignatureEquals(MethodDesc other) =>
        Signature.ReturnType == other.Signature.ReturnType &&
        Signature.ParameterTypes.SequenceEqual(other.Signature.ParameterTypes) &&
        IsStatic == other.IsStatic;

    public override string ToString() =>
        $"{Owner.FullName}::{Name}({string.Join(", ", ParameterTypes.Select(p => p.FullName))})";
}
