using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.App.ViewModels;

public partial class MetronContinuitySeriesRowViewModel : ObservableObject
{
    public MetronContinuitySeriesRowViewModel(MetronUniverseSeries series, int? localSeriesId, bool alreadyInContinuity = false, bool hasIdentityCollision = false, string? identityMatch = null)
    {
        Series = series;
        LocalSeriesId = localSeriesId;
        HasIdentityCollision = hasIdentityCollision;
        IdentityMatch = identityMatch;
        _alreadyInContinuity = alreadyInContinuity;
    }

    public MetronUniverseSeries Series { get; }
    public int? LocalSeriesId { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd), nameof(LibraryStatus))]
    private bool _alreadyInContinuity;
    public bool HasIdentityCollision { get; }
    public string? IdentityMatch { get; }
    public bool HasExactLibraryMatch => LocalSeriesId.HasValue && !HasIdentityCollision;
    public bool CanAdd => HasExactLibraryMatch && !AlreadyInContinuity;
    public string Name => Series.Name;
    public string Details
    {
        get
        {
            var ids = new List<string> { $"Metron #{Series.Id}" };
            if (Series.ComicVineId is int cvId) ids.Add($"ComicVine #{cvId}");
            if (Series.GcdId is int gcdId) ids.Add($"GCD #{gcdId}");
            return string.IsNullOrWhiteSpace(Series.StartYear) ? string.Join(" · ", ids) : $"{Series.StartYear} · {string.Join(" · ", ids)}";
        }
    }
    public string LibraryStatus => HasIdentityCollision ? "Conflicting or duplicate provider IDs"
        : AlreadyInContinuity ? "Already in this continuity"
        : HasExactLibraryMatch ? IdentityMatch ?? "Exact library match"
        : "Not found in library";

    [ObservableProperty]
    private bool _isSelected;
}
