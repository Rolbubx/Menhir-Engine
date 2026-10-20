using Menhir.Editor.Build;

namespace Menhir.Editor.Tests.Build;

public class BinChunkTests
{
    [Fact]
    public void WriteThenRead_GivesTheChunksBack()
    {
        var writer = new BinChunkWriter();
        writer.Add(BinChunk.Game, 0, [1, 2, 3]);
        writer.Add(BinChunk.Scene, 0, new byte[100]);
        writer.Add(BinChunk.Scene, 1, []);
        byte[] file = writer.ToArray();

        List<Chunk> chunks = BinChunkReader.Read(file);
        Assert.Equal(3, chunks.Count);
        Assert.Equal("GAME", BinChunk.TypeName(chunks[0].Type));
        Assert.Equal(new byte[] { 1, 2, 3 }, chunks[0].Data);
        Assert.Equal((BinChunk.Scene, 1u), (chunks[2].Type, chunks[2].Id));
        Assert.Equal(100, chunks[1].Data.Length);
    }

    [Fact]
    public void Header_IsMagicVersionCountFlags_AndChunksAreAligned()
    {
        var writer = new BinChunkWriter();
        writer.Add(BinChunk.Mesh, 7, [9]);
        writer.Add(BinChunk.Mesh, 8, [10]);
        byte[] file = writer.ToArray();

        Assert.Equal("MNHR"u8.ToArray(), file[..4]);
        Assert.Equal(1u, BitConverter.ToUInt32(file, 4));
        Assert.Equal(2u, BitConverter.ToUInt32(file, 8));
        ulong first = BitConverter.ToUInt64(file, 16 + 8), second = BitConverter.ToUInt64(file, 16 + 24 + 8);
        Assert.Equal(0ul, first % 16);
        Assert.Equal(0ul, second % 16);
        Assert.Equal(9, file[first]);
        Assert.Equal(10, file[second]);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var writer = new BinChunkWriter();
        writer.Add(BinChunk.Texture, 1, []);
        Assert.Throws<InvalidOperationException>(() => writer.Add(BinChunk.Texture, 1, []));
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => BinChunkReader.Read("not a binchunk file"u8.ToArray()));
        byte[] file = new BinChunkWriter().ToArray();
        file[4] = 2; // version
        Assert.Throws<InvalidDataException>(() => BinChunkReader.Read(file));
    }
}
