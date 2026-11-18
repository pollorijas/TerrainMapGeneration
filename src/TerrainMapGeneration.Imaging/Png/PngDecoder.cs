using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TerrainMapGeneration.Imaging.Png;

/// <summary>
/// Imagen PNG decodificada. Todas las muestras se expresan en 16 bits (0–65535) sea cual sea la
/// profundidad original, y las imágenes con paleta se expanden a RGB.
/// </summary>
public sealed class PngPixelData
{
    internal PngPixelData(int width, int height, int channels, ushort[] samples,
        PngColorType sourceColorType, int sourceBitDepth)
    {
        Width = width;
        Height = height;
        Channels = channels;
        Samples = samples;
        SourceColorType = sourceColorType;
        SourceBitDepth = sourceBitDepth;
    }

    /// <summary>Ancho en píxeles.</summary>
    public int Width { get; }

    /// <summary>Alto en píxeles.</summary>
    public int Height { get; }

    /// <summary>Canales por píxel: 1 = gris, 2 = gris+alfa, 3 = RGB, 4 = RGBA.</summary>
    public int Channels { get; }

    /// <summary>Muestras en orden de filas e intercaladas por canal: <c>(y·Width + x)·Channels + c</c>.</summary>
    public ushort[] Samples { get; }

    /// <summary>Tipo de color del archivo original.</summary>
    public PngColorType SourceColorType { get; }

    /// <summary>Profundidad en bits del archivo original.</summary>
    public int SourceBitDepth { get; }

    /// <summary>
    /// Luminancia del píxel en [0, 1]. En imágenes en color usa los coeficientes Rec. 709
    /// (0.2126 R + 0.7152 G + 0.0722 B). El canal alfa se ignora.
    /// </summary>
    public float GetLuminance(int x, int y)
    {
        var i = ((y * Width) + x) * Channels;
        if (Channels <= 2)
        {
            return Samples[i] / 65535f;
        }

        return ((0.2126f * Samples[i]) + (0.7152f * Samples[i + 1]) + (0.0722f * Samples[i + 2])) / 65535f;
    }
}

