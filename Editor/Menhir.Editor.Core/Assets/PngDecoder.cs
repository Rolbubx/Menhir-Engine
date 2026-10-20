using System.Buffers.Binary;
using System.IO.Compression;

namespace Menhir.Editor.Assets;

/// <summary>An image as 8-bit RGBA pixels, top row first.</summary>
public sealed record ImageData(int Width, int Height, byte[] Rgba);

/// <summary>
/// Minimal PNG reader (no dependency): 8-bit grayscale, gray + alpha, RGB, RGBA and palette
/// images, without interlacing. Enough for lookup textures; CRCs are not checked.
/// </summary>
public static class PngDecoder
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static ImageData Decode(byte[] file)
    {
        if (file.Length < 8 || !file.AsSpan(0, 8).SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file.");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        using var compressed = new MemoryStream();

        int offset = 8;
        while (offset + 8 <= file.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(offset));
            string type = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
            if (length < 0 || offset + 12 + length > file.Length)
                throw new InvalidDataException("Truncated PNG chunk.");
            ReadOnlySpan<byte> data = file.AsSpan(offset + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;
                case "PLTE":
                    palette = data.ToArray();
                    break;
                case "tRNS":
                    paletteAlpha = data.ToArray();
                    break;
                case "IDAT":
                    compressed.Write(data);
                    break;
            }
            offset += 12 + length;
            if (type == "IEND")
                break;
        }

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("PNG without a valid header.");
        if (bitDepth != 8 || interlace != 0)
            throw new NotSupportedException("Only 8-bit, non-interlaced PNG files are supported.");

        int channels = colorType switch
        {
            0 => 1, // gray
            2 => 3, // RGB
            3 => 1, // palette
            4 => 2, // gray + alpha
            6 => 4, // RGBA
            _ => throw new NotSupportedException($"PNG color type {colorType} is not supported."),
        };
        if (colorType == 3 && palette == null)
            throw new InvalidDataException("Palette PNG without a palette.");

        byte[] raw = Inflate(compressed.ToArray());
        int stride = width * channels;
        if (raw.Length < (stride + 1) * height)
            throw new InvalidDataException("PNG image data is too short.");

        var pixels = new byte[stride * height];
        Unfilter(raw, pixels, stride, height, channels);

        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            int s = i * channels, d = i * 4;
            switch (colorType)
            {
                case 0:
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = pixels[s];
                    rgba[d + 3] = 255;
                    break;
                case 2:
                    rgba[d] = pixels[s]; rgba[d + 1] = pixels[s + 1]; rgba[d + 2] = pixels[s + 2];
                    rgba[d + 3] = 255;
                    break;
                case 3:
                    int index = pixels[s];
                    rgba[d] = palette![index * 3]; rgba[d + 1] = palette[index * 3 + 1]; rgba[d + 2] = palette[index * 3 + 2];
                    rgba[d + 3] = paletteAlpha != null && index < paletteAlpha.Length ? paletteAlpha[index] : (byte)255;
                    break;
                case 4:
                    rgba[d] = rgba[d + 1] = rgba[d + 2] = pixels[s];
                    rgba[d + 3] = pixels[s + 1];
                    break;
                default:
                    Array.Copy(pixels, s, rgba, d, 4);
                    break;
            }
        }
        return new ImageData(width, height, rgba);
    }

    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new ZLibStream(new MemoryStream(zlib), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Undoes the per-row filters (None, Sub, Up, Average, Paeth).</summary>
    private static void Unfilter(byte[] raw, byte[] pixels, int stride, int height, int bpp)
    {
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            int src = y * (stride + 1) + 1;
            int row = y * stride;
            int prev = row - stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? pixels[row + x - bpp] : 0;
                int b = y > 0 ? pixels[prev + x] : 0;
                int c = x >= bpp && y > 0 ? pixels[prev + x - bpp] : 0;
                int value = raw[src + x];
                value += filter switch
                {
                    0 => 0,
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => throw new InvalidDataException($"Unknown PNG filter {filter}."),
                };
                pixels[row + x] = (byte)value;
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
