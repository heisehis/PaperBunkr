using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="IssueTagExtensions.MergeFromCategorized"/> (docs/superpowers/specs/
/// 2026-09-18-external-metadata-full-extraction-design.md §4) - pure in-memory, no database
/// needed, same diff-not-replace contract as <see cref="IssueTagExtensions.MergeFrom"/> but with a
/// per-value Category instead of one blanket default.
/// </summary>
public class IssueTagExtensionsTests
{
    [Fact]
    public void MergeFromCategorized_AddsNewValues_WithGivenCategoryAndUnsetWeight()
    {
        var issue = new Issue { Id = 1 };

        issue.MergeFromCategorized(IssueTagField.Tags, new[] { ("Pirates", "Setting"), ("Time Skip", "Theme") });

        Assert.Equal(2, issue.Tags.Count);
        Assert.Contains(issue.Tags, t => t.Value == "Pirates" && t.Category == "Setting" && t.Weight == IssueTagWeight.Unset);
        Assert.Contains(issue.Tags, t => t.Value == "Time Skip" && t.Category == "Theme" && t.Weight == IssueTagWeight.Unset);
    }

    [Fact]
    public void MergeFromCategorized_RemovesValuesNoLongerPresent()
    {
        var issue = new Issue { Id = 1 };
        issue.Tags.Add(new IssueTag { IssueId = 1, Field = IssueTagField.Tags, Value = "Stale", Category = "Uncategorized" });

        issue.MergeFromCategorized(IssueTagField.Tags, new[] { ("Fresh", "Setting") });

        Assert.DoesNotContain(issue.Tags, t => t.Value == "Stale");
        Assert.Contains(issue.Tags, t => t.Value == "Fresh");
    }

    [Fact]
    public void MergeFromCategorized_SurvivingValue_KeepsItsOwnCategoryAndWeight_NotOverwritten()
    {
        var issue = new Issue { Id = 1 };
        issue.Tags.Add(new IssueTag { IssueId = 1, Field = IssueTagField.Tags, Value = "Pirates", Category = "Setting", Weight = IssueTagWeight.Core });

        // Re-import sends the same value with a different category - the existing row must survive untouched.
        issue.MergeFromCategorized(IssueTagField.Tags, new[] { ("Pirates", "SomethingElse") });

        var tag = Assert.Single(issue.Tags);
        Assert.Equal("Setting", tag.Category);
        Assert.Equal(IssueTagWeight.Core, tag.Weight);
    }

    [Fact]
    public void MergeFromCategorized_DoesNotTouchTheOtherField()
    {
        var issue = new Issue { Id = 1 };
        issue.Tags.Add(new IssueTag { IssueId = 1, Field = IssueTagField.Genre, Value = "Action", Category = "Genre" });

        issue.MergeFromCategorized(IssueTagField.Tags, new[] { ("Pirates", "Setting") });

        Assert.Contains(issue.Tags, t => t.Field == IssueTagField.Genre && t.Value == "Action");
        Assert.Contains(issue.Tags, t => t.Field == IssueTagField.Tags && t.Value == "Pirates");
        Assert.Equal(2, issue.Tags.Count);
    }
}
