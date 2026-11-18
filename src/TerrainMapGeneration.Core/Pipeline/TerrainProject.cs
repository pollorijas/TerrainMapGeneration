using System.Text.Json;
using System.Text.Json.Serialization;
using TerrainMapGeneration.Core.Erosion;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Layers;

namespace TerrainMapGeneration.Core.Pipeline;

/// <summary>
/// Configuración completa de una generación de terreno. Se guarda como JSON para poder repetir un mapa
/// exactamente (misma entrada + mismo proyecto ⇒ mismo resultado).
/// </summary>
public sealed class TerrainProject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new RgbaJsonConverter() },
    };

    /// <summary>Heightmap de entrada (PNG). Las rutas relativas son relativas al archivo del proyecto.</summary>
    public string Input { get; set; } = "heightmap.png";

    /// <summary>Carpeta de salida. Relativa al archivo del proyecto si no es absoluta.</summary>
    public string OutputDirectory { get; set; } = "output";

    /// <summary>Si es <c>true</c>, reescala la entrada para que use todo el rango [0, 1].</summary>
    public bool NormalizeInput { get; set; }

    /// <summary>
    /// Altura máxima del terreno medida en píxeles horizontales. Controla las pendientes: con 32, un pico de
    /// altura 1.0 es tan alto como 32 píxeles de ancho. Súbelo para un terreno más escarpado (más roca).
    /// </summary>
    public float HeightScale { get; set; } = 32f;

    /// <summary>Erosión térmica; <c>null</c> para omitirla.</summary>
    public ThermalErosionSettings? Thermal { get; set; } = new();

    /// <summary>Erosión hidráulica; <c>null</c> para omitirla.</summary>
    public HydraulicErosionSettings? Hydraulic { get; set; } = new();

    /// <summary>
    /// Radio de suavizado de la pendiente y de los mapas de erosión/depósito antes de calcular las máscaras.
    /// Evita el moteado que dejan los surcos de un píxel. 0 = sin suavizado.
    /// </summary>
    public int MaskSmoothingRadius { get; set; } = 1;

    /// <summary>Capas de superficie, en orden (el orden define los ids de tile y los canales del splatmap).</summary>
    public List<SurfaceLayer> Layers { get; set; } = [.. SurfaceLayer.Defaults];

    /// <summary>Capa que se usa donde ninguna regla aplica. <c>null</c> = la primera capa.</summary>
    public string? FallbackLayer { get; set; } = "roca";

    /// <summary>Tamaño del tile en píxeles del heightmap.</summary>
    public int TileSize { get; set; } = 16;

    /// <summary>Intensidad del sombreado del relieve en la vista previa (0–1).</summary>
    public float ShadingStrength { get; set; } = 0.5f;

    /// <summary>Lee un proyecto desde JSON. Las propiedades ausentes toman su valor por defecto.</summary>
    /// <exception cref="JsonException">El JSON no es válido.</exception>
    /// <exception cref="ArgumentException">El proyecto tiene valores no válidos.</exception>
    public static TerrainProject FromJson(string json)
    {
        var project = JsonSerializer.Deserialize<TerrainProject>(json, JsonOptions)
                      ?? throw new JsonException("El proyecto está vacío.");
        project.Validate();
        return project;
    }

    /// <summary>Serializa el proyecto como JSON legible.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Índice de <see cref="FallbackLayer"/> en <see cref="Layers"/>.</summary>
    public int FallbackLayerIndex =>
        FallbackLayer is null ? 0 : Layers.FindIndex(l => l.Name == FallbackLayer);

    /// <summary>Comprueba que los valores tengan sentido; lanza <see cref="ArgumentException"/> si no.</summary>
    public void Validate()
    {
        Require(!string.IsNullOrWhiteSpace(Input), "'input' no puede estar vacío.");
        Require(!string.IsNullOrWhiteSpace(OutputDirectory), "'outputDirectory' no puede estar vacío.");
        Require(HeightScale > 0, "'heightScale' debe ser mayor que 0.");
        Require(TileSize >= 1, "'tileSize' debe ser al menos 1.");
        Require(MaskSmoothingRadius >= 0, "'maskSmoothingRadius' no puede ser negativo.");
        Require(ShadingStrength is >= 0 and <= 1, "'shadingStrength' debe estar entre 0 y 1.");
        Require(Layers is { Count: > 0 }, "Se necesita al menos una capa en 'layers'.");
        Require(Layers.All(l => !string.IsNullOrWhiteSpace(l.Name)), "Todas las capas necesitan 'name'.");
        Require(Layers.Select(l => l.Name).Distinct().Count() == Layers.Count, "Los nombres de capa deben ser únicos.");
        Require(Layers.All(l => l.Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && l.Name != "." && l.Name != ".."),
            "Los nombres de capa no pueden contener caracteres no válidos en nombres de archivo.");
        Require(FallbackLayerIndex >= 0, $"'fallbackLayer' = '{FallbackLayer}' no es ninguna de las capas.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }

    /// <summary>Serializa <see cref="Rgba"/> como "#RRGGBB".</summary>
    private sealed class RgbaJsonConverter : JsonConverter<Rgba>
    {
        public override Rgba Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return Rgba.FromHex(reader.GetString() ?? string.Empty);
            }
            catch (FormatException ex)
            {
                throw new JsonException(ex.Message, ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, Rgba value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToHex());
    }
}
