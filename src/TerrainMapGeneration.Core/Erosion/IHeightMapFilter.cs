using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Erosion;

/// <summary>Filtro que modifica un heightmap en el lugar.</summary>
public interface IHeightMapFilter
{
    /// <summary>Aplica el filtro sobre <paramref name="heightMap"/>.</summary>
    void Apply(FloatMap heightMap);
}
