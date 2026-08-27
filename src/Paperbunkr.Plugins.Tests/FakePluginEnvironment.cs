using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.Plugins.Tests;

internal sealed class FakePluginEnvironment : IPluginEnvironment
{
    public IPluginHostWindow MainWindow { get; } = new FakeHostWindow();
    public IApplication App { get; }
    public IOpenBooksManager OpenBooks { get; } = new FakeOpenBooksManager();
    public IBrowser Browser { get; } = new FakeBrowser();
    public IComicDisplay ComicDisplay { get; } = new FakeComicDisplay();
    public IThemePlugin ThemePlugin { get; } = new FakeThemePlugin();
    public string CommandPath { get; set; } = string.Empty;
    public IEnumerable<string> LibraryPaths { get; } = Array.Empty<string>();

    public FakePluginEnvironment(IApplication? app = null)
    {
        App = app ?? new FakeApplication();
    }

    public string Localize(string resourceKey, string elementKey, string text) => text;

    public object Clone() => MemberwiseClone();

    private sealed class FakeHostWindow : IPluginHostWindow
    {
        public object Owner { get; } = new object();
    }

    private sealed class FakeOpenBooksManager : IOpenBooksManager
    {
        public bool Open(Data.Entities.Issue issue, int page) => true;
        public bool OpenFile(string file, int page) => true;
        public bool IsOpen(Data.Entities.Issue issue) => false;
    }

    private sealed class FakeBrowser : IBrowser
    {
        public bool OpenNextComic() => true;
        public bool OpenPrevComic() => true;
        public bool OpenRandomComic() => true;
        public void SelectComics(IEnumerable<Data.Entities.Issue> books) { }
    }

    private sealed class FakeComicDisplay : IComicDisplay
    {
        public Data.Entities.Issue? CurrentBook => null;
        public int CurrentPageIndex => 0;
        public int PageCount => 0;
        public event Action<int>? CurrentPageIndexChanged;
        public void NextPage() { }
        public void PreviousPage() { }
        public void GoToPage(int index) { }
    }

    private sealed class FakeThemePlugin : IThemePlugin
    {
        public string CurrentSkinKey => "default";
    }

    public sealed class FakeApplication : IApplication
    {
        public string ProductVersion => "0.0.0-test";
        public void Restart() { }
        public void ScanFolders() { }
        public List<Data.Entities.Issue> Library { get; set; } = new();
        public IEnumerable<Data.Entities.Issue> GetLibraryBooks() => Library;
        public Data.Entities.Issue? GetBook(int issueId) => null;
        public bool RemoveBook(Data.Entities.Issue issue) => false;
        public bool SetCustomBookThumbnail(Data.Entities.Issue issue, byte[] imageBytes) => false;
        public byte[]? GetComicPage(Data.Entities.Issue issue, int page) => null;
        public byte[]? GetComicThumbnail(Data.Entities.Issue issue) => null;
        public Task<string?> ReadInternetAsync(string url) => Task.FromResult<string?>(null);
        public List<(string Question, string ButtonText, string OptionText)> AskedQuestions { get; } = new();
        public int AskQuestion(string question, string buttonText, string optionText)
        {
            AskedQuestions.Add((question, buttonText, optionText));
            return 0;
        }
        public void ShowComicInfo(IEnumerable<Data.Entities.Issue> books) { }
    }
}
