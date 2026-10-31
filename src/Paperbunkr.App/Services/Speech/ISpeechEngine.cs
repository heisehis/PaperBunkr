using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Speech.Synthesis;

namespace Paperbunkr.App.Services.Speech;

/// <summary>The text-to-speech engine the reader uses (docs/superpowers/specs/2026-10-27-books-slice4d-read-aloud-design.md §2), behind an interface so the reader is tested with a fake.</summary>
public interface ISpeechEngine : IDisposable
{
    /// <summary>Names of the voices installed on this machine.</summary>
    IReadOnlyList<string> Voices { get; }

    /// <summary>The voice to use; an unknown or null name keeps the system default.</summary>
    string? VoiceName { get; set; }

    /// <summary>SAPI speed, -10 (slowest) to +10 (fastest); applied from the next <see cref="Speak"/>.</summary>
    int Rate { get; set; }

    /// <summary>Raised when an utterance ends; true when it was cut short by <see cref="Stop"/> rather than finished.</summary>
    event Action<bool>? Completed;

    void Speak(string text);

    void Pause();

    void Resume();

    void Stop();
}

/// <summary>The Windows speech engine (<c>System.Speech</c>, the voices installed in Windows); the app targets plain net10.0, so this is only created where <see cref="OperatingSystem.IsWindows"/>.</summary>
[SupportedOSPlatform("windows")]
public sealed class SystemSpeechEngine : ISpeechEngine
{
    private readonly SpeechSynthesizer _synth = new();

    public SystemSpeechEngine()
    {
        _synth.SpeakCompleted += (_, e) => Completed?.Invoke(e.Cancelled);
    }

    public static ISpeechEngine? TryCreate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var engine = new SystemSpeechEngine();
            return engine.Voices.Count == 0 ? null : engine;
        }
        catch (Exception)
        {
            return null; // no speech platform on this machine
        }
    }

    public IReadOnlyList<string> Voices =>
        _synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToList();

    public string? VoiceName
    {
        get => _synth.Voice?.Name;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && Voices.Contains(value))
            {
                _synth.SelectVoice(value);
            }
        }
    }

    public int Rate
    {
        get => _synth.Rate;
        set => _synth.Rate = Math.Clamp(value, -10, 10);
    }

    public event Action<bool>? Completed;

    public void Speak(string text) => _synth.SpeakAsync(text);

    public void Pause() => _synth.Pause();

    public void Resume() => _synth.Resume();

    public void Stop() => _synth.SpeakAsyncCancelAll();

    public void Dispose() => _synth.Dispose();
}
