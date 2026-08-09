namespace Paperbunkr.Data.Entities;

/// <summary>
/// The four base-matcher families a <see cref="SmartListField"/> belongs to, mirroring CE's
/// <c>ComicBookStringMatcher</c>/<c>NumericMatcher</c>/<c>YesNoMatcher</c>/<c>DateMatcher</c>
/// split exactly. <c>Toggle</c> collapses CE's tri-state <c>YesNo</c> (Yes/No/Unknown) to a plain
/// two-state bool, matching how Paperbunkr's schema already models these facts (e.g.
/// <see cref="Series.IsComplete"/>, <see cref="Issue.FileIsMissing"/>) rather than porting the
/// <c>YesNo</c> enum. Determines which <see cref="SmartListOperator"/> values a field's
/// rule-builder row offers.
/// </summary>
public enum SmartListDataType
{
    Text,
    Number,
    Toggle,
    Date,
}
