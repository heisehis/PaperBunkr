using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Renders <see cref="MdBlock"/>s from <see cref="MarkdownLite"/> (docs/superpowers/specs/2026-09-26-about-polish-design.md §2): the
/// changelog and the legal viewer both use it. Each block is one <see cref="SelectableTextBlock"/> built from inline runs, so a
/// paragraph wraps as one piece of text and can be selected and copied - the old renderers laid runs out as separate text blocks,
/// which is what broke sentences mid-line. Links are small inline buttons (keyboard-reachable) that run <see cref="LinkCommand"/>
/// with the link's target. Every brush is a theme resource, so skins and live theme switches apply. Built in code (no .axaml), so
/// it needs no XAML weave.
/// </summary>
public sealed class MarkdownView : Border
{
    public static readonly StyledProperty<IReadOnlyList<MdBlock>?> BlocksProperty =
        AvaloniaProperty.Register<MarkdownView, IReadOnlyList<MdBlock>?>(nameof(Blocks));

    public static readonly StyledProperty<double> BaseFontSizeProperty =
        AvaloniaProperty.Register<MarkdownView, double>(nameof(BaseFontSize), 13);

    /// <summary>Resource key of the body text brush. Headings always use <c>PbTextBrush</c>.</summary>
    public static readonly StyledProperty<string> BodyBrushKeyProperty =
        AvaloniaProperty.Register<MarkdownView, string>(nameof(BodyBrushKey), "PbTextBrush");

    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<MarkdownView, ICommand?>(nameof(LinkCommand));

    private static readonly FontFamily MonoFont = new("Consolas, Cascadia Mono, monospace");

    private readonly StackPanel _panel = new() { Spacing = 6 };
    private readonly List<IDisposable> _bindings = new();
    private readonly List<Button> _links = new();

    public MarkdownView()
    {
        Child = _panel;
    }

    public IReadOnlyList<MdBlock>? Blocks
    {
        get => GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public double BaseFontSize
    {
        get => GetValue(BaseFontSizeProperty);
        set => SetValue(BaseFontSizeProperty, value);
    }

    public string BodyBrushKey
    {
        get => GetValue(BodyBrushKeyProperty);
        set => SetValue(BodyBrushKeyProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BlocksProperty || change.Property == BaseFontSizeProperty || change.Property == BodyBrushKeyProperty)
        {
            Rebuild();
        }
        else if (change.Property == LinkCommandProperty)
        {
            foreach (var link in _links)
            {
                link.Command = LinkCommand;
            }
        }
    }

    private void Rebuild()
    {
        foreach (var binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
        _links.Clear();
        _panel.Children.Clear();

        // Inherited by every text block below, so body text needs no binding of its own.
        _bindings.Add(this.Bind(TextElement.ForegroundProperty, this.GetResourceObservable(BodyBrushKey)));

        var blocks = Blocks;
        if (blocks is null)
        {
            return;
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            _panel.Children.Add(BuildBlock(blocks[i], isFirst: i == 0));
        }
    }

    private Control BuildBlock(MdBlock block, bool isFirst)
    {
        double size = BaseFontSize;
        switch (block.Kind)
        {
            case MdBlockKind.Heading1:
            case MdBlockKind.Heading2:
            case MdBlockKind.Heading3:
            {
                double headingSize = block.Kind switch
                {
                    MdBlockKind.Heading1 => size + 4,
                    MdBlockKind.Heading2 => size + 1.5,
                    _ => size,
                };
                var heading = BuildText(block, headingSize);
                heading.FontWeight = FontWeight.SemiBold;
                heading.Margin = new Thickness(0, isFirst ? 0 : 8, 0, 0);
                _bindings.Add(heading.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PbTextBrush")));
                return heading;
            }

            case MdBlockKind.Bullet:
            {
                var dot = new TextBlock { Text = "•", FontSize = size, Margin = new Thickness(2, 0, 9, 0) };
                var text = BuildText(block, size);
                Grid.SetColumn(text, 1);
                return new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { dot, text } };
            }

            case MdBlockKind.Quote:
            {
                var text = BuildText(block, size);
                text.FontStyle = FontStyle.Italic;
                _bindings.Add(text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PbTextMutedBrush")));
                var quote = new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 1, 0, 1), Child = text };
                _bindings.Add(quote.Bind(BorderBrushProperty, this.GetResourceObservable("PbBorderBrush")));
                return quote;
            }

            case MdBlockKind.Preformatted:
                // Wraps rather than scrolling sideways: a nested horizontal ScrollViewer would swallow the wheel inside the outer
                // vertical one. LICENSE's 80-column lines fit the viewer's width anyway.
                return new SelectableTextBlock
                {
                    Text = block.PlainText,
                    FontFamily = MonoFont,
                    FontSize = size - 1.5,
                    TextWrapping = TextWrapping.Wrap,
                };

            default:
                return BuildText(block, size);
        }
    }

    private SelectableTextBlock BuildText(MdBlock block, double size)
    {
        var text = new SelectableTextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
        var inlines = text.Inlines!;
        foreach (var run in block.Runs)
        {
            switch (run.Style)
            {
                case MdRunStyle.Bold:
                    inlines.Add(new Run(run.Text) { FontWeight = FontWeight.SemiBold });
                    break;
                case MdRunStyle.Italic:
                    inlines.Add(new Run(run.Text) { FontStyle = FontStyle.Italic });
                    break;
                case MdRunStyle.Code:
                {
                    var code = new Run(run.Text) { FontFamily = MonoFont, FontSize = size - 1 };
                    _bindings.Add(code.Bind(TextElement.BackgroundProperty, this.GetResourceObservable("PbSurface3Brush")));
                    inlines.Add(code);
                    break;
                }

                case MdRunStyle.Link:
                    inlines.Add(new InlineUIContainer(BuildLink(run, size)) { BaselineAlignment = BaselineAlignment.TextBottom });
                    break;
                default:
                    inlines.Add(new Run(run.Text));
                    break;
            }
        }

        return text;
    }

    private Button BuildLink(MdRun run, double size)
    {
        var label = new TextBlock { Text = run.Text, FontSize = size, TextDecorations = TextDecorations.Underline };
        _bindings.Add(label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("PbAccentTextBrush")));
        var link = new Button
        {
            Content = label,
            Padding = new Thickness(0),
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = new Cursor(StandardCursorType.Hand),
            Command = LinkCommand,
            CommandParameter = run.Target,
        };
        link.Classes.Add("mdLink");
        ToolTip.SetTip(link, run.Target);
        AutomationProperties.SetName(link, run.Text);
        _links.Add(link);
        return link;
    }
}
