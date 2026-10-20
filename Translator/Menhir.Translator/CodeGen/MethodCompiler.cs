using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using Menhir.Translator.IL;
using Menhir.Translator.TypeSystem;

namespace Menhir.Translator.CodeGen;

/// <summary>
/// Translates one method body from IL to C.
/// </summary>
/// <remarks>
/// The IL evaluation stack becomes C variables named after their depth and kind
/// (<c>s0_i4</c>, <c>s1_o</c>...), so a value pushed on one path and used after a branch joins
/// lands in the same variable on every path. Each instruction becomes a C statement; branch
/// targets become labels. Values on the stack use the IL stack types: int32, int64, native int,
/// float, double, object reference, managed pointer, or a struct.
/// </remarks>
public sealed class MethodCompiler
{
    private enum Kind
    {
        I4,
        I8,
        NI,
        R4,
        R8,
        Ref,
        ByRef,
        Value,
        /// <summary>ldtoken of a field (only consumed by RuntimeHelpers.InitializeArray).</summary>
        FieldToken,
    }

    /// <param name="Type">Static type: the object's type, the struct, or the type a pointer points to.</param>
    /// <param name="NonNull">Known not to be null (this, new objects): no null check needed.</param>
    private readonly record struct Entry(Kind Kind, TypeDesc? Type = null, bool NonNull = false, FieldDesc? Field = null);

    private readonly Il2cTranslator _translator;
    private readonly TypeSystemContext _context;
    private readonly CNames _names;
    private readonly MethodDesc _method;
    private readonly ModuleDesc _module;
    private readonly MethodBodyBlock _body;
    private readonly List<Instruction> _instructions;
    private readonly ImmutableArray<TypeDesc> _locals;
    private readonly List<TypeDesc> _args = new();
    private readonly HashSet<int> _labels = new();
    private readonly Dictionary<int, List<Entry>> _stackAtTarget = new();
    private readonly SortedDictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly StringBuilder _code = new();
    private readonly List<ExceptionRegion> _regions;
    private readonly Dictionary<int, Dictionary<int, int>> _finallyTargets = new();

    private List<Entry> _stack = new();
    private Instruction _current;
    private TypeDesc? _constrained;
    private int _temp;

    public MethodCompiler(Il2cTranslator translator, MethodDesc method)
    {
        _translator = translator;
        _context = translator.Context;
        _names = translator.Names;
        _method = method;
        _module = method.Owner.Module!;
        _body = method.GetBody();
        _instructions = ILReader.Decode(_body);
        _locals = _context.DecodeLocals(_module, _body.LocalSignature);
        _regions = _body.ExceptionRegions.ToList();

        if (!method.IsStatic)
            _args.Add(method.Owner.IsValueType ? _context.MakeByRef(method.Owner) : method.Owner);
        _args.AddRange(method.ParameterTypes);
    }

    public string Compile()
    {
        CollectLabels();

        if (_regions.Any(r => r.Kind is ExceptionRegionKind.Filter or ExceptionRegionKind.Fault))
            throw new TranslationException("Exception filters and fault blocks are not supported yet (IL2C-2).");

        // Static methods and constructors trigger the static constructor (the type's own
        // static constructor excepted).
        if (!_method.IsStaticConstructor && (_method.IsStatic || _method.IsConstructor) &&
            !_method.Owner.IsBeforeFieldInit && _translator.ClassInitCall(_method.Owner) is { Length: > 0 } init)
            Emit(init);

        bool reachable = true;
        foreach (Instruction instruction in _instructions)
        {
            _current = instruction;
            if (_stackAtTarget.TryGetValue(instruction.Offset, out List<Entry>? recorded))
            {
                if (!reachable)
                    _stack = new List<Entry>(recorded);
            }
            else if (!reachable)
            {
                _stack = HandlerStartStack(instruction.Offset) ?? new List<Entry>();
            }
            if (_labels.Contains(instruction.Offset))
                _code.Append($"IL_{instruction.Offset:x4}:;\n");
            TranslateInstruction(instruction);
            reachable = !instruction.EndsBlock;
        }

        var function = new StringBuilder();
        function.Append(_translator.Prototype(_method)).Append("\n{\n");
        for (int i = 0; i < _locals.Length; i++)
            function.Append($"    {_translator.CType(_locals[i])} l{i};\n    memset(&l{i}, 0, sizeof l{i});\n");
        foreach ((string name, string type) in _variables)
            function.Append($"    {type} {name};\n");
        foreach (int region in _finallyTargets.Keys)
            function.Append($"    int fin{region}_next = 0;\n");
        function.Append(_code);
        function.Append("}\n");
        return function.ToString();
    }

    // ---- Control flow -------------------------------------------------------------------------

    private void CollectLabels()
    {
        foreach (Instruction instruction in _instructions)
        {
            if (instruction.IsBranch)
                _labels.Add(instruction.Token);
            if (instruction.OpCode == ILOpCode.Switch)
            {
                foreach (int target in instruction.Targets)
                    _labels.Add(target);
            }
        }
        foreach (ExceptionRegion region in _regions)
            _labels.Add(region.HandlerOffset);
    }

    /// <summary>The stack when a handler starts: the exception for catch blocks, empty for finally.</summary>
    private List<Entry>? HandlerStartStack(int offset)
    {
        foreach (ExceptionRegion region in _regions)
        {
            if (region.HandlerOffset != offset)
                continue;
            return region.Kind == ExceptionRegionKind.Catch
                ? [new Entry(Kind.Ref, _context.ObjectType, NonNull: true)]
                : new List<Entry>();
        }
        return null;
    }

    private void RecordStack(int target)
    {
        if (!_stackAtTarget.ContainsKey(target))
            _stackAtTarget[target] = new List<Entry>(_stack);
    }

    private void Branch(string? condition, int target)
    {
        RecordStack(target);
        Emit(condition == null ? $"goto IL_{target:x4};" : $"if ({condition}) goto IL_{target:x4};");
    }

    /// <summary>
    /// leave: runs the finally blocks between here and the target, innermost first. Each finally
    /// knows where to continue through its fin&lt;n&gt;_next variable (see EndFinally).
    /// </summary>
    private void Leave(int target)
    {
        _stack.Clear();
        int from = _current.Offset;
        var finallys = _regions
            .Select((region, index) => (region, index))
            .Where(r => r.region.Kind == ExceptionRegionKind.Finally &&
                        Inside(from, r.region.TryOffset, r.region.TryLength) &&
                        !Inside(target, r.region.TryOffset, r.region.TryLength))
            .OrderBy(r => r.region.TryLength)
            .ToList();
        if (finallys.Count == 0)
        {
            Branch(null, target);
            return;
        }
        for (int i = 0; i < finallys.Count; i++)
        {
            int next = i + 1 < finallys.Count ? finallys[i + 1].region.HandlerOffset : target;
            Emit($"fin{finallys[i].index}_next = {FinallyTarget(finallys[i].index, next)};");
        }
        RecordStack(target);
        Emit($"goto IL_{finallys[0].region.HandlerOffset:x4};");
    }

