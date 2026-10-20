using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Menhir.Editor.Assets;

namespace Menhir.Editor.Tests.Assets;

public class PngDecoderTests
{
    /// <summary>Writes a PNG with the given rows (each prefixed by its filter byte already applied).</summary>
    private static byte[] MakePng(int width, int height, byte colorType, byte[] filteredRows, byte[]? palette = null)
    {
        using var file = new MemoryStream();
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        void Chunk(string type, byte[] data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            file.Write(length);
            file.Write(Encoding.ASCII.GetBytes(type));
            file.Write(data);
            file.Write(new byte[4]); // CRC, not checked
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = colorType;
        Chunk("IHDR", header);
        if (palette != null)
            Chunk("PLTE", palette);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(filteredRows);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return file.ToArray();
    }

    [Fact]
    public void Decode_RgbWithSubAndUpFilters()
    {
        // 2x2 RGB: row 0 with Sub filter, row 1 with Up filter.
        byte[] rows =
        [
            1, 10, 20, 30, 5, 5, 5, // (10,20,30) (15,25,35)
            2, 1, 1, 1, 1, 1, 1,    // (11,21,31) (16,26,36)
        ];
        ImageData image = PngDecoder.Decode(MakePng(2, 2, 2, rows));

        Assert.Equal(2, image.Width);
        Assert.Equal(
            new byte[] { 10, 20, 30, 255, 15, 25, 35, 255, 11, 21, 31, 255, 16, 26, 36, 255 },
            image.Rgba);
    }

    [Fact]
    public void Decode_RgbaWithPaethFilter()
    {
        byte[] rows =
        [
            0, 1, 2, 3, 4, 5, 6, 7, 8,
            4, 1, 1, 1, 1, 1, 1, 1, 1,
        ];
        ImageData image = PngDecoder.Decode(MakePng(2, 2, 6, rows));
        // Paeth of the first pixel of row 1 picks the pixel above (a = c = 0).
        Assert.Equal(new byte[] { 2, 3, 4, 5 }, image.Rgba[8..12]);
    }

    [Fact]
    public void Decode_Palette()
    {
        byte[] palette = [255, 0, 0, 0, 0, 255];
        ImageData image = PngDecoder.Decode(MakePng(2, 1, 3, [0, 1, 0], palette));
        Assert.Equal(new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }, image.Rgba);
    }

    [Fact]
    public void Decode_RejectsOtherFiles()
    {
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(Encoding.ASCII.GetBytes("not a png file")));
    }
}
