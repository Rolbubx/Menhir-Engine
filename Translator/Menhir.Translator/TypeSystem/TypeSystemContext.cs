using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Menhir.Translator.TypeSystem;

/// <summary>Raised for code the translator cannot handle (yet), with a readable reason.</summary>
public sealed class TranslationException(string message) : Exception(message);

/// <summary>The type arguments that generic parameters stand for while resolving a method's tokens.</summary>
public readonly record struct GenericContext(IReadOnlyList<TypeDesc> TypeArguments, IReadOnlyList<TypeDesc> MethodArguments)
{
    public static GenericContext Empty { get; } = new([], []);

    public static GenericContext Of(MethodDesc method) => new(method.Owner.TypeArguments, method.MethodArguments);
}

/// <summary>
/// The loaded assemblies and every type in them, resolved across assemblies. Each type exists
/// once, so types compare by reference.
/// </summary>
public sealed class TypeSystemContext : IDisposable
{
    private readonly List<ModuleDesc> _modules = new();
    private readonly Dictionary<(ModuleDesc, TypeDefinitionHandle), TypeDesc> _definitions = new();
    private readonly Dictionary<(ModuleDesc, string), TypeDesc> _byName = new();
    private readonly Dictionary<(TypeDesc, TypeKind), TypeDesc> _constructed = new();
    private readonly Dictionary<string, TypeDesc> _instantiations = new();
    private readonly Dictionary<string, MethodDesc> _methodInstantiations = new();
    private readonly Dictionary<(bool, int), TypeDesc> _genericParameters = new();
    private readonly SignatureProvider _signatures;

    public TypeSystemContext()
    {
        _signatures = new SignatureProvider(this);
    }

    public IReadOnlyList<ModuleDesc> Modules => _modules;

    /// <summary>The assembly defining System.Object (Menhir.CoreLib).</summary>
    public ModuleDesc CoreLib { get; private set; } = null!;

    public TypeDesc ObjectType { get; private set; } = null!;
    public TypeDesc StringType { get; private set; } = null!;
    public TypeDesc ValueTypeType { get; private set; } = null!;
    public TypeDesc EnumType { get; private set; } = null!;
    public TypeDesc ArrayType { get; private set; } = null!;

    private readonly Dictionary<PrimitiveKind, TypeDesc> _primitives = new();

    public TypeDesc GetPrimitive(PrimitiveKind kind) => _primitives[kind];

    /// <summary>Loads the core library first, then the program's assemblies.</summary>
    public void Load(string coreLibPath, IEnumerable<string> assemblyPaths)
    {
        CoreLib = LoadModule(coreLibPath);
        foreach (string path in assemblyPaths)
            LoadModule(path);

        ObjectType = CoreType("System", "Object");
        StringType = CoreType("System", "String");
        ValueTypeType = CoreType("System", "ValueType");
        EnumType = CoreType("System", "Enum");
        ArrayType = CoreType("System", "Array");
        (PrimitiveKind Kind, string Name)[] primitives =
        [
            (PrimitiveKind.Void, "Void"), (PrimitiveKind.Boolean, "Boolean"), (PrimitiveKind.Char, "Char"),
            (PrimitiveKind.I1, "SByte"), (PrimitiveKind.U1, "Byte"), (PrimitiveKind.I2, "Int16"),
            (PrimitiveKind.U2, "UInt16"), (PrimitiveKind.I4, "Int32"), (PrimitiveKind.U4, "UInt32"),
            (PrimitiveKind.I8, "Int64"), (PrimitiveKind.U8, "UInt64"), (PrimitiveKind.I, "IntPtr"),
            (PrimitiveKind.U, "UIntPtr"), (PrimitiveKind.R4, "Single"), (PrimitiveKind.R8, "Double"),
        ];
        foreach ((PrimitiveKind kind, string name) in primitives)
        {
            TypeDesc type = CoreType("System", name);
            type.Primitive = kind;
            _primitives[kind] = type;
        }
    }

    private ModuleDesc LoadModule(string path)
    {
        var pe = new PEReader(File.OpenRead(path));
        if (!pe.HasMetadata)
            throw new TranslationException($"{path} is not a .NET assembly.");
        MetadataReader reader = pe.GetMetadataReader();
        var module = new ModuleDesc
        {
            Name = reader.GetString(reader.GetAssemblyDefinition().Name),
            Path = path,
            PE = pe,
            Reader = reader,
        };
        _modules.Add(module);

        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            GetDefinition(module, handle);
        return module;
    }