    private int FinallyTarget(int region, int offset)
    {
        if (!_finallyTargets.TryGetValue(region, out Dictionary<int, int>? targets))
            _finallyTargets[region] = targets = new Dictionary<int, int>();
        if (!targets.TryGetValue(offset, out int id))
            targets[offset] = id = targets.Count + 1;
        _labels.Add(offset);
        return id;
    }

    private void EndFinally()
    {
        int index = _regions.FindIndex(r => r.Kind == ExceptionRegionKind.Finally &&
                                             Inside(_current.Offset, r.HandlerOffset, r.HandlerLength));
        var builder = new StringBuilder($"switch (fin{index}_next) {{");
        if (_finallyTargets.TryGetValue(index, out Dictionary<int, int>? targets))
        {
            foreach ((int offset, int id) in targets)
                builder.Append($" case {id}: goto IL_{offset:x4};");
        }
        builder.Append(" default: m_throw_named(\"System.InvalidProgramException\", \"Bad finally exit.\"); }");
        Emit(builder.ToString());
        _stack.Clear();
    }

    private static bool Inside(int offset, int start, int length) => offset >= start && offset < start + length;

    // ---- Stack --------------------------------------------------------------------------------

    private static string Suffix(Entry entry, CNames names) => entry.Kind switch
    {
        Kind.I4 => "i4",
        Kind.I8 => "i8",
        Kind.NI => "ni",
        Kind.R4 => "r4",
        Kind.R8 => "r8",
        Kind.Ref => "o",
        Kind.ByRef => "p",
        Kind.Value => "v_" + names.Type(entry.Type!),
        _ => "token",
    };

    private string KindCType(Entry entry) => entry.Kind switch
    {
        Kind.I4 => "int32_t",
        Kind.I8 => "int64_t",
        Kind.NI => "intptr_t",
        Kind.R4 => "float",
        Kind.R8 => "double",
        Kind.Ref => "MObject*",
        Kind.ByRef => "void*",
        Kind.Value => _translator.CType(entry.Type!),
        _ => "int",
    };

    private string Var(int depth, Entry entry)
    {
        string name = $"s{depth}_{Suffix(entry, _names)}";
        _variables.TryAdd(name, KindCType(entry));
        return name;
    }

    private void Push(Entry entry, string expression)
    {
        string name = Var(_stack.Count, entry);
        if (entry.Kind != Kind.FieldToken)
            Emit($"{name} = {expression};");
        _stack.Add(entry);
    }

    private (Entry Entry, string Var) Pop()
    {
        if (_stack.Count == 0)
            throw Unsupported("stack underflow");
        Entry entry = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        return (entry, Var(_stack.Count, entry));
    }

    private void Emit(string statement) => _code.Append("    ").Append(statement).Append('\n');

    private TranslationException Unsupported(string what) =>
        new($"IL_{_current.Offset:x4} {_current.OpCode}: {what}");

    // ---- Types and conversions ----------------------------------------------------------------

    private Entry EntryFor(TypeDesc type)
    {
        if (type.IsByRef)
            return new Entry(Kind.ByRef, type.ElementType);
        if (type.IsPointer)
            return new Entry(Kind.NI, type.ElementType);
        if (type.IsEnum)
            return EntryFor(type.EnumUnderlyingType);
        if (type.IsPrimitive)
        {
            return type.Primitive switch
            {
                PrimitiveKind.I8 or PrimitiveKind.U8 => new Entry(Kind.I8),
                PrimitiveKind.I or PrimitiveKind.U => new Entry(Kind.NI),
                PrimitiveKind.R4 => new Entry(Kind.R4),
                PrimitiveKind.R8 => new Entry(Kind.R8),
                PrimitiveKind.Void => throw Unsupported("void value"),
                _ => new Entry(Kind.I4),
            };
        }
        return type.IsValueType ? new Entry(Kind.Value, type) : new Entry(Kind.Ref, type);
    }

    /// <summary>Pushes a value read from storage of <paramref name="type"/>.</summary>
    private void PushFrom(TypeDesc type, string expression, bool nonNull = false)
    {
        Entry entry = EntryFor(type) with { NonNull = nonNull };
        Push(entry, entry.Kind == Kind.Value ? expression : $"({KindCType(entry)})({expression})");
    }

    /// <summary>A stack value converted for storage of <paramref name="type"/> (field, local, argument).</summary>
    private string ToStorage(TypeDesc type, Entry entry, string variable)
    {
        if (type.IsValueType && !type.IsPrimitive && !type.IsEnum)
            return variable;
        return $"({_translator.CType(type)})({variable})";
    }

    private static bool IsInteger(Kind kind) => kind is Kind.I4 or Kind.I8 or Kind.NI;
    private static bool IsFloat(Kind kind) => kind is Kind.R4 or Kind.R8;

    private static string Unsigned(Entry entry, string variable) => entry.Kind switch
    {
        Kind.I4 => $"(uint32_t){variable}",
        Kind.I8 => $"(uint64_t){variable}",
        _ => $"(uintptr_t){variable}",
    };

    // ---- Instructions -------------------------------------------------------------------------

