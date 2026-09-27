using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class LegalDocumentViewerOverlay : UserControl
{
    private PreferencesScreenViewModel? _hooked;

    public LegalDocumentViewerOverlay() => InitializeComponent();

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_hooked is not null)
        {
            _hooked.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _hooked = DataContext as PreferencesScreenViewModel;
        if (_hooked is not null)
        {
            _hooked.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // A newly opened document (from a row, or a link inside another document) starts at its top, not wherever the last one was left.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreferencesScreenViewModel.SelectedLegalDocumentBlocks))
        {
            this.FindControl<ScrollViewer>("BodyScroller")!.Offset = new Vector(0, 0);
        }
    }
}
