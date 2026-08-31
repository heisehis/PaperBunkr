using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Layout;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class Breadcrumb : UserControl
{
    public Breadcrumb()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebind();
        Rebind();
    }

    private MainViewModel? _boundViewModel;

    private void Rebind()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _boundViewModel = DataContext as MainViewModel;
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Rebuild();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.BreadcrumbTrail) or nameof(MainViewModel.RootScreenLabel))
        {
            Rebuild();
        }
    }

    /// <summary>Rebuilds the segment row from scratch - cheap enough (a handful of controls, only on
    /// an actual navigation) that a diffing update isn't worth the complexity.</summary>
    private void Rebuild()
    {
        var segments = this.FindControl<StackPanel>("Segments");
        if (segments is null)
        {
            return;
        }

        segments.Children.Clear();

        if (_boundViewModel is not MainViewModel vm)
        {
            return;
        }

        var trail = vm.BreadcrumbTrail;
        bool rootIsCurrent = trail.Count == 0;
        segments.Children.Add(MakeSegment(vm.RootScreenLabel, -1, isCurrent: rootIsCurrent, vm));

        for (int i = 0; i < trail.Count; i++)
        {
            segments.Children.Add(new TextBlock { Classes = { "breadcrumbSeparator" }, Text = "›" });
            bool isCurrent = i == trail.Count - 1;
            segments.Children.Add(MakeSegment(trail[i].Label, i, isCurrent, vm));
        }
    }

    private static Button MakeSegment(string label, int index, bool isCurrent, MainViewModel vm)
    {
        var button = new Button
        {
            Classes = { "breadcrumbSegment" },
            Content = label,
            VerticalAlignment = VerticalAlignment.Center,
            Command = vm.NavigateToBreadcrumbIndexCommand,
            CommandParameter = index,
        };

        if (isCurrent)
        {
            button.Classes.Add("current");
        }

        return button;
    }
}
