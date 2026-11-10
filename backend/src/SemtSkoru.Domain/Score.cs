namespace SemtSkoru.Domain;

public readonly record struct Score
{
    public int Value { get; }

    public Score(int value)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Score must be between 0 and 100.");
        }

        Value = value;
    }
}
