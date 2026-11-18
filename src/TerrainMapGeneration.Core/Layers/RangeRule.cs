namespace TerrainMapGeneration.Core.Layers;

/// <summary>
/// Regla de pertenencia a un rango con bordes suaves. Devuelve 1 dentro de [<see cref="Min"/>,
/// <see cref="Max"/>], 0 lejos de él, y una transición <c>smoothstep</c> de ancho
/// <see cref="Falloff"/> <b>centrada</b> en cada borde.
/// </summary>
/// <remarks>
/// Al estar centradas, dos capas que comparten un borde (una con <c>Max = b</c> y otra con
/// <c>Min = b</c>) y usan el mismo <see cref="Falloff"/> suman exactamente 1 en la transición:
/// no quedan huecos ni solapamientos. <c>null</c> en <see cref="Min"/> o <see cref="Max"/> significa
/// "sin límite" por ese lado.
/// </remarks>
/// <param name="Min">Límite inferior, o <c>null</c> si no hay.</param>
/// <param name="Max">Límite superior, o <c>null</c> si no hay.</param>
/// <param name="Falloff">Ancho de la transición en cada borde (0 = corte duro).</param>
public sealed record RangeRule(float? Min = null, float? Max = null, float Falloff = 0f)
{
    /// <summary>Grado de pertenencia de <paramref name="value"/> al rango, en [0, 1].</summary>
    public float Evaluate(float value)
    {
        var result = 1f;
        if (Min is { } min)
        {
            result *= Edge(min, value);
        }

        if (Max is { } max)
        {
            result *= 1f - Edge(max, value);
        }

        return result;
    }

    /// <summary>0 antes del borde, 1 después, con transición suave centrada en el borde.</summary>
    private float Edge(float edge, float value)
    {
        if (Falloff <= 0f)
        {
            return value >= edge ? 1f : 0f;
        }

        var t = Math.Clamp(((value - edge) / Falloff) + 0.5f, 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
