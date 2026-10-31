using System.Text;
using System.Text.Json;
using Paperbunkr.App.Scraper.Import;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Organizing;
using Paperbunkr.Plugins.Automation;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="LopProfileReader"/> against the real profile files a Library Organizer user supplied (<c>TestData\Lop</c>) and small inline documents
/// (docs/superpowers/specs/2026-10-04-lop-profile-import-design.md).
/// </summary>
public class LopProfileReaderTests
{
    private static LopReadResult ReadFile(string name)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestData", "Lop", name));
        return LopProfileReader.Read(stream);
    }

    private static LopReadResult ReadXml(string xml) => LopProfileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static ImportedProfile Only(LopReadResult result)
    {
        Assert.Null(result.FileError);
        Assert.Empty(result.Rejected);
        return Assert.Single(result.Profiles);
    }

    /// <summary>A minimal <c>&lt;Profile&gt;</c> with the given extra child XML.</summary>
    private static string Profile(string extra = "", string name = "P", string mode = "Move", string folder = "{&lt;publisher&gt;}", string file = "{&lt;series&gt;}") =>
        $"<Profile Name=\"{name}\" Version=\"2.1\"><FolderTemplate>{folder}</FolderTemplate><FileTemplate>{file}</FileTemplate><BaseFolder>C:\\Comics</BaseFolder><Mode>{mode}</Mode>{extra}</Profile>";

    // --- The supplied files ---

    [Theory]
    [InlineData("01 - Marvel-DC - Main-Limited-B&W-Annual.lop")]
    [InlineData("02 - Marvel-DC - Director-TPB-One-Shot.lop")]
    [InlineData("03 - Marvel-DC - Alternate Universes.lop")]
    [InlineData("04 - Marvel-DC - Universes.lop")]
    [InlineData("05 - Publishers.lop")]
    [InlineData("Events.lop")]
    [InlineData("v2.1.8 marvel-dc 2.lop")]
    [InlineData("v2.1.8 marvel-dc non universe.lop")]
    [InlineData("v2.1.8 publishers.lop")]
    public void EverySuppliedFile_Reads_AndNeverRunsByItself(string file)
    {
        var imported = Only(ReadFile(file));

        Assert.False(imported.Profile.UseForScheduledRun);
        Assert.NotEmpty(imported.Profile.Name);
        Assert.NotEmpty(imported.Profile.FileTemplate);
    }

    [Fact]
    public void TheMainProfile_KeepsItsTemplatesNameModeAndBaseFolder()
    {
        var profile = Only(ReadFile("01 - Marvel-DC - Main-Limited-B&W-Annual.lop")).Profile;

        Assert.Equal("01 - Marvel/DC - Main/Limited/B&W/Annual", profile.Name);
        Assert.Equal(@"{<publisher>}\{ <Custom(First Series)>}\{ <seriesgroup>}\{ <Custom(Second Series)>}\{ <maincharacter>}\{ <format>}\{ (<volume0>)} { <series>}\{ <Custom(Volume Number)>}", profile.FolderTemplate);
        Assert.Equal("{<series>} - {<number3>} {(<volume>)}", profile.FileTemplate);
        Assert.Equal(@"D:\Books\ComicBooks\", profile.BaseFolder);
        Assert.Equal(OrganizerMode.Move, profile.Mode);
        Assert.True(profile.UseFolder);
        Assert.True(profile.UseFileName);
        Assert.True(profile.RemoveEmptyFolders);
        Assert.Empty(profile.MonthNames);
    }

    [Fact]
    public void AMoveProfile_SaysToCheckTheBaseFolder()
    {
        var imported = Only(ReadFile("05 - Publishers.lop"));

        Assert.Contains(imported.Notes, n => n.Contains("Check the base folder") && n.Contains(@"D:\Books\ComicBooks\"));
    }

    [Fact]
    public void ACopyProfile_IsACopy_WithNoBaseFolderWarning_AndNoLibraryAddNote()
    {
        var imported = Only(ReadFile("Events.lop"));

        Assert.Equal(OrganizerMode.Copy, imported.Profile.Mode);
        Assert.DoesNotContain(imported.Notes, n => n.Contains("base folder"));
        Assert.DoesNotContain(imported.Notes, n => n.Contains("added to"));
        Assert.Null(imported.Profile.ExcludeRuleJson);
    }

    [Fact]
    public void AnOnlyProfile_StoresTheInvertedRules()
    {
        // 01: ExcludeMode Only, Operator All, three groups (Any / All / Any).
        var profile = Only(ReadFile("01 - Marvel-DC - Main-Limited-B&W-Annual.lop")).Profile;

        var group = JsonSerializer.Deserialize<PluginConditionGroup>(profile.ExcludeRuleJson!)!;

        // Only + All: the books to skip are those failing any group, so the root flips to Or and each group flips too.
        Assert.Equal(SmartListGroupMode.Or, group.Mode);
        Assert.Equal(3, group.ChildGroups.Count);
        Assert.Equal(SmartListGroupMode.And, group.ChildGroups[0].Mode);
        Assert.Equal(4, group.ChildGroups[0].Conditions.Count);
        Assert.All(group.ChildGroups[0].Conditions, c => Assert.True(c.Not));
        Assert.All(group.ChildGroups[0].Conditions, c => Assert.False(c.IgnoreCase));
    }

    [Theory]
    [InlineData("02 - Marvel-DC - Director-TPB-One-Shot.lop")]
    [InlineData("03 - Marvel-DC - Alternate Universes.lop")]
    [InlineData("04 - Marvel-DC - Universes.lop")]
    [InlineData("05 - Publishers.lop")]
    public void EveryOnlyProfile_HasStoredRules(string file)
    {
        Assert.NotNull(Only(ReadFile(file)).Profile.ExcludeRuleJson);
    }

    [Theory]
    [InlineData("v2.1.8 marvel-dc 2.lop")]
    [InlineData("v2.1.8 marvel-dc non universe.lop")]
    [InlineData("v2.1.8 publishers.lop")]
    public void ADoNotProfileWithNoRules_StoresNone(string file)
    {
        Assert.Null(Only(ReadFile(file)).Profile.ExcludeRuleJson);
    }

    // --- File level ---

    [Fact]
    public void AProfilesFile_ReadsEveryProfileInIt()
    {
        var result = ReadXml($"<Profiles LastUsed=\"A\">{Profile(name: "A")}{Profile(name: "B")}</Profiles>");

        Assert.Equal(["A", "B"], result.Profiles.Select(p => p.Profile.Name));
    }

    [Fact]
    public void APre20File_IsConvertedIntoAProfile()
    {
        var result = ReadXml("<Settings><Setting Name=\"Old\" /></Settings>");

        Assert.Null(result.FileError);
        Assert.Equal("Old", Assert.Single(result.Profiles).Profile.Name);
    }

    [Fact]
    public void APre20Setting_ConvertsModeAndLegacyEmptyDataNames_AndReportsOldVersionLimit()
    {
        var imported = Only(ReadXml("<Settings Version=\"1.6\"><Setting Name=\"Legacy\"><Mode>Test</Mode><EmptyData><Item Name=\"Series\" Value=\"Unknown\" /></EmptyData></Setting></Settings>"));

        Assert.Equal(OrganizerMode.Simulate, imported.Profile.Mode);
        Assert.Equal("Unknown", imported.Profile.EmptyData["ShadowSeries"]);
        Assert.Contains(imported.Notes, n => n.Contains("1.6 or earlier"));
    }

    [Theory]
    [InlineData("this is not xml")]
    [InlineData("<Something />")]
    public void NotAProfileFile_IsAFileError(string content)
    {
        var result = ReadXml(content);

        Assert.NotNull(result.FileError);
        Assert.Empty(result.Profiles);
    }

    // --- Profiles that are refused ---

    [Fact]
    public void AProfileWithAnUntranslatableRule_IsRefused_AndTheOthersStillImport()
    {
        string bad = Profile(
            "<ExcludeRules Operator=\"All\" ExcludeMode=\"Only\"><ExcludeRule Field=\"Start Year\" Operator=\"is\" Value=\"2000\" /></ExcludeRules>",
            name: "Bad");

        var result = ReadXml($"<Profiles>{bad}{Profile(name: "Good")}</Profiles>");

        Assert.Equal("Good", Assert.Single(result.Profiles).Profile.Name);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal("Bad", rejected.Name);
        Assert.Contains("Start Year", rejected.Reason);
    }

    [Fact]
    public void AProfileWithoutAName_IsRefused()
    {
        var result = ReadXml("<Profile Version=\"2.1\"><FileTemplate>{&lt;series&gt;}</FileTemplate></Profile>");

        Assert.Empty(result.Profiles);
        Assert.Contains("no name", Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void AnUnknownMode_IsRefused()
    {
        var result = ReadXml(Profile(mode: "Teleport"));

        Assert.Contains("Teleport", Assert.Single(result.Rejected).Reason);
    }

    // --- Settings that are copied ---

    [Fact]
    public void TheListAndDictionarySettings_AreCopied()
    {
        var profile = Only(ReadXml(Profile(
            "<EmptyFolder>Unknown</EmptyFolder>" +
            "<EmptyData><Item Name=\"publisher\" Value=\"No Publisher\" /></EmptyData>" +
            "<FailEmptyValues>true</FailEmptyValues><FailedFields><Item>publisher</Item><Item>series</Item></FailedFields>" +
            "<ExcludeFolders><Item>\\Keep</Item></ExcludeFolders>" +
            "<UseFolder>false</UseFolder><UseFileName>false</UseFileName><RemoveEmptyFolder>false</RemoveEmptyFolder>"))).Profile;

        Assert.Equal("Unknown", profile.EmptyFolder);
        Assert.Equal("No Publisher", profile.EmptyData["publisher"]);
        Assert.True(profile.FailEmptyValues);
        Assert.Equal(["publisher", "series"], profile.FailedFields);
        Assert.Equal([@"\Keep"], profile.ExcludeFolders);
        Assert.False(profile.UseFolder);
        Assert.False(profile.UseFileName);
        Assert.False(profile.RemoveEmptyFolders);
    }

    [Fact]
    public void TheStandardMonthNames_AreNotStored_ButACustomOneIs()
    {
        string standard = "<Months><Item Name=\"1\" Value=\"January\" /><Item Name=\"2\" Value=\"February\" /></Months>";
        string custom = "<Months><Item Name=\"1\" Value=\"Jan\" /><Item Name=\"2\" Value=\"February\" /></Months>";

        Assert.Empty(Only(ReadXml(Profile(standard))).Profile.MonthNames);
        Assert.Equal("Jan", Only(ReadXml(Profile(custom))).Profile.MonthNames[1]);
    }

    [Fact]
    public void ALegacyRuleWithATextAttribute_IsRead()
    {
        var profile = Only(ReadXml(Profile(
            "<ExcludeRules Operator=\"Any\" ExcludeMode=\"Do not\"><ExcludeRule Field=\"Format\" Operator=\"is\" Text=\"TPB\" /></ExcludeRules>"))).Profile;

        var group = JsonSerializer.Deserialize<PluginConditionGroup>(profile.ExcludeRuleJson!)!;
        Assert.Equal("TPB", Assert.Single(group.Conditions).Value);
    }

    // --- Notes for what the profile model does not have ---

    [Fact]
    public void ACleanCopyProfile_HasNoNotes()
    {
        var imported = Only(ReadXml(Profile(mode: "Copy", extra: "<CopyMode>false</CopyMode>")));

        Assert.Empty(imported.Notes);
    }

    [Fact]
    public void TheStandardIllegalCharacterTable_NeedsNoNote_ACustomOneDoes()
    {
        string standard = "<IllegalCharacters><Item Name=\":\" Value=\" - \" /><Item Name=\"?\" Value=\"\" /></IllegalCharacters>";
        string custom = "<IllegalCharacters><Item Name=\":\" Value=\"_\" /></IllegalCharacters>";

        Assert.DoesNotContain(Only(ReadXml(Profile(mode: "Copy", extra: standard))).Notes, n => n.Contains("illegal"));
        Assert.Contains(Only(ReadXml(Profile(mode: "Copy", extra: custom))).Notes, n => n.Contains("illegal"));
    }

    [Theory]
    [InlineData("<Prefix><Item Name=\"series\" Value=\"[\" /></Prefix>", "'Prefix'")]
    [InlineData("<Postfix><Item Name=\"series\" Value=\"]\" /></Postfix>", "'Postfix'")]
    [InlineData("<Seperator><Item Name=\"writer\" Value=\"; \" /></Seperator>", "'Separator'")]
    [InlineData("<TextBox><Item Name=\"x\" Value=\"y\" /></TextBox>", "'TextBox'")]
    [InlineData("<ExcludedEmptyFolder><Item>C:\\Keep</Item></ExcludedEmptyFolder>", "keep when they are empty")]
    [InlineData("<MoveFileless>true</MoveFileless>", "no file")]
    [InlineData("<MoveFailed>true</MoveFailed>", "could not name")]
    [InlineData("<AutoSpaceFields>false</AutoSpaceFields>", "spacing")]
    [InlineData("<ReplaceMultipleSpaces>false</ReplaceMultipleSpaces>", "spaces")]
    public void ASettingTheModelLacks_IsReportedWhenTheFileUsesIt(string extra, string expectedFragment)
    {
        var imported = Only(ReadXml(Profile(mode: "Copy", extra: "<CopyMode>false</CopyMode>" + extra)));

        Assert.Contains(imported.Notes, n => n.Contains(expectedFragment));
    }

    [Fact]
    public void ACopyThatAddsToTheLibrary_IsReported()
    {
        var imported = Only(ReadXml(Profile(mode: "Copy", extra: "<CopyMode>true</CopyMode>")));

        Assert.Contains(imported.Notes, n => n.Contains("added to"));
    }

    [Fact]
    public void ATemplateTheEditorWouldRefuse_IsReportedButStillImported()
    {
        var imported = Only(ReadXml(Profile(mode: "Copy", folder: "{&lt;publisher&gt;", file: "{&lt;series&gt;}")));

        Assert.Contains(imported.Notes, n => n.StartsWith("Folder template:"));
    }

    [Fact]
    public void AMoveProfileWithNoBaseFolder_SaysSo()
    {
        var imported = Only(ReadXml("<Profile Name=\"P\"><FolderTemplate></FolderTemplate><FileTemplate>{&lt;series&gt;}</FileTemplate><Mode>Move</Mode></Profile>"));

        Assert.Contains(imported.Notes, n => n.Contains("no base folder set"));
    }
}
