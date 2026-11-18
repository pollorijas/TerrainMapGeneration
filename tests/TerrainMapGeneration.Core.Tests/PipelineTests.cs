using System.Text.Json;
using TerrainMapGeneration.Core.Erosion;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Layers;
using TerrainMapGeneration.Core.Pipeline;

namespace TerrainMapGeneration.Core.Tests;

public class PipelineTests
{
    private static TerrainProject FastProject() => new()
    {
        Thermal = new ThermalErosionSettings(Iterations: 5),
        Hydraulic = new HydraulicErosionSettings(Droplets: 500),
        TileSize = 8,
    };

    [Fact]
    public void Project_JsonRoundTrip_KeepsEverything()
    {
        var project = FastProject();
        project.Layers[0] = project.Layers[0] with { Color = Rgba.FromHex("#123456") };

        var copy = TerrainProject.FromJson(project.ToJson());

        Assert.Equal(project.ToJson(), copy.ToJson());
        Assert.Equal(Rgba.FromHex("#123456"), copy.Layers[0].Color);
        Assert.Equal(500, copy.Hydraulic!.Droplets);
    }

    [Fact]
    public void Project_PartialJson_UsesDefaults()
    {
        var project = TerrainProject.FromJson("""
            {
              // Los comentarios están permitidos.
              "input": "mapa.png",
              "hydraulic": { "droplets": 1000 },
              "thermal": null,
            }
            """);

        Assert.Equal("mapa.png", project.Input);
        Assert.Null(project.Thermal);
        Assert.Equal(1000, project.Hydraulic!.Droplets);
        Assert.Equal(new HydraulicErosionSettings().Inertia, project.Hydraulic.Inertia);
        Assert.Equal(SurfaceLayer.Defaults.Count, project.Layers.Count);
    }

    [Theory]
    [InlineData("""{ "tileSize": 0 }""")]
    [InlineData("""{ "heightScale": -1 }""")]
    [InlineData("""{ "fallbackLayer": "lava" }""")]
    [InlineData("""{ "layers": [] }""")]
    [InlineData("""{ "layers": [ { "name": "a", "color": "#000000" }, { "name": "a", "color": "#000000" } ] }""")]
    [InlineData("""{ "fallbackLayer": null, "layers": [ { "name": "a/b", "color": "#000000" } ] }""")]
    public void Project_InvalidValues_Throw(string json)
    {
        Assert.Throws<ArgumentException>(() => TerrainProject.FromJson(json));
    }

    [Fact]
    public void Project_InvalidColor_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            TerrainProject.FromJson("""{ "layers": [ { "name": "a", "color": "rojo" } ], "fallbackLayer": "a" }"""));
    }

    [Fact]
    public void Run_ProducesConsistentOutputs()
    {
        var input = TestMaps.Cone(40);
        var log = new List<string>();

        var result = TerrainPipeline.Run(input, FastProject(), log.Add);

        Assert.Equal(40, result.Height.Width);
        Assert.Equal(40, result.Preview.Width);
        Assert.Equal(6, result.Masks.Masks.Count);
        Assert.Equal(2, result.Splatmaps.Count);
        Assert.Equal(5, result.Tiles.Width); // ceil(40 / 8)
        Assert.NotNull(result.Erosion);
        Assert.All(result.Erosion!.Data, v => Assert.InRange(v, 0f, 1f));
        Assert.Equal(4, log.Count);
        Assert.Equal(TestMaps.Cone(40).Data, input.Data); // la entrada no se modifica
    }

    [Fact]
    public void Run_IsDeterministic()
    {
        var a = TerrainPipeline.Run(TestMaps.Cone(32), FastProject());
        var b = TerrainPipeline.Run(TestMaps.Cone(32), FastProject());
        Assert.Equal(a.Height.Data, b.Height.Data);
        Assert.Equal(a.Tiles.LayerIds, b.Tiles.LayerIds);
    }

    [Fact]
    public void Run_WithoutErosion_SkipsErosionMaps()
    {
        var project = FastProject();
        project.Thermal = null;
        project.Hydraulic = null;

        var result = TerrainPipeline.Run(TestMaps.Cone(16), project);

        Assert.Null(result.Erosion);
        Assert.Null(result.Deposition);
        Assert.Equal(TestMaps.Cone(16).Data, result.Height.Data);
    }

    [Fact]
    public void Run_NormalizeInput_StretchesRange()
    {
        var project = FastProject();
        project.Thermal = null;
        project.Hydraulic = null;
        project.NormalizeInput = true;

        var result = TerrainPipeline.Run(TestMaps.RampX(8, 4, 0.05f), project);

        Assert.Equal((0f, 1f), result.Height.GetRange());
    }
}
