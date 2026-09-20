using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Naming;

namespace Paperbunkr.Data.Acquisition;

/// <summary>What <see cref="TemplateUpgrade.Run"/> did.</summary>
public enum TemplateUpgradeOutcome
{
    /// <summary>No acquisition settings row yet, or it is already in the Organizer grammar.</summary>
    NothingToDo,

    /// <summary>The stored template was translated exactly.</summary>
    Translated,

    /// <summary>The stored template could not be translated exactly; the default is in use and the original was kept.</summary>
    FellBackToDefault,
}

/// <summary>
/// One-time, idempotent upgrade of the acquisition rename template from the importer's original grammar to the shared Organizer grammar
/// (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md section 5). It is a startup data step, not C# inside an EF migration:
/// migrations must not depend on application code that keeps changing.
/// <para>
/// It only ever touches a row still marked <see cref="TemplateGrammar.Import"/>, so running it on every launch costs one indexed read. The user's
/// original text is always kept in <see cref="AcquisitionSettings.RenameTemplateOriginal"/> until they explicitly save a new template. A template that
/// can't be translated exactly is never approximated: the default is used and <see cref="AcquisitionSettings.RenameTemplateUpgradeFailed"/> tells
/// Preferences to say so.
/// </para>
/// </summary>
public static class TemplateUpgrade
{
    public static TemplateUpgradeOutcome Run(PaperbunkrDbContext context)
    {
        var settings = context.AcquisitionSettings.FirstOrDefault(a => a.Id == 1);
        if (settings is null || settings.RenameTemplateGrammar != TemplateGrammar.Import)
        {
            return TemplateUpgradeOutcome.NothingToDo;
        }

        var original = settings.RenameTemplate;
        var result = NameTemplateTranslator.Translate(original);

        settings.RenameTemplateOriginal = original;
        settings.RenameTemplateGrammar = TemplateGrammar.Organizer;
        settings.RenameTemplateUpgradeFailed = !result.Success;
        settings.RenameTemplate = result.Success ? result.Template! : AcquisitionSettings.DefaultRenameTemplate;
        context.SaveChanges();

        return result.Success ? TemplateUpgradeOutcome.Translated : TemplateUpgradeOutcome.FellBackToDefault;
    }
}
