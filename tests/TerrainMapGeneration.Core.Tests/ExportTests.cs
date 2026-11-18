using TerrainMapGeneration.Core.Analysis;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Layers;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

public class ExportTests
{
    private static readonly Rgba Red = new(255, 0, 0);
    private static readonly Rgba Blue = new(0, 0, 255);

    /// <summary>Capas de prueba: "bajo" (altura &lt; 0.5) y "alto" (≥ 0.5), con corte duro.</summary>
    private static readonly SurfaceLayer[] TwoLayers =
    [
        new("bajo", Red, Altitude: new RangeRule(Max: 0.5f)),
        new("alto", Blue, Altitude: new RangeRule(Min: 0.5f)),
    ];

    private static LayerMasks Masks(FloatMap height, IReadOnlyList<SurfaceLayer> layers) =>
        MaskGenerator.Generate(new TerrainContext(height, new FloatMap(height.Width, height.Height)), layers);

    [Fact]
    public void Splatmap_FourOrFewerLayers_IsOneImage()
    {
        var masks = Masks(TestMaps.Flat(4, 0.2f), TwoLayers);

        var splatmaps = SplatmapEncoder.Encode(masks);

        var splat = Assert.Single(splatmaps);
        Assert.Equal(["bajo", "alto", null, null], splat.ChannelLayers);
        Assert.All(splat.Image.Pixels, p => Assert.Equal(new Rgba(255, 0, 0, 255), p));
    }

    [Fact]
    public void Splatmap_SixLayers_IsTwoImages_WithWeightsInChannels()
    {
        var height = TestMaps.Cone(16);
        var masks = MaskGenerator.Generate(
            new TerrainContext(height, TerrainAnalysis.SlopeDegrees(height, 20)), SurfaceLayer.Defaults);

        var splatmaps = SplatmapEncoder.Encode(masks);

        Assert.Equal(2, splatmaps.Count);
        Assert.Equal(["roca", "nieve", null, null], splatmaps[1].ChannelLayers);
        var i = (5 * 16) + 7;
        Assert.Equal((byte)MathF.Round(masks.Masks[1].Data[i] * 255), splatmaps[0].Image.Pixels[i].G);
        Assert.Equal((byte)MathF.Round(masks.Masks[4].Data[i] * 255), splatmaps[1].Image.Pixels[i].R);
    }

    [Fact]
    public void ColorBlender_MixesColorsByWeight()
    {
        var layers = new[] { new SurfaceLayer("a", Red), new SurfaceLayer("b", Blue) }; // 50 % cada una

        var image = ColorBlender.Blend(Masks(TestMaps.Flat(2), layers));

        Assert.All(image.Pixels, p => Assert.Equal(new Rgba(128, 0, 128), p));
    }

    [Fact]
    public void ColorBlender_ShadingDarkens()
    {
        var masks = Masks(TestMaps.Flat(2, 0.2f), TwoLayers);
        var dark = ColorBlender.Blend(masks, shading: TestMaps.Flat(2, 0f), shadingStrength: 0.5f);
        Assert.All(dark.Pixels, p => Assert.Equal(new Rgba(128, 0, 0), p));
    }

    [Fact]
    public void TileClassifier_ComputesTileGridSize()
    {
        var masks = Masks(new FloatMap(64, 48), TwoLayers);
        var tiles = TileClassifier.Classify(masks, 16);
        Assert.Equal(4, tiles.Width);
        Assert.Equal(3, tiles.Height);

        var partial = TileClassifier.Classify(Masks(new FloatMap(70, 33), TwoLayers), 16);
        Assert.Equal(5, partial.Width);
        Assert.Equal(3, partial.Height);
    }

    [Fact]
    public void TileClassifier_UsesWeightedMajority()
    {
        // Tile de 4×4: 10 píxeles "alto" y 6 "bajo" → "alto". Segundo tile todo "bajo".
        var height = FloatMap.Create(8, 4, (x, y) => x < 4 && (y * 4) + x < 10 ? 0.9f : 0.1f);

        var tiles = TileClassifier.Classify(Masks(height, TwoLayers), 4);

        Assert.Equal(1, tiles[0, 0]);
        Assert.Equal(0, tiles[1, 0]);
    }

    [Fact]
    public void TileMap_ImageAndCsv()
    {
        var height = FloatMap.Create(4, 2, (x, _) => x < 2 ? 0.1f : 0.9f);
        var tiles = TileClassifier.Classify(Masks(height, TwoLayers), 2);

        Assert.Equal("0,1\n", tiles.ToCsv());

        var small = tiles.ToImage();
        Assert.Equal([Red, Blue], small.Pixels);

        var full = tiles.ToImage(pixelsPerTile: 2);
        Assert.Equal(4, full.Width);
        Assert.Equal(Blue, full[3, 1]);
    }

    [Fact]
    public void Hillshade_FlatIsUniform_SlopesFacingLightAreBrighter()
    {
        var flat = TerrainAnalysis.Hillshade(TestMaps.Flat(5), 10);
        Assert.All(flat.Data, v => Assert.Equal(MathF.Sin(MathF.PI / 4), v, 4));

        // Luz desde el oeste (azimut 270). Si h crece con x, la ladera mira al oeste (hacia la luz)
        // y debe quedar más iluminada que una ladera que mira al este.
        var facingWest = TerrainAnalysis.Hillshade(TestMaps.RampX(5, 5, 0.05f), 10, azimuthDegrees: 270);
        var facingEast = TerrainAnalysis.Hillshade(FloatMap.Create(5, 5, (x, _) => (4 - x) * 0.05f), 10, 270);
        Assert.True(facingWest[2, 2] > facingEast[2, 2]);
    }
}
