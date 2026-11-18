using TerrainMapGeneration.Core.Analysis;
using TerrainMapGeneration.Core.Maps;

namespace TerrainMapGeneration.Core.Tests;

public class TerrainAnalysisTests
{
    [Fact]
    public void Slope_FlatMap_IsZero()
    {
        var slope = TerrainAnalysis.SlopeDegrees(TestMaps.Flat(8), heightScale: 100);
        Assert.All(slope.Data, s => Assert.Equal(0f, s));
    }

    [Theory]
    [InlineData(1f, 45f)]
    [InlineData(0.5f, 26.565f)]
    [InlineData(1.7320508f, 60f)]
    public void Slope_Ramp_MatchesExpectedAngle(float riseOverRun, float expectedDegrees)
    {
        // Rampa en X con Δh/Δx = 0.01 y heightScale tal que la pendiente real = riseOverRun.
        const float step = 0.01f;
        var ramp = TestMaps.RampX(16, 8, step);

        var slope = TerrainAnalysis.SlopeDegrees(ramp, riseOverRun / step);

        // Incluye los bordes, que usan diferencias hacia un solo lado.
        Assert.All(slope.Data, s => Assert.Equal(expectedDegrees, s, 0.05f));
    }

    [Fact]
    public void Slope_IsDirectionIndependent()
    {
        var alongX = TerrainAnalysis.SlopeDegrees(FloatMap.Create(9, 9, (x, _) => x * 0.1f), 1);
        var alongY = TerrainAnalysis.SlopeDegrees(FloatMap.Create(9, 9, (_, y) => y * 0.1f), 1);
        Assert.Equal(alongX[4, 4], alongY[4, 4], 5);
    }

    [Fact]
    public void BoxBlur_PreservesConstantAndSpreadsSpike()
    {
        Assert.All(TerrainAnalysis.BoxBlur(TestMaps.Flat(6, 0.3f), 2).Data, v => Assert.Equal(0.3f, v, 5));

        var blurred = TerrainAnalysis.BoxBlur(TestMaps.Spike(9, 0f, 9f), 1);
        Assert.Equal(1f, blurred[4, 4], 5); // 9 repartido en 3×3
        Assert.Equal(1f, blurred[3, 3], 5);
        Assert.Equal(0f, blurred[2, 2], 5);
    }

    [Fact]
    public void NormalizeByPercentile_ClampsOutliers()
    {
        // 99 valores de 1 y un valor atípico de 100: el percentil 0.9 es 1, el atípico se recorta a 1.
        var data = Enumerable.Repeat(1f, 99).Append(100f).ToArray();
        var map = new FloatMap(10, 10, data);

        var normalized = TerrainAnalysis.NormalizeByPercentile(map, 0.9f);

        Assert.All(normalized.Data, v => Assert.Equal(1f, v));
    }

    [Fact]
    public void NormalizeByPercentile_AllZero_StaysZero()
    {
        var normalized = TerrainAnalysis.NormalizeByPercentile(new FloatMap(4, 4));
        Assert.All(normalized.Data, v => Assert.Equal(0f, v));
    }
}
