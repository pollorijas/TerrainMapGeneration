using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Export;

/// <summary>Una parada del gradiente: a la altura <see cref="Position"/> corresponde <see cref="Color"/>.</summary>
public sealed record ColorStop(float Position, Rgba Color);

/// <summary>
/// Gradiente de color por altura. Es la forma más simple de "mapa RGB": colorea cada píxel
/// según su altura interpolando linealmente entre paradas.
/// </summary>
public sealed class ColorRamp
{
    private readonly ColorStop[] _stops;

    /// <summary>Crea un gradiente; las paradas se ordenan por posición.</summary>
    public ColorRamp(IEnumerable<ColorStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        _stops = [.. stops.OrderBy(s => s.Position)];
        if (_stops.Length == 0)
        {
            throw new ArgumentException("El gradiente necesita al menos una parada.", nameof(stops));
        }
    }

    /// <summary>Paradas ordenadas por posición.</summary>
    public IReadOnlyList<ColorStop> Stops => _stops;

    /// <summary>Gradiente por defecto: agua profunda → agua → arena → pasto → bosque → roca → nieve.</summary>
    public static ColorRamp Terrain { get; } = new(
    [
        new(0.00f, Rgba.FromHex("#1B3A6B")),
        new(0.18f, Rgba.FromHex("#3A78C2")),
        new(0.22f, Rgba.FromHex("#E3D29A")),
        new(0.28f, Rgba.FromHex("#6DB352")),
        new(0.55f, Rgba.FromHex("#2F7A3A")),
        new(0.72f, Rgba.FromHex("#8A8173")),
        new(0.85f, Rgba.FromHex("#F4F6F8")),
    ]);

    /// <summary>Color para el valor <paramref name="t"/> (fuera de rango se usa la parada extrema).</summary>
    public Rgba Evaluate(float t)
    {
        if (t <= _stops[0].Position)
        {
            return _stops[0].Color;
        }

        for (var i = 1; i < _stops.Length; i++)
        {
            var b = _stops[i];
            if (t <= b.Position)
            {
                var a = _stops[i - 1];
                var span = b.Position - a.Position;
                var f = span > 0 ? (t - a.Position) / span : 1f;
                return Lerp(a.Color, b.Color, f);
            }
        }

        return _stops[^1].Color;
    }

    /// <summary>Colorea un mapa completo.</summary>
    public RgbaImage Apply(FloatMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var image = new RgbaImage(map.Width, map.Height);
        for (var i = 0; i < map.Length; i++)
        {
            image.Pixels[i] = Evaluate(map.Data[i]);
        }

        return image;
    }

    private static Rgba Lerp(Rgba a, Rgba b, float t)
    {
        static byte L(byte x, byte y, float t) => (byte)MathF.Round(x + ((y - x) * t));
        return new Rgba(L(a.R, b.R, t), L(a.G, b.G, t), L(a.B, b.B, t), L(a.A, b.A, t));
    }
}
