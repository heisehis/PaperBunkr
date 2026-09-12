namespace ClusterLibraryManager.Templating;

/// <summary>How a parsed `{prefix&lt;name(args)&gt;postfix}` group behaves once evaluated (design
/// doc §5) - determined by whether `name` carries a leading `?` (conditional) or `!` (inversion)
/// marker, stripped before field lookup.</summary>
public enum TokenGroupKind
{
    Normal,
    Conditional,
    Inversion,
}

/// <summary>One parsed piece of a naming template - either verbatim text or a token group. A
/// template is just an ordered list of these; there's no real nesting to represent (CE's own grammar
/// doesn't nest braces - see <see cref="TemplateParser"/>'s doc comment), so a flat list is exactly as
/// expressive as CE's actual grammar, not a simplification of it.</summary>
public abstract record TemplateNode;

public sealed record LiteralNode(string Text) : TemplateNode;

public sealed record TokenNode(string Prefix, string Name, string Args, string Postfix, TokenGroupKind Kind) : TemplateNode;
