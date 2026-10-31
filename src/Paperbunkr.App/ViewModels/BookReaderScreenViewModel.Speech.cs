using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services.Speech;

namespace Paperbunkr.App.ViewModels;

// Read aloud (docs/superpowers/specs/2026-10-27-books-slice4d-read-aloud-design.md): the Windows voices read the chapter one paragraph at a time from the
// block at the top of the page, the page highlights and scrolls to the block being spoken, and it stops at the chapter end. The speaking pill and the
// highlight live in the page (the WebView paints above Avalonia); this class holds the state and drives the engine.
public partial class BookReaderScreenViewModel
{
    public const int MinSpeechRate = -5;
    public const int MaxSpeechRate = 5;

    private ISpeechEngine? _speech;
    private bool _speechCreated;
    private bool _seedingSpeech;
    private IReadOnlyList<SpeechBlock> _speechPlan = Array.Empty<SpeechBlock>();
    private int _speechIndex;

    /// <summary>Test seam: creates the speech engine (null when this machine has no voices). Called once, on first use.</summary>
    internal Func<ISpeechEngine?> SpeechFactory { get; set; } = () => OperatingSystem.IsWindows() ? CreateWindowsEngine() : null;

    private static ISpeechEngine? CreateWindowsEngine() => SystemSpeechEngine.TryCreate();

    /// <summary>Raised with the block being spoken (null when speech ends); the page highlights it and scrolls it into view.</summary>
    public event Action<string?>? SpeakingBlockChanged;

    [ObservableProperty]
    private bool _isSpeaking;

    [ObservableProperty]
    private bool _isSpeakingPaused;

    [ObservableProperty]
    private int _speechRate;

    [ObservableProperty]
    private string? _speechVoice;

    /// <summary>False when this machine has no speech voices: the read-aloud controls are hidden.</summary>
    [ObservableProperty]
    private bool _hasSpeech;

    public IReadOnlyList<string> SpeechVoices { get; private set; } = Array.Empty<string>();

    private ISpeechEngine? Engine()
    {
        if (!_speechCreated)
        {
            _speechCreated = true;
            _speech = SpeechFactory();
            if (_speech is not null)
            {
                _speech.Completed += cancelled => Dispatcher.UIThread.Post(() => OnBlockSpoken(cancelled));
            }
        }

        return _speech;
    }

    /// <summary>True when a speech engine can be created on this machine (creates it on first call); the T shortcut declines when there is none.</summary>
    public bool HasSpeechEngine() => Engine() is not null;

    /// <summary>Called when the font sheet opens: finds out whether voices exist, which hides or shows the read-aloud card.</summary>
    private void RefreshSpeechAvailability()
    {
        var engine = Engine();
        SpeechVoices = engine?.Voices ?? Array.Empty<string>();
        HasSpeech = engine is not null;
        OnPropertyChanged(nameof(SpeechVoices));
    }

    private void SeedSpeech(Paperbunkr.Data.Entities.AppSettings appSettings)
    {
        StopSpeaking();
        _seedingSpeech = true;
        SpeechRate = Math.Clamp(appSettings.BookReaderSpeechRate, MinSpeechRate, MaxSpeechRate);
        SpeechVoice = appSettings.BookReaderSpeechVoice;
        _seedingSpeech = false;
    }

    partial void OnSpeechRateChanged(int value)
    {
        int clamped = Math.Clamp(value, MinSpeechRate, MaxSpeechRate);
        if (clamped != value)
        {
            SpeechRate = clamped;
            return;
        }

        if (!_seedingSpeech)
        {
            SaveAidSetting(a => a.BookReaderSpeechRate = clamped);
        }
    }

    partial void OnSpeechVoiceChanged(string? value)
    {
        if (!_seedingSpeech)
        {
            SaveAidSetting(a => a.BookReaderSpeechVoice = string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    partial void OnCurrentChapterHtmlChanged(string? value) => StopSpeaking();

    /// <summary>The font sheet's switch: on starts reading from the top of the page, off stops.</summary>
    partial void OnIsSpeakingChanged(bool value)
    {
        // Switched from outside StartSpeaking / StopSpeaking (the sheet's switch is bound to this property).
        if (value && _speechPlan.Count == 0)
        {
            StartSpeaking();
        }
        else if (!value && _speechPlan.Count > 0)
        {
            StopSpeaking();
        }
    }

    [RelayCommand]
    private void ToggleSpeak()
    {
        if (IsSpeaking)
        {
            StopSpeaking();
        }
        else
        {
            StartSpeaking();
        }
    }

    private void StartSpeaking()
    {
        var engine = Engine();
        if (engine is null || _source is null)
        {
            IsSpeaking = false;
            return;
        }

        var plan = BookReadAloudPlan.Build(CurrentChapterHtml, _position.BlockId);
        if (plan.Count == 0)
        {
            IsSpeaking = false;
            return;
        }

        _speechPlan = plan;
        _speechIndex = 0;
        engine.VoiceName = SpeechVoice;
        engine.Rate = SpeechRate * 2;
        IsSpeakingPaused = false;
        IsSpeaking = true;
        SpeakCurrentBlock();
    }

    private void SpeakCurrentBlock()
    {
        var block = _speechPlan[_speechIndex];
        _activity.NoteInput(); // listening counts as reading; the idle cut-off still ends a session nobody is at
        SpeakingBlockChanged?.Invoke(block.BlockId);
        _speech!.Speak(block.Text);
    }

    /// <summary>An utterance ended: on to the next block, or stop at the end of the chapter. A cancelled one (from <c>Stop</c>) is ignored.</summary>
    internal void OnBlockSpoken(bool cancelled)
    {
        if (cancelled || !IsSpeaking || IsSpeakingPaused)
        {
            return;
        }

        _speechIndex++;
        if (_speechIndex >= _speechPlan.Count)
        {
            StopSpeaking();
        }
        else
        {
            SpeakCurrentBlock();
        }
    }

    /// <summary>Stops reading (a jump, a page turn, another chapter, closing the book); safe to call when nothing is playing.</summary>
    private void StopSpeaking()
    {
        bool was = IsSpeaking || IsSpeakingPaused || _speechPlan.Count > 0;
        _speechPlan = Array.Empty<SpeechBlock>();
        _speechIndex = 0;
        IsSpeakingPaused = false;
        IsSpeaking = false;
        if (was)
        {
            _speech?.Stop();
            SpeakingBlockChanged?.Invoke(null);
        }
    }

    [RelayCommand]
    private void ToggleSpeakPause()
    {
        if (!IsSpeaking)
        {
            return;
        }

        if (IsSpeakingPaused)
        {
            _speech?.Resume();
            IsSpeakingPaused = false;
        }
        else
        {
            _speech?.Pause();
            IsSpeakingPaused = true;
        }

        _activity.NoteInput();
    }

    [RelayCommand]
    private void SpeakFaster() => ChangeSpeechRate(+1);

    [RelayCommand]
    private void SpeakSlower() => ChangeSpeechRate(-1);

    private void ChangeSpeechRate(int delta)
    {
        SpeechRate += delta;
        if (_speech is not null && IsSpeaking)
        {
            _speech.Rate = SpeechRate * 2; // takes effect from the next paragraph
        }

        _activity.NoteInput();
    }
}
