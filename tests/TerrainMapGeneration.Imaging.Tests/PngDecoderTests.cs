using TerrainMapGeneration.Imaging.Png;

namespace TerrainMapGeneration.Imaging.Tests;

/// <summary>
/// Decodifica PNG creados por codificadores externos (Pillow e ImageMagick) y compara con los
/// patrones conocidos con los que se generaron (13×7 píxeles).
/// </summary>
public class PngDecoderTests
{
    private const int W = 13;
    private const int H = 7;

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static int G8(int x, int y) => ((x * 19) + (y * 37)) % 256;

    private static int G16(int x, int y) => ((x * 4099) + (y * 9001)) % 65536;

    private static float Lum(float r, float g, float b) => (0.2126f * r) + (0.7152f * g) + (0.0722f * b);

    private static float Rgb8Lum(int x, int y) => Lum(G8(x, y), ((y * 37) + x) % 256, (x * y * 11) % 256) / 255f;

    public static TheoryData<string, int, PngColorType> Headers => new()
    {
        { "gray1.png", 1, PngColorType.Grayscale },
        { "gray4.png", 4, PngColorType.Grayscale },
        { "gray8.png", 8, PngColorType.Grayscale },
        { "gray16.png", 16, PngColorType.Grayscale },
        { "gray16_interlaced.png", 16, PngColorType.Grayscale },
        { "grayalpha8.png", 8, PngColorType.GrayscaleAlpha },
        { "palette8.png", 8, PngColorType.Palette },
        { "rgb8.png", 8, PngColorType.Rgb },
        { "rgb8_interlaced.png", 8, PngColorType.Palette },
        { "rgb16.png", 16, PngColorType.Rgb },
        { "rgba8.png", 8, PngColorType.RgbAlpha },
    };

    [Theory]
    [MemberData(nameof(Headers))]
    public void Decode_ReadsHeader(string file, int bitDepth, PngColorType colorType)
    {
        var png = PngDecoder.Decode(Fixture(file));
        Assert.Equal(W, png.Width);
        Assert.Equal(H, png.Height);
        Assert.Equal(bitDepth, png.SourceBitDepth);
        Assert.Equal(colorType, png.SourceColorType);
    }

    [Theory]
    [InlineData("gray8.png")]
    [InlineData("grayalpha8.png")]
    public void Decode_Gray8(string file) => AssertPixels(file, (x, y) => G8(x, y) / 255f);

    [Theory]
    [InlineData("gray16.png")]
    [InlineData("gray16_interlaced.png")]
    public void Decode_Gray16_IsExact(string file)
    {
        var png = PngDecoder.Decode(Fixture(file));
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(G16(x, y), png.Samples[(y * W) + x]);
            }
        }
    }

    [Fact]
    public void Decode_Gray4_PackedSamples() => AssertPixels("gray4.png", (x, y) => ((x + (y * 3)) % 16) / 15f);

    [Fact]
    public void Decode_Gray1_PackedBits() => AssertPixels("gray1.png", (x, y) => (x + y) % 2);

    [Theory]
    [InlineData("rgb8.png")]
    [InlineData("rgba8.png")]
    [InlineData("rgb8_interlaced.png")]
    public void Decode_Color_UsesRec709Luminance(string file) => AssertPixels(file, Rgb8Lum);

    [Fact]
    public void Decode_Palette_ExpandsColors() =>
        AssertPixels("palette8.png", (x, y) =>
        {
            var i = G8(x, y);
            return Lum(i, (i * 7) % 256, 255 - i) / 255f;
        });

    [Fact]
    public void Decode_Rgb16() =>
        AssertPixels("rgb16.png", (x, y) =>
        {
            var v = G16(x, y);
            return Lum(v, (v * 3) % 65536, 65535 - v) / 65535f;
        });

    [Fact]
    public void Decode_NotPng_Throws()
    {
        using var stream = new MemoryStream("no soy un png"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(stream));
    }

    [Fact]
    public void Decode_CorruptedData_FailsCrcCheck()
    {
        var bytes = File.ReadAllBytes(Fixture("gray8.png"));
        bytes[^20] ^= 0xFF; // altera un byte dentro del último chunk de datos
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(stream));
    }

    [Fact]
    public void Decode_Truncated_Throws()
    {
        var bytes = File.ReadAllBytes(Fixture("gray8.png"));
        using var stream = new MemoryStream(bytes[..(bytes.Length / 2)]);
        Assert.Throws<InvalidDataException>(() => PngDecoder.Decode(stream));
    }

    private static void AssertPixels(string file, Func<int, int, float> expected)
    {
        var png = PngDecoder.Decode(Fixture(file));
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                Assert.True(MathF.Abs(expected(x, y) - png.GetLuminance(x, y)) < 1e-4f,
                    $"{file} ({x},{y}): esperado {expected(x, y)}, obtenido {png.GetLuminance(x, y)}");
            }
        }
    }
}
