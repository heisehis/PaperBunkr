using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Dictionary;

namespace Paperbunkr.App.ViewModels;

// Dictionary look-up from the highlight popup (docs/superpowers/specs/2026-10-27-books-slice4b-dictionary-design.md §4): a single selected word
// can be looked up in the offline WordNet file, with an optional online fallback that is off by default.
public partial class BookReaderScreenViewModel
{
    private string _selectedText = string.Empty;
    private bool _seedingLookUpSetting;

    /// <summary>Where the offline dictionary lives; tests point it at a scratch path so a dictionary installed on the machine does not leak in.</summary>
    public DictionaryDataService DictionaryData { get; set; } = new();

    /// <summary>Test seam: the offline lookup (word → result). The default opens the downloaded dictionary file for the call.</summary>
    internal Func<string, DictionaryResult?>? OfflineLookup { get; set; }

    /// <summary>Test seam: the online lookup.</summary>
    internal Func<string, Task<DictionaryResult?>>? OnlineLookup { get; set; }

    /// <summary>Test seam: the dictionary download.</summary>
    internal Func<IProgress<double>, Task>? DownloadDictionary { get; set; }

    public ObservableCollection<DictionaryPosGroup> LookUpGroups { get; } = new();

    /// <summary>True while a brand-new single-word selection is waiting for a colour: the popup offers Look up.</summary>
    [ObservableProperty]
    private bool _canLookUp;

    /// <summary>True while the popup shows the definition card instead of the colours.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotLookingUp))]
    private bool _isLookingUp;

    public bool IsNotLookingUp => !IsLookingUp;

    [ObservableProperty]
    private string _lookUpWord = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLookUpStatus))]
    private string _lookUpStatus = string.Empty;

    public bool HasLookUpStatus => !string.IsNullOrWhiteSpace(LookUpStatus);

    [ObservableProperty]
    private bool _offerDictionaryDownload;

    [ObservableProperty]
    private bool _isDownloadingDictionary;

    [ObservableProperty]
    private double _dictionaryProgress;

    [ObservableProperty]
    private bool _offerOnlineLookUp;

    /// <summary>Global setting: ask dictionaryapi.dev for words the offline file does not have. Off by default.</summary>
    [ObservableProperty]
    private bool _lookUpOnline;

    partial void OnLookUpOnlineChanged(bool value)
    {
        if (!_seedingLookUpSetting)
        {
            SaveAidSetting(a => a.BookReaderLookUpOnline = value);
        }
    }

    partial void OnIsHighlightPopupOpenChanged(bool value)
    {
        if (!value)
        {
            ResetLookUp();
        }
    }

    private void SeedLookUp(Paperbunkr.Data.Entities.AppSettings appSettings)
    {
        _seedingLookUpSetting = true;
        LookUpOnline = appSettings.BookReaderLookUpOnline;
        _seedingLookUpSetting = false;
    }

    private void ResetLookUp()
    {
        IsLookingUp = false;
        LookUpGroups.Clear();
        LookUpStatus = string.Empty;
        OfferDictionaryDownload = false;
        OfferOnlineLookUp = false;
        IsDownloadingDictionary = false;
    }

    /// <summary>Called when a new selection opens the popup: only a single plausible word can be looked up.</summary>
    private void UpdateLookUpState(string selectedText)
    {
        ResetLookUp();
        _selectedText = selectedText;
        CanLookUp = DictionaryLookup.Normalize(selectedText) is not null;
    }

    [RelayCommand]
    private async Task LookUpAsync()
    {
        string? word = DictionaryLookup.Normalize(_selectedText);
        if (word is null)
        {
            return;
        }

        LookUpWord = word;
        LookUpGroups.Clear();
        LookUpStatus = string.Empty;
        OfferDictionaryDownload = false;
        OfferOnlineLookUp = false;
        IsLookingUp = true;

        bool englishOrUnknown = string.IsNullOrWhiteSpace(BookLanguage) || BookLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        if (!englishOrUnknown)
        {
            LookUpStatus = "No offline dictionary for this language.";
            OfferOnlineLookUp = LookUpOnline;
            return;
        }

        var lookup = OfflineLookup;
        if (lookup is null && !DictionaryData.IsInstalled)
        {
            LookUpStatus = "The offline dictionary is not downloaded yet (about 12 MB, from Princeton WordNet).";
            OfferDictionaryDownload = true;
            return;
        }

        DictionaryResult? result;
        try
        {
            result = await Task.Run(() => lookup is not null ? lookup(word) : LookUpInDownloadedFile(word));
        }
        catch (Exception ex)
        {
            LookUpStatus = $"The dictionary could not be read: {ex.Message}";
            return;
        }

        ShowResult(result);
    }

    private DictionaryResult? LookUpInDownloadedFile(string word)
    {
        using var db = DictionaryDatabase.Open(DictionaryData.DatabasePath);
        return db.AsLookup().Lookup(word);
    }

    private void ShowResult(DictionaryResult? result)
    {
        LookUpGroups.Clear();
        if (result is null)
        {
            LookUpStatus = "No definition found.";
            OfferOnlineLookUp = LookUpOnline;
            return;
        }

        LookUpStatus = result.Lemma == result.Word ? string.Empty : $"Form of “{result.Lemma}”";
        foreach (var group in result.Groups)
        {
            LookUpGroups.Add(group);
        }
    }

    [RelayCommand]
    private async Task LookUpOnlineNowAsync()
    {
        if (!LookUpOnline || string.IsNullOrWhiteSpace(LookUpWord))
        {
            return;
        }

        OfferOnlineLookUp = false;
        LookUpStatus = "Looking up online…";
        var online = OnlineLookup ?? (word => new OnlineDictionaryClient().LookupAsync(word));
        ShowResult(await online(LookUpWord));
    }

    [RelayCommand]
    private async Task DownloadDictionaryAsync()
    {
        IsDownloadingDictionary = true;
        OfferDictionaryDownload = false;
        DictionaryProgress = 0;
        LookUpStatus = "Downloading the dictionary…";
        try
        {
            var progress = new Progress<double>(p => DictionaryProgress = p);
            await (DownloadDictionary is { } download ? download(progress) : DictionaryData.DownloadAsync(progress));
        }
        catch (Exception ex)
        {
            IsDownloadingDictionary = false;
            LookUpStatus = $"The download failed: {ex.Message}";
            OfferDictionaryDownload = true;
            return;
        }

        IsDownloadingDictionary = false;
        await LookUpAsync();
    }

    [RelayCommand]
    private void BackFromLookUp() => IsLookingUp = false;

    /// <summary>The Look up shortcut: acts on the selection the popup is open for and declines when there is none.</summary>
    public bool TryLookUpSelection()
    {
        if (!IsHighlightPopupOpen || !CanLookUp)
        {
            return false;
        }

        _ = LookUpAsync();
        return true;
    }
}
