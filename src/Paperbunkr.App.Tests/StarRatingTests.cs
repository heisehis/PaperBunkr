using Avalonia.Input;
using Paperbunkr.App.Controls;

namespace Paperbunkr.App.Tests;

[Collection(nameof(AvaloniaTestCollection))]
public class StarRatingTests
{
    [Theory]
    [InlineData(0, 0.5)]    // left half of star 1
    [InlineData(9, 1.0)]    // right half of star 1
    [InlineData(18, 1.5)]   // left half of star 2 (pitch 18)
    [InlineData(27, 2.0)]
    [InlineData(89, 5.0)]   // right half of the last star
    [InlineData(500, 5.0)]  // past the end clamps
    [InlineData(-10, 0.5)]  // before the start clamps
    public void ValueAt_PicksHalfOrWholeStarUnderThePointer(double x, double expected)
    {
        Assert.Equal(expected, StarRating.ValueAt(x, starSize: 16, spacing: 2));
    }

    [Fact]
    public void Value_IsSnappedToHalfStars_AndClamped()
    {
        var control = new StarRating { Value = 3.3 };
        Assert.Equal(3.5, control.Value);

        control.Value = 9;
        Assert.Equal(5.0, control.Value);

        control.Value = 0;
        Assert.Null(control.Value);

        control.Value = -2;
        Assert.Null(control.Value);

        control.Value = double.NaN;
        Assert.Null(control.Value);
    }

    [Fact]
    public void Keyboard_StepsByHalfAStar_AndDeleteClears()
    {
        var control = new StarRating();

        Press(control, Key.Right);
        Assert.Equal(0.5, control.Value);
        Press(control, Key.Right);
        Press(control, Key.Right);
        Assert.Equal(1.5, control.Value);
        Press(control, Key.Left);
        Assert.Equal(1.0, control.Value);
        Press(control, Key.Left);
        Press(control, Key.Left);
        Assert.Null(control.Value);

        control.Value = 4;
        Press(control, Key.Delete);
        Assert.Null(control.Value);

        control.Value = 5;
        Press(control, Key.Right);
        Assert.Equal(5.0, control.Value);
    }

    [Fact]
    public void ReadOnly_IgnoresTheKeyboard()
    {
        var control = new StarRating { Value = 2, IsReadOnly = true };

        Press(control, Key.Right);
        Press(control, Key.Delete);

        Assert.Equal(2.0, control.Value);
    }

    [Fact]
    public void Measure_IsFiveStarsWide()
    {
        var control = new StarRating { StarSize = 16, Spacing = 2 };
        control.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(5 * 16 + 4 * 2, control.DesiredSize.Width);
        Assert.Equal(16, control.DesiredSize.Height);
    }

    private static void Press(StarRating control, Key key)
    {
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    }
}
