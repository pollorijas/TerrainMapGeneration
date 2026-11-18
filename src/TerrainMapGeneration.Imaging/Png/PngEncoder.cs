using System.Buffers.Binary;
using System.IO.Compression;

namespace TerrainMapGeneration.Imaging.Png;

/// <summary>
/// Codificador PNG mínimo, sin dependencias externas. Escribe imágenes no entrelazadas en
/// gris, gris+alfa, RGB o RGBA, a 8 o 16 bits por muestra.
/// </summary>
/// <remarks>
/// Cada fila se filtra con el filtro que da la menor suma de diferencias absolutas
/// (heurística recomendada por la especificación) y el resultado se comprime con zlib.
/// </remarks>
public static class PngEncoder
{
    /// <summary>Escribe un PNG en disco.</summary>
    /// <param name="path">Ruta de salida.</param>
    /// <param name="width">Ancho en píxeles.</param>
    /// <param name="height">Alto en píxeles.</param>
    /// <param name="colorType">Tipo de color (no se admite <see cref="PngColorType.Palette"/>).</param>
    /// <param name="bitDepth">8 o 16.</param>
    /// <param name="pixels">Filas sin filtrar, una tras otra; las muestras de 16 bits en big-endian.</param>
    public static void Encode(string path, int width, int height, PngColorType colorType, int bitDepth,
        ReadOnlySpan<byte> pixels)
    {
        using var stream = File.Create(path);
        Encode(stream, width, height, colorType, bitDepth, pixels);
    }

    /// <summary>Escribe un PNG en un flujo. Ver <see cref="Encode(string,int,int,PngColorType,int,ReadOnlySpan{byte})"/>.</summary>
    public static void Encode(Stream output, int width, int height, PngColorType colorType, int bitDepth,
        ReadOnlySpan<byte> pixels)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (colorType == PngColorType.Palette || bitDepth is not (8 or 16))
        {
            throw new NotSupportedException("El codificador solo escribe gris/RGB(A) a 8 o 16 bits.");
        }

        var bpp = PngFormat.SamplesPerPixel(colorType) * bitDepth / 8;
        var rowBytes = width * bpp;
        if (pixels.Length != (long)rowBytes * height)
        {
            throw new ArgumentException($"Se esperaban {rowBytes * height} bytes de píxeles y hay {pixels.Length}.",
                nameof(pixels));
        }

        output.Write(PngFormat.Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colorType;
        ihdr[10] = 0; // compresión deflate
        ihdr[11] = 0; // filtrado adaptativo estándar
        ihdr[12] = 0; // sin entrelazado
        PngFormat.WriteChunk(output, "IHDR", ihdr);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                var previous = new byte[rowBytes];
                var candidate = new byte[rowBytes];
                var best = new byte[rowBytes];
                for (var y = 0; y < height; y++)
                {
                    var row = pixels.Slice(y * rowBytes, rowBytes);
                    var bestFilter = ChooseFilter(row, previous, bpp, candidate, best);
                    zlib.WriteByte(bestFilter);
                    zlib.Write(best);
                    row.CopyTo(previous);
                }
            }

            PngFormat.WriteChunk(output, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        }

        PngFormat.WriteChunk(output, "IEND", []);
    }

    /// <summary>Prueba los 5 filtros y deja en <paramref name="best"/> el de menor suma absoluta.</summary>
    private static byte ChooseFilter(ReadOnlySpan<byte> row, ReadOnlySpan<byte> prev, int bpp,
        Span<byte> candidate, Span<byte> best)
    {
        byte bestFilter = 0;
        var bestScore = long.MaxValue;
        for (byte filter = 0; filter <= 4; filter++)
        {
            ApplyFilter(filter, row, prev, bpp, candidate);
            long score = 0;
            foreach (var b in candidate)
            {
                score += Math.Abs((int)(sbyte)b);
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestFilter = filter;
                candidate.CopyTo(best);
            }
        }

        return bestFilter;
    }

    /// <summary>Aplica un filtro PNG a una fila (operación inversa de <see cref="PngDecoder.Unfilter"/>).</summary>
    internal static void ApplyFilter(byte filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> prev, int bpp,
        Span<byte> output)
    {
        for (var i = 0; i < row.Length; i++)
        {
            var left = i >= bpp ? row[i - bpp] : (byte)0;
            var upLeft = i >= bpp ? prev[i - bpp] : (byte)0;
            output[i] = filter switch
            {
                0 => row[i],
                1 => (byte)(row[i] - left),
                2 => (byte)(row[i] - prev[i]),
                3 => (byte)(row[i] - ((left + prev[i]) / 2)),
                4 => (byte)(row[i] - PngFormat.Paeth(left, prev[i], upLeft)),
                _ => throw new ArgumentOutOfRangeException(nameof(filter)),
            };
        }
    }
}
