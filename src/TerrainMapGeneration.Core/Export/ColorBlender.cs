using TerrainMapGeneration.Core.Layers;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Export;

/// <summary>Vista previa en color: mezcla el color de cada capa según su peso.</summary>
public static class ColorBlender
{
    /// <summary>Mezcla los colores de las capas.</summary>
    /// <param name="masks">Máscaras normalizadas.</param>
    /// <param name="shading">
    /// Opcional: iluminación en [0, 1] (por ejemplo <see cref="Analysis.TerrainAnalysis.Hillshade"/>) que
    /// oscurece el color para dar sensación de relieve.
    /// </param>
    /// <param name="shadingStrength">Cuánto oscurece la sombra (0 = nada, 1 = hasta negro).</param>
    public static RgbaImage Blend(LayerMasks masks, FloatMap? shading = null, float shadingStrength = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(masks);
        var image = new RgbaImage(masks.Width, masks.Height);
        for (var i = 0; i < image.Pixels.Length; i++)
        {
            float r = 0, g = 0, b = 0;
            for (var l = 0; l < masks.Layers.Count; l++)
            {
                var w = masks.Masks[l].Data[i];
                var c = masks.Layers[l].Color;
                r += c.R * w;
                g += c.G * w;
                b += c.B * w;
            }

            if (shading is not null)
            {
                var light = 1f - (shadingStrength * (1f - shading.Data[i]));
                r *= light;
                g *= light;
                b *= light;
            }

            image.Pixels[i] = new Rgba(ToByte(r), ToByte(g), ToByte(b));
        }

        return image;
    }

    private static byte ToByte(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 255f));
}