    public IEnumerable<TypeDesc> AllDefinitions => _definitions.Values;

    public TypeDesc CoreType(string ns, string name) =>
        _byName.TryGetValue((CoreLib, ns.Length > 0 ? ns + "." + name : name), out TypeDesc? type)
            ? type
            : throw new TranslationException($"The core library has no type {ns}.{name}.");

    internal TypeDesc GetDefinition(ModuleDesc module, TypeDefinitionHandle handle)
    {
        if (_definitions.TryGetValue((module, handle), out TypeDesc? existing))
            return existing;

        MetadataReader reader = module.Reader;
        TypeDefinition definition = reader.GetTypeDefinition(handle);
        TypeDefinitionHandle declaring = definition.GetDeclaringType();
        var type = new TypeDesc(this, TypeKind.Definition)
        {
            Module = module,
            Handle = handle,
            Namespace = reader.GetString(definition.Namespace),
            Name = reader.GetString(definition.Name),
            Attributes = definition.Attributes,
            DeclaringType = declaring.IsNil ? null : GetDefinition(module, declaring),
        };
        _definitions[(module, handle)] = type;
        _byName[(module, type.FullName)] = type;
        return type;
    }

    public TypeDesc MakeArray(TypeDesc element) => Construct(element, TypeKind.Array);
    public TypeDesc MakeByRef(TypeDesc element) => Construct(element, TypeKind.ByRef);
    public TypeDesc MakePointer(TypeDesc element) => Construct(element, TypeKind.Pointer);

    private TypeDesc Construct(TypeDesc element, TypeKind kind)
    {
        if (!_constructed.TryGetValue((element, kind), out TypeDesc? type))
        {
            type = new TypeDesc(this, kind) { ElementType = element };
            _constructed[(element, kind)] = type;
        }
        return type;
    }

    public IEnumerable<TypeDesc> ConstructedTypes => _constructed.Values;

    // ---- Generics -----------------------------------------------------------------------------

    public TypeDesc GetGenericParameter(bool method, int index)
    {
        if (!_genericParameters.TryGetValue((method, index), out TypeDesc? type))
        {
            type = new TypeDesc(this, TypeKind.GenericParameter) { GenericIndex = index, IsMethodParameter = method, Name = (method ? "!!" : "!") + index };
            _genericParameters[(method, index)] = type;
        }
        return type;
    }

    /// <summary>The generic type <paramref name="definition"/> with <paramref name="arguments"/> (each instantiation exists once).</summary>
    public TypeDesc Instantiate(TypeDesc definition, IReadOnlyList<TypeDesc> arguments)
    {
        if (definition.IsInstantiation)
            definition = definition.GenericDefinition!;
        if (!definition.IsGenericDefinition)
            throw new TranslationException($"{definition} is not generic.");
        string key = KeyOf(definition) + "<" + string.Join(",", arguments.Select(KeyOf)) + ">";
        if (!_instantiations.TryGetValue(key, out TypeDesc? type))
        {
            type = new TypeDesc(this, TypeKind.Instantiation)
            {
                Module = definition.Module,
                Handle = definition.Handle,
                Namespace = definition.Namespace,
                Name = definition.Name,
                Attributes = definition.Attributes,
                DeclaringType = definition.DeclaringType,
                GenericDefinition = definition,
                TypeArguments = arguments.ToArray(),
            };
            _instantiations[key] = type;
        }
        return type;
    }

    /// <summary>A generic method with its type arguments.</summary>
    public MethodDesc InstantiateMethod(MethodDesc method, IReadOnlyList<TypeDesc> arguments)
    {
        if (!method.IsGeneric)
            throw new TranslationException($"{method} is not generic.");
        string key = KeyOf(method.Owner) + "::" + method.Handle.GetHashCode() + "<" + string.Join(",", arguments.Select(KeyOf)) + ">";
        if (!_methodInstantiations.TryGetValue(key, out MethodDesc? instance))
        {
            instance = new MethodDesc(method.Owner, method.Handle, arguments.ToArray());
            _methodInstantiations[key] = instance;
        }
        return instance;
    }

    public IEnumerable<TypeDesc> Instantiations => _instantiations.Values;

