using TerrainMapGeneration.Core.Maps;
using TerrainMapGeneration.Imaging.Png;

namespace TerrainMapGeneration.Imaging;

/// <summary>Carga heightmaps desde archivos PNG.</summary>
public static class HeightMapReader
{
    /// <summary>Carga un PNG como heightmap con alturas en [0, 1].</summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Gris de 16 bits: se conserva toda la precisión (65 536 niveles). Formato recomendado.</item>
    /// <item>Gris de 1–8 bits: cada nivel se escala exactamente a [0, 1].</item>
    /// <item>RGB, RGBA o paleta: se convierte a luminancia (Rec. 709). El alfa se ignora.</item>
    /// </list>
    /// Negro es la altura mínima y blanco la máxima. Los valores no se reescalan: un heightmap que no
    /// usa todo el rango conserva sus alturas absolutas.
    /// </remarks>
    public static FloatMap Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Igual que <see cref="Load(string)"/> pero desde un flujo.</summary>
    public static FloatMap Load(Stream stream)
    {
        var png = PngDecoder.Decode(stream);
        var map = new FloatMap(png.Width, png.Height);
        for (var y = 0; y < png.Height; y++)
        {
            for (var x = 0; x < png.Width; x++)
            {
                map.Data[(y * png.Width) + x] = png.GetLuminance(x, y);
            }
        }

        return map;
    }
}
