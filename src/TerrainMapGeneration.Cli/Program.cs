using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Imaging;

namespace TerrainMapGeneration.Cli;

/// <summary>Punto de entrada de la herramienta de línea de comandos <c>terrain</c>.</summary>
public static class Program
{
    private const string Usage = """
        Uso:
          terrain colorize <heightmap.png> <salida.png>   Mapa RGB coloreado por altura
          terrain help                                     Muestra esta ayuda
        """;

    /// <summary>Ejecuta el comando indicado y devuelve el código de salida (0 = éxito).</summary>
    public static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "colorize" when args.Length == 3 => Colorize(args[1], args[2]),
                "help" or "--help" or "-h" => PrintUsage(0),
                _ => PrintUsage(1),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                                       or ArgumentException or NotSupportedException
                                       or InvalidDataException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static int Colorize(string input, string output)
    {
        var height = HeightMapReader.Load(input);
        ImageWriter.SaveRgba(ColorRamp.Terrain.Apply(height), output);
        Console.WriteLine($"{input} ({height.Width}x{height.Height}) -> {output}");
        return 0;
    }

    private static int PrintUsage(int exitCode)
    {
        (exitCode == 0 ? Console.Out : Console.Error).WriteLine(Usage);
        return exitCode;
    }
}
