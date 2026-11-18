using System.Globalization;
using System.Text;
using TerrainMapGeneration.Core.Layers;

namespace TerrainMapGeneration.Core.Export;

/// <summary>
/// Mapa por tiles: cada tile tiene un único tipo de superficie (el índice de su capa).
/// Es el formato que consume un juego 2D de vista cenital.
/// </summary>
public sealed class TileMap
{
    internal TileMap(int width, int height, int tileSize, int[] layerIds, IReadOnlyList<SurfaceLayer> layers)
    {
        Width = width;
        Height = height;
        TileSize = tileSize;
        LayerIds = layerIds;
        Layers = layers;
    }

    /// <summary>Ancho en tiles.</summary>
    public int Width { get; }

    /// <summary>Alto en tiles.</summary>
    public int Height { get; }

    /// <summary>Tamaño de cada tile en píxeles del heightmap.</summary>
    public int TileSize { get; }

    /// <summary>Índice de capa de cada tile, en orden de filas.</summary>
    public int[] LayerIds { get; }

    /// <summary>Capas: el id de un tile es el índice en esta lista.</summary>
    public IReadOnlyList<SurfaceLayer> Layers { get; }

    /// <summary>Id de capa del tile (x, y).</summary>
    public int this[int x, int y] => LayerIds[(y * Width) + x];

    /// <summary>
    /// Imagen con el color de la capa de cada tile. <paramref name="pixelsPerTile"/> = 1 genera una imagen
    /// de un píxel por tile; = <see cref="TileSize"/> la alinea con el heightmap original.
    /// </summary>
    public RgbaImage ToImage(int pixelsPerTile = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixelsPerTile, 1);
        var image = new RgbaImage(Width * pixelsPerTile, Height * pixelsPerTile);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                image[x, y] = Layers[this[x / pixelsPerTile, y / pixelsPerTile]].Color;
            }
        }

        return image;
    }

    /// <summary>Ids en formato CSV: una fila de texto por fila de tiles (compatible con capas CSV de Tiled).</summary>
    public string ToCsv()
    {
        var sb = new StringBuilder();
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (x > 0)
                {
                    sb.Append(',');
                }

                sb.Append(this[x, y].ToString(CultureInfo.InvariantCulture));
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }
}

/// <summary>Reduce las máscaras por píxel a un tipo de superficie por tile.</summary>
public static class TileClassifier
{
    /// <summary>
    /// Divide el mapa en tiles de <paramref name="tileSize"/>×<paramref name="tileSize"/> píxeles y asigna a
    /// cada uno la capa con mayor peso acumulado dentro del tile (mayoría ponderada). Si el tamaño del mapa
    /// no es múltiplo del tile, los tiles del borde son parciales.
    /// </summary>
    public static TileMap Classify(LayerMasks masks, int tileSize)
    {
        ArgumentNullException.ThrowIfNull(masks);
        ArgumentOutOfRangeException.ThrowIfLessThan(tileSize, 1);

        var tilesX = (masks.Width + tileSize - 1) / tileSize;
        var tilesY = (masks.Height + tileSize - 1) / tileSize;
        var ids = new int[tilesX * tilesY];
        var totals = new double[masks.Layers.Count];

        for (var ty = 0; ty < tilesY; ty++)
        {
            for (var tx = 0; tx < tilesX; tx++)
            {
                Array.Clear(totals);
                var x1 = Math.Min((tx + 1) * tileSize, masks.Width);
                var y1 = Math.Min((ty + 1) * tileSize, masks.Height);
                for (var y = ty * tileSize; y < y1; y++)
                {
                    for (var x = tx * tileSize; x < x1; x++)
                    {
                        var index = (y * masks.Width) + x;
                        for (var l = 0; l < totals.Length; l++)
                        {
                            totals[l] += masks.Masks[l].Data[index];
                        }
                    }
                }

                var best = 0;
                for (var l = 1; l < totals.Length; l++)
                {
                    if (totals[l] > totals[best])
                    {
                        best = l;
                    }
                }

                ids[(ty * tilesX) + tx] = best;
            }
        }

        return new TileMap(tilesX, tilesY, tileSize, ids, masks.Layers);
    }
}
