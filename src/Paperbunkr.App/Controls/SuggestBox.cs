using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A free-text field with a suggestion drop-down, built entirely from a <see cref="TextBox"/> + a
/// disclosure <see cref="Button"/> + a plain <see cref="Popup"/> of clickable rows - the same
/// idiom <c>LibraryToolbar</c>'s own dropdowns use, which is proven to work on this project's
/// runtime target.
///
/// <para><b>Why this exists instead of <c>ComboBox</c>/<c>AutoCompleteBox</c>:</b> on the
/// Win11 IoT LTSC target (degraded render stack), opening either of those permanently froze the
/// UI thread - <c>Popup.Open()</c> re-entered a <c>bool</c> <c>TemplateBinding</c> that oscillates
/// between the control's <c>IsDropDownOpen</c> and the template popup's <c>IsOpen</c>, amplified by
/// <c>ComboBox.PopupOpened</c>'s ancestor-<c>IsVisible</c> subscriptions and native popup-window
/// teardown. Freeze stacks 2026-09-10; see <c>Services/FluentAvaloniaWorkarounds</c>. This control
/// has no cross-property two-way binding on the popup (the theme binds
/// <c>Popup.IsOpen="{TemplateBinding IsDropDownOpen}"</c> one-way; code owns the closes) and no
/// per-open ancestor subscriptions.</para>
///
/// Code-only <see cref="TemplatedControl"/> - the template is an implicit <c>ControlTheme</c> in
/// <c>Styles/FormControls.axaml</c> (the <c>BrandMark</c> pattern).
/// </summary>
public class SuggestBox : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SuggestBox, string?>(
            nameof(Text), defaultValue: string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<string>?> SuggestionsProperty =
        AvaloniaProperty.Register<SuggestBox, IEnumerable<string>?>(nameof(Suggestions));

    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<SuggestBox, string?>(nameof(Watermark));

    /// <summary>Complete only the segment after the last comma, splicing the rest back in - for
    /// list fields (Writer, Genre, Characters, ...). Mirrors the old <c>MultiValueAutoComplete</c>.</summary>
    public static readonly StyledProperty<bool> IsMultiValueProperty =
        AvaloniaProperty.Register<SuggestBox, bool>(nameof(IsMultiValue));

    /// <summary>Text is not user-editable - the drop-down is the only way to set it (closed enums
    /// like Color Mode). The field still shows the current value and opens the full list on click.</summary>
    public static readonly StyledProperty<bool> IsStrictProperty =
        AvaloniaProperty.Register<SuggestBox, bool>(nameof(IsStrict));

    public static readonly StyledProperty<bool> IsDropDownOpenProperty =
        AvaloniaProperty.Register<SuggestBox, bool>(nameof(IsDropDownOpen));

    public static readonly DirectProperty<SuggestBox, bool> HasFilteredItemsProperty =
        AvaloniaProperty.RegisterDirect<SuggestBox, bool>(nameof(HasFilteredItems), o => o._hasFilteredItems);

    private const int MaxRows = 60;

    private TextBox? _textBox;
    private Button? _button;
    private Popup? _popup;
    private ListBox? _list;
    private bool _syncingText;
    private bool _hasFilteredItems;

    public ObservableCollection<string> FilteredItems { get; } = new();

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IEnumerable<string>? Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    public bool IsMultiValue
    {
        get => GetValue(IsMultiValueProperty);
        set => SetValue(IsMultiValueProperty, value);
    }

    public bool IsStrict
    {
        get => GetValue(IsStrictProperty);
        set => SetValue(IsStrictProperty, value);
    }

    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    public bool HasFilteredItems => _hasFilteredItems;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        Detach();

        _textBox = e.NameScope.Find<TextBox>("PART_TextBox");
        _button = e.NameScope.Find<Button>("PART_DropDownButton");
        _popup = e.NameScope.Find<Popup>("PART_Popup");
        _list = e.NameScope.Find<ListBox>("PART_Items");

        if (_textBox is not null)
        {
            _textBox.Text = Text;
            _textBox.TextChanged += OnTextBoxTextChanged;
            _textBox.KeyDown += OnTextBoxKeyDown;
            _textBox.GotFocus += OnTextBoxGotFocus;
        }

        if (_button is not null)
        {
            _button.Click += OnButtonClick;
        }

        if (_popup is not null)
        {
            _popup.Closed += OnPopupClosed;
        }

        if (_list is not null)
        {
            _list.PointerReleased += OnListPointerReleased;
            _list.KeyDown += OnListKeyDown;
        }
    }

    private void Detach()
    {
        if (_textBox is not null)
        {
            _textBox.TextChanged -= OnTextBoxTextChanged;
            _textBox.KeyDown -= OnTextBoxKeyDown;
            _textBox.GotFocus -= OnTextBoxGotFocus;
        }

        if (_button is not null)
        {
            _button.Click -= OnButtonClick;
        }

        if (_popup is not null)
        {
            _popup.Closed -= OnPopupClosed;
        }

        if (_list is not null)
        {
            _list.PointerReleased -= OnListPointerReleased;
            _list.KeyDown -= OnListKeyDown;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && !_syncingText && _textBox is not null)
        {
            _syncingText = true;
            _textBox.Text = Text;
            _syncingText = false;
        }
        else if (change.Property == SuggestionsProperty && IsDropDownOpen)
        {
            Repopulate();
        }
    }

    private void OnTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingText)
        {
            return;
        }

        _syncingText = true;
        SetCurrentValue(TextProperty, _textBox?.Text);
        _syncingText = false;

        if (!IsStrict && _textBox?.IsFocused == true)
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
            Repopulate();
        }
    }

    private void OnTextBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (IsStrict)
        {
            // Read-only field: focusing it is a request to pick from the list.
            SetCurrentValue(IsDropDownOpenProperty, true);
            Repopulate();
        }
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (!IsDropDownOpen)
                {
                    SetCurrentValue(IsDropDownOpenProperty, true);
                    Repopulate();
                }

                _list?.Focus();
                if (_list is not null && _list.ItemCount > 0)
                {
                    _list.SelectedIndex = 0;
                }

                e.Handled = true;
                break;

            case Key.Escape:
                if (IsDropDownOpen)
                {
                    SetCurrentValue(IsDropDownOpenProperty, false);
                    e.Handled = true;
                }

                break;

            case Key.Enter:
                if (IsDropDownOpen)
                {
                    SetCurrentValue(IsDropDownOpenProperty, false);
                    e.Handled = true;
                }

                break;
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        bool open = !IsDropDownOpen;
        SetCurrentValue(IsDropDownOpenProperty, open);
        if (open)
        {
            Repopulate();
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        // Light-dismiss / programmatic close - keep our own flag in step without a two-way binding.
        SetCurrentValue(IsDropDownOpenProperty, false);
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_list?.SelectedItem is string picked)
        {
            Commit(picked);
            e.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _list?.SelectedItem is string picked)
        {
            Commit(picked);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            _textBox?.Focus();
            e.Handled = true;
        }
    }

    private void Commit(string picked)
    {
        string next = IsMultiValue ? SpliceMultiValue(Text, picked) : picked;

        _syncingText = true;
        SetCurrentValue(TextProperty, next);
        if (_textBox is not null)
        {
            _textBox.Text = next;
            _textBox.CaretIndex = next.Length;
        }

        _syncingText = false;

        // Don't close the popup synchronously here: this runs *inside* the PointerReleased/KeyDown
        // handler that the list item itself raised, and closing detaches the popup's whole content
        // tree - including the ListBox still mid-route for that very event. Avalonia's detach walk
        // (OnDetachedFromVisualTreeCore -> SetVisualParent -> AvaloniaList<T>.Remove) isn't
        // reentrant-safe against being torn down mid-event, and throws ArgumentOutOfRangeException.
        // Defer the close to the next dispatcher cycle so the event finishes routing first.
        Dispatcher.UIThread.Post(() =>
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            _textBox?.Focus();
        });
    }

    internal static string SpliceMultiValue(string? current, string picked)
    {
        current ??= string.Empty;
        int comma = current.LastIndexOf(',');
        string prefix = comma < 0 ? string.Empty : current[..(comma + 1)] + " ";
        return $"{prefix}{picked.Trim()}, ";
    }

    /// <summary>
    /// The suggestion list a popup-open should show. <see cref="IsStrict"/> fields render an
    /// <c>IsReadOnly</c> <c>TextBox</c> (see the <c>ControlTheme</c> in FormControls.axaml) - the
    /// user can never type into one, so <see cref="Text"/> there is never "what's being typed to
    /// filter," it's always the already-committed value. Filtering by it (as the non-strict path
    /// does) means a strict field's own current selection is the only thing that can ever match
    /// itself, permanently hiding every other option the moment a value is set - found 2026-09-11
    /// when the reader's "Canvas background" picker (Auto/Color/Texture) could only ever show
    /// "Auto" once that was the set value. Strict fields always get the unfiltered full list;
    /// only the free-typing (non-strict, e.g. multi-value tag) path filters by what's typed.
    /// </summary>
    internal static List<string> FilterSuggestions(IEnumerable<string>? suggestions, string? text, bool isStrict, bool multiValue, int maxRows)
    {
        var source = suggestions ?? Array.Empty<string>();

        IEnumerable<string> matches;
        if (isStrict)
        {
            matches = source;
        }
        else
        {
            string filter = CurrentSegment(text, multiValue);
            matches = filter.Length == 0
                ? source
                : source.Where(s => s?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
        }

        return matches
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxRows)
            .ToList();
    }

    private void Repopulate()
    {
        var wanted = FilterSuggestions(Suggestions, Text, IsStrict, IsMultiValue, MaxRows);

        // In-place update so the ListBox doesn't churn its whole container set each keystroke.
        FilteredItems.Clear();
        foreach (string s in wanted)
        {
            FilteredItems.Add(s);
        }

        bool has = FilteredItems.Count > 0;
        SetAndRaise(HasFilteredItemsProperty, ref _hasFilteredItems, has);

        // Nothing to show and the user is typing free text -> don't leave an empty popup floating.
        if (!has && !IsStrict)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
        }
    }

    internal static string CurrentSegment(string? text, bool multiValue)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (!multiValue)
        {
            return text.Trim();
        }

        int comma = text.LastIndexOf(',');
        return (comma < 0 ? text : text[(comma + 1)..]).Trim();
    }
}
