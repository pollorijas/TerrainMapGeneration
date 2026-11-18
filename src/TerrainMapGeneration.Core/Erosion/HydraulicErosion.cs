using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Erosion;

/// <summary>
/// Parámetros de la erosión hidráulica por partículas. Los valores por defecto están pensados para
/// alturas en [0, 1] y mapas de 256–1024 píxeles.
/// </summary>
/// <param name="Droplets">Número de gotas simuladas. Más gotas = erosión más marcada (y más lenta).</param>
/// <param name="Seed">Semilla del generador aleatorio: misma semilla ⇒ mismo resultado.</param>
/// <param name="BrushRadius">Radio (en píxeles) del área de la que cada gota arranca material.</param>
/// <param name="Inertia">0 = la gota sigue siempre la pendiente; 1 = nunca cambia de dirección.</param>
/// <param name="SedimentCapacityFactor">Cuánto sedimento puede cargar una gota (multiplicador).</param>
/// <param name="MinSedimentCapacity">Capacidad mínima, para que erosione también en terreno casi plano.</param>
/// <param name="ErodeSpeed">Fracción de la capacidad libre que se erosiona en cada paso (0–1).</param>
/// <param name="DepositSpeed">Fracción del exceso de sedimento que se deposita en cada paso (0–1).</param>
/// <param name="EvaporateSpeed">Fracción del agua que se evapora en cada paso (0–1).</param>
/// <param name="Gravity">Aceleración que gana la gota al bajar.</param>
/// <param name="MaxLifetime">Pasos máximos de cada gota.</param>
/// <param name="InitialWater">Agua inicial de cada gota.</param>
/// <param name="InitialSpeed">Velocidad inicial de cada gota.</param>
public sealed record HydraulicErosionSettings(
    int Droplets = 50_000,
    int Seed = 0,
    int BrushRadius = 3,
    float Inertia = 0.05f,
    float SedimentCapacityFactor = 4f,
    float MinSedimentCapacity = 0.01f,
    float ErodeSpeed = 0.3f,
    float DepositSpeed = 0.3f,
    float EvaporateSpeed = 0.01f,
    float Gravity = 4f,
    int MaxLifetime = 30,
    float InitialWater = 1f,
    float InitialSpeed = 1f);

/// <summary>
/// Erosión hidráulica por partículas (Hans T. Beyer, 2015: "Implementation of a method for hydraulic
/// erosion"). Cada gota cae en un punto aleatorio, baja siguiendo el gradiente con algo de inercia,
/// arranca material cuando tiene capacidad libre y lo deposita cuando va cuesta arriba o se satura.
/// </summary>
/// <remarks>
/// <para>Diferencias con la implementación de referencia:</para>
/// <list type="bullet">
/// <item>La velocidad aumenta al bajar: <c>v² ← v² − Δh·g</c> (algunas versiones tienen el signo invertido).</item>
/// <item>Cuando una gota muere dentro del mapa deposita el sedimento que le queda; solo se pierde
/// material cuando la gota sale por un borde (como un río que sale del mapa).</item>
/// <item>Registra cuánto se erosionó (<see cref="ErosionMap"/>) y depositó (<see cref="DepositionMap"/>)
/// en cada celda; estos mapas alimentan las capas de superficie.</item>
/// </list>
/// <para>Siempre se cumple: <c>altura final = altura inicial − erosión + depósito</c>.</para>
/// </remarks>
public sealed class HydraulicErosion(HydraulicErosionSettings settings) : IHeightMapFilter
{
    /// <summary>Parámetros con los que se creó el filtro.</summary>
    public HydraulicErosionSettings Settings { get; } = Validate(settings);

    /// <summary>Material retirado de cada celda en la última ejecución (≥ 0).</summary>
    public FloatMap? ErosionMap { get; private set; }

    /// <summary>Material depositado en cada celda en la última ejecución (≥ 0).</summary>
    public FloatMap? DepositionMap { get; private set; }

