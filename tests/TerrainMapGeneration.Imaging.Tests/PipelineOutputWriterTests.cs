using System.Text.Json;
using TerrainMapGeneration.Core.Erosion;
using TerrainMapGeneration.Core.Maps;
using TerrainMapGeneration.Core.Pipeline;

namespace TerrainMapGeneration.Imaging.Tests;

public sealed class PipelineOutputWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("terrain-out-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Write_CreatesAllExpectedFiles()
    {
        var project = new TerrainProject
        {
            Thermal = new ThermalErosionSettings(Iterations: 2),
            Hydraulic = new HydraulicErosionSettings(Droplets: 200),
            TileSize = 8,
        };
        var height = FloatMap.Create(32, 24, (x, y) => (x + y) / 54f);
        var result = TerrainPipeline.Run(height, project);

        var files = PipelineOutputWriter.Write(result, _dir);

        string[] expected =
        [
            "heightmap_eroded.png", "height_colors.png", "preview.png", "slope.png", "erosion.png",
            "deposition.png", "splatmap_0.png", "splatmap_1.png", "splatmaps.json", "tiles.png",
            "tiles_full.png", "tiles.csv", "tiles.json", Path.Combine("masks", "mask_pasto.png"),
        ];
        foreach (var name in expected)
        {
            Assert.True(File.Exists(Path.Combine(_dir, name)), $"Falta {name}");
        }

        Assert.Equal(files.Count, files.Distinct().Count());
        Assert.All(files, f => Assert.True(File.Exists(f)));

        // Las imágenes escritas se pueden volver a leer y tienen el tamaño correcto.
        Assert.Equal(32, HeightMapReader.Load(Path.Combine(_dir, "heightmap_eroded.png")).Width);
        Assert.Equal(4, HeightMapReader.Load(Path.Combine(_dir, "tiles.png")).Width);

        var csvRows = File.ReadAllLines(Path.Combine(_dir, "tiles.csv"));
        Assert.Equal(3, csvRows.Length);
        Assert.All(csvRows, row => Assert.Equal(4, row.Split(',').Length));

        using var legend = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "tiles.json")));
        Assert.Equal(8, legend.RootElement.GetProperty("tileSize").GetInt32());
        Assert.Equal("agua", legend.RootElement.GetProperty("layers")[0].GetProperty("name").GetString());
    }
}