    private void TranslateInstruction(Instruction ins)
    {
        switch (ins.OpCode)
        {
            case ILOpCode.Nop:
            case ILOpCode.Break:
            case ILOpCode.Readonly:
            case ILOpCode.Volatile:
            case ILOpCode.Tail:
            case ILOpCode.Unaligned:
                break;
            case ILOpCode.Constrained:
                _constrained = _context.ResolveType(_module, MetadataTokens.EntityHandle(ins.Token));
                break;

            // Arguments and locals
            case ILOpCode.Ldarg_0: LoadArg(0); break;
            case ILOpCode.Ldarg_1: LoadArg(1); break;
            case ILOpCode.Ldarg_2: LoadArg(2); break;
            case ILOpCode.Ldarg_3: LoadArg(3); break;
            case ILOpCode.Ldarg_s:
            case ILOpCode.Ldarg: LoadArg((int)ins.Operand); break;
            case ILOpCode.Starg_s:
            case ILOpCode.Starg:
            {
                var (entry, v) = Pop();
                Emit($"a{ins.Operand} = {ToStorage(_args[(int)ins.Operand], entry, v)};");
                break;
            }
            case ILOpCode.Ldarga_s:
            case ILOpCode.Ldarga: Push(new Entry(Kind.ByRef, _args[(int)ins.Operand], NonNull: true), $"(void*)&a{ins.Operand}"); break;
            case ILOpCode.Ldloc_0: LoadLocal(0); break;
            case ILOpCode.Ldloc_1: LoadLocal(1); break;
            case ILOpCode.Ldloc_2: LoadLocal(2); break;
            case ILOpCode.Ldloc_3: LoadLocal(3); break;
            case ILOpCode.Ldloc_s:
            case ILOpCode.Ldloc: LoadLocal((int)ins.Operand); break;
            case ILOpCode.Stloc_0: StoreLocal(0); break;
            case ILOpCode.Stloc_1: StoreLocal(1); break;
            case ILOpCode.Stloc_2: StoreLocal(2); break;
            case ILOpCode.Stloc_3: StoreLocal(3); break;
            case ILOpCode.Stloc_s:
            case ILOpCode.Stloc: StoreLocal((int)ins.Operand); break;
            case ILOpCode.Ldloca_s:
            case ILOpCode.Ldloca: Push(new Entry(Kind.ByRef, _locals[(int)ins.Operand], NonNull: true), $"(void*)&l{ins.Operand}"); break;

            // Constants
            case ILOpCode.Ldnull: Push(new Entry(Kind.Ref, _context.ObjectType), "NULL"); break;
            case ILOpCode.Ldc_i4_m1: PushInt(-1); break;
            case ILOpCode.Ldc_i4_0: PushInt(0); break;
            case ILOpCode.Ldc_i4_1: PushInt(1); break;
            case ILOpCode.Ldc_i4_2: PushInt(2); break;
            case ILOpCode.Ldc_i4_3: PushInt(3); break;
            case ILOpCode.Ldc_i4_4: PushInt(4); break;
            case ILOpCode.Ldc_i4_5: PushInt(5); break;
            case ILOpCode.Ldc_i4_6: PushInt(6); break;
            case ILOpCode.Ldc_i4_7: PushInt(7); break;
            case ILOpCode.Ldc_i4_8: PushInt(8); break;
            case ILOpCode.Ldc_i4_s:
            case ILOpCode.Ldc_i4: PushInt((int)ins.Operand); break;
            case ILOpCode.Ldc_i8: Push(new Entry(Kind.I8), Int64Literal(ins.Operand)); break;
            case ILOpCode.Ldc_r4: Push(new Entry(Kind.R4), Il2cTranslator.FloatLiteral(ins.FloatOperand, single: true)); break;
            case ILOpCode.Ldc_r8: Push(new Entry(Kind.R8), Il2cTranslator.FloatLiteral(ins.FloatOperand, single: false)); break;
            case ILOpCode.Ldstr:
                Push(new Entry(Kind.Ref, _context.StringType, NonNull: true),
                    $"(MObject*)&{_translator.StringLiteral(_context.ResolveUserString(_module, ins.Token))}");
                break;

            // Stack
            case ILOpCode.Dup:
            {
                Entry top = _stack[^1];
                Push(top, Var(_stack.Count - 1, top));
                break;
            }
            case ILOpCode.Pop: Pop(); break;

            // Arithmetic and logic
            case ILOpCode.Add: Arithmetic("+"); break;
            case ILOpCode.Sub: Arithmetic("-"); break;
            case ILOpCode.Mul: Arithmetic("*"); break;
            case ILOpCode.Div: Divide(remainder: false, unsigned: false); break;
            case ILOpCode.Div_un: Divide(remainder: false, unsigned: true); break;
            case ILOpCode.Rem: Divide(remainder: true, unsigned: false); break;
            case ILOpCode.Rem_un: Divide(remainder: true, unsigned: true); break;
            case ILOpCode.And: Bitwise("&"); break;
            case ILOpCode.Or: Bitwise("|"); break;
            case ILOpCode.Xor: Bitwise("^"); break;
            case ILOpCode.Shl: Shift("<<", unsigned: false); break;
            case ILOpCode.Shr: Shift(">>", unsigned: false); break;
            case ILOpCode.Shr_un: Shift(">>", unsigned: true); break;
            case ILOpCode.Neg:
            {
                var (e, v) = Pop();
                Push(e, IsFloat(e.Kind) ? $"-{v}" : $"({KindCType(e)})(0 - {Unsigned(e, v)})");
                break;
            }
            case ILOpCode.Not:
            {
                var (e, v) = Pop();
                Push(e, $"~{v}");
                break;
            }
            case ILOpCode.Add_ovf: CheckedArithmetic("add", unsigned: false); break;
            case ILOpCode.Add_ovf_un: CheckedArithmetic("add", unsigned: true); break;
            case ILOpCode.Sub_ovf: CheckedArithmetic("sub", unsigned: false); break;
            case ILOpCode.Sub_ovf_un: CheckedArithmetic("sub", unsigned: true); break;
            case ILOpCode.Mul_ovf: CheckedArithmetic("mul", unsigned: false); break;
            case ILOpCode.Mul_ovf_un: CheckedArithmetic("mul", unsigned: true); break;
            case ILOpCode.Ckfinite:
            {
                var (e, v) = Pop();
                Emit($"if (!isfinite({v})) m_throw_named(\"System.ArithmeticException\", \"Function does not accept floating point Not-a-Number values.\");");
                Push(e, v);
                break;
            }

            // Comparisons
            case ILOpCode.Ceq: Compare("=="); break;
            case ILOpCode.Cgt: Compare(">"); break;
            case ILOpCode.Cgt_un: Compare(">", unsignedOrUnordered: true); break;
            case ILOpCode.Clt: Compare("<"); break;
            case ILOpCode.Clt_un: Compare("<", unsignedOrUnordered: true); break;

            // Conversions
            case ILOpCode.Conv_i1: Convert(Target.I1, false, false); break;
            case ILOpCode.Conv_u1: Convert(Target.U1, false, false); break;
            case ILOpCode.Conv_i2: Convert(Target.I2, false, false); break;
            case ILOpCode.Conv_u2: Convert(Target.U2, false, false); break;
            case ILOpCode.Conv_i4: Convert(Target.I4, false, false); break;
            case ILOpCode.Conv_u4: Convert(Target.U4, false, false); break;
            case ILOpCode.Conv_i8: Convert(Target.I8, false, false); break;
            case ILOpCode.Conv_u8: Convert(Target.U8, false, false); break;
            case ILOpCode.Conv_i: Convert(Target.I, false, false); break;
            case ILOpCode.Conv_u: Convert(Target.U, false, false); break;
            case ILOpCode.Conv_r4: Convert(Target.R4, false, false); break;
            case ILOpCode.Conv_r8: Convert(Target.R8, false, false); break;
            case ILOpCode.Conv_r_un: Convert(Target.R8, false, true); break;
            case ILOpCode.Conv_ovf_i1: Convert(Target.I1, true, false); break;
            case ILOpCode.Conv_ovf_u1: Convert(Target.U1, true, false); break;
            case ILOpCode.Conv_ovf_i2: Convert(Target.I2, true, false); break;
            case ILOpCode.Conv_ovf_u2: Convert(Target.U2, true, false); break;
            case ILOpCode.Conv_ovf_i4: Convert(Target.I4, true, false); break;
            case ILOpCode.Conv_ovf_u4: Convert(Target.U4, true, false); break;
            case ILOpCode.Conv_ovf_i8: Convert(Target.I8, true, false); break;
            case ILOpCode.Conv_ovf_u8: Convert(Target.U8, true, false); break;
            case ILOpCode.Conv_ovf_i: Convert(Target.I, true, false); break;
            case ILOpCode.Conv_ovf_u: Convert(Target.U, true, false); break;
            case ILOpCode.Conv_ovf_i1_un: Convert(Target.I1, true, true); break;
            case ILOpCode.Conv_ovf_u1_un: Convert(Target.U1, true, true); break;
            case ILOpCode.Conv_ovf_i2_un: Convert(Target.I2, true, true); break;
            case ILOpCode.Conv_ovf_u2_un: Convert(Target.U2, true, true); break;
            case ILOpCode.Conv_ovf_i4_un: Convert(Target.I4, true, true); break;
            case ILOpCode.Conv_ovf_u4_un: Convert(Target.U4, true, true); break;
            case ILOpCode.Conv_ovf_i8_un: Convert(Target.I8, true, true); break;
            case ILOpCode.Conv_ovf_u8_un: Convert(Target.U8, true, true); break;
            case ILOpCode.Conv_ovf_i_un: Convert(Target.I, true, true); break;
            case ILOpCode.Conv_ovf_u_un: Convert(Target.U, true, true); break;

            // Branches
            case ILOpCode.Br_s:
            case ILOpCode.Br: Branch(null, ins.Token); break;
            case ILOpCode.Brtrue_s:
            case ILOpCode.Brtrue:
            {
                var (e, v) = Pop();
                Branch($"{v} != 0", ins.Token);
                break;
            }
            case ILOpCode.Brfalse_s:
            case ILOpCode.Brfalse:
            {
                var (e, v) = Pop();
                Branch($"{v} == 0", ins.Token);
                break;
            }
            case ILOpCode.Beq_s: case ILOpCode.Beq: BranchCompare("==", false, ins.Token); break;
            case ILOpCode.Bne_un_s: case ILOpCode.Bne_un: BranchCompare("!=", true, ins.Token); break;
            case ILOpCode.Bge_s: case ILOpCode.Bge: BranchCompare(">=", false, ins.Token); break;
            case ILOpCode.Bge_un_s: case ILOpCode.Bge_un: BranchCompare(">=", true, ins.Token); break;
            case ILOpCode.Bgt_s: case ILOpCode.Bgt: BranchCompare(">", false, ins.Token); break;
            case ILOpCode.Bgt_un_s: case ILOpCode.Bgt_un: BranchCompare(">", true, ins.Token); break;
            case ILOpCode.Ble_s: case ILOpCode.Ble: BranchCompare("<=", false, ins.Token); break;
            case ILOpCode.Ble_un_s: case ILOpCode.Ble_un: BranchCompare("<=", true, ins.Token); break;
            case ILOpCode.Blt_s: case ILOpCode.Blt: BranchCompare("<", false, ins.Token); break;
            case ILOpCode.Blt_un_s: case ILOpCode.Blt_un: BranchCompare("<", true, ins.Token); break;
            case ILOpCode.Switch:
            {
                var (e, v) = Pop();
                var builder = new StringBuilder($"switch ({v}) {{");
                for (int i = 0; i < ins.Targets.Length; i++)
                {
                    RecordStack(ins.Targets[i]);
                    builder.Append($" case {i}: goto IL_{ins.Targets[i]:x4};");
                }
                Emit(builder.Append(" default: break; }").ToString());
                break;
            }
            case ILOpCode.Leave_s:
            case ILOpCode.Leave: Leave(ins.Token); break;
            case ILOpCode.Endfinally: EndFinally(); break;
            case ILOpCode.Ret:
                if (_method.ReturnType.IsVoid)
                    Emit("return;");
                else
                {
                    var (e, v) = Pop();
                    Emit($"return {ToStorage(_method.ReturnType, e, v)};");
                }
                _stack.Clear();
                break;
            case ILOpCode.Throw:
            {
                var (e, v) = Pop();
                Emit($"m_throw({v});");
                _stack.Clear();
                break;
            }
            case ILOpCode.Rethrow:
                Emit("m_throw_named(\"System.Exception\", \"rethrow\");");
                _stack.Clear();
                break;

            // Objects and fields
            case ILOpCode.Newobj: NewObject(ResolveMethod(ins)); break;
            case ILOpCode.Call: Call(ResolveMethod(ins), virtualCall: false); break;
            case ILOpCode.Callvirt: Call(ResolveMethod(ins), virtualCall: true); break;
            case ILOpCode.Ldfld: LoadField(ResolveField(ins), address: false); break;
            case ILOpCode.Ldflda: LoadField(ResolveField(ins), address: true); break;
            case ILOpCode.Stfld: StoreField(ResolveField(ins)); break;
            case ILOpCode.Ldsfld: LoadStaticField(ResolveField(ins), address: false); break;
            case ILOpCode.Ldsflda: LoadStaticField(ResolveField(ins), address: true); break;
            case ILOpCode.Stsfld: StoreStaticField(ResolveField(ins)); break;
            case ILOpCode.Ldtoken:
            {
                EntityHandle handle = MetadataTokens.EntityHandle(ins.Token);
                if (handle.Kind != HandleKind.FieldDefinition && handle.Kind != HandleKind.MemberReference)
                    throw Unsupported("ldtoken of types and methods is not supported yet (IL2C-2)");
                _stack.Add(new Entry(Kind.FieldToken, Field: _context.ResolveField(_module, handle)));
                break;
            }

            // Indirect access
            case ILOpCode.Ldind_i1: LoadIndirect(PrimitiveKind.I1); break;
            case ILOpCode.Ldind_u1: LoadIndirect(PrimitiveKind.U1); break;
            case ILOpCode.Ldind_i2: LoadIndirect(PrimitiveKind.I2); break;
            case ILOpCode.Ldind_u2: LoadIndirect(PrimitiveKind.U2); break;
            case ILOpCode.Ldind_i4: LoadIndirect(PrimitiveKind.I4); break;
            case ILOpCode.Ldind_u4: LoadIndirect(PrimitiveKind.U4); break;
            case ILOpCode.Ldind_i8: LoadIndirect(PrimitiveKind.I8); break;
            case ILOpCode.Ldind_i: LoadIndirect(PrimitiveKind.I); break;
            case ILOpCode.Ldind_r4: LoadIndirect(PrimitiveKind.R4); break;
            case ILOpCode.Ldind_r8: LoadIndirect(PrimitiveKind.R8); break;
            case ILOpCode.Ldind_ref:
            {
                var (e, v) = Pop();
                Push(new Entry(Kind.Ref, e.Type ?? _context.ObjectType), $"*(MObject**){v}");
                break;
            }
            case ILOpCode.Stind_i1: StoreIndirect(_context.GetPrimitive(PrimitiveKind.I1)); break;
            case ILOpCode.Stind_i2: StoreIndirect(_context.GetPrimitive(PrimitiveKind.I2)); break;
            case ILOpCode.Stind_i4: StoreIndirect(_context.GetPrimitive(PrimitiveKind.I4)); break;
            case ILOpCode.Stind_i8: StoreIndirect(_context.GetPrimitive(PrimitiveKind.I8)); break;
            case ILOpCode.Stind_i: StoreIndirect(_context.GetPrimitive(PrimitiveKind.I)); break;
            case ILOpCode.Stind_r4: StoreIndirect(_context.GetPrimitive(PrimitiveKind.R4)); break;
            case ILOpCode.Stind_r8: StoreIndirect(_context.GetPrimitive(PrimitiveKind.R8)); break;
            case ILOpCode.Stind_ref: StoreIndirect(_context.ObjectType); break;
            case ILOpCode.Ldobj:
            {
                TypeDesc type = ResolveType(ins);
                var (e, v) = Pop();
                PushFrom(type, $"*({_translator.CType(type)}*){v}");
                break;
            }
            case ILOpCode.Stobj: StoreIndirect(ResolveType(ins)); break;
            case ILOpCode.Initobj:
            {
                TypeDesc type = ResolveType(ins);
                var (e, v) = Pop();
                Emit($"memset({v}, 0, sizeof({_translator.CType(type)}));");
                break;
            }
            case ILOpCode.Cpobj:
            {
                TypeDesc type = ResolveType(ins);
                var (_, source) = Pop();
                var (_, destination) = Pop();
                Emit($"memcpy({destination}, {source}, sizeof({_translator.CType(type)}));");
                break;
            }
            case ILOpCode.Sizeof: PushInt($"(int32_t)sizeof({_translator.CType(ResolveType(ins))})"); break;
            case ILOpCode.Localloc:
            {
                var (e, v) = Pop();
                string size = $"(size_t){v}";
                string temp = Temp("void*", $"__builtin_alloca({size})");
                if (_body.LocalVariablesInitialized)
                    Emit($"memset({temp}, 0, {size});");
                Push(new Entry(Kind.NI), $"(intptr_t){temp}");
                break;
            }

            // Arrays
            case ILOpCode.Newarr:
            {
                TypeDesc array = _context.MakeArray(ResolveType(ins));
                _translator.UseArrayType(array);
                var (e, v) = Pop();
                Push(new Entry(Kind.Ref, array, NonNull: true), $"(MObject*)m_new_array(&{_names.TypeInfo(array)}, (intptr_t){v})");
                break;
            }
            case ILOpCode.Ldlen:
            {
                var (e, v) = Pop();
                if (!e.NonNull)
                    Emit($"M_NULL_CHECK({v});");
                Push(new Entry(Kind.NI), $"(intptr_t)((MArray*){v})->length");
                break;
            }
            case ILOpCode.Ldelem_i1: LoadElement(_context.GetPrimitive(PrimitiveKind.I1)); break;
            case ILOpCode.Ldelem_u1: LoadElement(_context.GetPrimitive(PrimitiveKind.U1)); break;
            case ILOpCode.Ldelem_i2: LoadElement(_context.GetPrimitive(PrimitiveKind.I2)); break;
            case ILOpCode.Ldelem_u2: LoadElement(_context.GetPrimitive(PrimitiveKind.U2)); break;
            case ILOpCode.Ldelem_i4: LoadElement(_context.GetPrimitive(PrimitiveKind.I4)); break;
            case ILOpCode.Ldelem_u4: LoadElement(_context.GetPrimitive(PrimitiveKind.U4)); break;
            case ILOpCode.Ldelem_i8: LoadElement(_context.GetPrimitive(PrimitiveKind.I8)); break;
            case ILOpCode.Ldelem_i: LoadElement(_context.GetPrimitive(PrimitiveKind.I)); break;
            case ILOpCode.Ldelem_r4: LoadElement(_context.GetPrimitive(PrimitiveKind.R4)); break;
            case ILOpCode.Ldelem_r8: LoadElement(_context.GetPrimitive(PrimitiveKind.R8)); break;
            case ILOpCode.Ldelem_ref: LoadElement(null); break;
            case ILOpCode.Ldelem: LoadElement(ResolveType(ins)); break;
            case ILOpCode.Stelem_i1: StoreElement(_context.GetPrimitive(PrimitiveKind.I1)); break;
            case ILOpCode.Stelem_i2: StoreElement(_context.GetPrimitive(PrimitiveKind.I2)); break;
            case ILOpCode.Stelem_i4: StoreElement(_context.GetPrimitive(PrimitiveKind.I4)); break;
            case ILOpCode.Stelem_i8: StoreElement(_context.GetPrimitive(PrimitiveKind.I8)); break;
            case ILOpCode.Stelem_i: StoreElement(_context.GetPrimitive(PrimitiveKind.I)); break;
            case ILOpCode.Stelem_r4: StoreElement(_context.GetPrimitive(PrimitiveKind.R4)); break;
            case ILOpCode.Stelem_r8: StoreElement(_context.GetPrimitive(PrimitiveKind.R8)); break;
            case ILOpCode.Stelem_ref: StoreElement(null); break;
            case ILOpCode.Stelem: StoreElement(ResolveType(ins)); break;
            case ILOpCode.Ldelema:
            {
                TypeDesc element = ResolveType(ins);
                var (_, index) = Pop();
                var (_, array) = Pop();
                Push(new Entry(Kind.ByRef, element, NonNull: true),
                    $"(void*)&(({_translator.CType(element)}*)M_ARRAY_DATA(m_check_index((MArray*){array}, {index})))[{index}]");
                break;
            }

            // Casts and boxing
            case ILOpCode.Box: Box(ResolveType(ins)); break;
            case ILOpCode.Unbox_any:
            {
                TypeDesc type = ResolveType(ins);
                var (e, v) = Pop();
                if (type.IsReferenceType)
                    Push(new Entry(Kind.Ref, type), $"m_castclass({v}, &{TypeInfoFor(type)})");
                else
                    PushFrom(type, $"*({_translator.CType(type)}*)m_unbox({v}, &{TypeInfoFor(type)})");
                break;
            }
            case ILOpCode.Unbox:
            {
                TypeDesc type = ResolveType(ins);
                var (e, v) = Pop();
                Push(new Entry(Kind.ByRef, type, NonNull: true), $"m_unbox({v}, &{TypeInfoFor(type)})");
                break;
            }
            case ILOpCode.Isinst:
            case ILOpCode.Castclass:
            {
                TypeDesc type = ResolveType(ins);
                if (type.IsInterface)
                    throw Unsupported("casts to interfaces are not supported yet (IL2C-2)");
                var (e, v) = Pop();
                string function = ins.OpCode == ILOpCode.Isinst ? "m_isinst" : "m_castclass";
                Push(new Entry(Kind.Ref, type), $"{function}({v}, &{TypeInfoFor(type)})");
                break;
            }

            default:
                throw Unsupported("this instruction is not supported yet");
        }

        if (ins.OpCode != ILOpCode.Constrained)
            _constrained = null;
    }

