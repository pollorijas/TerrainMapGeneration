using System.Buffers.Binary;
using System.Text;

namespace TerrainMapGeneration.Imaging.Png;

/// <summary>Tipos de color definidos por la especificación PNG (campo "color type" de IHDR).</summary>
public enum PngColorType : byte
{
    /// <summary>Gris (1 canal).</summary>
    Grayscale = 0,

    /// <summary>Color verdadero RGB (3 canales).</summary>
    Rgb = 2,

    /// <summary>Índices a una paleta PLTE.</summary>
    Palette = 3,

    /// <summary>Gris con alfa (2 canales).</summary>
    GrayscaleAlpha = 4,

    /// <summary>RGB con alfa (4 canales).</summary>
    RgbAlpha = 6,
}

/// <summary>Constantes y utilidades compartidas por el codificador y el decodificador PNG.</summary>
internal static class PngFormat
{
    /// <summary>Firma de 8 bytes con la que empieza todo archivo PNG.</summary>
    public static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>Límite de seguridad para no reservar memoria absurda con un archivo corrupto.</summary>
    public const long MaxPixels = 16384L * 16384L;

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Número de muestras (canales) por píxel tal como se almacenan en el archivo.</summary>
    public static int SamplesPerPixel(PngColorType type) => type switch
    {
        PngColorType.Grayscale or PngColorType.Palette => 1,
        PngColorType.GrayscaleAlpha => 2,
        PngColorType.Rgb => 3,
        PngColorType.RgbAlpha => 4,
        _ => throw new NotSupportedException($"Tipo de color PNG desconocido: {(byte)type}."),
    };

    /// <summary>Indica si la combinación tipo de color / profundidad es válida según la especificación.</summary>
    public static bool IsValidBitDepth(PngColorType type, int bitDepth) => type switch
    {
        PngColorType.Grayscale => bitDepth is 1 or 2 or 4 or 8 or 16,
        PngColorType.Palette => bitDepth is 1 or 2 or 4 or 8,
        _ => bitDepth is 8 or 16,
    };

    /// <summary>Bytes que ocupa una fila de <paramref name="width"/> píxeles (sin el byte de filtro).</summary>
    public static int RowBytes(int width, int bitsPerPixel) => (int)(((long)width * bitsPerPixel + 7) / 8);

    /// <summary>CRC-32 (polinomio 0xEDB88320) sobre el tipo y los datos de un chunk.</summary>
    public static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>Escribe un chunk completo: longitud, tipo, datos y CRC.</summary>
    public static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, header[4..]);
        output.Write(header);
        output.Write(data);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(header[4..], data));
        output.Write(crc);
    }

    /// <summary>Predictor de Paeth (filtro tipo 4).</summary>
    public static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
