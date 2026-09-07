using System.Threading.Tasks;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Services;

/// <summary>Thin wrapper over the shared <see cref="ConfirmDialogViewModel"/> instance mounted in
/// <c>MainWindow.axaml</c> - see <see cref="IDialogService"/> for the contract.</summary>
public sealed class DialogService : IDialogService
{
    private readonly ConfirmDialogViewModel _viewModel;

    public DialogService(ConfirmDialogViewModel viewModel) => _viewModel = viewModel;

    public Task<int> ShowAsync(ConfirmDialogRequest request) => _viewModel.ShowAsync(request);

    public async Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
        string cancelLabel = "Cancel", bool isDestructive = false)
    {
        int answer = await ShowAsync(new ConfirmDialogRequest(message, title, PrimaryLabel: confirmLabel,
            SecondaryLabel: cancelLabel, IsDestructive: isDestructive));
        return answer == 0;
    }
}
