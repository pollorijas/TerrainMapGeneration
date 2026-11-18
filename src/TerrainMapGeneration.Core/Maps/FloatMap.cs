namespace TerrainMapGeneration.Core.Maps;

/// <summary>
/// Rejilla rectangular de valores <see cref="float"/>. Es la estructura de datos central de la
/// herramienta: se usa tanto para alturas (heightmap) como para máscaras, pendientes o mapas de erosión.
/// </summary>
/// <remarks>
/// Los datos se guardan en un arreglo plano en orden de filas: <c>índice = y * Width + x</c>.
/// Por convención las alturas se mantienen normalizadas en el rango [0, 1].
/// </remarks>
public sealed class FloatMap
{
    /// <summary>Crea un mapa lleno de ceros.</summary>
    public FloatMap(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        Data = new float[width * height];
    }

    /// <summary>Crea un mapa que envuelve un arreglo existente (no se copia).</summary>
    public FloatMap(int width, int height, float[] data)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != width * height)
        {
            throw new ArgumentException($"Se esperaban {width * height} valores y hay {data.Length}.", nameof(data));
        }

        Width = width;
        Height = height;
        Data = data;
    }

    /// <summary>Ancho en celdas (píxeles).</summary>
    public int Width { get; }

    /// <summary>Alto en celdas (píxeles).</summary>
    public int Height { get; }

    /// <summary>Valores en orden de filas. Acceso directo para algoritmos que necesitan velocidad.</summary>
    public float[] Data { get; }

    /// <summary>Número total de celdas.</summary>
    public int Length => Data.Length;

    /// <summary>Lee o escribe la celda (x, y). Lanza excepción fuera de los límites.</summary>
    public float this[int x, int y]
    {
        get => Data[IndexOf(x, y)];
        set => Data[IndexOf(x, y)] = value;
    }

    /// <summary>Crea un mapa cuyos valores se calculan con <paramref name="generator"/>(x, y).</summary>
    public static FloatMap Create(int width, int height, Func<int, int, float> generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        var map = new FloatMap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                map.Data[(y * width) + x] = generator(x, y);
            }
        }

        return map;
    }

    /// <summary>Indica si (x, y) está dentro del mapa.</summary>
    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>Lee la celda (x, y) recortando las coordenadas al borde más cercano.</summary>
    public float GetClamped(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        return Data[(y * Width) + x];
    }

    /// <summary>
    /// Interpolación bilineal en coordenadas continuas. (0,0) es el centro de la primera celda.
    /// Las coordenadas fuera del mapa se recortan al borde.
    /// </summary>
    public float SampleBilinear(float x, float y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, Width - 1);
        var y1 = Math.Min(y0 + 1, Height - 1);
        var fx = x - x0;
        var fy = y - y0;

        var top = Lerp(this[x0, y0], this[x1, y0], fx);
        var bottom = Lerp(this[x0, y1], this[x1, y1], fx);
        return Lerp(top, bottom, fy);
    }

    /// <summary>Devuelve una copia independiente.</summary>
    public FloatMap Clone() => new(Width, Height, (float[])Data.Clone());

    /// <summary>Copia los valores de <paramref name="source"/> (mismas dimensiones) en este mapa.</summary>
    public void CopyFrom(FloatMap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureSameSize(source);
        Array.Copy(source.Data, Data, Data.Length);
    }

    /// <summary>Valor mínimo y máximo del mapa.</summary>
    public (float Min, float Max) GetRange()
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var v in Data)
        {
            if (v < min)
            {
                min = v;
            }

            if (v > max)
            {
                max = v;
            }
        }

        return (min, max);
    }

    /// <summary>Suma de todos los valores (en doble precisión, para medir conservación de masa).</summary>
    public double Sum()
    {
        double sum = 0;
        foreach (var v in Data)
        {
            sum += v;
        }

        return sum;
    }

    /// <summary>
    /// Reescala linealmente los valores al rango [0, 1]. Si el mapa es constante, queda en 0.
    /// </summary>
    public void Normalize()
    {
        var (min, max) = GetRange();
        var range = max - min;
        for (var i = 0; i < Data.Length; i++)
        {
            Data[i] = range > 0 ? (Data[i] - min) / range : 0f;
        }
    }

    /// <summary>Lanza una excepción si <paramref name="other"/> no tiene las mismas dimensiones.</summary>
    public void EnsureSameSize(FloatMap other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Width != Width || other.Height != Height)
        {
            throw new ArgumentException(
                $"Dimensiones distintas: {Width}x{Height} frente a {other.Width}x{other.Height}.", nameof(other));
        }
    }

    private int IndexOf(int x, int y)
    {
        if (!Contains(x, y))
        {
            throw new ArgumentOutOfRangeException($"({x}, {y}) está fuera de un mapa de {Width}x{Height}.");
        }

        return (y * Width) + x;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
