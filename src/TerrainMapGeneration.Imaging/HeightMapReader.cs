using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Imaging;

/// <summary>Carga heightmaps desde archivos de imagen (PNG, TIFF, BMP…).</summary>
public static class HeightMapReader
{
    /// <summary>
    /// Carga una imagen como heightmap normalizado en [0, 1].
    /// </summary>
    /// <remarks>
    /// La imagen siempre se decodifica a gris de 16 bits (<see cref="L16"/>):
    /// <list type="bullet">
    /// <item>PNG en gris de 16 bits: se conserva toda la precisión.</item>
    /// <item>PNG en gris de 8 bits: cada nivel v pasa a v·257, sin pérdida.</item>
    /// <item>Imágenes RGB: se convierten a luminancia (la conversión de ImageSharp).</item>
    /// </list>
    /// El valor 0 (negro) es la altura mínima y el blanco es la máxima.
    /// No se reescala automáticamente: un heightmap que no usa todo el rango conserva sus alturas relativas.
    /// </remarks>
    public static FloatMap Load(string path)
    {
        using var image = Image.Load<L16>(path);
        return FromImage(image);
    }

    /// <summary>Igual que <see cref="Load(string)"/> pero desde un flujo.</summary>
    public static FloatMap Load(Stream stream)
    {
        using var image = Image.Load<L16>(stream);
        return FromImage(image);
    }

    private static FloatMap FromImage(Image<L16> image)
    {
        var map = new FloatMap(image.Width, image.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var offset = y * map.Width;
                for (var x = 0; x < row.Length; x++)
                {
                    map.Data[offset + x] = row[x].PackedValue / (float)ushort.MaxValue;
                }
            }
        });
        return map;
    }
}