    private MethodDesc ResolveMethod(Instruction ins) => _context.ResolveMethod(_module, MetadataTokens.EntityHandle(ins.Token));
    private FieldDesc ResolveField(Instruction ins) => _context.ResolveField(_module, MetadataTokens.EntityHandle(ins.Token));
    private TypeDesc ResolveType(Instruction ins) => _context.ResolveType(_module, MetadataTokens.EntityHandle(ins.Token));

    private string TypeInfoFor(TypeDesc type)
    {
        if (type.IsArray)
            _translator.UseArrayType(type);
        return _names.TypeInfo(type);
    }

    private string Temp(string type, string value)
    {
        string name = $"t{_temp++}";
        _variables[name] = type;
        Emit($"{name} = {value};");
        return name;
    }

    private void PushInt(int value) => Push(new Entry(Kind.I4), Int32Literal(value));
    private void PushInt(string expression) => Push(new Entry(Kind.I4), expression);

    private static string Int32Literal(int value) =>
        value == int.MinValue ? "INT32_MIN" : value.ToString(CultureInfo.InvariantCulture);

    private static string Int64Literal(long value) =>
        value == long.MinValue ? "INT64_MIN" : $"INT64_C({value.ToString(CultureInfo.InvariantCulture)})";

    private void LoadArg(int index)
    {
        bool isThis = index == 0 && !_method.IsStatic;
        TypeDesc type = _args[index];
        if (isThis && type.IsByRef)
        {
            Push(new Entry(Kind.ByRef, type.ElementType, NonNull: true), "(void*)a0");
            return;
        }
        PushFrom(type, $"a{index}", nonNull: isThis);
    }

