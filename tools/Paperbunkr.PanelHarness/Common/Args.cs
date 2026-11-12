namespace Paperbunkr.PanelHarness.Common;

/// <summary>Reads <c>--name value</c> pairs and <c>--flag</c> switches from the command line.</summary>
internal sealed class Args(string[] args)
{
    public string[] Raw { get; } = args;

    public bool Has(string name) => Array.IndexOf(Raw, name) >= 0;

    public string Str(string name, string fallback)
    {
        int i = Array.IndexOf(Raw, name);
        return i >= 0 && i + 1 < Raw.Length ? Raw[i + 1] : fallback;
    }

    public int Int(string name, int fallback) => int.TryParse(Str(name, ""), out var v) ? v : fallback;

    /// <summary>The value after <paramref name="name"/>, or an error message and null when it is missing.</summary>
    public string? Required(string name)
    {
        string value = Str(name, "");
        if (value.Length == 0 || value.StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"{name} needs a value");
            return null;
        }

        return value;
    }
}