/// <summary>
/// Decodificador PNG mínimo, sin dependencias externas (usa <see cref="ZLibStream"/> de .NET).
/// </summary>
/// <remarks>
/// Soporta todos los tipos de color y profundidades de la especificación PNG (1, 2, 4, 8 y 16 bits),
/// paletas y entrelazado Adam7. Verifica el CRC de cada chunk. No aplica corrección gamma ni perfiles
/// de color, y la transparencia por chunk tRNS se ignora: para un heightmap solo importa el valor.
/// </remarks>
public static class PngDecoder
{
    // Pasadas de Adam7: (inicioX, inicioY, pasoX, pasoY).
    private static readonly (int X, int Y, int Dx, int Dy)[] Adam7 =
    [
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2),
    ];

    /// <summary>Decodifica un archivo PNG.</summary>
    public static PngPixelData Decode(string path)
    {
        using var stream = File.OpenRead(path);
        return Decode(stream);
    }

    /// <summary>Decodifica un PNG desde un flujo.</summary>
    /// <exception cref="InvalidDataException">El archivo no es un PNG válido o está dañado.</exception>
    /// <exception cref="NotSupportedException">Usa una característica que este decodificador no soporta.</exception>
    public static PngPixelData Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Span<byte> signature = stackalloc byte[8];
        ReadExactly(stream, signature);
        if (!signature.SequenceEqual(PngFormat.Signature))
        {
            throw new InvalidDataException("El archivo no es un PNG (firma incorrecta).");
        }

        Header? header = null;
        byte[]? palette = null;
        using var idat = new MemoryStream();
        Span<byte> chunkHeader = stackalloc byte[8];
        Span<byte> crcBytes = stackalloc byte[4];

        while (true)
        {
            ReadExactly(stream, chunkHeader);
            var length = BinaryPrimitives.ReadUInt32BigEndian(chunkHeader);
            if (length > int.MaxValue)
            {
                throw new InvalidDataException("Chunk PNG con longitud no válida.");
            }

            var type = Encoding.ASCII.GetString(chunkHeader[4..]);
            var data = new byte[length];
            ReadExactly(stream, data);
            ReadExactly(stream, crcBytes);
            if (BinaryPrimitives.ReadUInt32BigEndian(crcBytes) != PngFormat.Crc32(chunkHeader[4..], data))
            {
                throw new InvalidDataException($"CRC incorrecto en el chunk '{type}': archivo dañado.");
            }

            switch (type)
            {
                case "IHDR":
                    header = ParseHeader(data);
                    break;
                case "PLTE":
                    palette = data;
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    if (header is null)
                    {
                        throw new InvalidDataException("PNG sin chunk IHDR.");
                    }

                    idat.Position = 0;
                    return DecodeImage(header, palette, idat);
                default:
                    // Bit 5 del primer byte en 0 = chunk crítico: no se puede ignorar.
                    if ((chunkHeader[4] & 0x20) == 0)
                    {
                        throw new NotSupportedException($"Chunk crítico PNG no soportado: '{type}'.");
                    }

                    break;
            }
        }
    }

    private static Header ParseHeader(byte[] d)
    {
        if (d.Length != 13)
        {
            throw new InvalidDataException("IHDR con tamaño incorrecto.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(d);
        var height = BinaryPrimitives.ReadInt32BigEndian(d.AsSpan(4));
        var bitDepth = d[8];
        var colorType = (PngColorType)d[9];
        if (width <= 0 || height <= 0 || (long)width * height > PngFormat.MaxPixels)
        {
            throw new InvalidDataException($"Dimensiones PNG no válidas o demasiado grandes: {width}x{height}.");
        }

        if (!Enum.IsDefined(colorType) || !PngFormat.IsValidBitDepth(colorType, bitDepth))
        {
            throw new InvalidDataException($"Combinación no válida: tipo de color {d[9]}, {bitDepth} bits.");
        }

        if (d[10] != 0 || d[11] != 0 || d[12] > 1)
        {
            throw new NotSupportedException("Método de compresión, filtro o entrelazado PNG desconocido.");
        }

        return new Header(width, height, bitDepth, colorType, Interlaced: d[12] == 1);
    }

    private static PngPixelData DecodeImage(Header h, byte[]? palette, Stream idat)
    {
        if (h.ColorType == PngColorType.Palette && (palette is null || palette.Length % 3 != 0))
        {
            throw new InvalidDataException("PNG con paleta sin chunk PLTE válido.");
        }

        var fileSamples = PngFormat.SamplesPerPixel(h.ColorType);
        var bitsPerPixel = fileSamples * h.BitDepth;
        var filterBpp = Math.Max(1, bitsPerPixel / 8);
        var outChannels = h.ColorType == PngColorType.Palette ? 3 : fileSamples;
        var samples = new ushort[(long)h.Width * h.Height * outChannels];

        using var inflater = new ZLibStream(idat, CompressionMode.Decompress);
        var passes = h.Interlaced ? Adam7 : [(0, 0, 1, 1)];

        foreach (var (x0, y0, dx, dy) in passes)
        {
            var passWidth = (h.Width - x0 + dx - 1) / dx;
            var passHeight = (h.Height - y0 + dy - 1) / dy;
            if (passWidth <= 0 || passHeight <= 0)
            {
                continue;
            }

            var rowBytes = PngFormat.RowBytes(passWidth, bitsPerPixel);
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            var filter = new byte[1];

            for (var py = 0; py < passHeight; py++)
            {
                ReadExactly(inflater, filter);
                ReadExactly(inflater, current);
                Unfilter(filter[0], current, previous, filterBpp);

                var y = y0 + (py * dy);
                for (var px = 0; px < passWidth; px++)
                {
                    var outIndex = (((long)y * h.Width) + x0 + (px * dx)) * outChannels;
                    if (h.ColorType == PngColorType.Palette)
                    {
                        var entry = ReadSample(current, px, h.BitDepth) * 3;
                        if (entry + 2 >= palette!.Length)
                        {
                            throw new InvalidDataException("Índice de paleta fuera de rango.");
                        }

                        samples[outIndex] = (ushort)(palette[entry] * 257);
                        samples[outIndex + 1] = (ushort)(palette[entry + 1] * 257);
                        samples[outIndex + 2] = (ushort)(palette[entry + 2] * 257);
                    }
                    else
                    {
                        for (var c = 0; c < fileSamples; c++)
                        {
                            var raw = ReadSample(current, (px * fileSamples) + c, h.BitDepth);
                            samples[outIndex + c] = ScaleTo16(raw, h.BitDepth);
                        }
                    }
                }

                (previous, current) = (current, previous);
            }
        }

        return new PngPixelData(h.Width, h.Height, outChannels, samples, h.ColorType, h.BitDepth);
    }

    /// <summary>Revierte el filtro de una fila. <paramref name="prev"/> es la fila anterior ya decodificada.</summary>
    internal static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> prev, int bpp)
    {
        switch (filter)
        {
            case 0:
                break;
            case 1: // Sub
                for (var i = bpp; i < row.Length; i++)
                {
                    row[i] += row[i - bpp];
                }

                break;
            case 2: // Up
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] += prev[i];
                }

                break;
            case 3: // Average
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bpp ? row[i - bpp] : 0;
                    row[i] += (byte)((left + prev[i]) / 2);
                }

                break;
            case 4: // Paeth
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= bpp ? row[i - bpp] : (byte)0;
                    var upLeft = i >= bpp ? prev[i - bpp] : (byte)0;
                    row[i] += PngFormat.Paeth(left, prev[i], upLeft);
                }

                break;
            default:
                throw new InvalidDataException($"Tipo de filtro PNG no válido: {filter}.");
        }
    }

    /// <summary>Lee la muestra número <paramref name="index"/> de una fila empaquetada.</summary>
    private static int ReadSample(ReadOnlySpan<byte> row, int index, int bitDepth)
    {
        switch (bitDepth)
        {
            case 16:
                return (row[index * 2] << 8) | row[(index * 2) + 1];
            case 8:
                return row[index];
            default:
                // 1, 2 o 4 bits: varias muestras por byte, empezando por los bits más significativos.
                var bitOffset = index * bitDepth;
                var shift = 8 - bitDepth - (bitOffset % 8);
                return (row[bitOffset / 8] >> shift) & ((1 << bitDepth) - 1);
        }
    }

    private static ushort ScaleTo16(int value, int bitDepth) =>
        bitDepth == 16 ? (ushort)value : (ushort)(value * 65535 / ((1 << bitDepth) - 1));

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("El PNG está truncado.", ex);
        }
    }

    private sealed record Header(int Width, int Height, int BitDepth, PngColorType ColorType, bool Interlaced);
}
