using TerrainMapGeneration.Core.Analysis;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Layers;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

public class MaskGeneratorTests
{
    private static readonly IReadOnlyList<SurfaceLayer> Defaults = SurfaceLayer.Defaults;

    private static int IndexOf(string name) => Defaults.ToList().FindIndex(l => l.Name == name);

    /// <summary>Contexto de 1×1 píxel con los valores dados.</summary>
    private static TerrainContext Pixel(float altitude, float slope, float erosion = 0f) =>
        new(new FloatMap(1, 1, [altitude]), new FloatMap(1, 1, [slope]), new FloatMap(1, 1, [erosion]));

    [Fact]
    public void RangeRule_InsideOutsideAndUnbounded()
    {
        var rule = new RangeRule(0.2f, 0.6f, 0.1f);
        Assert.Equal(1f, rule.Evaluate(0.4f));
        Assert.Equal(0f, rule.Evaluate(0.0f));
        Assert.Equal(0f, rule.Evaluate(0.9f));
        Assert.Equal(1f, new RangeRule().Evaluate(123f));
    }

    [Fact]
    public void RangeRule_TransitionIsCenteredAndSmooth()
    {
        var rule = new RangeRule(Min: 0.5f, Falloff: 0.2f);
        Assert.Equal(0.5f, rule.Evaluate(0.5f), 5);
        var below = rule.Evaluate(0.45f);
        var above = rule.Evaluate(0.55f);
        Assert.InRange(below, 0.01f, 0.49f);
        Assert.InRange(above, 0.51f, 0.99f);
        Assert.Equal(0f, rule.Evaluate(0.39f));
        Assert.Equal(1f, rule.Evaluate(0.61f));
    }

    [Theory]
    [InlineData(0.3f)]
    [InlineData(0.42f)]
    [InlineData(0.5f)]
    [InlineData(0.58f)]
    public void RangeRule_SharedEdgesAreComplementary(float value)
    {
        var lower = new RangeRule(Max: 0.5f, Falloff: 0.2f);
        var upper = new RangeRule(Min: 0.5f, Falloff: 0.2f);
        Assert.Equal(1f, lower.Evaluate(value) + upper.Evaluate(value), 5);
    }

    [Fact]
    public void Masks_SumToOneEverywhere()
    {
        var height = TestMaps.Cone(48);
        var context = new TerrainContext(height, TerrainAnalysis.SlopeDegrees(height, 40),
            FloatMap.Create(48, 48, (x, _) => x / 47f));

        var masks = MaskGenerator.Generate(context, Defaults);

        for (var i = 0; i < height.Length; i++)
        {
            var sum = masks.Masks.Sum(m => m.Data[i]);
            Assert.Equal(1f, sum, 4);
            Assert.All(masks.Masks, m => Assert.InRange(m.Data[i], 0f, 1f));
        }
    }

    [Theory]
    [InlineData(0.05f, 0f, 0f, "agua")]
    [InlineData(0.22f, 2f, 0f, "arena")]
    [InlineData(0.50f, 5f, 0f, "pasto")]
    [InlineData(0.50f, 5f, 1f, "tierra")]
    [InlineData(0.50f, 60f, 0f, "roca")]
    [InlineData(0.95f, 5f, 0f, "nieve")]
    [InlineData(0.95f, 70f, 0f, "roca")]
    public void DefaultLayers_DominantLayerMatchesTerrain(float altitude, float slope, float erosion, string expected)
    {
        var masks = MaskGenerator.Generate(Pixel(altitude, slope, erosion), Defaults);
        Assert.Equal(expected, masks.Layers[masks.DominantLayerAt(0, 0)].Name);
    }

    [Fact]
    public void NoApplicableRule_UsesFallbackLayer()
    {
        var layers = new[]
        {
            new SurfaceLayer("a", new Rgba(0, 0, 0), Altitude: new RangeRule(Max: 0.1f)),
            new SurfaceLayer("b", new Rgba(0, 0, 0), Altitude: new RangeRule(Max: 0.2f)),
        };

        var masks = MaskGenerator.Generate(Pixel(0.9f, 0f), layers, fallbackLayer: 1);

        Assert.Equal(0f, masks.Masks[0][0, 0]);
        Assert.Equal(1f, masks.Masks[1][0, 0]);
    }

    [Fact]
    public void Weight_BreaksTies()
    {
        var layers = new[]
        {
            new SurfaceLayer("a", new Rgba(0, 0, 0)),
            new SurfaceLayer("b", new Rgba(0, 0, 0), Weight: 3f),
        };

        var masks = MaskGenerator.Generate(Pixel(0.5f, 0f), layers);

        Assert.Equal(0.25f, masks.Masks[0][0, 0], 5);
        Assert.Equal(0.75f, masks.Masks[1][0, 0], 5);
    }

    [Fact]
    public void Generate_MismatchedSizes_Throws()
    {
        var context = new TerrainContext(new FloatMap(4, 4), new FloatMap(3, 4));
        Assert.Throws<ArgumentException>(() => MaskGenerator.Generate(context, Defaults));
    }

    [Fact]
    public void DefaultLayers_HaveUniqueNames()
    {
        Assert.Equal(Defaults.Count, Defaults.Select(l => l.Name).Distinct().Count());
        Assert.True(IndexOf("roca") >= 0);
    }
}
