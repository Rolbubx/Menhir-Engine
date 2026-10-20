using System.Numerics;
using System.Text;

namespace Menhir.Editor.Build;

/// <summary>
/// The container format of GameData.binchunk (see Docs/BinChunk.md), little-endian:
/// a 16-byte header ("MNHR", version, chunk count, flags), a table of 24-byte entries
/// (type, id, offset, size) and the chunks' data, each starting on a 16-byte boundary.
/// The game reads the table at startup and loads chunks when it needs them.
/// </summary>
public static class BinChunk
{
    public const uint Magic = 0x52484E4D; // "MNHR"
    public const uint Version = 1;
    public const int HeaderSize = 16;
    public const int EntrySize = 24;
    public const int Alignment = 16;

    // Chunk types (four characters, read as a little-endian u32).
    public static readonly uint Game = FourCC("GAME");
    public static readonly uint Scene = FourCC("SCNE");
    public static readonly uint Mesh = FourCC("MESH");
    public static readonly uint Texture = FourCC("TEXR");

    public static uint FourCC(string code)
    {
        if (code.Length != 4)
            throw new ArgumentException("A chunk type has four characters.", nameof(code));
        return code[0] | (uint)code[1] << 8 | (uint)code[2] << 16 | (uint)code[3] << 24;
    }

    public static string TypeName(uint type) =>
        new([(char)(type & 0xFF), (char)(type >> 8 & 0xFF), (char)(type >> 16 & 0xFF), (char)(type >> 24)]);
}

/// <summary>One chunk: its type, an id unique for that type, and its bytes.</summary>
public sealed record Chunk(uint Type, uint Id, byte[] Data);

/// <summary>Builds a binchunk file from chunks.</summary>
public sealed class BinChunkWriter
{
    private readonly List<Chunk> _chunks = new();

    public IReadOnlyList<Chunk> Chunks => _chunks;

    public void Add(uint type, uint id, byte[] data)
    {
        if (_chunks.Any(c => c.Type == type && c.Id == id))
            throw new InvalidOperationException($"Two {BinChunk.TypeName(type)} chunks have the id {id}.");
        _chunks.Add(new Chunk(type, id, data));
    }

    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(BinChunk.Magic);
        w.Write(BinChunk.Version);
        w.Write((uint)_chunks.Count);
        w.Write(0u);

        long offset = Align(BinChunk.HeaderSize + (long)BinChunk.EntrySize * _chunks.Count);
        var offsets = new long[_chunks.Count];
        for (int i = 0; i < _chunks.Count; i++)
        {
            offsets[i] = offset;
            w.Write(_chunks[i].Type);
            w.Write(_chunks[i].Id);
            w.Write((ulong)offset);
            w.Write((ulong)_chunks[i].Data.Length);
            offset = Align(offset + _chunks[i].Data.Length);
        }
        for (int i = 0; i < _chunks.Count; i++)
        {
            while (stream.Position < offsets[i])
                w.Write((byte)0);
            w.Write(_chunks[i].Data);
        }
        w.Flush();
        return stream.ToArray();
    }

    private static long Align(long value) => (value + BinChunk.Alignment - 1) / BinChunk.Alignment * BinChunk.Alignment;
}

/// <summary>Reads the chunks of a binchunk file (the editor's tests use it; the game has a C reader).</summary>
public static class BinChunkReader
{
    public static List<Chunk> Read(byte[] file)
    {
        if (file.Length < BinChunk.HeaderSize)
            throw new InvalidDataException("The file is too small to be a binchunk.");
        using var r = new BinaryReader(new MemoryStream(file));
        if (r.ReadUInt32() != BinChunk.Magic)
            throw new InvalidDataException("Not a binchunk file.");
        uint version = r.ReadUInt32();
        if (version != BinChunk.Version)
            throw new InvalidDataException($"Unsupported binchunk version {version}.");
        uint count = r.ReadUInt32();
        r.ReadUInt32();
        if (BinChunk.HeaderSize + (long)count * BinChunk.EntrySize > file.Length)
            throw new InvalidDataException("The chunk table is cut off.");

        var chunks = new List<Chunk>((int)count);
        for (int i = 0; i < count; i++)
        {
            uint type = r.ReadUInt32(), id = r.ReadUInt32();
            ulong offset = r.ReadUInt64(), size = r.ReadUInt64();
            if (offset > (ulong)file.Length || size > (ulong)file.Length - offset)
                throw new InvalidDataException($"Chunk {BinChunk.TypeName(type)} {id} is outside the file.");
            chunks.Add(new Chunk(type, id, file.AsSpan((int)offset, (int)size).ToArray()));
        }
        return chunks;
    }
}

/// <summary>Little-endian writer for the inside of a chunk.</summary>
public sealed class ChunkWriter
{
    private readonly MemoryStream _stream = new();
    private readonly BinaryWriter _w;

    public ChunkWriter()
    {
        _w = new BinaryWriter(_stream);
    }

    public void U32(uint value) => _w.Write(value);
    public void I32(int value) => _w.Write(value);
    public void F32(float value) => _w.Write(value);
    public void Bool(bool value) => _w.Write(value ? 1u : 0u);
    public void Vec2(Vector2 v) { F32(v.X); F32(v.Y); }
    public void Vec3(Vector3 v) { F32(v.X); F32(v.Y); F32(v.Z); }
    public void Vec4(Vector4 v) { F32(v.X); F32(v.Y); F32(v.Z); F32(v.W); }
    public void Quat(Quaternion q) { F32(q.X); F32(q.Y); F32(q.Z); F32(q.W); }
    public void Bytes(ReadOnlySpan<byte> bytes) => _w.Write(bytes);

    /// <summary>u32 byte length, then UTF-8 bytes (no terminator).</summary>
    public void String(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        U32((uint)bytes.Length);
        _w.Write(bytes);
    }

    public byte[] ToArray()
    {
        _w.Flush();
        return _stream.ToArray();
    }
}
