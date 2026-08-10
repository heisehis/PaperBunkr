using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Builds a minimal, hand-authored PDF 1.4 file (one Type1/Helvetica text-drawing content stream
/// per page) for testing <c>PdfBookSource</c> against a real file rather than a mock - same
/// "generate via the real code path" precedent as <see cref="CbzFixture"/>, adapted to raw PDF
/// syntax since there's no PDF-writing library already in this codebase to generate one through
/// (PDFiumSharpV2/pdfium.dll are read/render-only here). Byte offsets for the xref table are
/// computed from the actual object lengths as they're built, not hardcoded - deterministic and
/// correct regardless of how the page text content changes.
/// </summary>
internal static class PdfFixture
{
    public static string Create(string path, params string[] pageTexts)
    {
        var objects = new List<byte[]>();

        int pageCount = pageTexts.Length;
        var pageObjectNumbers = new int[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            pageObjectNumbers[i] = 3 + i; // object 1 = Catalog, 2 = Pages, 3..3+n-1 = Page N
        }

        int fontObjectNumber = 3 + pageCount;
        var contentObjectNumbers = new int[pageCount];
        for (int i = 0; i < pageCount; i++)
        {
            contentObjectNumbers[i] = fontObjectNumber + 1 + i;
        }

        // 1: Catalog
        objects.Add(Ascii($"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n"));

        // 2: Pages
        string kids = string.Join(" ", System.Array.ConvertAll(pageObjectNumbers, n => $"{n} 0 R"));
        objects.Add(Ascii($"2 0 obj\n<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>\nendobj\n"));

        // 3..3+n-1: Page objects
        for (int i = 0; i < pageCount; i++)
        {
            objects.Add(Ascii(
                $"{pageObjectNumbers[i]} 0 obj\n" +
                $"<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 {fontObjectNumber} 0 R >> >> " +
                $"/MediaBox [0 0 300 300] /Contents {contentObjectNumbers[i]} 0 R >>\nendobj\n"));
        }

        // Font
        objects.Add(Ascii($"{fontObjectNumber} 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n"));

        // Content streams - one Tj per line, escaping PDF string-literal special characters.
        for (int i = 0; i < pageCount; i++)
        {
            string escaped = pageTexts[i].Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
            string stream = $"BT /F1 14 Tf 40 250 Td ({escaped}) Tj ET";
            byte[] streamBytes = Ascii(stream);
            objects.Add(Concat(
                Ascii($"{contentObjectNumbers[i]} 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n"),
                streamBytes,
                Ascii("\nendstream\nendobj\n")));
        }

        using var output = new MemoryStream();
        void Write(byte[] bytes) => output.Write(bytes, 0, bytes.Length);

        Write(Ascii("%PDF-1.4\n"));

        var offsets = new List<long>();
        foreach (byte[] obj in objects)
        {
            offsets.Add(output.Position);
            Write(obj);
        }

        long xrefOffset = output.Position;
        int totalObjects = objects.Count + 1; // +1 for the free-list head entry
        Write(Ascii($"xref\n0 {totalObjects}\n0000000000 65535 f \n"));
        foreach (long offset in offsets)
        {
            Write(Ascii($"{offset:D10} 00000 n \n"));
        }

        Write(Ascii($"trailer\n<< /Size {totalObjects} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF"));

        File.WriteAllBytes(path, output.ToArray());
        return path;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (var part in parts)
        {
            total += part.Length;
        }

        var result = new byte[total];
        int offset = 0;
        foreach (var part in parts)
        {
            System.Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
    }
}
