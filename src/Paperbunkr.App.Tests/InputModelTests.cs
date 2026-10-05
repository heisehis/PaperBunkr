using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Tests;

/// <summary>The Phase 1 input-service domain models (docs/superpowers/specs/2026-10-03-input-service-design.md §5.1-§5.2, §5.5): action ids, scopes, contexts and event args.</summary>
public class InputModelTests
{
    private static IReadOnlyList<string> AllCoreIds() =>
        typeof(InputActionIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    [Fact]
    public void CoreActionIds_AreUniqueNonEmptyAndNamespaced()
    {
        var ids = AllCoreIds();

        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id =>
        {
            Assert.False(string.IsNullOrWhiteSpace(id));
            Assert.True(
                id.StartsWith("Reader.") || id.StartsWith("App.") || id.StartsWith("Library.") || id.StartsWith("BookReader.") || id.StartsWith("Compare.")
                || id.StartsWith("Books.") || id.StartsWith("Detail.") || id.StartsWith("SmartLists.") || id.StartsWith("Plugin."),
                $"'{id}' has no recognised prefix");
        });
    }

    [Fact]
    public void InputAction_ComparesByIdAndConvertsFromString()
    {
        InputAction fromString = InputActionIds.ZoomIn;

        Assert.Equal(new InputAction("Reader.ZoomIn"), fromString);
        Assert.Equal("Reader.ZoomIn", fromString.ToString());
        Assert.NotEqual(fromString, (InputAction)InputActionIds.ZoomOut);
        Assert.True(fromString.IsDefined);
    }

    [Fact]
    public void InputAction_DefaultNamesNothing_AndBlankIdsAreRejected()
    {
        InputAction none = default;

        Assert.False(none.IsDefined);
        Assert.Equal(string.Empty, none.ToString());
        Assert.Throws<ArgumentException>(() => new InputAction(""));
        Assert.Throws<ArgumentException>(() => new InputAction("  "));
        Assert.Throws<ArgumentNullException>(() => new InputAction(null!));
    }

    [Fact]
    public void StandardScopes_HaveTheSpecifiedPriorities()
    {
        Assert.Equal(0, InputScope.Global.Priority);
        Assert.True(InputScope.Global.IsGlobal);
        Assert.Equal(100, InputScope.Reader.Priority);
        Assert.False(InputScope.Reader.IsGlobal);
        Assert.False(InputScope.Reader.IsModal);
        Assert.True(InputScope.Overlay.IsModal);
        Assert.True(InputScope.Overlay.Priority > InputScope.Reader.Priority);
        Assert.True(InputScope.Reader.Priority > InputScope.Global.Priority);
    }

    [Fact]
    public void ContextOverlap_NoneMatchesNothing_AndASingleStateMatchesItsGroups()
    {
        Assert.False(InputContext.None.Overlaps(InputContext.Always));
        Assert.True(InputContext.PagedZoomed.Overlaps(InputContext.Paged));
        Assert.True(InputContext.PagedZoomed.Overlaps(InputContext.Always));
        Assert.False(InputContext.PagedZoomed.Overlaps(InputContext.Continuous));
    }

    [Fact]
    public void EventArgs_ResolvePositionThroughTheDelegate_AndOnlyUntilCompleted()
    {
        var canvas = new Border();
        var other = new Border();
        var args = new InputActionEventArgs(
            InputActionIds.ZoomIn, InputDevice.Mouse, wheelDelta: new Vector(0, 1), modifiers: KeyModifiers.Control,
            positionResolver: visual => ReferenceEquals(visual, canvas) ? new Point(10, 20) : new Point(110, 120));

        Assert.Equal(new Point(10, 20), args.GetPosition(canvas));
        Assert.Equal(new Point(110, 120), args.GetPosition(other));
        Assert.Equal(1, args.Value);
        Assert.Equal(new Vector(0, 1), args.WheelDelta);
        Assert.Equal(KeyModifiers.Control, args.Modifiers);

        args.Complete();

        Assert.Null(args.GetPosition(canvas));
    }

    [Fact]
    public void EventArgs_WithoutAPointer_HaveNoPosition()
    {
        var args = new InputActionEventArgs(InputActionIds.NextPage, InputDevice.Keyboard);

        Assert.Null(args.GetPosition(new Border()));
        Assert.False(args.Handled);
        Assert.Throws<ArgumentNullException>(() => args.GetPosition(null!));
    }

    [Fact]
    public void Payload_DefaultIsAPlainPress()
    {
        InputPayload payload = default;

        Assert.Null(payload.Value);
        Assert.Equal(KeyModifiers.None, payload.Modifiers);
        Assert.Null(payload.PositionResolver);
    }
}
