using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// A cover that loads itself the first time something reads <see cref="Image"/>. Rows of a virtualized list only bind - and so only read -
/// <see cref="Image"/> while they are realized, which is what keeps a 3000-row Wanted list from downloading 3000 covers: the request
/// happens at bind time, not at construction. A row with no usable URL never loads and <see cref="Image"/> stays null (the view shows a placeholder).
/// </summary>
public sealed class RemoteCoverSource : ObservableObject
{
    private readonly string? _url;
    private Bitmap? _image;
    private bool _requested;

    public RemoteCoverSource(string? url) => _url = RemoteCoverCache.IsFetchable(url) ? url : null;

    public bool HasCover => _url is not null;

    public Bitmap? Image
    {
        get
        {
            if (_image is null && !_requested && _url is not null)
            {
                _requested = true;
                if (RemoteCoverCache.TryGetCached(_url) is { } hit)
                {
                    _image = hit;
                }
                else
                {
                    _ = LoadAsync(_url);
                }
            }

            return _image;
        }
    }

    private async Task LoadAsync(string url)
    {
        var bitmap = await Task.Run(() => RemoteCoverCache.GetAsync(url, CancellationToken.None)).ConfigureAwait(false);
        if (bitmap is null)
        {
            return;
        }

        void Apply()
        {
            _image = bitmap;
            OnPropertyChanged(nameof(Image));
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply();
        }
        else
        {
            Dispatcher.UIThread.Post(Apply);
        }
    }
}
