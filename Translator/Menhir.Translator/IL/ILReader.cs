using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Menhir.Translator.IL;

/// <summary>One decoded IL instruction.</summary>
/// <param name="Operand">Integer operand (constants, local / argument index, branch target, token).</param>
/// <param name="Targets">Branch targets of a switch.</param>
public readonly record struct Instruction(int Offset, ILOpCode OpCode, long Operand, double FloatOperand,
    ImmutableArray<int> Targets, int Size)
{
    public int Next => Offset + Size;

    public int Token => (int)Operand;

    public bool IsBranch => OpCode is >= ILOpCode.Br_s and <= ILOpCode.Blt_un or ILOpCode.Leave or ILOpCode.Leave_s;

    /// <summary>Control does not fall through to the next instruction.</summary>
    public bool EndsBlock => OpCode is ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Ret or ILOpCode.Throw
        or ILOpCode.Rethrow or ILOpCode.Leave or ILOpCode.Leave_s or ILOpCode.Endfinally or ILOpCode.Endfilter
        or ILOpCode.Jmp;

    public override string ToString() => $"IL_{Offset:x4}: {OpCode}";
}

/// <summary>Decodes a method body's IL bytes into instructions.</summary>
public static class ILReader
{
    public static List<Instruction> Decode(MethodBodyBlock body)
    {
        var instructions = new List<Instruction>();
        BlobReader reader = body.GetILReader();
        while (reader.RemainingBytes > 0)
        {
            int offset = reader.Offset;
            ILOpCode opCode = ReadOpCode(ref reader);
            long operand = 0;
            double floatOperand = 0;
            ImmutableArray<int> targets = default;

            switch (OperandKind(opCode))
            {
                case Operand.None:
                    break;
                case Operand.Int8:
                    operand = reader.ReadSByte();
                    break;
                case Operand.UInt8:
                    operand = reader.ReadByte();
                    break;
                case Operand.UInt16:
                    operand = reader.ReadUInt16();
                    break;
                case Operand.Int32:
                case Operand.Token:
                    operand = reader.ReadInt32();
                    break;
                case Operand.Int64:
                    operand = reader.ReadInt64();
                    break;
                case Operand.Float32:
                    floatOperand = reader.ReadSingle();
                    break;
                case Operand.Float64:
                    floatOperand = reader.ReadDouble();
                    break;
                case Operand.ShortBranch:
                    operand = reader.ReadSByte();
                    operand += reader.Offset;
                    break;
                case Operand.Branch:
                    operand = reader.ReadInt32();
                    operand += reader.Offset;
                    break;
                case Operand.Switch:
                {
                    int count = reader.ReadInt32();
                    var deltas = new int[count];
                    for (int i = 0; i < count; i++)
                        deltas[i] = reader.ReadInt32();
                    int end = reader.Offset;
                    targets = deltas.Select(d => end + d).ToImmutableArray();
                    break;
                }
            }
            instructions.Add(new Instruction(offset, opCode, operand, floatOperand, targets, reader.Offset - offset));
        }
        return instructions;
    }

    private static ILOpCode ReadOpCode(ref BlobReader reader)
    {
        byte first = reader.ReadByte();
        return first == 0xFE ? (ILOpCode)(0xFE00 | reader.ReadByte()) : (ILOpCode)first;
    }

    private enum Operand
    {
        None,
        Int8,
        UInt8,
        UInt16,
        Int32,
        Int64,
        Float32,
        Float64,
        Token,
        ShortBranch,
        Branch,
        Switch,
    }

    private static Operand OperandKind(ILOpCode op) => op switch
    {
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s
            or ILOpCode.Stloc_s or ILOpCode.Unaligned => Operand.UInt8,
        ILOpCode.Ldc_i4_s => Operand.Int8,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca
            or ILOpCode.Stloc => Operand.UInt16,
        ILOpCode.Ldc_i4 => Operand.Int32,
        ILOpCode.Ldc_i8 => Operand.Int64,
        ILOpCode.Ldc_r4 => Operand.Float32,
        ILOpCode.Ldc_r8 => Operand.Float64,
        ILOpCode.Br_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s or ILOpCode.Beq_s or ILOpCode.Bge_s
            or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s
            or ILOpCode.Bgt_un_s or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s or ILOpCode.Leave_s => Operand.ShortBranch,
        ILOpCode.Br or ILOpCode.Brfalse or ILOpCode.Brtrue or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt
            or ILOpCode.Ble or ILOpCode.Blt or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
            or ILOpCode.Ble_un or ILOpCode.Blt_un or ILOpCode.Leave => Operand.Branch,
        ILOpCode.Switch => Operand.Switch,
        ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Calli or ILOpCode.Callvirt or ILOpCode.Cpobj
            or ILOpCode.Ldobj or ILOpCode.Ldstr or ILOpCode.Newobj or ILOpCode.Castclass or ILOpCode.Isinst
            or ILOpCode.Unbox or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld or ILOpCode.Ldsfld
            or ILOpCode.Ldsflda or ILOpCode.Stsfld or ILOpCode.Stobj or ILOpCode.Box or ILOpCode.Newarr
            or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Unbox_any
            or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Ldtoken or ILOpCode.Ldftn
            or ILOpCode.Ldvirtftn or ILOpCode.Initobj or ILOpCode.Constrained or ILOpCode.Sizeof => Operand.Token,
        _ => Operand.None,
    };
}
