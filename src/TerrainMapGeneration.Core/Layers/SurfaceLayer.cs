using TerrainMapGeneration.Core.Export;

namespace TerrainMapGeneration.Core.Layers;

/// <summary>
/// Tipo de superficie (agua, arena, pasto…) y las reglas que deciden dónde aparece.
/// </summary>
/// <remarks>
/// El peso bruto de la capa en un píxel es el producto de <see cref="Weight"/> por cada regla
/// definida (las reglas <c>null</c> no restringen). Después los pesos de todas las capas se
/// normalizan para que sumen 1 (ver <see cref="MaskGenerator"/>).
/// </remarks>
/// <param name="Name">Nombre de la capa; se usa en los nombres de archivo exportados.</param>
/// <param name="Color">Color de la capa en la vista previa y en el mapa de tiles.</param>
/// <param name="Altitude">Rango de altura normalizada [0, 1].</param>
/// <param name="Slope">Rango de pendiente en grados.</param>
/// <param name="Erosion">Rango de erosión normalizada [0, 1] (1 = de las zonas más erosionadas).</param>
/// <param name="Deposition">Rango de depósito de sedimento normalizado [0, 1].</param>
/// <param name="Weight">Multiplicador para desempatar capas que coinciden.</param>
public sealed record SurfaceLayer(
    string Name,
    Rgba Color,
    RangeRule? Altitude = null,
    RangeRule? Slope = null,
    RangeRule? Erosion = null,
    RangeRule? Deposition = null,
    float Weight = 1f)
{
    /// <summary>Peso bruto (sin normalizar) para los valores de un píxel.</summary>
    public float Evaluate(float altitude, float slope, float erosion, float deposition)
    {
        var w = Weight;
        if (Altitude is not null)
        {
            w *= Altitude.Evaluate(altitude);
        }

        if (Slope is not null)
        {
            w *= Slope.Evaluate(slope);
        }

        if (Erosion is not null)
        {
            w *= Erosion.Evaluate(erosion);
        }

        if (Deposition is not null)
        {
            w *= Deposition.Evaluate(deposition);
        }

        return w;
    }

    /// <summary>
    /// Capas por defecto para un juego de vista cenital: agua, arena, pasto, tierra, roca y nieve.
    /// Los bordes compartidos usan el mismo falloff para que las transiciones sumen 1.
    /// </summary>
    public static IReadOnlyList<SurfaceLayer> Defaults { get; } =
    [
        new("agua", Rgba.FromHex("#3A78C2"), Altitude: new(Max: 0.20f, Falloff: 0.02f)),
        new("arena", Rgba.FromHex("#E3D29A"), Altitude: new(0.20f, 0.25f, 0.02f), Slope: new(Max: 25f, Falloff: 10f)),
        new("pasto", Rgba.FromHex("#5FA84A"), Altitude: new(0.25f, 0.78f, 0.02f), Slope: new(Max: 30f, Falloff: 10f),
            Erosion: new(Max: 0.45f, Falloff: 0.3f)),
        new("tierra", Rgba.FromHex("#9C7A4F"), Altitude: new(0.25f, 0.78f, 0.02f), Slope: new(Max: 30f, Falloff: 10f),
            Erosion: new(Min: 0.45f, Falloff: 0.3f)),
        new("roca", Rgba.FromHex("#8A8173"), Altitude: new(Min: 0.20f, Falloff: 0.02f), Slope: new(Min: 30f, Falloff: 10f)),
        new("nieve", Rgba.FromHex("#F4F6F8"), Altitude: new(Min: 0.78f, Falloff: 0.02f), Slope: new(Max: 30f, Falloff: 10f)),
    ];
}
