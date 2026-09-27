using Paperbunkr.Data.Organizing;

namespace Paperbunkr.Data.Tests.Organizing;

public sealed class OrganizerSafetyTests
{
    private static OrganizerProfile Profile(string name, string baseFolder, OrganizerMode mode) => new() { Name = name, BaseFolder = baseFolder, Mode = mode };

    [Theory]
    [InlineData(@"C:\Comics", @"C:\Comics")]                       // the watched folder itself
    [InlineData(@"C:\Comics", @"C:\Comics\Organized")]              // a folder inside it
    [InlineData(@"C:\Comics", @"c:\comics\ORGANIZED\Marvel")]      // case does not matter on Windows paths
    [InlineData(@"C:\Comics\", @"C:\Comics\Copies")]               // a trailing separator does not matter
    public void ACopyIntoAWatchedFolder_IsFlagged(string watched, string baseFolder)
    {
        var problems = OrganizerSafety.CopiesIntoWatchedFolders(new[] { Profile("Backup", baseFolder, OrganizerMode.Copy) }, new[] { watched });

        var problem = Assert.Single(problems);
        Assert.Equal("Backup", problem.Profile.Name);
    }

    [Theory]
    [InlineData(@"C:\Comics", @"D:\Backup")]                       // a different place
    [InlineData(@"C:\Comics", @"C:\ComicsBackup")]                 // a sibling whose name merely starts the same
    [InlineData(@"C:\Comics\Marvel", @"C:\Comics")]                // ABOVE a watched folder: the copies are not inside it
    public void ACopyElsewhere_IsNotFlagged(string watched, string baseFolder)
    {
        Assert.Empty(OrganizerSafety.CopiesIntoWatchedFolders(new[] { Profile("Backup", baseFolder, OrganizerMode.Copy) }, new[] { watched }));
    }

    [Theory]
    [InlineData(OrganizerMode.Move)]
    [InlineData(OrganizerMode.Simulate)]
    public void OnlyCopyModeCreatesNewFiles_SoMoveAndSimulateAreNeverFlagged(OrganizerMode mode)
    {
        Assert.Empty(OrganizerSafety.CopiesIntoWatchedFolders(new[] { Profile("Library", @"C:\Comics\Organized", mode) }, new[] { @"C:\Comics" }));
    }

    [Fact]
    public void TheMessage_NamesTheProfile_TheFolder_AndWhatToDo()
    {
        var problems = OrganizerSafety.CopiesIntoWatchedFolders(new[] { Profile("Backup", @"C:\Comics\Copies", OrganizerMode.Copy) }, new[] { @"C:\Comics" });

        var text = OrganizerSafety.Describe(problems);

        Assert.Contains("\"Backup\"", text);
        Assert.Contains(@"C:\Comics", text);
        Assert.Contains("duplicate", text);
        Assert.Contains("switch the profile to Move", text);
        Assert.Contains("Nothing was copied", text);
    }
}
