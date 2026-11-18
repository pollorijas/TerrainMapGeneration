using System.Text.Json;
using TerrainMapGeneration.Core.Maps;
using TerrainMapGeneration.Core.Pipeline;

namespace TerrainMapGeneration.Imaging;

/// <summary>
/// Escribe en disco todos los resultados de <see cref="TerrainPipeline"/>.
/// </summary>
/// <remarks>
/// Archivos generados dentro de la carpeta de salida:
/// <code>
/// heightmap_eroded.png      Heightmap tras la erosión (gris 16 bits)
/// height_colors.png         Mapa RGB coloreado por altura
/// preview.png               Capas mezcladas con sombreado de relieve
/// slope.png                 Pendiente (negro 0°, blanco 90°)
/// erosion.png, deposition.png   Solo si hubo erosión hidráulica
/// masks/mask_&lt;capa&gt;.png    Peso de cada capa (gris 8 bits)
/// splatmap_N.png            4 capas por imagen en R, G, B, A
/// splatmaps.json            Qué capa va en cada canal de cada splatmap
/// tiles.png                 Color de la capa de cada tile (1 píxel por tile)
/// tiles_full.png            Igual, a la resolución del heightmap
/// tiles.csv                 Id de capa por tile (filas separadas por saltos de línea)
/// tiles.json                Leyenda: id → nombre y color, tamaño del mapa
/// </code>
/// </remarks>
public static class PipelineOutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Escribe los resultados y devuelve las rutas de los archivos creados.</summary>
    public static IReadOnlyList<string> Write(PipelineResult result, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var written = new List<string>();

        string P(string name)
        {
            var path = Path.Combine(outputDirectory, name);
            written.Add(path);
            return path;
        }

        ImageWriter.SaveGray16(result.Height, P("heightmap_eroded.png"));
        ImageWriter.SaveRgba(result.HeightColors, P("height_colors.png"));
        ImageWriter.SaveRgba(result.Preview, P("preview.png"));
        ImageWriter.SaveGray8(Scale(result.Slope, 1f / 90f), P("slope.png"));
        if (result.Erosion is not null)
        {
            ImageWriter.SaveGray8(result.Erosion, P("erosion.png"));
        }

        if (result.Deposition is not null)
        {
            ImageWriter.SaveGray8(result.Deposition, P("deposition.png"));
        }

        for (var i = 0; i < result.Masks.Layers.Count; i++)
        {
            ImageWriter.SaveGray8(result.Masks.Masks[i], P(Path.Combine("masks", $"mask_{result.Masks.Layers[i].Name}.png")));
        }

        var splatLegend = new List<object>();
        for (var i = 0; i < result.Splatmaps.Count; i++)
        {
            var file = $"splatmap_{i}.png";
            ImageWriter.SaveRgba(result.Splatmaps[i].Image, P(file));
            var c = result.Splatmaps[i].ChannelLayers;
            splatLegend.Add(new { file, r = c[0], g = c[1], b = c[2], a = c[3] });
        }

        File.WriteAllText(P("splatmaps.json"), JsonSerializer.Serialize(splatLegend, JsonOptions));

        var tiles = result.Tiles;
        ImageWriter.SaveRgba(tiles.ToImage(), P("tiles.png"));
        ImageWriter.SaveRgba(tiles.ToImage(tiles.TileSize), P("tiles_full.png"));
        File.WriteAllText(P("tiles.csv"), tiles.ToCsv());
        var legend = new
        {
            width = tiles.Width,
            height = tiles.Height,
            tileSize = tiles.TileSize,
            layers = tiles.Layers.Select((l, id) => new { id, name = l.Name, color = l.Color.ToHex() }),
        };
        File.WriteAllText(P("tiles.json"), JsonSerializer.Serialize(legend, JsonOptions));

        return written;
    }

    private static FloatMap Scale(FloatMap map, float factor)
    {
        var scaled = map.Clone();
        for (var i = 0; i < scaled.Length; i++)
        {
            scaled.Data[i] *= factor;
        }

        return scaled;
    }
}