    /// <summary>A key unique per type (module and handle for definitions, so equal names in two assemblies differ).</summary>
    private static string KeyOf(TypeDesc type) => type.Kind switch
    {
        TypeKind.Definition => $"{type.Module!.Name}:{type.FullName}",
        TypeKind.Instantiation => KeyOf(type.GenericDefinition!) + "<" + string.Join(",", type.TypeArguments.Select(KeyOf)) + ">",
        TypeKind.Array => KeyOf(type.ElementType!) + "[]",
        TypeKind.ByRef => KeyOf(type.ElementType!) + "&",
        TypeKind.Pointer => KeyOf(type.ElementType!) + "*",
        _ => type.FullName,
    };

    /// <summary>Replaces generic parameters by the type (!n) and method (!!n) arguments.</summary>
    public TypeDesc Substitute(TypeDesc type, IReadOnlyList<TypeDesc> typeArguments, IReadOnlyList<TypeDesc> methodArguments)
    {
        switch (type.Kind)
        {
            case TypeKind.GenericParameter:
            {
                IReadOnlyList<TypeDesc> arguments = type.IsMethodParameter ? methodArguments : typeArguments;
                return type.GenericIndex < arguments.Count ? arguments[type.GenericIndex] : type;
            }
            case TypeKind.Array:
                return MakeArray(Substitute(type.ElementType!, typeArguments, methodArguments));
            case TypeKind.ByRef:
                return MakeByRef(Substitute(type.ElementType!, typeArguments, methodArguments));
            case TypeKind.Pointer:
                return MakePointer(Substitute(type.ElementType!, typeArguments, methodArguments));
            case TypeKind.Instantiation:
                if (!type.ContainsGenericParameters)
                    return type;
                return Instantiate(type.GenericDefinition!, type.TypeArguments.Select(a => Substitute(a, typeArguments, methodArguments)).ToArray());
            default:
                return type;
        }
    }

    public TypeDesc Substitute(TypeDesc type, GenericContext context) =>
        Substitute(type, context.TypeArguments, context.MethodArguments);

    public MethodSignature<TypeDesc> SubstituteSignature(MethodSignature<TypeDesc> signature, IReadOnlyList<TypeDesc> typeArguments,
        IReadOnlyList<TypeDesc> methodArguments)
    {
        if (typeArguments.Count == 0 && methodArguments.Count == 0)
            return signature;
        return new MethodSignature<TypeDesc>(signature.Header, Substitute(signature.ReturnType, typeArguments, methodArguments),
            signature.RequiredParameterCount, signature.GenericParameterCount,
            signature.ParameterTypes.Select(p => Substitute(p, typeArguments, methodArguments)).ToImmutableArray());
    }

    // ---- Resolution -------------------------------------------------------------------------

