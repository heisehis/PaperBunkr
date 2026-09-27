using System.Collections.Generic;
using System.Windows.Input;
using Avalonia.Input;

namespace Paperbunkr.App.Models;

/// <summary>A set of key gestures and the command they run, for reader commands that need no canvas state of their own (info panel, pin, clip): <c>PageCanvas.ExtraKeyBindings</c> checks these in order.</summary>
public sealed record KeyCommandBinding(IReadOnlyList<KeyGesture> Gestures, ICommand Command);
