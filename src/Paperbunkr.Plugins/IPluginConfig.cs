namespace Paperbunkr.Plugins;

/// <summary>Ported from ComicRackCE's <c>IPluginConfig</c> unchanged in shape.</summary>
public interface IPluginConfig
{
    IEnumerable<string> LibraryPaths { get; }
}