    /// <summary>A TypeDefinition, TypeReference or TypeSpecification handle, from <paramref name="module"/>.</summary>
    public TypeDesc ResolveType(ModuleDesc module, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return GetDefinition(module, (TypeDefinitionHandle)handle);
            case HandleKind.TypeReference:
                return ResolveTypeReference(module, (TypeReferenceHandle)handle);
            case HandleKind.TypeSpecification:
                TypeSpecification spec = module.Reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                return spec.DecodeSignature(_signatures, module);
            default:
                throw new TranslationException($"Unexpected type handle {handle.Kind}.");
        }
    }

    private TypeDesc ResolveTypeReference(ModuleDesc module, TypeReferenceHandle handle)
    {
        MetadataReader reader = module.Reader;
        TypeReference reference = reader.GetTypeReference(handle);
        string name = reader.GetString(reference.Name);
        string ns = reader.GetString(reference.Namespace);

        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            TypeDesc outer = ResolveTypeReference(module, (TypeReferenceHandle)reference.ResolutionScope);
            return _byName.TryGetValue((outer.Module!, outer.FullName + "+" + name), out TypeDesc? nested)
                ? nested
                : throw new TranslationException($"Nested type {outer.FullName}+{name} not found.");
        }

        ModuleDesc target = reference.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => FindModule(reader.GetString(
                reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name)),
            HandleKind.ModuleDefinition => module,
            _ => throw new TranslationException($"Unsupported resolution scope for {ns}.{name}."),
        };
        string fullName = ns.Length > 0 ? ns + "." + name : name;
        return _byName.TryGetValue((target, fullName), out TypeDesc? type)
            ? type
            : throw new TranslationException($"Type {fullName} not found in {target.Name}.");
    }

    private ModuleDesc FindModule(string assemblyName) =>
        _modules.FirstOrDefault(m => m.Name == assemblyName)
        ?? throw new TranslationException(
            $"Assembly '{assemblyName}' is referenced but not loaded. Code translated by IL2C must be " +
            "compiled against Menhir.CoreLib, not the .NET libraries (NoStdLib, see Translator/README.md).");

    /// <summary>A type token, with the generic parameters of <paramref name="context"/> substituted.</summary>
    public TypeDesc ResolveType(ModuleDesc module, EntityHandle handle, GenericContext context) =>
        Substitute(ResolveType(module, handle), context);

    /// <summary>A MethodDefinition, MemberReference or MethodSpecification (to a method), used by call instructions.</summary>
    public MethodDesc ResolveMethod(ModuleDesc module, EntityHandle handle) => ResolveMethod(module, handle, GenericContext.Empty);

    public MethodDesc ResolveMethod(ModuleDesc module, EntityHandle handle, GenericContext context)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                MethodDefinition definition = module.Reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                TypeDesc owner = GetDefinition(module, definition.GetDeclaringType());
                return owner.Methods.First(m => m.Handle == (MethodDefinitionHandle)handle);
            }
            case HandleKind.MemberReference:
            {
                MemberReference reference = module.Reader.GetMemberReference((MemberReferenceHandle)handle);
                if (reference.Parent.Kind is not (HandleKind.TypeReference or HandleKind.TypeDefinition or HandleKind.TypeSpecification))
                    throw new TranslationException("Unsupported method reference parent.");
                TypeDesc owner = ResolveType(module, (EntityHandle)reference.Parent, context);
                string name = module.Reader.GetString(reference.Name);
                // Both signatures as declared (!0, !!0), so they compare whatever the type arguments.
                MethodSignature<TypeDesc> signature = reference.DecodeMethodSignature(_signatures, module);
                foreach (MethodDesc candidate in owner.Methods)
                {
                    MethodSignature<TypeDesc> formal = candidate.FormalSignature;
                    if (candidate.Name == name && formal.ReturnType == signature.ReturnType &&
                        formal.ParameterTypes.SequenceEqual(signature.ParameterTypes) &&
                        candidate.IsStatic != signature.Header.IsInstance &&
                        candidate.GenericArity == signature.GenericParameterCount)
                        return candidate;
                }
                throw new TranslationException($"Method {owner.FullName}::{name} not found (is it in Menhir.CoreLib?).");
            }
            case HandleKind.MethodSpecification:
            {
                MethodSpecification specification = module.Reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                MethodDesc method = ResolveMethod(module, specification.Method, context);
                ImmutableArray<TypeDesc> arguments = specification.DecodeSignature(_signatures, module);
                return InstantiateMethod(method, arguments.Select(a => Substitute(a, context)).ToArray());
            }
            default:
                throw new TranslationException($"Unexpected method handle {handle.Kind}.");
        }
    }

    public FieldDesc ResolveField(ModuleDesc module, EntityHandle handle) => ResolveField(module, handle, GenericContext.Empty);

    public FieldDesc ResolveField(ModuleDesc module, EntityHandle handle, GenericContext context)
    {
        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
            {
                FieldDefinition definition = module.Reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                TypeDesc owner = GetDefinition(module, definition.GetDeclaringType());
                return owner.Fields.First(f => f.Handle == (FieldDefinitionHandle)handle);
            }
            case HandleKind.MemberReference:
            {
                MemberReference reference = module.Reader.GetMemberReference((MemberReferenceHandle)handle);
                TypeDesc owner = ResolveType(module, (EntityHandle)reference.Parent, context);
                string name = module.Reader.GetString(reference.Name);
                return owner.Fields.FirstOrDefault(f => f.Name == name)
                       ?? throw new TranslationException($"Field {owner.FullName}::{name} not found.");
            }
            default:
                throw new TranslationException($"Unexpected field handle {handle.Kind}.");
        }
    }

    public string ResolveUserString(ModuleDesc module, int token) =>
        module.Reader.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF));

    // ---- Signatures -------------------------------------------------------------------------

    internal TypeDesc DecodeFieldType(FieldDesc field) =>
        field.Owner.Module!.Reader.GetFieldDefinition(field.Handle).DecodeSignature(_signatures, field.Owner.Module);

    internal MethodSignature<TypeDesc> DecodeMethodSignature(MethodDesc method) =>
        method.Owner.Module!.Reader.GetMethodDefinition(method.Handle).DecodeSignature(_signatures, method.Owner.Module);

    public ImmutableArray<TypeDesc> DecodeLocals(ModuleDesc module, StandaloneSignatureHandle handle, GenericContext context) =>
        handle.IsNil
            ? ImmutableArray<TypeDesc>.Empty
            : module.Reader.GetStandaloneSignature(handle).DecodeLocalSignature(_signatures, module)
                .Select(t => Substitute(t, context)).ToImmutableArray();

    private sealed class SignatureProvider(TypeSystemContext context) : ISignatureTypeProvider<TypeDesc, ModuleDesc>
    {
        public TypeDesc GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Void => context.GetPrimitive(PrimitiveKind.Void),
            PrimitiveTypeCode.Boolean => context.GetPrimitive(PrimitiveKind.Boolean),
            PrimitiveTypeCode.Char => context.GetPrimitive(PrimitiveKind.Char),
            PrimitiveTypeCode.SByte => context.GetPrimitive(PrimitiveKind.I1),
            PrimitiveTypeCode.Byte => context.GetPrimitive(PrimitiveKind.U1),
            PrimitiveTypeCode.Int16 => context.GetPrimitive(PrimitiveKind.I2),
            PrimitiveTypeCode.UInt16 => context.GetPrimitive(PrimitiveKind.U2),
            PrimitiveTypeCode.Int32 => context.GetPrimitive(PrimitiveKind.I4),
            PrimitiveTypeCode.UInt32 => context.GetPrimitive(PrimitiveKind.U4),
            PrimitiveTypeCode.Int64 => context.GetPrimitive(PrimitiveKind.I8),
            PrimitiveTypeCode.UInt64 => context.GetPrimitive(PrimitiveKind.U8),
            PrimitiveTypeCode.IntPtr => context.GetPrimitive(PrimitiveKind.I),
            PrimitiveTypeCode.UIntPtr => context.GetPrimitive(PrimitiveKind.U),
            PrimitiveTypeCode.Single => context.GetPrimitive(PrimitiveKind.R4),
            PrimitiveTypeCode.Double => context.GetPrimitive(PrimitiveKind.R8),
            PrimitiveTypeCode.Object => context.ObjectType,
            PrimitiveTypeCode.String => context.StringType,
            _ => throw new TranslationException($"Type {typeCode} is not supported."),
        };

        public TypeDesc GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            context.GetDefinition(context._modules.First(m => m.Reader == reader), handle);

        public TypeDesc GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            context.ResolveTypeReference(context._modules.First(m => m.Reader == reader), handle);

        public TypeDesc GetTypeFromSpecification(MetadataReader reader, ModuleDesc module, TypeSpecificationHandle handle, byte rawTypeKind) =>
            context.ResolveType(module, handle);

        public TypeDesc GetSZArrayType(TypeDesc elementType) => context.MakeArray(elementType);
        public TypeDesc GetByReferenceType(TypeDesc elementType) => context.MakeByRef(elementType);
        public TypeDesc GetPointerType(TypeDesc elementType) => context.MakePointer(elementType);

        // Pinned locals and modifiers (e.g. volatile) change nothing for the generated C.
        public TypeDesc GetPinnedType(TypeDesc elementType) => elementType;
        public TypeDesc GetModifiedType(TypeDesc modifier, TypeDesc unmodifiedType, bool isRequired) => unmodifiedType;

        public TypeDesc GetArrayType(TypeDesc elementType, ArrayShape shape) =>
            throw new TranslationException("Multi-dimensional arrays are not supported yet.");

        public TypeDesc GetGenericInstantiation(TypeDesc genericType, ImmutableArray<TypeDesc> typeArguments) =>
            context.Instantiate(genericType, typeArguments);

        public TypeDesc GetGenericMethodParameter(ModuleDesc genericContext, int index) => context.GetGenericParameter(true, index);

        public TypeDesc GetGenericTypeParameter(ModuleDesc genericContext, int index) => context.GetGenericParameter(false, index);

        public TypeDesc GetFunctionPointerType(MethodSignature<TypeDesc> signature) =>
            throw new TranslationException("Function pointers are not supported yet.");
    }

    public void Dispose()
    {
        foreach (ModuleDesc module in _modules)
            module.PE.Dispose();
    }
}
