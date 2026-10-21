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
    /// <summary>A generic type with its type arguments (List&lt;int&gt;): a class or struct like a definition.</summary>
    Instantiation,
    /// <summary>T of a generic type (!0) or method (!!0), in signatures before substitution.</summary>
    GenericParameter,
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

/// <summary>
/// A type: a definition from an assembly, a generic instantiation of one, an array, byref or
/// pointer of another type, or a generic parameter (only in signatures, before substitution).
/// </summary>
public sealed class TypeDesc
{
    private List<FieldDesc>? _fields;
    private List<MethodDesc>? _methods;
    private TypeDesc? _baseType;
    private bool _baseTypeLoaded;

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

    // Instantiations: the generic definition and the type arguments
    public TypeDesc? GenericDefinition { get; internal init; }
    public IReadOnlyList<TypeDesc> TypeArguments { get; internal init; } = [];

    // Generic parameters
    public int GenericIndex { get; internal init; }
    public bool IsMethodParameter { get; internal init; }

    /// <summary>A class, struct, enum or interface with metadata: a definition or an instantiation.</summary>
    public bool IsDefinition => Kind is TypeKind.Definition or TypeKind.Instantiation;
    public bool IsInstantiation => Kind == TypeKind.Instantiation;
    public bool IsGenericParameter => Kind == TypeKind.GenericParameter;
    public bool IsArray => Kind == TypeKind.Array;
    public bool IsByRef => Kind == TypeKind.ByRef;
    public bool IsPointer => Kind == TypeKind.Pointer;
    public bool IsPrimitive => Primitive != PrimitiveKind.None;
    public bool IsVoid => Primitive == PrimitiveKind.Void;
    public bool IsInterface => IsDefinition && (Attributes & TypeAttributes.Interface) != 0;
    public bool IsAbstract => IsDefinition && (Attributes & TypeAttributes.Abstract) != 0;
    public bool IsSealed => IsDefinition && (Attributes & TypeAttributes.Sealed) != 0;
    public bool IsBeforeFieldInit => (Attributes & TypeAttributes.BeforeFieldInit) != 0;
    public bool IsGenericDefinition => Kind == TypeKind.Definition && Module!.Reader.GetTypeDefinition(Handle).GetGenericParameters().Count > 0;

    /// <summary>A generic parameter appears somewhere in the type (it cannot be compiled as is).</summary>
    public bool ContainsGenericParameters => Kind switch
    {
        TypeKind.GenericParameter => true,
        TypeKind.Instantiation => TypeArguments.Any(a => a.ContainsGenericParameters),
        TypeKind.Array or TypeKind.ByRef or TypeKind.Pointer => ElementType!.ContainsGenericParameters,
        _ => IsGenericDefinition,
    };

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
            if (!_baseTypeLoaded)
            {
                TypeDefinition definition = Module!.Reader.GetTypeDefinition(Handle);
                _baseType = definition.BaseType.IsNil
                    ? null
                    : Context.Substitute(Context.ResolveType(Module, definition.BaseType), TypeArguments, []);
                _baseTypeLoaded = true;
            }
            return _baseType;
        }
    }

    /// <summary>Full name as .NET shows it: "Namespace.Outer+Inner", or "System.Int32[]".</summary>
    public string FullName => Kind switch
    {
        TypeKind.Array => ElementType!.FullName + "[]",
        TypeKind.ByRef => ElementType!.FullName + "&",
        TypeKind.Pointer => ElementType!.FullName + "*",
        TypeKind.Instantiation => GenericDefinition!.FullName + "[" + string.Join(",", TypeArguments.Select(a => a.FullName)) + "]",
        TypeKind.GenericParameter => (IsMethodParameter ? "!!" : "!") + GenericIndex,
        _ => DeclaringType != null
            ? DeclaringType.FullName + "+" + Name
            : Namespace.Length > 0 ? Namespace + "." + Name : Name,
    };

    public IReadOnlyList<FieldDesc> Fields => _fields ??= LoadFields();
    public IReadOnlyList<MethodDesc> Methods => _methods ??= LoadMethods();

    /// <summary>Interfaces the type declares it implements (not those of its base types).</summary>
    public IEnumerable<TypeDesc> DeclaredInterfaces
    {
        get
        {
            if (!IsDefinition)
                yield break;
            foreach (InterfaceImplementationHandle handle in Module!.Reader.GetTypeDefinition(Handle).GetInterfaceImplementations())
            {
                InterfaceImplementation implementation = Module.Reader.GetInterfaceImplementation(handle);
                yield return Context.Substitute(Context.ResolveType(Module, implementation.Interface), TypeArguments, []);
            }
        }
    }

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
            methods.Add(new MethodDesc(this, handle, []));
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

    public TypeDesc Type => _type ??= Owner.Context.Substitute(Owner.Context.DecodeFieldType(this), Owner.TypeArguments, []);

    /// <summary>The field as its generic definition declares it (itself for non-generic types).</summary>
    public FieldDesc Definition =>
        Owner.IsInstantiation ? Owner.GenericDefinition!.Fields.First(f => f.Handle == Handle) : this;

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
    private MethodSignature<TypeDesc>? _formalSignature;

    internal MethodDesc(TypeDesc owner, MethodDefinitionHandle handle, IReadOnlyList<TypeDesc> methodArguments)
    {
        Owner = owner;
        Handle = handle;
        MethodArguments = methodArguments;
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
    public bool IsFinal => (Attributes & MethodAttributes.Final) != 0;
    public bool IsConstructor => Name == ".ctor";
    public bool IsStaticConstructor => Name == ".cctor";
    public bool IsInternalCall => (ImplAttributes & MethodImplAttributes.InternalCall) != 0 ||
                                  (Attributes & MethodAttributes.PinvokeImpl) != 0;
    public bool HasBody => BodyRva != 0;
    /// <summary>Declares type parameters of its own (instantiated or not).</summary>
    public bool IsGeneric => GenericArity > 0;
    public int GenericArity => Owner.Module!.Reader.GetMethodDefinition(Handle).GetGenericParameters().Count;

    /// <summary>The type arguments of a generic method instantiation (empty otherwise).</summary>
    public IReadOnlyList<TypeDesc> MethodArguments { get; }

    /// <summary>A generic method without its type arguments, or a method of a generic definition: not compiled.</summary>
    public bool IsOpen => (IsGeneric && MethodArguments.Count == 0) || Owner.ContainsGenericParameters;

    /// <summary>The signature with the owner's and the method's type arguments substituted.</summary>
    public MethodSignature<TypeDesc> Signature => _signature ??= Owner.Context.SubstituteSignature(FormalSignature, Owner.TypeArguments, MethodArguments);

    /// <summary>The signature as declared, with generic parameters (!0, !!0): what member references match.</summary>
    public MethodSignature<TypeDesc> FormalSignature => _formalSignature ??= Owner.Context.DecodeMethodSignature(this);

    /// <summary>The method as its generic type declares it, without method arguments.</summary>
    public MethodDesc Definition =>
        Owner.IsInstantiation ? Owner.GenericDefinition!.Methods.First(m => m.Handle == Handle) : this;

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
        $"{Owner.FullName}::{Name}{(MethodArguments.Count > 0 ? "<" + string.Join(",", MethodArguments.Select(a => a.FullName)) + ">" : "")}" +
        $"({string.Join(", ", ParameterTypes.Select(p => p.FullName))})";
}
