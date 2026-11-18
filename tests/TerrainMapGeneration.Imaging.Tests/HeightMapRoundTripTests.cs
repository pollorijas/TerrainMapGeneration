using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Maps;
using TerrainMapGeneration.Imaging.Png;

namespace TerrainMapGeneration.Imaging.Tests;

public sealed class HeightMapRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("terrain-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FloatMap Gradient() => FloatMap.Create(37, 23, (x, y) => (x + (y * 37)) / ((37f * 23f) - 1));

    [Fact]
    public void Gray16_RoundTrip_IsPracticallyLossless()
    {
        var original = Gradient();
        var path = Path.Combine(_dir, "h16.png");

        ImageWriter.SaveGray16(original, path);
        var loaded = HeightMapReader.Load(path);

        Assert.Equal(original.Width, loaded.Width);
        Assert.Equal(original.Height, loaded.Height);
        for (var i = 0; i < original.Length; i++)
        {
            Assert.True(MathF.Abs(original.Data[i] - loaded.Data[i]) <= 1f / ushort.MaxValue);
        }
    }

    [Fact]
    public void Gray8_RoundTrip_ErrorAtMostOneLevel()
    {
        var original = Gradient();
        var path = Path.Combine(_dir, "h8.png");

        ImageWriter.SaveGray8(original, path);
        var loaded = HeightMapReader.Load(path);

        for (var i = 0; i < original.Length; i++)
        {
            Assert.True(MathF.Abs(original.Data[i] - loaded.Data[i]) <= (1f / 255f) + 1e-6f);
        }
    }

    [Fact]
    public void SaveRgba_Opaque_WritesRgbWithExactColors()
    {
        var source = new RgbaImage(2, 1);
        source[0, 0] = new Rgba(10, 20, 30);
        source[1, 0] = new Rgba(200, 100, 50);
        var path = Path.Combine(_dir, "rgb.png");

        ImageWriter.SaveRgba(source, path);

        var png = PngDecoder.Decode(path);
        Assert.Equal(PngColorType.Rgb, png.SourceColorType);
        Assert.Equal([10 * 257, 20 * 257, 30 * 257, 200 * 257, 100 * 257, 50 * 257], png.Samples.Select(s => (int)s));
    }

    [Fact]
    public void SaveRgba_WithTransparency_KeepsAlpha()
    {
        var source = new RgbaImage(1, 1);
        source[0, 0] = new Rgba(1, 2, 3, 4);
        var path = Path.Combine(_dir, "rgba.png");

        ImageWriter.SaveRgba(source, path);

        var png = PngDecoder.Decode(path);
        Assert.Equal(PngColorType.RgbAlpha, png.SourceColorType);
        Assert.Equal(4 * 257, png.Samples[3]);
    }

    [Fact]
    public void Encoder_RandomData_RoundTripsThroughAllFilters()
    {
        // Datos aleatorios + rampa: obliga a que el codificador elija distintos filtros por fila.
        var random = new Random(42);
        const int width = 31, height = 17;
        var bytes = new byte[width * height * 3];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (i / (width * 3)) % 2 == 0 ? (byte)random.Next(256) : (byte)(i % 251);
        }

        using var stream = new MemoryStream();
        PngEncoder.Encode(stream, width, height, PngColorType.Rgb, 8, bytes);
        stream.Position = 0;
        var png = PngDecoder.Decode(stream);

        Assert.Equal(bytes.Select(b => b * 257), png.Samples.Select(s => (int)s));
    }

    [Fact]
    public void Writer_CreatesMissingDirectories()
    {
        var path = Path.Combine(_dir, "a", "b", "map.png");
        ImageWriter.SaveGray8(new FloatMap(2, 2), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SampleHeightmap_FromRepository_Loads()
    {
        // samples/heightmaps/island_256.png fue escrito por un codificador externo (ImageSharp, 16 bits).
        var path = Path.Combine(FindRepoRoot(), "samples", "heightmaps", "island_256.png");
        var map = HeightMapReader.Load(path);
        var (min, max) = map.GetRange();
        Assert.Equal(256, map.Width);
        Assert.True(min >= 0f && max <= 1f && max > min);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TerrainMapGeneration.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
