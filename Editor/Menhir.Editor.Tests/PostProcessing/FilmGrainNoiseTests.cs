using Menhir.Editor.PostProcessing;

namespace Menhir.Editor.Tests.PostProcessing;

public class FilmGrainNoiseTests
{
    [Fact]
    public void Generate_IsDeterministicAndCenteredOnMidGray()
    {
        byte[] a = FilmGrainNoise.Generate(FilmGrainType.Medium2);
        Assert.Equal(a, FilmGrainNoise.Generate(FilmGrainType.Medium2));
        Assert.Equal(FilmGrainNoise.Size * FilmGrainNoise.Size, a.Length);
        Assert.InRange(a.Average(v => (double)v), 120, 136);
    }

    [Fact]
    public void LargeGrain_IsSmootherThanThinGrain()
    {
        static double NeighborDifference(byte[] t) =>
            Enumerable.Range(0, t.Length - 1).Average(i => Math.Abs(t[i] - t[i + 1]));

        Assert.True(NeighborDifference(FilmGrainNoise.Generate(FilmGrainType.Large02)) <
                    NeighborDifference(FilmGrainNoise.Generate(FilmGrainType.Thin1)) / 2);
    }
}
