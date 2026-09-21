using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.Acquisition;

public class TemplateUpgradeTests : AcquisitionTestBase
{
    private AcquisitionSettings Legacy(string template)
    {
        var settings = Context.GetOrCreateAcquisitionSettings();
        settings.RenameTemplate = template;
        settings.RenameTemplateGrammar = TemplateGrammar.Import;   // how a row that predates the shared engine looks
        Context.SaveChanges();
        return settings;
    }

    private AcquisitionSettings Reload() => Context.AcquisitionSettings.AsNoTracking().Single();

    [Fact]
    public void ANewInstall_IsAlreadyInTheNewGrammar_AndNothingHappens()
    {
        var settings = Context.GetOrCreateAcquisitionSettings();

        Assert.Equal(TemplateGrammar.Organizer, settings.RenameTemplateGrammar);
        Assert.Equal(AcquisitionSettings.DefaultRenameTemplate, settings.RenameTemplate);
        Assert.Equal(TemplateUpgradeOutcome.NothingToDo, TemplateUpgrade.Run(Context));
        Assert.Null(Reload().RenameTemplateOriginal);
    }

    [Fact]
    public void NoSettingsRowYet_IsANoOp_AndDoesNotCreateOne()
    {
        Assert.Equal(TemplateUpgradeOutcome.NothingToDo, TemplateUpgrade.Run(Context));
        Assert.Empty(Context.AcquisitionSettings);
    }

    [Fact]
    public void TheOldDefault_IsTranslatedToTheNewDefault_AndTheOriginalIsKept()
    {
        Legacy(AcquisitionSettings.LegacyDefaultRenameTemplate);

        Assert.Equal(TemplateUpgradeOutcome.Translated, TemplateUpgrade.Run(Context));

        var saved = Reload();
        Assert.Equal(AcquisitionSettings.DefaultRenameTemplate, saved.RenameTemplate);
        Assert.Equal(AcquisitionSettings.LegacyDefaultRenameTemplate, saved.RenameTemplateOriginal);
        Assert.Equal(TemplateGrammar.Organizer, saved.RenameTemplateGrammar);
        Assert.False(saved.RenameTemplateUpgradeFailed);
    }

    [Fact]
    public void ACustomTemplate_IsTranslated_NotReplaced()
    {
        Legacy("{series} #{number:00}[ - {title}]");

        Assert.Equal(TemplateUpgradeOutcome.Translated, TemplateUpgrade.Run(Context));

        var saved = Reload();
        Assert.Equal("{<series>} #{<number2>}{ - <title>}", saved.RenameTemplate);
        Assert.Equal("{series} #{number:00}[ - {title}]", saved.RenameTemplateOriginal);
    }

    [Fact]
    public void AnUntranslatableTemplate_FallsBackToTheDefault_KeepsTheOriginal_AndSaysSo()
    {
        Legacy("{series}[ {volumeyear} {title}]");           // two tokens in one optional group: not expressible

        Assert.Equal(TemplateUpgradeOutcome.FellBackToDefault, TemplateUpgrade.Run(Context));

        var saved = Reload();
        Assert.Equal(AcquisitionSettings.DefaultRenameTemplate, saved.RenameTemplate);
        Assert.Equal("{series}[ {volumeyear} {title}]", saved.RenameTemplateOriginal);
        Assert.True(saved.RenameTemplateUpgradeFailed);
        Assert.Equal(TemplateGrammar.Organizer, saved.RenameTemplateGrammar);
    }

    [Fact]
    public void RunningItAgain_ChangesNothing()
    {
        Legacy("{series}[ {volumeyear} {title}]");
        TemplateUpgrade.Run(Context);
        var first = Reload();

        Assert.Equal(TemplateUpgradeOutcome.NothingToDo, TemplateUpgrade.Run(Context));

        var second = Reload();
        Assert.Equal(first.RenameTemplate, second.RenameTemplate);
        Assert.Equal(first.RenameTemplateOriginal, second.RenameTemplateOriginal);
    }
}