    private void LoadLocal(int index) => PushFrom(_locals[index], $"l{index}");

    private void StoreLocal(int index)
    {
        var (entry, v) = Pop();
        Emit($"l{index} = {ToStorage(_locals[index], entry, v)};");
    }

    // ---- Arithmetic ---------------------------------------------------------------------------

    private void Arithmetic(string op)
    {
        var (b, vb) = Pop();
        var (a, va) = Pop();

        if (a.Kind == Kind.ByRef || b.Kind == Kind.ByRef)
        {
            if (a.Kind == Kind.ByRef && b.Kind == Kind.ByRef)
            {
                Push(new Entry(Kind.NI), $"(intptr_t)((char*){va} - (char*){vb})");
                return;
            }
            (Entry pointer, string vp, string vo) = a.Kind == Kind.ByRef ? (a, va, vb) : (b, vb, va);
            Push(new Entry(Kind.ByRef, pointer.Type), $"(void*)((char*){vp} {op} (intptr_t){vo})");
            return;
        }

        Entry result = BinaryResult(a, b);
        if (IsFloat(result.Kind))
        {
            Push(result, $"({KindCType(result)}){va} {op} ({KindCType(result)}){vb}");
            return;
        }
        // Integer arithmetic wraps around (the code is compiled with -fwrapv).
        string type = KindCType(result);
        Push(result, $"({type})(({type}){va} {op} ({type}){vb})");
    }

