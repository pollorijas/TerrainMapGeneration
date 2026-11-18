using TerrainMapGeneration.Core.Erosion;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

public class HydraulicErosionTests
{
    private static HydraulicErosion Create(int droplets = 5_000, int seed = 1) =>
        new(new HydraulicErosionSettings(Droplets: droplets, Seed: seed));

    /// <summary>Cuenco cerrado: h = 0.2 + 0.6·r², con r = 1 en el borde. Ninguna gota sale del mapa.</summary>
    private static FloatMap Bowl(int size = 64) => FloatMap.Create(size, size, (x, y) =>
    {
        var c = (size - 1) / 2f;
        var dx = (x - c) / c;
        var dy = (y - c) / c;
        return 0.2f + (0.6f * ((dx * dx) + (dy * dy)));
    });

    [Fact]
    public void SameSeed_GivesIdenticalResult()
    {
        var a = TestMaps.Cone(64);
        var b = TestMaps.Cone(64);
        Create(seed: 7).Apply(a);
        Create(seed: 7).Apply(b);
        Assert.Equal(a.Data, b.Data);
    }

    [Fact]
    public void DifferentSeed_GivesDifferentResult()
    {
        var a = TestMaps.Cone(64);
        var b = TestMaps.Cone(64);
        Create(seed: 1).Apply(a);
        Create(seed: 2).Apply(b);
        Assert.NotEqual(a.Data, b.Data);
    }

    [Fact]
    public void FlatMap_IsUnchanged()
    {
        var map = TestMaps.Flat(32, 0.5f);
        Create().Apply(map);
        Assert.All(map.Data, v => Assert.Equal(0.5f, v));
    }

    [Fact]
    public void SlopesErode_AndBasinFloorReceivesSediment()
    {
        var map = Bowl();
        var original = map.Clone();

        Create(5_000).Apply(map);

        double floorChange = 0, wallChange = 0;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var r = MathF.Sqrt(MathF.Pow((x - 31.5f) / 31.5f, 2) + MathF.Pow((y - 31.5f) / 31.5f, 2));
                var change = map[x, y] - original[x, y];
                if (r < 0.3f)
                {
                    floorChange += change;
                }
                else if (r > 0.8f)
                {
                    wallChange += change;
                }
            }
        }

        Assert.True(wallChange < 0, $"La parte alta debería perder material (cambio {wallChange}).");
        Assert.True(floorChange > 0, $"El fondo debería recibir sedimento (cambio {floorChange}).");
    }

    [Fact]
    public void HeightChange_EqualsDepositionMinusErosion()
    {
        var map = TestMaps.Cone(64);
        var original = map.Clone();
        var erosion = Create();

        erosion.Apply(map);

        for (var i = 0; i < map.Length; i++)
        {
            var expected = original.Data[i] - erosion.ErosionMap!.Data[i] + erosion.DepositionMap!.Data[i];
            Assert.Equal(expected, map.Data[i], 1e-5f);
        }
    }

    [Fact]
    public void Mass_IsConservedExceptWhatLeavesTheMap()
    {
        // En un cuenco ninguna gota sale por los bordes, así que la masa total se conserva.
        var bowl = Bowl();
        var before = bowl.Sum();
        var erosion = Create();

        erosion.Apply(bowl);

        Assert.True(erosion.ErosionMap!.Sum() > 0, "Debería haber erosión.");
        Assert.Equal(before, bowl.Sum(), before * 1e-4);
    }

    [Fact]
    public void Result_HasNoInvalidValues_AndPeakDoesNotGrow()
    {
        var map = TestMaps.Cone(64);
        var (_, maxBefore) = map.GetRange();
        var erosion = Create(20_000);

        erosion.Apply(map);

        Assert.All(map.Data, v => Assert.True(float.IsFinite(v) && v >= 0f));
        Assert.True(map.GetRange().Max <= maxBefore + 1e-6f);
        Assert.All(erosion.ErosionMap!.Data, v => Assert.True(v >= 0f));
        Assert.All(erosion.DepositionMap!.Data, v => Assert.True(v >= 0f));
    }

    [Fact]
    public void ZeroDroplets_DoesNothing()
    {
        var map = TestMaps.Cone(16);
        var original = map.Clone();
        Create(droplets: 0).Apply(map);
        Assert.Equal(original.Data, map.Data);
    }

    [Fact]
    public void InvalidSettings_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HydraulicErosion(new HydraulicErosionSettings(Inertia: 1.5f)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HydraulicErosion(new HydraulicErosionSettings(Droplets: -1)));
    }
}
