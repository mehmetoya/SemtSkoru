namespace SemtSkoru.Domain.Tests;

public class ScoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Constructor_accepts_values_within_range(int value)
    {
        var score = new Score(value);

        Assert.Equal(value, score.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Constructor_rejects_values_outside_range(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Score(value));
    }
}
