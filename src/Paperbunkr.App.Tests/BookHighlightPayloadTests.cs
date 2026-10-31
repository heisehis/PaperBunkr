using System.Text.Json;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

public class BookHighlightPayloadTests
{
    private static BookHighlightSummary Highlight(int id, string? note) =>
        new() { Id = id, ChapterIndex = 0, BlockId = "pb-p1", StartOffset = 2, Length = 5, Color = BookHighlightColor.Green, Note = note };

    private static JsonElement[] Parse(params BookHighlightSummary[] highlights) =>
        JsonDocument.Parse(BookHighlightPayload.Build(highlights)).RootElement.EnumerateArray().ToArray();

    [Fact]
    public void HasNote_IsTrueOnlyForNonBlankNotes()
    {
        var items = Parse(Highlight(1, "remember this"), Highlight(2, null), Highlight(3, "   "), Highlight(4, ""));

        Assert.True(items[0].GetProperty("hasNote").GetBoolean());
        Assert.False(items[1].GetProperty("hasNote").GetBoolean());
        Assert.False(items[2].GetProperty("hasNote").GetBoolean());
        Assert.False(items[3].GetProperty("hasNote").GetBoolean());
    }

    [Fact]
    public void Payload_KeepsTheFieldsThePageNeeds()
    {
        var item = Parse(Highlight(7, "n"))[0];

        Assert.Equal(7, item.GetProperty("id").GetInt32());
        Assert.Equal("pb-p1", item.GetProperty("blockId").GetString());
        Assert.Equal(2, item.GetProperty("startOffset").GetInt32());
        Assert.Equal(5, item.GetProperty("length").GetInt32());
        Assert.Equal("Green", item.GetProperty("color").GetString());
    }

    [Fact]
    public void Summary_HasNoteFollowsTheNote()
    {
        Assert.True(Highlight(1, "x").HasNote);
        Assert.False(Highlight(1, " ").HasNote);
        Assert.False(Highlight(1, null).HasNote);
    }
}
