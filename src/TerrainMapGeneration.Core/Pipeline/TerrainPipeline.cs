using System.Diagnostics;
using TerrainMapGeneration.Core.Analysis;
using TerrainMapGeneration.Core.Erosion;
using TerrainMapGeneration.Core.Export;
using TerrainMapGeneration.Core.Layers;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Pipeline;

/// <summary>Todo lo que produce una ejecución del pipeline (en memoria, sin escribir archivos).</summary>
/// <param name="Height">Heightmap tras la erosión.</param>
/// <param name="Slope">Pendiente en grados (suavizada con <see cref="TerrainProject.MaskSmoothingRadius"/>).</param>
/// <param name="Erosion">Erosión normalizada [0, 1], o <c>null</c> sin erosión hidráulica.</param>
/// <param name="Deposition">Depósito normalizado [0, 1], o <c>null</c> sin erosión hidráulica.</param>
/// <param name="Masks">Máscaras de las capas.</param>
/// <param name="Splatmaps">Splatmaps RGBA (4 capas por imagen).</param>
/// <param name="Preview">Vista previa: colores de capas mezclados y sombreados.</param>
/// <param name="HeightColors">Mapa RGB coloreado solo por altura.</param>
/// <param name="Tiles">Tipo de superficie por tile.</param>
public sealed record PipelineResult(
    FloatMap Height,
    FloatMap Slope,
    FloatMap? Erosion,
    FloatMap? Deposition,
    LayerMasks Masks,
    IReadOnlyList<Splatmap> Splatmaps,
    RgbaImage Preview,
    RgbaImage HeightColors,
    TileMap Tiles);

/// <summary>
/// Orquesta el flujo completo: heightmap → erosión térmica → erosión hidráulica → pendiente →
/// máscaras → splatmaps, vista previa y tiles.
/// </summary>
public static class TerrainPipeline
{
    /// <summary>Ejecuta el pipeline. No modifica <paramref name="input"/>.</summary>
    /// <param name="input">Heightmap de entrada en [0, 1].</param>
    /// <param name="project">Configuración.</param>
    /// <param name="log">Opcional: recibe un mensaje por cada etapa terminada (con su duración).</param>
    public static PipelineResult Run(FloatMap input, TerrainProject project, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(project);
        project.Validate();

        var height = input.Clone();
        FloatMap? erosion = null;
        FloatMap? deposition = null;

        if (project.NormalizeInput)
        {
            height.Normalize();
        }

        if (project.Thermal is not null)
        {
            Step("Erosión térmica", () => new ThermalErosion(project.Thermal, project.HeightScale).Apply(height));
        }

        if (project.Hydraulic is not null)
        {
            var hydraulic = new HydraulicErosion(project.Hydraulic);
            Step($"Erosión hidráulica ({project.Hydraulic.Droplets:N0} gotas)", () => hydraulic.Apply(height));
            erosion = Prepare(hydraulic.ErosionMap!, project.MaskSmoothingRadius);
            deposition = Prepare(hydraulic.DepositionMap!, project.MaskSmoothingRadius);
        }

        FloatMap slope = null!;
        LayerMasks masks = null!;
        Step("Pendiente y máscaras", () =>
        {
            slope = TerrainAnalysis.BoxBlur(
                TerrainAnalysis.SlopeDegrees(height, project.HeightScale), project.MaskSmoothingRadius);
            masks = MaskGenerator.Generate(new TerrainContext(height, slope, erosion, deposition),
                project.Layers, project.FallbackLayerIndex);
        });

        IReadOnlyList<Splatmap> splatmaps = null!;
        RgbaImage preview = null!;
        RgbaImage heightColors = null!;
        TileMap tiles = null!;
        Step("Splatmaps, vista previa y tiles", () =>
        {
            splatmaps = SplatmapEncoder.Encode(masks);
            var shade = TerrainAnalysis.Hillshade(height, project.HeightScale);
            preview = ColorBlender.Blend(masks, shade, project.ShadingStrength);
            heightColors = ColorRamp.Terrain.Apply(height);
            tiles = TileClassifier.Classify(masks, project.TileSize);
        });

        return new PipelineResult(height, slope, erosion, deposition, masks, splatmaps, preview, heightColors, tiles);

        void Step(string name, Action action)
        {
            var watch = Stopwatch.StartNew();
            action();
            log?.Invoke($"{name}: {watch.Elapsed.TotalSeconds:F2} s");
        }
    }

    private static FloatMap Prepare(FloatMap raw, int blurRadius) =>
        TerrainAnalysis.NormalizeByPercentile(TerrainAnalysis.BoxBlur(raw, blurRadius));
}
