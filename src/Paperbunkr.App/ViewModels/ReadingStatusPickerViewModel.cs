using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The clickable reading-status setter shared by the detail hero and the detail band
/// (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md Part 2 §C). Owns the
/// single write to <see cref="Series.ReadingStatus"/>; the host screen builds one per loaded
/// series and hands the same instance to both surfaces so they stay in lock-step.
/// </summary>
public partial class ReadingStatusPickerViewModel : ViewModelBase
{
    /// <summary>Menu order = the reading lifecycle, "Not set" first so it reads as the neutral/clear choice.</summary>
    private static readonly ReadingStatus[] MenuOrder =
    {
        ReadingStatus.Unknown, ReadingStatus.Planned, ReadingStatus.Reading, ReadingStatus.Completed,
        ReadingStatus.ReReading, ReadingStatus.Paused, ReadingStatus.Dropped,
    };

    private readonly int _seriesId;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action? _onChanged;

    public ReadingStatusPickerViewModel(int seriesId, Action? onChanged = null)
        : this(seriesId, PaperbunkrDb.CreateContext, onChanged)
    {
    }

    /// <summary>Test seam - production uses the real per-user database.</summary>
    internal ReadingStatusPickerViewModel(int seriesId, Func<PaperbunkrDbContext> contextFactory, Action? onChanged = null)
    {
        _seriesId = seriesId;
        _contextFactory = contextFactory;
        _onChanged = onChanged;

        using var context = _contextFactory();
        _current = context.Series.Find(seriesId)?.ReadingStatus ?? ReadingStatus.Unknown;
        Options = new ObservableCollection<ReadingStatusOption>();
        RebuildOptions();
    }

    [ObservableProperty]
    private ReadingStatus _current;

    public ObservableCollection<ReadingStatusOption> Options { get; }

    /// <summary>Enum name for a <c>BrandMark Family="ReadingStatus"</c> binding, or <see langword="null"/>
    /// for <see cref="ReadingStatus.Unknown"/> (chip renders the "Set status" affordance instead).</summary>
    public string? CurrentValue => Current == ReadingStatus.Unknown ? null : Current.ToString();

    public bool HasStatus => Current != ReadingStatus.Unknown;

    partial void OnCurrentChanged(ReadingStatus value)
    {
        OnPropertyChanged(nameof(CurrentValue));
        OnPropertyChanged(nameof(HasStatus));
        RebuildOptions();
    }

    private void RebuildOptions()
    {
        Options.Clear();
        foreach (var status in MenuOrder)
        {
            var p = ReadingStatusPresentation.For(status);
            string label = status == ReadingStatus.Unknown ? "Not set" : p.Label;
            Symbol glyph = status == ReadingStatus.Unknown ? Symbol.Circle : p.Glyph;
            Options.Add(new ReadingStatusOption(status, label, glyph, IsChecked: status == Current));
        }
    }

    [RelayCommand]
    private void Set(ReadingStatus value)
    {
        if (value == Current)
        {
            return;
        }

        using (var context = _contextFactory())
        {
            var series = context.Series.Find(_seriesId);
            if (series is null)
            {
                return;
            }

            series.ReadingStatus = value;
            context.SaveChanges();
        }

        Current = value;
        _onChanged?.Invoke();
    }
}
