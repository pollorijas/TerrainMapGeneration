using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Analysis;

/// <summary>Mapas derivados del heightmap: pendiente, suavizado y normalización.</summary>
public static class TerrainAnalysis
{
    /// <summary>
    /// Pendiente de cada celda en grados (0° = plano, 90° = vertical).
    /// </summary>
    /// <param name="height">Heightmap con alturas en [0, 1].</param>
    /// <param name="heightScale">
    /// Altura máxima del terreno medida en píxeles horizontales. Por ejemplo, con 64 una diferencia de
    /// altura de 1.0 equivale a subir lo mismo que avanzar 64 píxeles. Convierte alturas normalizadas en
    /// pendientes reales.
    /// </param>
    /// <remarks>
    /// Usa diferencias centrales <c>(h[x+1] − h[x−1]) / 2</c>; en los bordes, diferencias hacia un solo lado.
    /// </remarks>
    public static FloatMap SlopeDegrees(FloatMap height, float heightScale)
    {
        ArgumentNullException.ThrowIfNull(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightScale);

        var w = height.Width;
        var h = height.Height;
        var slope = new FloatMap(w, h);
        for (var y = 0; y < h; y++)
        {
            var y0 = Math.Max(y - 1, 0);
            var y1 = Math.Min(y + 1, h - 1);
            for (var x = 0; x < w; x++)
            {
                var x0 = Math.Max(x - 1, 0);
                var x1 = Math.Min(x + 1, w - 1);
                var dx = x1 > x0 ? (height[x1, y] - height[x0, y]) / (x1 - x0) : 0f;
                var dy = y1 > y0 ? (height[x, y1] - height[x, y0]) / (y1 - y0) : 0f;
                var gradient = MathF.Sqrt((dx * dx) + (dy * dy)) * heightScale;
                slope[x, y] = MathF.Atan(gradient) * (180f / MathF.PI);
            }
        }

        return slope;
    }

    /// <summary>
    /// Suavizado por caja (box blur) separable de radio <paramref name="radius"/>. Devuelve un mapa nuevo.
    /// </summary>
    public static FloatMap BoxBlur(FloatMap map, int radius)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        if (radius == 0)
        {
            return map.Clone();
        }

        var temp = new FloatMap(map.Width, map.Height);
        var result = new FloatMap(map.Width, map.Height);
        BlurPass(map, temp, radius, horizontal: true);
        BlurPass(temp, result, radius, horizontal: false);
        return result;
    }

    /// <summary>
    /// Escala un mapa no negativo a [0, 1] dividiendo por su percentil <paramref name="percentile"/>
    /// (y recortando). Más robusto que dividir por el máximo cuando unos pocos valores son extremos,
    /// como ocurre en los mapas de erosión.
    /// </summary>
    public static FloatMap NormalizeByPercentile(FloatMap map, float percentile = 0.99f)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(percentile, 0f);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 1f);

        var sorted = (float[])map.Data.Clone();
        Array.Sort(sorted);
        var reference = sorted[(int)MathF.Round((sorted.Length - 1) * percentile)];
        var result = new FloatMap(map.Width, map.Height);
        if (reference <= 0f)
        {
            return result;
        }

        for (var i = 0; i < map.Length; i++)
        {
            result.Data[i] = Math.Clamp(map.Data[i] / reference, 0f, 1f);
        }

        return result;
    }

    private static void BlurPass(FloatMap source, FloatMap target, int radius, bool horizontal)
    {
        var w = source.Width;
        var h = source.Height;
        var outer = horizontal ? h : w;
        var inner = horizontal ? w : h;
        for (var o = 0; o < outer; o++)
        {
            for (var i = 0; i < inner; i++)
            {
                var sum = 0f;
                var count = 0;
                for (var k = Math.Max(0, i - radius); k <= Math.Min(inner - 1, i + radius); k++)
                {
                    sum += horizontal ? source[k, o] : source[o, k];
                    count++;
                }

                if (horizontal)
                {
                    target[i, o] = sum / count;
                }
                else
                {
                    target[o, i] = sum / count;
                }
            }
        }
    }
}
