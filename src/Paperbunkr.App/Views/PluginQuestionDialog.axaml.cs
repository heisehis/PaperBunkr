using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Paperbunkr.App.Views;

/// <summary>Backs <c>IApplication.AskQuestion</c> - see the .axaml doc comment for scope.</summary>
public partial class PluginQuestionDialog : Window
{
    /// <summary>0 = primary button clicked, 1 = the optional secondary button clicked.</summary>
    public int Answer { get; private set; }

    public PluginQuestionDialog() : this(string.Empty, "OK", "Cancel")
    {
    }

    public PluginQuestionDialog(string question, string primaryText, string? optionText)
    {
        InitializeComponent();
        QuestionText.Text = question;
        PrimaryButton.Content = primaryText;
        if (string.IsNullOrEmpty(optionText))
        {
            OptionButton.IsVisible = false;
        }
        else
        {
            OptionButton.Content = optionText;
        }
    }

    private void OnPrimaryClick(object? sender, RoutedEventArgs e)
    {
        Answer = 0;
        Close();
    }

    private void OnOptionClick(object? sender, RoutedEventArgs e)
    {
        Answer = 1;
        Close();
    }

    /// <summary>Blocks the calling UI thread until the user answers, same <see cref="DispatcherFrame"/> pattern as <see cref="CrashReportWindow.ShowModal"/> - <c>IApplication.AskQuestion</c> is a synchronous CE-shaped signature, and plugin scripts calling it expect an answer in hand before the call returns.</summary>
    public static int ShowModal(string question, string primaryText, string? optionText)
    {
        var dialog = new PluginQuestionDialog(question, primaryText, optionText);
        var frame = new DispatcherFrame();
        dialog.Closed += (_, _) => frame.Continue = false;
        dialog.Show();
        Dispatcher.UIThread.PushFrame(frame);
        return dialog.Answer;
    }
}