    private Entry BinaryResult(Entry a, Entry b)
    {
        if (IsFloat(a.Kind) || IsFloat(b.Kind))
            return new Entry(a.Kind == Kind.R4 && b.Kind == Kind.R4 ? Kind.R4 : Kind.R8);
        if (a.Kind == Kind.NI || b.Kind == Kind.NI)
            return new Entry(Kind.NI);
        if (a.Kind == Kind.I8 || b.Kind == Kind.I8)
            return new Entry(Kind.I8);
        if (a.Kind == Kind.I4 && b.Kind == Kind.I4)
            return new Entry(Kind.I4);
        throw Unsupported($"arithmetic on {a.Kind} and {b.Kind}");
    }

    private void Divide(bool remainder, bool unsigned)
    {
        var (b, vb) = Pop();
        var (a, va) = Pop();
        Entry result = BinaryResult(a, b);
        if (IsFloat(result.Kind))
        {
            string type = KindCType(result);
            Push(result, remainder
                ? $"{(result.Kind == Kind.R4 ? "m_rem_r4" : "m_rem_r8")}(({type}){va}, ({type}){vb})"
                : $"({type}){va} / ({type}){vb}");
            return;
        }
        string op = remainder ? "rem" : "div";
        string function = (result.Kind, unsigned) switch
        {
            (Kind.I4, false) => $"m_{op}_i4",
            (Kind.I4, true) => $"(int32_t)m_{op}_u4",
            (Kind.I8, false) => $"m_{op}_i8",
            (Kind.I8, true) => $"(int64_t)m_{op}_u8",
            (_, false) => $"(intptr_t)m_{op}_i8",
            _ => $"(intptr_t)m_{op}_u8",
        };
        string cast = (result.Kind, unsigned) switch
        {
            (Kind.I4, false) => "(int32_t)",
            (Kind.I4, true) => "(uint32_t)",
            (_, false) => "(int64_t)",
            _ => "(uint64_t)",
        };
        Push(result, $"{function}({cast}{va}, {cast}{vb})");
    }

    private void Bitwise(string op)
    {
        var (b, vb) = Pop();
        var (a, va) = Pop();
        Entry result = BinaryResult(a, b);
        string type = KindCType(result);
        Push(result, $"({type})(({type}){va} {op} ({type}){vb})");
    }

    private void Shift(string op, bool unsigned)
    {
        var (b, vb) = Pop();
        var (a, va) = Pop();
        // .NET masks the shift count; C leaves large counts undefined.
        int bits = a.Kind switch { Kind.I4 => 31, Kind.I8 => 63, _ => -1 };
        string mask = bits >= 0 ? bits.ToString(CultureInfo.InvariantCulture) : "(int)(sizeof(intptr_t) * 8 - 1)";
        string type = KindCType(a);
        string left = op == "<<" || unsigned ? Unsigned(a, va) : va;
        Push(a, $"({type})({left} {op} ((int){vb} & {mask}))");
    }

    private void CheckedArithmetic(string op, bool unsigned)
    {
        var (b, vb) = Pop();
        var (a, va) = Pop();
        Entry result = BinaryResult(a, b);
        if (IsFloat(result.Kind))
            throw Unsupported("checked float arithmetic");
        string type = result.Kind switch
        {
            Kind.I4 => unsigned ? "uint32_t" : "int32_t",
            Kind.I8 => unsigned ? "uint64_t" : "int64_t",
            _ => unsigned ? "uintptr_t" : "intptr_t",
        };
        string temp = $"t{_temp++}";
        _variables[temp] = type;
        Emit($"M_CHECKED({op}, ({type}){va}, ({type}){vb}, &{temp});");
        Push(result, $"({KindCType(result)}){temp}");
    }

    private string Comparison(Entry a, string va, Entry b, string vb, string op, bool unsignedOrUnordered)
    {
        if (IsFloat(a.Kind) || IsFloat(b.Kind))
        {
            if (!unsignedOrUnordered || op is "==")
                return $"{va} {op} {vb}";
            if (op == "!=")
                return $"{va} != {vb}";
            // Unordered: true when either is NaN, so negate the opposite ordered comparison.
            string opposite = op switch { ">" => "<=", ">=" => "<", "<" => ">=", _ => ">" };
            return $"!({va} {opposite} {vb})";
        }
        if (a.Kind is Kind.Ref or Kind.ByRef || b.Kind is Kind.Ref or Kind.ByRef)
            return $"(uintptr_t){va} {op} (uintptr_t){vb}";
        Entry result = BinaryResult(a, b);
        if (unsignedOrUnordered)
        {
            string type = result.Kind switch { Kind.I4 => "uint32_t", Kind.I8 => "uint64_t", _ => "uintptr_t" };
            return $"({type}){va} {op} ({type}){vb}";
        }
        string signedType = KindCType(result);
        return $"({signedType}){va} {op} ({signedType}){vb}";
    }

    private void Compare(string op, bool unsignedOrUnordered = false)
    {
        var (b, vb) = Pop();
        var (a, va) = Pop();
        Push(new Entry(Kind.I4), $"({Comparison(a, va, b, vb, op, unsignedOrUnordered)}) ? 1 : 0");
    }

    private void BranchCompare(string op, bool unsignedOrUnordered, int target)
    {
        var (b, vb) = Pop();
        var (a, va) = Pop();
        Branch(Comparison(a, va, b, vb, op, unsignedOrUnordered), target);
    }

    // ---- Conversions --------------------------------------------------------------------------

    private enum Target
    {
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

