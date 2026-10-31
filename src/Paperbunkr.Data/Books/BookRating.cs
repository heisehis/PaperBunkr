namespace Paperbunkr.Data.Books;

/// <summary>The one place a book rating is validated: 0-5 in 0.5 steps, zero/blank meaning "unrated" (null).</summary>
public static class BookRating
{
    public const float Max = 5f;

    public static float? Normalize(float? value)
    {
        if (value is not float v || float.IsNaN(v) || v <= 0f)
        {
            return null;
        }

        float clamped = Math.Min(v, Max);
        return (float)(Math.Round(clamped * 2d, MidpointRounding.AwayFromZero) / 2d);
    }
}
