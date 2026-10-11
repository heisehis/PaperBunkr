namespace Paperbunkr.App.Services.Input;

/// <summary>Plain-language names for an action's <see cref="InputScope"/> and <see cref="InputContext"/>, for the "where it works" line in Preferences &gt; Keyboard Shortcuts.</summary>
public static class InputScopeDisplay
{
    /// <summary>"comic reader, paged mode", "library", "everywhere": where the action can fire. A scope this table does not know falls back to its own name, so a plugin's scope still reads.</summary>
    public static string Describe(InputScope scope, InputContext context)
    {
        string where = ScopeName(scope);
        string state = ContextName(context);
        return state.Length == 0 ? where : $"{where}, {state}";
    }

    public static string ScopeName(InputScope scope) => scope.Name switch
    {
        "Global" => "everywhere",
        "Reader" => "comic reader",
        "BookReader" => "book reader",
        "PdfReader" => "PDF reader",
        "SmartLists" => "smart lists",
        "ReadingLists" => "reading lists",
        "Detail" => "detail screens",
        "Editor" => "issue editors",
        "Compare" => "compare view",
        "Overlay" => "dialogs and panels",
        _ => scope.Name.ToLowerInvariant(),
    };

    /// <summary>Empty for <see cref="InputContext.Always"/>: an action that works in every state needs no qualifier.</summary>
    public static string ContextName(InputContext context) => context switch
    {
        InputContext.Always or InputContext.None => string.Empty,
        InputContext.Paged => "paged mode",
        InputContext.PagedUnzoomed => "paged mode, page fitted",
        InputContext.PagedZoomed => "paged mode, zoomed in",
        InputContext.Continuous => "continuous scroll",
        _ => "some reading modes",
    };
}