    private void Convert(Target target, bool overflowChecked, bool unsignedSource)
    {
        var (source, v) = Pop();
        bool fromFloat = IsFloat(source.Kind);
        // Pointers converted to integers (conv.u on a fixed pointer) are native ints.
        bool fromPointer = source.Kind is Kind.Ref or Kind.ByRef;
        string value = fromPointer ? $"(intptr_t){v}" : v;
        if (fromPointer)
            source = new Entry(Kind.NI);

        if (target is Target.R4 or Target.R8)
        {
            string type = target == Target.R4 ? "float" : "double";
            string expression = unsignedSource && !fromFloat ? $"({type}){Unsigned(source, value)}" : $"({type}){value}";
            Push(new Entry(target == Target.R4 ? Kind.R4 : Kind.R8), expression);
            return;
        }

        Kind resultKind = target switch
        {
            Target.I8 or Target.U8 => Kind.I8,
            Target.I or Target.U => Kind.NI,
            _ => Kind.I4,
        };
        string resultType = KindCType(new Entry(resultKind));

        if (!overflowChecked)
        {
            string expression;
            if (fromFloat)
            {
                expression = target switch
                {
                    Target.I1 => $"m_conv_i1_r8({value})",
                    Target.U1 => $"m_conv_u1_r8({value})",
                    Target.I2 => $"m_conv_i2_r8({value})",
                    Target.U2 => $"m_conv_u2_r8({value})",
                    Target.I4 => $"m_conv_i4_r8({value})",
                    Target.U4 => $"(int32_t)m_conv_u4_r8({value})",
                    Target.I8 or Target.I => $"m_conv_i8_r8({value})",
                    _ => $"(int64_t)m_conv_u8_r8({value})",
                };
            }
            else
            {
                string cType = target switch
                {
                    Target.I1 => "int8_t",
                    Target.U1 => "uint8_t",
                    Target.I2 => "int16_t",
                    Target.U2 => "uint16_t",
                    Target.I4 => "int32_t",
                    Target.U4 => "uint32_t",
                    Target.I8 => "int64_t",
                    Target.U8 => "uint64_t",
                    Target.I => "intptr_t",
                    _ => "uintptr_t",
                };
                // Widening an int32 to 64 bits: conv.u8 / conv.u zero-extend, the others sign-extend.
                bool zeroExtend = target is Target.U8 or Target.U || unsignedSource;
                string widened = zeroExtend && source.Kind == Kind.I4 ? $"(uint32_t){value}" : value;
                expression = $"({cType}){widened}";
            }
            Push(new Entry(resultKind), $"({resultType})({expression})");
            return;
        }

        // Checked conversions: compute in 64 bits, then check the range.
        string wide;
        if (fromFloat)
            wide = unsignedSource || target is Target.U8 or Target.U ? $"(int64_t)m_ovf_u8_r8({value})" : $"m_ovf_i8_r8({value})";
        else if (unsignedSource)
            wide = $"(int64_t){Unsigned(source, value)}";
        else
            wide = $"(int64_t){value}";

        bool sourceUnsigned = unsignedSource && !fromFloat;
        string checkedExpression = target switch
        {
            Target.I1 => sourceUnsigned ? $"m_ovf_range_u4((uint64_t){wide}, 127)" : $"m_ovf_range_i4({wide}, -128, 127)",
            Target.U1 => sourceUnsigned ? $"m_ovf_range_u4((uint64_t){wide}, 255)" : $"m_ovf_range_i4({wide}, 0, 255)",
            Target.I2 => sourceUnsigned ? $"m_ovf_range_u4((uint64_t){wide}, 32767)" : $"m_ovf_range_i4({wide}, -32768, 32767)",
            Target.U2 => sourceUnsigned ? $"m_ovf_range_u4((uint64_t){wide}, 65535)" : $"m_ovf_range_i4({wide}, 0, 65535)",
            Target.I4 => sourceUnsigned ? $"m_ovf_i4_un((uint64_t){wide})" : $"m_ovf_i4({wide})",
            Target.U4 => sourceUnsigned ? $"(int32_t)m_ovf_u4_un((uint64_t){wide})" : $"(int32_t)m_ovf_u4({wide})",
            Target.I8 or Target.I => sourceUnsigned ? $"m_ovf_i8_un((uint64_t){wide})" : wide,
            _ => sourceUnsigned ? wide : $"(int64_t)m_ovf_u8({wide})",
        };
        Push(new Entry(resultKind), $"({resultType})({checkedExpression})");
    }

    // ---- Fields -------------------------------------------------------------------------------

    /// <summary>C lvalue of an instance field of the object / struct the entry stands for.</summary>
    private string FieldAccess(FieldDesc field, Entry target, string variable)
    {
        TypeDesc owner = field.Owner;
        if (owner.IsPrimitive || owner.IsEnum)
        {
            // The single field of Int32 & co. is the value itself.
            return target.Kind == Kind.Value || target.Kind is Kind.I4 or Kind.I8 or Kind.NI or Kind.R4 or Kind.R8
                ? variable
                : $"(*({_translator.CType(owner)}*){variable})";
        }
        if (target.Kind == Kind.Value)
            return $"{variable}.{_names.Field(field)}";
        if (target.Kind == Kind.Ref && !target.NonNull)
            Emit($"M_NULL_CHECK({variable});");
        string pointerType = owner.IsValueType ? _names.Type(owner) + "*" : _names.Type(owner) + "*";
        return $"(({pointerType}){variable})->{_names.Field(field)}";
    }

    private void LoadField(FieldDesc field, bool address)
    {
        var (target, v) = Pop();
        if (field.IsStatic)
        {
            LoadStaticField(field, address);
            return;
        }
        string access = FieldAccess(field, target, v);
        if (address)
            Push(new Entry(Kind.ByRef, field.Type, NonNull: true), $"(void*)&{access}");
        else
            PushFrom(field.Type, access);
    }

    private void StoreField(FieldDesc field)
    {
        var (value, vv) = Pop();
        var (target, vt) = Pop();
        if (field.IsStatic)
        {
            Emit($"{StaticFieldName(field)} = {ToStorage(field.Type, value, vv)};");
            return;
        }
        Emit($"{FieldAccess(field, target, vt)} = {ToStorage(field.Type, value, vv)};");
    }

    private string StaticFieldName(FieldDesc field)
    {
        if (_method.Owner != field.Owner && _translator.ClassInitCall(field.Owner) is { Length: > 0 } init)
            Emit(init);
        return _names.StaticField(field);
    }

    private void LoadStaticField(FieldDesc field, bool address)
    {
        if (field.HasRva)
        {
            var (name, _) = _translator.RvaData(field);
            Push(new Entry(Kind.ByRef, field.Type, NonNull: true), $"(void*){name}");
            return;
        }
        string name2 = StaticFieldName(field);
        if (address)
            Push(new Entry(Kind.ByRef, field.Type, NonNull: true), $"(void*)&{name2}");
        else
            PushFrom(field.Type, name2);
    }

    private void StoreStaticField(FieldDesc field)
    {
        var (value, v) = Pop();
        Emit($"{StaticFieldName(field)} = {ToStorage(field.Type, value, v)};");
    }

    // ---- Indirect access and arrays -----------------------------------------------------------

    private void LoadIndirect(PrimitiveKind kind)
    {
        var (e, v) = Pop();
        TypeDesc type = _context.GetPrimitive(kind);
        PushFrom(type, $"*({_translator.CType(type)}*){v}");
    }

