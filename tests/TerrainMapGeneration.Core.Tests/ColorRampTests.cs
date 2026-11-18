using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

public class ColorRampTests
{
    private static readonly ColorRamp BlackToWhite = new(
    [
        new ColorStop(1f, new Rgba(255, 255, 255)),
        new ColorStop(0f, new Rgba(0, 0, 0)),
    ]);

    [Fact]
    public void Evaluate_InterpolatesLinearly()
    {
        Assert.Equal(new Rgba(128, 128, 128), BlackToWhite.Evaluate(0.5f));
    }

    [Fact]
    public void Evaluate_OutOfRange_UsesExtremeStops()
    {
        Assert.Equal(new Rgba(0, 0, 0), BlackToWhite.Evaluate(-1f));
        Assert.Equal(new Rgba(255, 255, 255), BlackToWhite.Evaluate(2f));
    }

    [Fact]
    public void Apply_ProducesImageOfSameSize()
    {
        var image = ColorRamp.Terrain.Apply(new FloatMap(5, 3));
        Assert.Equal(5, image.Width);
        Assert.Equal(3, image.Height);
        Assert.All(image.Pixels, p => Assert.Equal(ColorRamp.Terrain.Stops[0].Color, p));
    }

    [Theory]
    [InlineData("#102030", 16, 32, 48, 255)]
    [InlineData("10203040", 16, 32, 48, 64)]
    public void FromHex_ParsesColors(string hex, byte r, byte g, byte b, byte a)
    {
        Assert.Equal(new Rgba(r, g, b, a), Rgba.FromHex(hex));
    }
}
