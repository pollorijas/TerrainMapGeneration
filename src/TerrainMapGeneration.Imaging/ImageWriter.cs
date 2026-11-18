using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Maps;
using TerrainMapGeneration.Imaging.Png;

namespace TerrainMapGeneration.Imaging;

/// <summary>Guarda mapas y colores del núcleo como archivos PNG. Crea las carpetas que falten.</summary>
public static class ImageWriter
{
    /// <summary>
    /// Guarda un <see cref="FloatMap"/> en gris de 16 bits (65 536 niveles). Recomendado para heightmaps.
    /// Los valores se recortan a [0, 1].
    /// </summary>
    public static void SaveGray16(FloatMap map, string path)
    {
        ArgumentNullException.ThrowIfNull(map);
        var bytes = new byte[map.Length * 2];
        for (var i = 0; i < map.Length; i++)
        {
            var v = (ushort)MathF.Round(Math.Clamp(map.Data[i], 0f, 1f) * ushort.MaxValue);
            bytes[i * 2] = (byte)(v >> 8);
            bytes[(i * 2) + 1] = (byte)v;
        }

        Encode(path, map.Width, map.Height, PngColorType.Grayscale, 16, bytes);
    }

    /// <summary>Guarda un <see cref="FloatMap"/> en gris de 8 bits. Suficiente para máscaras.</summary>
    public static void SaveGray8(FloatMap map, string path)
    {
        ArgumentNullException.ThrowIfNull(map);
        var bytes = new byte[map.Length];
        for (var i = 0; i < map.Length; i++)
        {
            bytes[i] = (byte)MathF.Round(Math.Clamp(map.Data[i], 0f, 1f) * byte.MaxValue);
        }

        Encode(path, map.Width, map.Height, PngColorType.Grayscale, 8, bytes);
    }

    /// <summary>
    /// Guarda una imagen RGBA. Si todos los píxeles son opacos se escribe como RGB (sin canal alfa),
    /// así los editores no alteran colores al premultiplicar alfa.
    /// </summary>
    public static void SaveRgba(RgbaImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        var opaque = image.Pixels.All(p => p.A == 255);
        var channels = opaque ? 3 : 4;
        var bytes = new byte[image.Pixels.Length * channels];
        for (var i = 0; i < image.Pixels.Length; i++)
        {
            var p = image.Pixels[i];
            var o = i * channels;
            bytes[o] = p.R;
            bytes[o + 1] = p.G;
            bytes[o + 2] = p.B;
            if (!opaque)
            {
                bytes[o + 3] = p.A;
            }
        }

        Encode(path, image.Width, image.Height, opaque ? PngColorType.Rgb : PngColorType.RgbAlpha, 8, bytes);
    }

    private static void Encode(string path, int width, int height, PngColorType type, int depth, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        PngEncoder.Encode(path, width, height, type, depth, bytes);
    }
}
