using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Layers;

/// <summary>
/// Datos de entrada para generar máscaras: todos los mapas tienen las mismas dimensiones.
/// </summary>
/// <param name="Altitude">Altura normalizada [0, 1].</param>
/// <param name="Slope">Pendiente en grados.</param>
/// <param name="Erosion">Erosión normalizada [0, 1], o <c>null</c> si no se simuló.</param>
/// <param name="Deposition">Depósito normalizado [0, 1], o <c>null</c> si no se simuló.</param>
public sealed record TerrainContext(FloatMap Altitude, FloatMap Slope, FloatMap? Erosion = null, FloatMap? Deposition = null)
{
    /// <summary>Ancho común de todos los mapas.</summary>
    public int Width => Altitude.Width;

    /// <summary>Alto común de todos los mapas.</summary>
    public int Height => Altitude.Height;
}

/// <summary>
/// Máscaras de capas: para cada capa, un <see cref="FloatMap"/> con su peso por píxel.
/// En cada píxel los pesos de todas las capas suman 1.
/// </summary>
public sealed class LayerMasks
{
    internal LayerMasks(IReadOnlyList<SurfaceLayer> layers, IReadOnlyList<FloatMap> masks)
    {
        Layers = layers;
        Masks = masks;
    }

    /// <summary>Capas en el mismo orden que <see cref="Masks"/>.</summary>
    public IReadOnlyList<SurfaceLayer> Layers { get; }

    /// <summary>Una máscara por capa.</summary>
    public IReadOnlyList<FloatMap> Masks { get; }

    /// <summary>Ancho de las máscaras.</summary>
    public int Width => Masks[0].Width;

    /// <summary>Alto de las máscaras.</summary>
    public int Height => Masks[0].Height;

    /// <summary>Índice de la capa con mayor peso en el píxel (x, y). En empate gana la primera.</summary>
    public int DominantLayerAt(int x, int y)
    {
        var index = (y * Width) + x;
        var best = 0;
        for (var i = 1; i < Masks.Count; i++)
        {
            if (Masks[i].Data[index] > Masks[best].Data[index])
            {
                best = i;
            }
        }

        return best;
    }
}

/// <summary>Calcula las máscaras normalizadas de cada capa a partir de altura, pendiente y erosión.</summary>
public static class MaskGenerator
{
    /// <summary>Por debajo de esta suma de pesos se considera que ninguna regla aplica.</summary>
    public const float MinTotalWeight = 1e-4f;

    /// <summary>Genera las máscaras.</summary>
    /// <param name="context">Mapas de entrada.</param>
    /// <param name="layers">Capas a evaluar (al menos una).</param>
    /// <param name="fallbackLayer">
    /// Índice de la capa que se asigna con peso 1 cuando ninguna regla aplica en un píxel.
    /// </param>
    public static LayerMasks Generate(TerrainContext context, IReadOnlyList<SurfaceLayer> layers, int fallbackLayer = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new ArgumentException("Se necesita al menos una capa.", nameof(layers));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(fallbackLayer);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fallbackLayer, layers.Count);
        context.Altitude.EnsureSameSize(context.Slope);
        if (context.Erosion is not null)
        {
            context.Altitude.EnsureSameSize(context.Erosion);
        }

        if (context.Deposition is not null)
        {
            context.Altitude.EnsureSameSize(context.Deposition);
        }

        var masks = layers.Select(_ => new FloatMap(context.Width, context.Height)).ToArray();
        var weights = new float[layers.Count];

        for (var i = 0; i < context.Altitude.Length; i++)
        {
            var altitude = context.Altitude.Data[i];
            var slope = context.Slope.Data[i];
            var erosion = context.Erosion?.Data[i] ?? 0f;
            var deposition = context.Deposition?.Data[i] ?? 0f;

            var total = 0f;
            for (var l = 0; l < layers.Count; l++)
            {
                weights[l] = Math.Max(0f, layers[l].Evaluate(altitude, slope, erosion, deposition));
                total += weights[l];
            }

            if (total < MinTotalWeight)
            {
                masks[fallbackLayer].Data[i] = 1f;
                continue;
            }

            for (var l = 0; l < layers.Count; l++)
            {
                masks[l].Data[i] = weights[l] / total;
            }
        }

        return new LayerMasks(layers, masks);
    }
}
