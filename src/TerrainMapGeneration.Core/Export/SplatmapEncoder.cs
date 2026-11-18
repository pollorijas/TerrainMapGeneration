using TerrainMapGeneration.Core.Layers;

namespace TerrainMapGeneration.Core.Export;

/// <summary>Una imagen splatmap y qué capa va en cada canal.</summary>
/// <param name="Image">Imagen RGBA: cada canal es el peso (0–255) de una capa.</param>
/// <param name="ChannelLayers">Nombre de la capa en R, G, B y A, o <c>null</c> si el canal no se usa.</param>
public sealed record Splatmap(RgbaImage Image, IReadOnlyList<string?> ChannelLayers);

/// <summary>
/// Codifica las máscaras como splatmaps RGBA: 4 capas por imagen (R, G, B, A en el orden de las capas).
/// Con 5–8 capas se generan 2 imágenes, y así sucesivamente.
/// </summary>
/// <remarks>
/// Cada canal vale <c>round(peso · 255)</c>. Si la última imagen tiene menos de 4 capas, los canales de
/// color sobrantes valen 0 y el alfa vale 255 (opaco) para que la imagen se vea bien en cualquier visor.
/// </remarks>
public static class SplatmapEncoder
{
    /// <summary>Capas por imagen.</summary>
    public const int ChannelsPerImage = 4;

    /// <summary>Genera los splatmaps.</summary>
    public static IReadOnlyList<Splatmap> Encode(LayerMasks masks)
    {
        ArgumentNullException.ThrowIfNull(masks);
        var result = new List<Splatmap>();
        for (var first = 0; first < masks.Layers.Count; first += ChannelsPerImage)
        {
            var count = Math.Min(ChannelsPerImage, masks.Layers.Count - first);
            var image = new RgbaImage(masks.Width, masks.Height);
            var channels = new byte[ChannelsPerImage];
            for (var i = 0; i < image.Pixels.Length; i++)
            {
                Array.Clear(channels);
                channels[3] = 255;
                for (var c = 0; c < count; c++)
                {
                    channels[c] = ToByte(masks.Masks[first + c].Data[i]);
                }

                image.Pixels[i] = new Rgba(channels[0], channels[1], channels[2], channels[3]);
            }

            var names = Enumerable.Range(0, ChannelsPerImage)
                .Select(c => c < count ? masks.Layers[first + c].Name : null)
                .ToArray();
            result.Add(new Splatmap(image, names));
        }

        return result;
    }

    private static byte ToByte(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
}
