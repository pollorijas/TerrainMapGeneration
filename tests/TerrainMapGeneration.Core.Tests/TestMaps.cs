using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

/// <summary>Generadores de terrenos sintéticos para las pruebas (no dependen de imágenes).</summary>
internal static class TestMaps
{
    public static FloatMap Flat(int size, float value = 0.5f) => FloatMap.Create(size, size, (_, _) => value);

    /// <summary>Rampa que sube en X: h = x * step.</summary>
    public static FloatMap RampX(int width, int height, float step) => FloatMap.Create(width, height, (x, _) => x * step);

    /// <summary>Cono centrado: altura 1 en el centro y 0 en el borde del radio.</summary>
    public static FloatMap Cone(int size)
    {
        var c = (size - 1) / 2f;
        return FloatMap.Create(size, size, (x, y) =>
        {
            var d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
            return Math.Max(0f, 1f - (d / c));
        });
    }

    /// <summary>Mapa plano con un único pico en el centro.</summary>
    public static FloatMap Spike(int size, float baseValue = 0.2f, float peak = 1f)
    {
        var map = Flat(size, baseValue);
        map[size / 2, size / 2] = peak;
        return map;
    }
}
