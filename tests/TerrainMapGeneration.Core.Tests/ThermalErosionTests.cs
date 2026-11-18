using TerrainMapGeneration.Core.Erosion;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

public class ThermalErosionTests
{
    private const float HeightScale = 64f;

    private static ThermalErosion Create(int iterations, float talus = 35f, float strength = 0.5f) =>
        new(new ThermalErosionSettings(iterations, talus, strength, HeightScale));

    /// <summary>Mayor exceso sobre el ángulo de reposo entre vecinos de 4 direcciones.</summary>
    private static float MaxExcessOverTalus(FloatMap map, float talusDegrees)
    {
        var threshold = MathF.Tan(talusDegrees * MathF.PI / 180f) / HeightScale;
        var max = 0f;
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (x + 1 < map.Width)
                {
                    max = Math.Max(max, MathF.Abs(map[x, y] - map[x + 1, y]) - threshold);
                }

                if (y + 1 < map.Height)
                {
                    max = Math.Max(max, MathF.Abs(map[x, y] - map[x, y + 1]) - threshold);
                }
            }
        }

        return max;
    }

    [Fact]
    public void FlatMap_IsUnchanged()
    {
        var map = TestMaps.Flat(16, 0.4f);
        Create(20).Apply(map);
        Assert.All(map.Data, v => Assert.Equal(0.4f, v));
    }

    [Fact]
    public void GentleSlope_BelowTalus_IsUnchanged()
    {
        // Pendiente real = 0.005 · 64 = 0.32 → 17.7°, menor que el talud de 35°.
        var map = TestMaps.RampX(16, 8, 0.005f);
        var original = map.Clone();

        Create(20).Apply(map);

        Assert.Equal(original.Data, map.Data);
    }

    [Fact]
    public void ConservesMass()
    {
        var map = TestMaps.Cone(32);
        var before = map.Sum();

        Create(100).Apply(map);

        Assert.Equal(before, map.Sum(), before * 1e-5);
    }

    [Fact]
    public void Spike_IsLoweredAndNeighborsRaised()
    {
        var map = TestMaps.Spike(9);
        Create(1, strength: 1f).Apply(map);

        Assert.True(map[4, 4] < 1f);
        Assert.True(map[3, 4] > 0.2f);
        Assert.True(map[5, 5] > 0.2f);
        Assert.Equal(0.2f, map[0, 0]); // lejos del pico no cambia
    }

    [Fact]
    public void ManyIterations_SlopesConvergeToTalusAngle()
    {
        var map = TestMaps.Cone(32); // pendiente ≈ 1/15.5 · 64 ≈ 76°, muy por encima del talud
        var threshold = MathF.Tan(35f * MathF.PI / 180f) / HeightScale;
        Assert.True(MaxExcessOverTalus(map, 35f) > threshold);

        Create(500).Apply(map);

        Assert.True(MaxExcessOverTalus(map, 35f) < threshold * 0.05f,
            $"Exceso restante {MaxExcessOverTalus(map, 35f)} frente a umbral {threshold}");
    }

    [Fact]
    public void Result_IsIndependentOfNothingButInput()
    {
        var a = TestMaps.Cone(20);
        var b = TestMaps.Cone(20);
        Create(30).Apply(a);
        Create(30).Apply(b);
        Assert.Equal(a.Data, b.Data);
    }

    [Fact]
    public void Result_IsSymmetricForSymmetricInput()
    {
        // Gracias al doble buffer, el orden de recorrido no introduce asimetrías.
        var map = TestMaps.Spike(11);
        Create(10, strength: 1f).Apply(map);
        Assert.Equal(map[3, 5], map[7, 5], 6);
        Assert.Equal(map[5, 3], map[5, 7], 6);
        Assert.Equal(map[4, 4], map[6, 6], 6);
    }

    [Theory]
    [InlineData(-1, 35f, 0.5f)]
    [InlineData(10, 90f, 0.5f)]
    [InlineData(10, 35f, 1.5f)]
    public void InvalidSettings_Throw(int iterations, float talus, float strength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(iterations, talus, strength));
    }
}
