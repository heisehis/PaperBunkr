using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises every derived value in <see cref="IssueMetadataExtensions"/> (docs/superpowers/specs/
/// 2026-08-17-metadata-model-phase1-canonical-metadata-design.md) - all resolved dynamically, none
/// stored. <see cref="NumberType_ClassifiesExamples"/> covers the exact example set from this
/// phase's design doc §71, taken verbatim.
/// </summary>
public class IssueMetadataExtensionsTests
{
    /// <summary>docs/superpowers/specs/2026-08-28-series-detail-specials-tab-design.md - Format-only, raw field not EffectiveFormat (a pending proposal shouldn't move an issue in/out of Specials).</summary>
    [Fact]
    public void IsSpecial_FormatValue_DelegatesToSpecialFormatCatalog()
    {
        Assert.True(new Issue { Format = "One Shot" }.IsSpecial());
        Assert.False(new Issue { Format = null }.IsSpecial());
        Assert.True(new Issue { Format = "Trade Paper Back" }.IsSpecial());
        Assert.False(new Issue { Format = "Hardcover" }.IsSpecial());
    }

    [Fact]
    public void IsSpecial_PendingProposalOnly_DoesNotCount()
    {
        var issue = new Issue
        {
            Format = null,
            MetadataProposals = { Proposal(MetadataProposalField.Format, "Omnibus", MetadataProposalStatus.Pending) },
        };

        Assert.False(issue.IsSpecial());
    }

    [Theory]
    [InlineData(null, 0, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(50, 100, 50)]
    [InlineData(100, 100, 100)]
    [InlineData(150, 100, 100)] // clamped, defensive against bad data
    public void ReadPercentage_ComputesClampedPercent(int? lastPageRead, int? pageCount, double expected)
    {
        var issue = new Issue { LastPageRead = lastPageRead, PageCount = pageCount };
        Assert.Equal(expected, issue.ReadPercentage());
    }

    [Fact]
    public void ReadPercentage_ZeroOrNullPageCount_ReturnsZero_NotNaNOrThrow()
    {
        Assert.Equal(0, new Issue { PageCount = 0, LastPageRead = 5 }.ReadPercentage());
        Assert.Equal(0, new Issue { PageCount = null, LastPageRead = 5 }.ReadPercentage());
    }

    [Fact]
    public void HasBeenRead_UsesCeVerified95PercentThreshold_Not90()
    {
        var at94 = new Issue { LastPageRead = 94, PageCount = 100 };
        var at95 = new Issue { LastPageRead = 95, PageCount = 100 };
        Assert.False(at94.HasBeenRead());
        Assert.True(at95.HasBeenRead());
    }

    [Fact]
    public void IsUnread_IsInProgress_HasBeenRead_AreMutuallyExclusiveBands()
    {
        var unread = new Issue { LastPageRead = 0, PageCount = 100 };
        var inProgress = new Issue { LastPageRead = 50, PageCount = 100 };
        var read = new Issue { LastPageRead = 100, PageCount = 100 };

        Assert.True(unread.IsUnread());
        Assert.False(unread.IsInProgress());
        Assert.False(unread.HasBeenRead());

        Assert.False(inProgress.IsUnread());
        Assert.True(inProgress.IsInProgress());
        Assert.False(inProgress.HasBeenRead());

        Assert.False(read.IsUnread());
        Assert.False(read.IsInProgress());
        Assert.True(read.HasBeenRead());
    }

    [Theory]
    [InlineData("1", 1f)]
    [InlineData("10.5", 10.5f)]
    [InlineData("½", 0.5f)]
    [InlineData("-1", -1f)]
    [InlineData("1A", 1f)] // TextNumberFloat extracts the leading numeric prefix, confirmed via real behavior - sorts near "1", not at the end alphabetically
    [InlineData("Annual 1", 1f)]
    [InlineData(null, null)]
    public void NumberSortKey_NumericOrNull(string? number, float? expected)
    {
        var issue = new Issue { Number = number };
        Assert.Equal(expected, issue.NumberSortKey());
    }

    [Fact]
    public void VolumeSortKey_SameTreatmentAsNumberSortKey()
    {
        Assert.Equal(2f, new Issue { Volume = "2" }.VolumeSortKey());
        Assert.Null(new Issue { Volume = "Special Edition" }.VolumeSortKey());
        Assert.Null(new Issue { Volume = null }.VolumeSortKey());
    }

    /// <summary>Every example from this phase's design doc §71 test-case list, verbatim.</summary>
    [Theory]
    [InlineData("1", IssueNumberType.Numeric)]
    [InlineData("2", IssueNumberType.Numeric)]
    [InlineData("10", IssueNumberType.Numeric)]
    [InlineData("10.5", IssueNumberType.Numeric)]
    [InlineData("½", IssueNumberType.Fraction)]
    [InlineData("0", IssueNumberType.Numeric)]
    [InlineData("-1", IssueNumberType.Numeric)]
    [InlineData("Annual 1", IssueNumberType.Annual)]
    [InlineData("Special", IssueNumberType.Special)]
    [InlineData("1A", IssueNumberType.AlphaNumeric)]
    [InlineData("1B", IssueNumberType.AlphaNumeric)]
    [InlineData(null, IssueNumberType.Unknown)]
    [InlineData("", IssueNumberType.Unknown)]
    [InlineData("Infinity", IssueNumberType.Text)]
    public void NumberType_ClassifiesExamples(string? number, IssueNumberType expected)
    {
        Assert.Equal(expected, new Issue { Number = number }.NumberType());
    }

    [Fact]
    public void PublishedDate_CombinesYearMonthDay()
    {
        Assert.Equal(new DateTime(2024, 3, 15), new Issue { Year = 2024, Month = 3, Day = 15 }.PublishedDate());
        Assert.Equal(new DateTime(2024, 1, 1), new Issue { Year = 2024 }.PublishedDate());
        Assert.Null(new Issue().PublishedDate());
    }

    [Fact]
    public void DatePrecision_ReflectsWhatsActuallyKnown_NeverFabricatesFinerPrecision()
    {
        Assert.Equal(PublicationDatePrecision.Unknown, new Issue().DatePrecision());
        Assert.Equal(PublicationDatePrecision.Year, new Issue { Year = 2024 }.DatePrecision());
        Assert.Equal(PublicationDatePrecision.Month, new Issue { Year = 2024, Month = 3 }.DatePrecision());
        Assert.Equal(PublicationDatePrecision.Day, new Issue { Year = 2024, Month = 3, Day = 15 }.DatePrecision());
    }

    [Fact]
    public void FileHelpers_DeriveFromFilePath()
    {
        var issue = new Issue { FilePath = @"C:\Comics\Batman\Batman 001.cbz" };
        Assert.Equal("Batman 001.cbz", issue.FileName());
        Assert.Equal(@"C:\Comics\Batman", issue.FileDirectory());
        Assert.Equal("cbz", issue.FileFormat());
        Assert.True(issue.IsLinked());
    }

    [Fact]
    public void FileHelpers_NullFilePath_AllReturnNullOrFalse()
    {
        var issue = new Issue { FilePath = null };
        Assert.Null(issue.FileName());
        Assert.Null(issue.FileDirectory());
        Assert.Null(issue.FileFormat());
        Assert.False(issue.IsLinked());
    }

    // --- EffectiveTitle/Format/Volume/Number/Count/Year (docs/superpowers/specs/2026-08-17-
    // metadata-model-phase2a-metadata-proposals-design.md) ---

    private static MetadataProposal Proposal(MetadataProposalField field, string? value, MetadataProposalStatus status, System.DateTime? createdAt = null) =>
        new()
        {
            Field = field,
            ProposedValue = value,
            Status = status,
            CreatedAt = createdAt ?? System.DateTime.UtcNow,
        };

    [Fact]
    public void EffectiveNumber_StoredValuePresent_ReturnsStoredValue_IgnoringAnyProposal()
    {
        var issue = new Issue
        {
            Number = "5",
            MetadataProposals = { Proposal(MetadataProposalField.Number, "7", MetadataProposalStatus.Accepted) },
        };

        Assert.Equal("5", issue.EffectiveNumber());
    }

    [Fact]
    public void EffectiveNumber_NoStoredValue_AcceptedProposalPresent_ReturnsProposedValue()
    {
        var issue = new Issue
        {
            Number = null,
            MetadataProposals = { Proposal(MetadataProposalField.Number, "7", MetadataProposalStatus.Accepted) },
        };

        Assert.Equal("7", issue.EffectiveNumber());
    }

    [Fact]
    public void EffectiveNumber_NoStoredValue_PendingProposal_ContributesNothing()
    {
        var issue = new Issue
        {
            Number = null,
            MetadataProposals = { Proposal(MetadataProposalField.Number, "7", MetadataProposalStatus.Pending) },
        };

        Assert.Null(issue.EffectiveNumber());
    }

    [Fact]
    public void EffectiveNumber_NoStoredValue_RejectedProposal_ContributesNothing()
    {
        var issue = new Issue
        {
            Number = null,
            MetadataProposals = { Proposal(MetadataProposalField.Number, "7", MetadataProposalStatus.Rejected) },
        };

        Assert.Null(issue.EffectiveNumber());
    }

    [Fact]
    public void EffectiveNumber_MultipleAcceptedProposalsForSameField_UsesMostRecent()
    {
        var older = Proposal(MetadataProposalField.Number, "3", MetadataProposalStatus.Accepted, System.DateTime.UtcNow.AddDays(-1));
        var newer = Proposal(MetadataProposalField.Number, "7", MetadataProposalStatus.Accepted, System.DateTime.UtcNow);
        var issue = new Issue { Number = null, MetadataProposals = { older, newer } };

        Assert.Equal("7", issue.EffectiveNumber());
    }

    [Fact]
    public void EffectiveVolume_NoStoredValue_AcceptedProposal_ReturnsProposedValue()
    {
        var issue = new Issue
        {
            Volume = null,
            MetadataProposals = { Proposal(MetadataProposalField.Volume, "2", MetadataProposalStatus.Accepted) },
        };

        Assert.Equal("2", issue.EffectiveVolume());
    }

    [Fact]
    public void EffectiveTitle_MultipleFieldsOnOneIssue_EachResolvesIndependently()
    {
        var issue = new Issue
        {
            Title = null,
            Number = "5",
            Volume = null,
            MetadataProposals =
            {
                Proposal(MetadataProposalField.Title, "Inferred Title", MetadataProposalStatus.Accepted),
                Proposal(MetadataProposalField.Number, "99", MetadataProposalStatus.Accepted), // ignored - Number is already stored
                Proposal(MetadataProposalField.Volume, "3", MetadataProposalStatus.Pending), // ignored - not accepted
            },
        };

        Assert.Equal("Inferred Title", issue.EffectiveTitle());
        Assert.Equal("5", issue.EffectiveNumber());
        Assert.Null(issue.EffectiveVolume());
    }

    [Fact]
    public void EffectiveYear_NoStoredValue_AcceptedProposal_ParsesToInt()
    {
        var issue = new Issue
        {
            Year = null,
            MetadataProposals = { Proposal(MetadataProposalField.Year, "2019", MetadataProposalStatus.Accepted) },
        };

        Assert.Equal(2019, issue.EffectiveYear());
    }

    [Fact]
    public void EffectiveCount_MalformedProposalValue_ResolvesToNull_DoesNotThrow()
    {
        var issue = new Issue
        {
            Count = null,
            MetadataProposals = { Proposal(MetadataProposalField.Count, "not-a-number", MetadataProposalStatus.Accepted) },
        };

        Assert.Null(issue.EffectiveCount());
    }

    [Fact]
    public void EffectiveYear_MalformedProposalValue_ResolvesToNull_DoesNotThrow()
    {
        var issue = new Issue
        {
            Year = null,
            MetadataProposals = { Proposal(MetadataProposalField.Year, "circa-2019", MetadataProposalStatus.Accepted) },
        };

        Assert.Null(issue.EffectiveYear());
    }

    [Fact]
    public void NumberSortKey_ReadsThroughEffectiveNumber_NotJustRawField()
    {
        var issue = new Issue
        {
            Number = null,
            MetadataProposals = { Proposal(MetadataProposalField.Number, "10.5", MetadataProposalStatus.Accepted) },
        };

        Assert.Equal(10.5f, issue.NumberSortKey());
    }
}
