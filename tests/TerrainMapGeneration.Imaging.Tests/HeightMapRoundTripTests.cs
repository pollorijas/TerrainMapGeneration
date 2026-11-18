using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Imaging.Tests;

public sealed class HeightMapRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("terrain-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FloatMap Gradient() => FloatMap.Create(37, 23, (x, y) => (x + (y * 37)) / (37f * 23f - 1));

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
            Assert.True(MathF.Abs(original.Data[i] - loaded.Data[i]) <= 1f / 255f + 1e-6f);
        }
    }

    [Fact]
    public void Load_RgbImage_ConvertsToLuminance()
    {
        var path = Path.Combine(_dir, "rgb.png");
        using (var image = new Image<Rgb24>(2, 1))
        {
            image[0, 0] = new Rgb24(0, 0, 0);
            image[1, 0] = new Rgb24(255, 255, 255);
            image.SaveAsPng(path);
        }

        var map = HeightMapReader.Load(path);

        Assert.Equal(0f, map[0, 0], 4);
        Assert.Equal(1f, map[1, 0], 4);
    }

    [Fact]
    public void SaveRgba_WritesExactColors()
    {
        var source = new RgbaImage(2, 1);
        source[0, 0] = new Rgba(10, 20, 30);
        source[1, 0] = new Rgba(200, 100, 50);
        var path = Path.Combine(_dir, "rgba.png");

        ImageWriter.SaveRgba(source, path);

        using var image = Image.Load<Rgba32>(path);
        Assert.Equal(new Rgba32(10, 20, 30, 255), image[0, 0]);
        Assert.Equal(new Rgba32(200, 100, 50, 255), image[1, 0]);
    }

    [Fact]
    public void Writer_CreatesMissingDirectories()
    {
        var path = Path.Combine(_dir, "a", "b", "map.png");
        ImageWriter.SaveGray8(new FloatMap(2, 2), path);
        Assert.True(File.Exists(path));
    }
}