    /// <inheritdoc />
    public void Apply(FloatMap heightMap)
    {
        ArgumentNullException.ThrowIfNull(heightMap);

        var s = Settings;
        var w = heightMap.Width;
        var h = heightMap.Height;
        var map = heightMap.Data;
        var erosion = new FloatMap(w, h);
        var deposition = new FloatMap(w, h);
        ErosionMap = erosion;
        DepositionMap = deposition;

        if (w < 2 || h < 2)
        {
            return;
        }

        var brush = BuildBrush(s.BrushRadius);
        var random = new Random(s.Seed);

        for (var droplet = 0; droplet < s.Droplets; droplet++)
        {
            // Posición continua dentro de [0, w-1) × [0, h-1), para que la celda (x+1, y+1) siempre exista.
            var posX = random.NextSingle() * (w - 1);
            var posY = random.NextSingle() * (h - 1);
            float dirX = 0, dirY = 0;
            var speed = s.InitialSpeed;
            var water = s.InitialWater;
            var sediment = 0f;

            for (var life = 0; life < s.MaxLifetime; life++)
            {
                var nodeX = (int)posX;
                var nodeY = (int)posY;
                var u = posX - nodeX;
                var v = posY - nodeY;
                var (height, gradX, gradY) = HeightAndGradient(map, w, posX, posY);

                // Nueva dirección: mezcla de la dirección anterior y la bajada más pronunciada.
                dirX = (dirX * s.Inertia) - (gradX * (1 - s.Inertia));
                dirY = (dirY * s.Inertia) - (gradY * (1 - s.Inertia));
                var length = MathF.Sqrt((dirX * dirX) + (dirY * dirY));
                if (length < 1e-12f)
                {
                    // Terreno plano y sin inercia: la gota se detiene aquí.
                    Deposit(map, deposition, w, nodeX, nodeY, u, v, sediment);
                    sediment = 0;
                    break;
                }

                dirX /= length;
                dirY /= length;
                posX += dirX;
                posY += dirY;

                if (posX < 0 || posX >= w - 1 || posY < 0 || posY >= h - 1)
                {
                    // Sale del mapa: el sedimento que lleva se pierde.
                    sediment = 0;
                    break;
                }

                var deltaHeight = HeightAndGradient(map, w, posX, posY).Height - height;
                var capacity = Math.Max(-deltaHeight * speed * water * s.SedimentCapacityFactor, s.MinSedimentCapacity);

                if (sediment > capacity || deltaHeight > 0)
                {
                    // Cuesta arriba: rellena el hoyo (sin pasar de la altura de salida).
                    // Saturada: deposita parte del exceso.
                    var amount = deltaHeight > 0
                        ? Math.Min(deltaHeight, sediment)
                        : (sediment - capacity) * s.DepositSpeed;
                    sediment -= amount;
                    Deposit(map, deposition, w, nodeX, nodeY, u, v, amount);
                }
                else
                {
                    // Nunca erosiona más que la diferencia de altura, para no cavar hoyos.
                    var amount = Math.Min((capacity - sediment) * s.ErodeSpeed, -deltaHeight);
                    sediment += Erode(map, erosion, w, h, nodeX, nodeY, brush, amount);
                }

                speed = MathF.Sqrt(Math.Max(0f, (speed * speed) - (deltaHeight * s.Gravity)));
                water *= 1 - s.EvaporateSpeed;

                if (life == s.MaxLifetime - 1)
                {
                    // Fin de vida dentro del mapa: deja lo que lleva donde está.
                    Deposit(map, deposition, w, (int)posX, (int)posY, posX - (int)posX, posY - (int)posY, sediment);
                    sediment = 0;
                }
            }
        }
    }

    /// <summary>Altura (interpolada) y gradiente en una posición continua.</summary>
    private static (float Height, float GradX, float GradY) HeightAndGradient(float[] map, int w, float x, float y)
    {
        var cx = (int)x;
        var cy = (int)y;
        var u = x - cx;
        var v = y - cy;
        var i = (cy * w) + cx;
        var nw = map[i];
        var ne = map[i + 1];
        var sw = map[i + w];
        var se = map[i + w + 1];

        var gradX = ((ne - nw) * (1 - v)) + ((se - sw) * v);
        var gradY = ((sw - nw) * (1 - u)) + ((se - ne) * u);
        var height = (nw * (1 - u) * (1 - v)) + (ne * u * (1 - v)) + (sw * (1 - u) * v) + (se * u * v);
        return (height, gradX, gradY);
    }

    /// <summary>Reparte <paramref name="amount"/> entre las 4 esquinas de la celda (pesos bilineales).</summary>
    private static void Deposit(float[] map, FloatMap deposition, int w, int x, int y, float u, float v, float amount)
    {
        if (amount <= 0)
        {
            return;
        }

        var i = (y * w) + x;
        Add(i, amount * (1 - u) * (1 - v));
        Add(i + 1, amount * u * (1 - v));
        Add(i + w, amount * (1 - u) * v);
        Add(i + w + 1, amount * u * v);

        void Add(int index, float value)
        {
            map[index] += value;
            deposition.Data[index] += value;
        }
    }

    /// <summary>
    /// Arranca hasta <paramref name="amount"/> de material repartido con el pincel alrededor de (x, y).
    /// Devuelve lo realmente erosionado (una celda no puede bajar de 0).
    /// </summary>
    private static float Erode(float[] map, FloatMap erosion, int w, int h, int x, int y,
        (int Dx, int Dy, float Weight)[] brush, float amount)
    {
        if (amount <= 0)
        {
            return 0;
        }

        // Normaliza el pincel solo con las celdas que caen dentro del mapa.
        var totalWeight = 0f;
        foreach (var (dx, dy, weight) in brush)
        {
            if ((uint)(x + dx) < (uint)w && (uint)(y + dy) < (uint)h)
            {
                totalWeight += weight;
            }
        }

        var removed = 0f;
        foreach (var (dx, dy, weight) in brush)
        {
            var bx = x + dx;
            var by = y + dy;
            if ((uint)bx >= (uint)w || (uint)by >= (uint)h)
            {
                continue;
            }

            var index = (by * w) + bx;
            var take = Math.Min(map[index], amount * weight / totalWeight);
            take = Math.Max(take, 0f);
            map[index] -= take;
            erosion.Data[index] += take;
            removed += take;
        }

        return removed;
    }

    /// <summary>Pincel circular con peso decreciente desde el centro (1 − d/r).</summary>
    private static (int Dx, int Dy, float Weight)[] BuildBrush(int radius)
    {
        var cells = new List<(int, int, float)>();
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                if (distance <= radius)
                {
                    cells.Add((dx, dy, radius == 0 ? 1f : 1f - (distance / (radius + 1))));
                }
            }
        }

        return [.. cells];
    }

    private static HydraulicErosionSettings Validate(HydraulicErosionSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentOutOfRangeException.ThrowIfNegative(s.Droplets);
        ArgumentOutOfRangeException.ThrowIfNegative(s.BrushRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(s.MaxLifetime);
        foreach (var (value, name) in new[]
                 {
                     (s.Inertia, nameof(s.Inertia)), (s.ErodeSpeed, nameof(s.ErodeSpeed)),
                     (s.DepositSpeed, nameof(s.DepositSpeed)), (s.EvaporateSpeed, nameof(s.EvaporateSpeed)),
                 })
        {
            if (value is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(name, value, "Debe estar entre 0 y 1.");
            }
        }

        return s;
    }
}
