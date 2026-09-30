namespace Paperbunkr.App.ViewModels;

/// <summary>A reading list the open list overlaps with.</summary>
public sealed record OverlapPartner(int ListId, string Name, int Shared, int OwnCount)
{
    public string MenuLabel => $"{Name} ({Shared} shared)";
}

/// <summary>"Move to folder ▸" target: a list and the folder it goes to (null = top level).</summary>
public sealed record ReadingListMoveRequest(int ListId, int? FolderId);

/// <summary>A sidebar "Export ▸" pick: which list, which format.</summary>
public sealed record ReadingListExportRequest(int ListId, string Format);
