using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Wires a Detail screen view (comic / manga / book) to the per-series accent (docs/superpowers/specs/2026-09-21-cosmetics-
/// pitch-2-design.md #9): re-applies <see cref="DetailAccentScope"/> whenever the screen's cover changes, its data context is
/// swapped, or the Appearance toggle flips. Attach once from the view's constructor; the static-event subscription only lives
/// while the view is in the visual tree.
/// </summary>
public static class DetailCosmetics
{
    public static void Attach(UserControl view)
    {
        INotifyPropertyChanged? observed = null;

        void Reapply()
        {
            var cover = (view.DataContext as IDetailHeaderSource)?.CoverImage;
            DetailAccentScope.Apply(view, cover, CosmeticThumbnailSettings.SeriesAccentColor);
        }

        void OnVmChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(IDetailHeaderSource.CoverImage))
            {
                Reapply();
            }
        }

        view.DataContextChanged += (_, _) =>
        {
            if (observed is not null)
            {
                observed.PropertyChanged -= OnVmChanged;
            }

            observed = view.DataContext as INotifyPropertyChanged;
            if (observed is not null)
            {
                observed.PropertyChanged += OnVmChanged;
            }

            Reapply();
        };
        view.AttachedToVisualTree += (_, _) =>
        {
            CosmeticThumbnailSettings.OverlaySettingsChanged += Reapply;
            Reapply();
        };
        view.DetachedFromVisualTree += (_, _) => CosmeticThumbnailSettings.OverlaySettingsChanged -= Reapply;
    }
}
