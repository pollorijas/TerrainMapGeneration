using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Erosion;

/// <summary>Parámetros de la erosión térmica.</summary>
/// <param name="Iterations">Número de pasadas sobre el mapa.</param>
/// <param name="TalusAngleDegrees">
/// Ángulo de reposo: el material se desmorona donde la pendiente lo supera. Valores típicos: 30–40°.
/// </param>
/// <param name="Strength">Fracción (0–1) del exceso que se mueve en cada pasada.</param>
public sealed record ThermalErosionSettings(
    int Iterations = 50,
    float TalusAngleDegrees = 35f,
    float Strength = 0.5f);

/// <summary>
/// Erosión térmica (Musgrave, Kolb y Mace, 1989): donde la diferencia de altura con un vecino supera
/// el ángulo de reposo, parte del material cae hacia ese vecino. Suaviza acantilados y forma
/// pedregales al pie de las laderas.
/// </summary>
/// <remarks>
/// <para>Se usan los 8 vecinos. El umbral para cada vecino es <c>T = tan(talud) · distancia / heightScale</c>
/// (distancia 1 en ejes y √2 en diagonales).</para>
/// <para>En cada pasada, cada celda calcula su exceso sobre el umbral respecto de cada vecino más bajo,
/// mueve <c>Strength · excesoMáximo / 2</c> repartido proporcionalmente al exceso de cada vecino, y todos
/// los movimientos se acumulan en un buffer aparte antes de aplicarse (doble buffer). Así el resultado
/// no depende del orden de recorrido y la masa total se conserva: lo que sale de una celda entra en otra
/// y nada sale por los bordes.</para>
/// </remarks>
/// <param name="settings">Parámetros de la erosión.</param>
/// <param name="heightScale">
/// Altura máxima del terreno en píxeles horizontales (la misma que se usa para calcular la pendiente).
/// </param>
public sealed class ThermalErosion(ThermalErosionSettings settings, float heightScale) : IHeightMapFilter
{
    private static readonly (int Dx, int Dy, float Distance)[] Neighbors =
    [
        (-1, -1, MathF.Sqrt(2)), (0, -1, 1), (1, -1, MathF.Sqrt(2)),
        (-1, 0, 1), (1, 0, 1),
        (-1, 1, MathF.Sqrt(2)), (0, 1, 1), (1, 1, MathF.Sqrt(2)),
    ];

    /// <summary>Parámetros con los que se creó el filtro.</summary>
    public ThermalErosionSettings Settings { get; } = Validate(settings);

    /// <summary>Altura máxima del terreno en píxeles horizontales.</summary>
    public float HeightScale { get; } = heightScale > 0
        ? heightScale
        : throw new ArgumentOutOfRangeException(nameof(heightScale), heightScale, "Debe ser mayor que 0.");

    /// <inheritdoc />
    public void Apply(FloatMap heightMap)
    {
        ArgumentNullException.ThrowIfNull(heightMap);

        var w = heightMap.Width;
        var h = heightMap.Height;
        var data = heightMap.Data;
        var delta = new float[data.Length];
        var tanTalus = MathF.Tan(Settings.TalusAngleDegrees * MathF.PI / 180f) / HeightScale;
        Span<float> thresholds = stackalloc float[Neighbors.Length];
        for (var n = 0; n < Neighbors.Length; n++)
        {
            thresholds[n] = tanTalus * Neighbors[n].Distance;
        }

        Span<float> excess = stackalloc float[Neighbors.Length];

        for (var iteration = 0; iteration < Settings.Iterations; iteration++)
        {
            Array.Clear(delta);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var index = (y * w) + x;
                    var height = data[index];
                    var total = 0f;
                    var max = 0f;

                    for (var n = 0; n < Neighbors.Length; n++)
                    {
                        var (dx, dy, _) = Neighbors[n];
                        var nx = x + dx;
                        var ny = y + dy;
                        excess[n] = 0f;
                        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h)
                        {
                            continue;
                        }

                        var e = height - data[(ny * w) + nx] - thresholds[n];
                        if (e > 0f)
                        {
                            excess[n] = e;
                            total += e;
                            max = Math.Max(max, e);
                        }
                    }

                    if (total <= 0f)
                    {
                        continue;
                    }

                    // La mitad del exceso máximo deja ambas celdas a la misma "distancia" del umbral.
                    var moved = Settings.Strength * max * 0.5f;
                    delta[index] -= moved;
                    for (var n = 0; n < Neighbors.Length; n++)
                    {
                        if (excess[n] > 0f)
                        {
                            var target = ((y + Neighbors[n].Dy) * w) + x + Neighbors[n].Dx;
                            delta[target] += moved * (excess[n] / total);
                        }
                    }
                }
            }

            for (var i = 0; i < data.Length; i++)
            {
                data[i] += delta[i];
            }
        }
    }

    private static ThermalErosionSettings Validate(ThermalErosionSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentOutOfRangeException.ThrowIfNegative(s.Iterations);
        ArgumentOutOfRangeException.ThrowIfNegative(s.TalusAngleDegrees);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(s.TalusAngleDegrees, 90f);
        ArgumentOutOfRangeException.ThrowIfNegative(s.Strength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(s.Strength, 1f);
        return s;
    }
}
