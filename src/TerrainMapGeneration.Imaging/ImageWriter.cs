using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Imaging;

/// <summary>Guarda mapas y colores del núcleo como archivos PNG.</summary>
public static class ImageWriter
{
    /// <summary>
    /// Guarda un <see cref="FloatMap"/> en gris de 16 bits (65 536 niveles). Recomendado para heightmaps.
    /// Los valores se recortan a [0, 1].
    /// </summary>
    public static void SaveGray16(FloatMap map, string path)
    {
        ArgumentNullException.ThrowIfNull(map);
        using var image = new Image<L16>(map.Width, map.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new L16(ToUInt16(map[x, y]));
                }
            }
        });
        Save(image, path, new PngEncoder { BitDepth = PngBitDepth.Bit16, ColorType = PngColorType.Grayscale });
    }

    /// <summary>Guarda un <see cref="FloatMap"/> en gris de 8 bits. Suficiente para máscaras.</summary>
    public static void SaveGray8(FloatMap map, string path)
    {
        ArgumentNullException.ThrowIfNull(map);
        using var image = new Image<L8>(map.Width, map.Height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new L8(ToByte(map[x, y]));
                }
            }
        });
        Save(image, path, new PngEncoder { BitDepth = PngBitDepth.Bit8, ColorType = PngColorType.Grayscale });
    }

    /// <summary>
    /// Guarda una imagen RGBA. Si todos los píxeles son opacos se escribe como RGB (sin canal alfa)
    /// para que los editores no alteren los colores al premultiplicar alfa.
    /// </summary>
    public static void SaveRgba(RgbaImage source, string path)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var image = new Image<Rgba32>(source.Width, source.Height);
        var opaque = true;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var c = source[x, y];
                    opaque &= c.A == 255;
                    row[x] = new Rgba32(c.R, c.G, c.B, c.A);
                }
            }
        });
        var colorType = opaque ? PngColorType.Rgb : PngColorType.RgbWithAlpha;
        Save(image, path, new PngEncoder { BitDepth = PngBitDepth.Bit8, ColorType = colorType });
    }

    private static void Save(Image image, string path, PngEncoder encoder)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        image.SaveAsPng(path, encoder);
    }

    private static ushort ToUInt16(float v) => (ushort)MathF.Round(Math.Clamp(v, 0f, 1f) * ushort.MaxValue);

    private static byte ToByte(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * byte.MaxValue);
}
