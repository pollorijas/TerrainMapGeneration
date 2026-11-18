using System.Globalization;

namespace TerrainMapGeneration.Core.Export;

/// <summary>Color de 8 bits por canal. Core lo usa para describir imágenes sin depender de una biblioteca gráfica.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Interpreta "#RRGGBB" o "#RRGGBBAA" (el '#' es opcional).</summary>
    public static Rgba FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        var s = hex.TrimStart('#');
        if (s.Length is not (6 or 8))
        {
            throw new FormatException($"Color no válido: '{hex}'. Usa #RRGGBB o #RRGGBBAA.");
        }

        byte Part(int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba(Part(0), Part(2), Part(4), s.Length == 8 ? Part(6) : (byte)255);
    }

    /// <summary>Devuelve "#RRGGBB" (o "#RRGGBBAA" si no es opaco).</summary>
    public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    /// <inheritdoc />
    public override string ToString() => ToHex();
}

/// <summary>Imagen RGBA en memoria (orden de filas), independiente de cualquier biblioteca de imágenes.</summary>
public sealed class RgbaImage
{
    /// <summary>Crea una imagen transparente de las dimensiones dadas.</summary>
    public RgbaImage(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        Pixels = new Rgba[width * height];
    }

    /// <summary>Ancho en píxeles.</summary>
    public int Width { get; }

    /// <summary>Alto en píxeles.</summary>
    public int Height { get; }

    /// <summary>Píxeles en orden de filas: <c>índice = y * Width + x</c>.</summary>
    public Rgba[] Pixels { get; }

    /// <summary>Lee o escribe el píxel (x, y).</summary>
    public Rgba this[int x, int y]
    {
        get => Pixels[(y * Width) + x];
        set => Pixels[(y * Width) + x] = value;
    }
}
