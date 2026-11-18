using System.Diagnostics;
using System.Text.Json;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Pipeline;
using TerrainMapGeneration.Imaging;

namespace TerrainMapGeneration.Cli;

/// <summary>Punto de entrada de la herramienta de línea de comandos <c>terrain</c>.</summary>
public static class Program
{
    private const string Usage = """
        terrain — genera mapas de terreno por capas a partir de un heightmap PNG.

        Uso:
          terrain init [proyecto.json]                     Crea un proyecto con la configuración por defecto
          terrain run <proyecto.json>                      Ejecuta el proyecto y exporta todos los mapas
          terrain colorize <heightmap.png> <salida.png>    Mapa RGB coloreado solo por altura
          terrain help                                     Muestra esta ayuda
        """;

    /// <summary>Ejecuta el comando indicado y devuelve el código de salida (0 = éxito).</summary>
    public static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault()?.ToLowerInvariant() switch
            {
                "init" when args.Length <= 2 => Init(args.Length == 2 ? args[1] : "terrain.json"),
                "run" when args.Length == 2 => Run(args[1]),
                "colorize" when args.Length == 3 => Colorize(args[1], args[2]),
                "help" or "--help" or "-h" => PrintUsage(0),
                _ => PrintUsage(1),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                                       or ArgumentException or NotSupportedException or InvalidDataException
                                       or JsonException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static int Init(string path)
    {
        if (File.Exists(path))
        {
            Console.Error.WriteLine($"Error: '{path}' ya existe; no se sobrescribe.");
            return 1;
        }

        File.WriteAllText(path, new TerrainProject().ToJson());
        Console.WriteLine($"Proyecto creado: {path}");
        Console.WriteLine("Edita 'input' con la ruta de tu heightmap y ejecuta: terrain run " + path);
        return 0;
    }

    private static int Run(string projectPath)
    {
        var total = Stopwatch.StartNew();
        var project = TerrainProject.FromJson(File.ReadAllText(projectPath));
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var input = Path.GetFullPath(project.Input, baseDirectory);
        var output = Path.GetFullPath(project.OutputDirectory, baseDirectory);

        var height = HeightMapReader.Load(input);
        Console.WriteLine($"Heightmap: {input} ({height.Width}x{height.Height})");

        var result = TerrainPipeline.Run(height, project, message => Console.WriteLine($"  {message}"));
        var files = PipelineOutputWriter.Write(result, output);

        Console.WriteLine($"Mapa de {result.Tiles.Width}x{result.Tiles.Height} tiles de {project.TileSize} px.");
        Console.WriteLine($"{files.Count} archivos escritos en {output} ({total.Elapsed.TotalSeconds:F1} s).");
        return 0;
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
