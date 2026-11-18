using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

public class FloatMapTests
{
    [Fact]
    public void Indexer_UsesRowMajorLayout()
    {
        var map = new FloatMap(3, 2);
        map[2, 1] = 7f;
        Assert.Equal(7f, map.Data[(1 * 3) + 2]);
    }

    [Fact]
    public void Indexer_OutOfBounds_Throws()
    {
        var map = new FloatMap(4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => map[4, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => map[0, -1]);
    }

    [Fact]
    public void Constructor_WithWrongDataLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FloatMap(2, 2, new float[3]));
    }

    [Fact]
    public void GetClamped_ReturnsEdgeValueOutsideMap()
    {
        var map = FloatMap.Create(3, 3, (x, y) => x + (10 * y));
        Assert.Equal(map[0, 0], map.GetClamped(-5, -5));
        Assert.Equal(map[2, 2], map.GetClamped(99, 99));
    }

    [Fact]
    public void SampleBilinear_InterpolatesBetweenCells()
    {
        var map = FloatMap.Create(2, 2, (x, y) => x + (2 * y)); // 0 1 / 2 3
        Assert.Equal(0.5f, map.SampleBilinear(0.5f, 0f), 5);
        Assert.Equal(1.5f, map.SampleBilinear(0.5f, 0.5f), 5);
        Assert.Equal(3f, map.SampleBilinear(1f, 1f), 5);
    }

    [Fact]
    public void Normalize_MapsRangeToZeroOne()
    {
        var map = new FloatMap(3, 1, [2f, 4f, 6f]);
        map.Normalize();
        Assert.Equal([0f, 0.5f, 1f], map.Data);
    }

    [Fact]
    public void Normalize_ConstantMap_BecomesZero()
    {
        var map = TestMaps.Flat(4, 0.7f);
        map.Normalize();
        Assert.All(map.Data, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var map = TestMaps.Flat(2, 1f);
        var copy = map.Clone();
        copy[0, 0] = 5f;
        Assert.Equal(1f, map[0, 0]);
    }
}
