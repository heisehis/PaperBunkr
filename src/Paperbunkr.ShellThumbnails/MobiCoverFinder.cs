using cYo.Projects.ComicRack.Engine.IO.Provider.Books.Mobi;

namespace Paperbunkr.ShellThumbnails;

/// <summary>
/// .mobi/.azw/.azw3 (decisions 10-11): the EXTH cover record the Books reader already uses
/// (<see cref="MobiHeaderReader.CoverRecordIndex"/>), else the first PDB record that is an image.
/// </summary>
internal static class MobiCoverFinder
{
    public static byte[]? FindCover(Stream stream)
    {
        byte[] file = ArchiveCoverFinder.ReadAll(stream);
        var pdb = new PalmDbReader(file);
        if (pdb.Records.Count == 0)
        {
            return null;
        }

        var header = new MobiHeaderReader(pdb.Records[0]);
        if (header.EncryptionType != 0)
        {
            return null; // DRM - decision 15
        }

        if (header.CoverRecordIndex is { } cover && cover > 0 && cover < pdb.Records.Count && IsImage(pdb.Records[cover]))
        {
            return pdb.Records[cover];
        }

        return pdb.Records.Skip(1).FirstOrDefault(IsImage);
    }

    internal static bool IsImage(byte[] data) =>
        (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        || (data.Length > 8 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
        || (data.Length > 6 && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8');
}