    private void StoreIndirect(TypeDesc type)
    {
        var (value, vv) = Pop();
        var (address, va) = Pop();
        Emit($"*({_translator.CType(type)}*){va} = {ToStorage(type, value, vv)};");
    }

    private string ElementType(TypeDesc? element, Entry array) =>
        element != null ? _translator.CType(element) : "MObject*";

    private void LoadElement(TypeDesc? element)
    {
        var (_, index) = Pop();
        var (array, va) = Pop();
        string access = $"(({ElementType(element, array)}*)M_ARRAY_DATA(m_check_index((MArray*){va}, {index})))[{index}]";
        if (element == null || element.IsReferenceType)
            Push(new Entry(Kind.Ref, element ?? array.Type?.ElementType ?? _context.ObjectType), $"(MObject*){access}");
        else
            PushFrom(element, access);
    }

    private void StoreElement(TypeDesc? element)
    {
        var (value, vv) = Pop();
        var (_, index) = Pop();
        var (array, va) = Pop();
        if (element == null || element.IsReferenceType)
        {
            // Arrays are covariant, so a store into object[] must check the element's real type.
            Emit($"m_check_array_store((MArray*)m_check_index((MArray*){va}, {index}), {vv});");
            Emit($"((MObject**)M_ARRAY_DATA({va}))[{index}] = {vv};");
            return;
        }
        Emit($"(({ElementType(element, array)}*)M_ARRAY_DATA(m_check_index((MArray*){va}, {index})))[{index}] = {ToStorage(element, value, vv)};");
    }

    private void Box(TypeDesc type)
    {
        var (e, v) = Pop();
        if (type.IsReferenceType)
        {
            Push(e, v);
            return;
        }
        string temp = Temp(_translator.CType(type), ToStorage(type, e, v));
        Push(new Entry(Kind.Ref, type, NonNull: true), $"m_box(&{_names.TypeInfo(type)}, &{temp})");
    }

    // ---- Calls --------------------------------------------------------------------------------

    private void NewObject(MethodDesc constructor)
    {
        TypeDesc type = constructor.Owner;
        if (type.IsGenericDefinition)
            throw Unsupported("generic types are not supported yet (IL2C-2)");
        var args = PopArguments(constructor.ParameterTypes.Count);
        string init = _translator.ClassInitCall(type);
        if (init.Length > 0 && _method.Owner != type)
            Emit(init);

        if (type.IsValueType)
        {
            string temp = $"t{_temp++}";
            _variables[temp] = _translator.CType(type);
            Emit($"memset(&{temp}, 0, sizeof {temp});");
            Emit($"{_names.Method(constructor)}({string.Join(", ", args.Prepend($"&{temp}"))});");
            PushFrom(type, temp);
            return;
        }
        if (type == _context.StringType)
            throw Unsupported("new string(...) is not supported yet");
        string obj = Temp("MObject*", $"m_new_object(&{_names.TypeInfo(type)})");
        Emit($"{_names.Method(constructor)}({string.Join(", ", args.Prepend($"({_translator.CType(type)}){obj}"))});");
        Push(new Entry(Kind.Ref, type, NonNull: true), obj);
    }

    /// <summary>Pops the arguments of a call, converted to the parameter types, in order.</summary>
    private List<string> PopArguments(int count, IReadOnlyList<TypeDesc>? types = null)
    {
        var popped = new List<(Entry, string)>();
        for (int i = 0; i < count; i++)
            popped.Add(Pop());
        popped.Reverse();
        return popped.Select((p, i) => types != null ? ToStorage(types[i], p.Item1, p.Item2) : p.Item2).ToList();
    }

    private void Call(MethodDesc target, bool virtualCall)
    {
        if (target.IsGeneric || target.Owner.IsGenericDefinition)
            throw Unsupported("generics are not supported yet (IL2C-2)");
        if (target.Owner.IsInterface)
            throw Unsupported($"interface calls ({target.Owner.Name}.{target.Name}) are not supported yet (IL2C-2)");

        // RuntimeHelpers.InitializeArray(array, ldtoken field): copy the constant data directly.
        if (target.Name == "InitializeArray" && target.Owner.FullName == "System.Runtime.CompilerServices.RuntimeHelpers")
        {
            var (token, _) = Pop();
            var (_, array) = Pop();
            var (data, size) = _translator.RvaData(token.Field!);
            Emit($"memcpy(M_ARRAY_DATA({array}), {data}, {size});");
            return;
        }

        List<string> args = PopArguments(target.ParameterTypes.Count, target.ParameterTypes);
        string name = _names.Method(target);
        string? call = null;

        if (!target.IsStatic)
        {
            var (thisEntry, thisVar) = Pop();
            TypeDesc? constrained = _constrained;
            if (constrained != null)
            {
                if (constrained.IsValueType)
                {
                    // A struct's own override is called directly on the value; otherwise the value is boxed.
                    MethodDesc? implementation = target.IsVirtual ? _translator.Implementation(constrained, target) : target;
                    if (implementation != null && implementation.Owner == constrained)
                    {
                        call = $"{_names.Method(implementation)}({string.Join(", ", args.Prepend($"({_translator.ThisCType(constrained)}){thisVar}"))})";
                    }
                    else
                    {
                        string boxed = Temp("MObject*", $"m_box(&{_names.TypeInfo(constrained)}, {thisVar})");
                        call = VirtualOrDirect(target, boxed, args, nonNull: true, virtualCall);
                    }
                }
                else
                {
                    string dereferenced = Temp("MObject*", $"*(MObject**){thisVar}");
                    call = VirtualOrDirect(target, dereferenced, args, nonNull: false, virtualCall);
                }
            }
            else if (target.Owner.IsValueType)
            {
                call = $"{name}({string.Join(", ", args.Prepend($"({_translator.ThisCType(target.Owner)}){thisVar}"))})";
            }
            else
            {
                call = VirtualOrDirect(target, thisVar, args, thisEntry.NonNull, virtualCall);
            }
        }
        else
        {
            call = $"{name}({string.Join(", ", args)})";
        }

        if (target.ReturnType.IsVoid)
            Emit(call + ";");
        else
            PushFrom(target.ReturnType, call);
    }

    private string VirtualOrDirect(MethodDesc target, string thisVar, List<string> args, bool nonNull, bool virtualCall)
    {
        if (virtualCall && !nonNull)
            Emit($"M_NULL_CHECK({thisVar});");
        string thisArg = $"({_translator.ThisCType(target.Owner)}){thisVar}";
        if (!virtualCall || !target.IsVirtual || target.VTableSlot < 0 && _translator.VTable(target.Owner).Count == 0)
            return $"{_names.Method(target)}({string.Join(", ", args.Prepend(thisArg))})";

        if (target.VTableSlot < 0)
            _translator.VTable(target.Owner);
        var parameterTypes = new List<string> { "void*" };
        parameterTypes.AddRange(target.ParameterTypes.Select(_translator.CType));
        string functionType = $"{_translator.CType(target.ReturnType)} (*)({string.Join(", ", parameterTypes)})";
        string function = $"(({functionType})((MObject*){thisVar})->type->vtable[{target.VTableSlot}])";
        return $"{function}({string.Join(", ", args.Prepend($"(void*){thisVar}"))})";
    }
}
