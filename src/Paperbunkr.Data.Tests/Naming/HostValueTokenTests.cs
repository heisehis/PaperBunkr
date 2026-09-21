using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Naming;

namespace Paperbunkr.Data.Tests.Naming;

public class HostValueTokenTests
{
    private static Issue Sample() => new() { Series = new Series { Name = "Spawn" }, Number = "7" };

    [Fact]
    public void VolumeYearAndFilename_ComeFromTheHostSuppliedValues()
    {
        var context = new TemplateContext
        {
            Extra = new Dictionary<string, string?> { ["volumeyear"] = "1992", ["filename"] = "Spawn 007 (1992)" },
        };

        Assert.Equal("Spawn (1992)", TemplateEvaluator.Evaluate("{<series>}{ (<volumeyear>)}", Sample(), context));
        Assert.Equal("Spawn 007 (1992)", TemplateEvaluator.Evaluate("{<filename>}", Sample(), context));
    }

    [Fact]
    public void AMissingHostValue_DropsItsWholeGroup_LikeAnyEmptyField()
    {
        Assert.Equal("Spawn", TemplateEvaluator.Evaluate("{<series>}{ (<volumeyear>)}", Sample(), new TemplateContext()));
        Assert.Equal("Spawn", TemplateEvaluator.Evaluate("{<series>}{ (<volumeyear>)}", Sample()));
    }
}
