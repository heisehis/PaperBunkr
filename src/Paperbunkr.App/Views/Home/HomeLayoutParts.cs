using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Models;
using Paperbunkr.App.Theming.Layout;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.Home;

namespace Paperbunkr.App.Views.Home;

/// <summary>
/// Home's public layout contract (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 4; design doc sections 8.3
/// and 8.4): the host components a theme's "Home" layout may place, the values it may show and the commands it may trigger. Everything a
/// layout can do on Home is listed here. Removing or re-meaning an entry is a breaking change: bump <see cref="ThemeLayoutContract.Version"/>.
/// </summary>
public static class HomeLayoutContract
{
    public const string Masthead = "Home.Masthead";
    public const string Search = "Home.Search";
    public const string Greeting = "Home.Greeting";
    public const string Sections = "Home.Sections";
    public const string Section = "Home.Section";

    public static ThemeLayoutContract Instance { get; } = new()
    {
        Screen = "Home",
        Version = 1,
        Slots = new[]
        {
            new ThemeLayoutSlot(Masthead, "The whole masthead: backdrop, greeting, search and refresh."),
            new ThemeLayoutSlot(Search, "The library search box and its button, on their own."),
            new ThemeLayoutSlot(Greeting, "The time-of-day greeting, on its own."),
            new ThemeLayoutSlot(Sections, "Every section the user has switched on, in the user's order."),
            new ThemeLayoutSlot(Section, "One section by key. Shows nothing while the user has that section hidden.", KeyProperty: "section", AllowedKeys: HomeSectionKey.Default),
        },
        Bindings = new Dictionary<string, string>
        {
            ["home.greeting"] = "The time-of-day greeting (text).",
            ["home.hasSections"] = "True while at least one section is switched on.",
            ["home.sectionCount"] = "How many sections are showing (number).",
            ["app.name"] = "The app's name (text).",
        },
        Commands = new Dictionary<string, string>
        {
            ["home.refresh"] = "Refresh the library.",
            ["home.search"] = "Search the library for what is typed in the search box.",
            ["home.openLibrary"] = "Open the Library.",
            ["home.openInsights"] = "Open Insights.",
            ["home.customize"] = "Open Preferences at the Home section settings.",
        },

        // Whatever a theme does with Home, the user can still search and still reach their sections.
        RequiredAnyOf = new IReadOnlyList<string>[]
        {
            new[] { Masthead, Search },
            new[] { Sections, Section },
        },
    };

    public static object? GetBinding(HomeScreenViewModel? vm, string name) => name switch
    {
        "home.greeting" => vm?.Greeting ?? string.Empty,
        "home.hasSections" => vm is { AllSectionsHidden: false },
        "home.sectionCount" => vm?.Sections.Count ?? 0,
        "app.name" => "Paperbunkr",
        _ => null,
    };

    public static ICommand? GetCommand(HomeScreenViewModel? vm, string name) => name switch
    {
        "home.refresh" => vm?.RefreshCommand,
        "home.search" => vm?.SearchCommand,
        "home.openLibrary" => vm?.GoToLibraryCommand,
        "home.openInsights" => vm?.OpenInsightsCommand,
        "home.customize" => vm?.OpenHomePreferencesCommand,
        _ => null,
    };

    /// <summary>Builds a new instance of the host component a slot node names. The components bind to the HomeScreenViewModel they inherit.</summary>
    public static Control? CreateSlot(ThemeLayoutNode node) => node.Type switch
    {
        Masthead => new HomeMasthead(),
        Sections => new HomeSections(),
        Section => new HomeSectionSlot { SectionKey = node.Text("section") ?? string.Empty },
        Search => CreateSearch(),
        Greeting => CreateGreeting(),
        _ => null,
    };

    private static Control CreateSearch()
    {
        var box = new TextBox { PlaceholderText = "Search for a comic series...", FontSize = 12.5 };
        box.Classes.Add("searchBox");
        box.Bind(TextBox.TextProperty, new Binding(nameof(HomeScreenViewModel.SearchQuery)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetAutomationId(box, "HomeSearchBox");
        AutomationProperties.SetName(box, "Search the library");

        // Enter in the box commits the search: the box's own key, same as the built-in masthead. (A KeyBinding built in code has no
        // data context to bind its command against, so this is a plain handler.)
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && box.DataContext is HomeScreenViewModel { SearchCommand: { } search } && search.CanExecute(null))
            {
                search.Execute(null);
                e.Handled = true;
            }
        };

        var button = new Button { Content = "Search", Margin = new Thickness(8, 0, 0, 0) };
        button.Classes.Add("primary");
        button.Bind(Button.CommandProperty, new Binding(nameof(HomeScreenViewModel.SearchCommand)));
        AutomationProperties.SetAutomationId(button, "HomeSearchButton");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(button, 1);
        grid.Children.Add(box);
        grid.Children.Add(button);
        return grid;
    }

    private static Control CreateGreeting()
    {
        var greeting = new SplitText { FontSize = 40, HorizontalAlignment = HorizontalAlignment.Left };
        greeting.Bind(SplitText.TextProperty, new Binding(nameof(HomeScreenViewModel.Greeting)));
        AutomationProperties.SetAutomationId(greeting, "HomeGreeting");
        return greeting;
    }
}

/// <summary>
/// One Home section by key (the "Home.Section" host component), using the same per-section templates as the full list. Shows nothing while
/// the user has that section hidden or it has nothing to show - a layout places sections; the user still decides which ones exist.
/// </summary>
public sealed class HomeSectionSlot : ContentControl
{
    public static readonly StyledProperty<string> SectionKeyProperty = AvaloniaProperty.Register<HomeSectionSlot, string>(nameof(SectionKey), string.Empty);

    private HomeScreenViewModel? _viewModel;
    private HomeSections? _templates;

    public HomeSectionSlot()
    {
        ClipToBounds = false;
        ContentTemplate = new HomeSectionTemplateSelector(Lookup);
        DataContextChanged += (_, _) => Attach(DataContext as HomeScreenViewModel);
    }

    public string SectionKey
    {
        get => GetValue(SectionKeyProperty);
        set => SetValue(SectionKeyProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ContentControl);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SectionKeyProperty)
        {
            Update();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Attach(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Attach(DataContext as HomeScreenViewModel);
    }

    /// <summary>The section templates live in HomeSections' resources; one unshown instance is kept only to look them up.</summary>
    private IDataTemplate? Lookup(string resourceKey) => (_templates ??= new HomeSections()).Lookup(resourceKey);

    private void Attach(HomeScreenViewModel? vm)
    {
        if (ReferenceEquals(vm, _viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            ((INotifyCollectionChanged)_viewModel.Sections).CollectionChanged -= OnSectionsChanged;
        }

        _viewModel = vm;
        if (_viewModel is not null)
        {
            ((INotifyCollectionChanged)_viewModel.Sections).CollectionChanged += OnSectionsChanged;
        }

        Update();
    }

    private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void Update()
    {
        HomeSectionViewModel? section = _viewModel?.Sections.FirstOrDefault(s => string.Equals(s.Key, SectionKey, StringComparison.Ordinal));
        Content = section;
        IsVisible = section is not null;
    }
}
