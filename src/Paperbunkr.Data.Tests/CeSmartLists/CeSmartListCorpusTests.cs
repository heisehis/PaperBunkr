using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.SmartLists.CeImport;
using Xunit.Abstractions;

namespace Paperbunkr.Data.Tests.CeSmartLists;

/// <summary>
/// Reads every ComicRack list in a folder you point it at, to see how a real collection fares (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md).
/// Set <c>PAPERBUNKR_CE_SMARTLIST_CORPUS</c> to one or more folders (separated by <c>;</c>); without it the test does nothing, so CI needs no files. It asserts
/// that no file throws, and prints how many lists imported, how many were refused, and a histogram of what was dropped or approximated.
/// </summary>
public class CeSmartListCorpusTests(ITestOutputHelper output)
{
    private const string Variable = "PAPERBUNKR_CE_SMARTLIST_CORPUS";

    [Fact]
    public void EveryFileInTheCorpus_ReadsWithoutThrowing()
    {
        string? folders = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(folders))
        {
            output.WriteLine($"{Variable} is not set; nothing to read.");
            return;
        }

        var reader = new CeSmartListReader();
        int lists = 0, empty = 0, otherKinds = 0, conditions = 0;
        var histogram = new SortedDictionary<string, int>();

        foreach (string file in folders.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .SelectMany(f => Directory.EnumerateFiles(f, "*.cbl", SearchOption.AllDirectories)))
        {
            if (CblFile.Detect(file) != CblFileKind.SmartList)
            {
                otherKinds++;
                continue;
            }

            using FileStream stream = File.OpenRead(file);
            CeSmartListDraft draft = reader.Read(stream, Path.GetFileNameWithoutExtension(file));
            lists++;
            empty += draft.IsEmpty ? 1 : 0;
            conditions += CountConditions(draft.List.RootGroup);
            foreach (CeImportWarning warning in draft.Warnings)
            {
                string key = $"{warning.ConditionType} {warning.Detail}";
                histogram[key] = histogram.GetValueOrDefault(key) + 1;
            }
        }

        output.WriteLine($"smart lists read: {lists}; refused (nothing usable): {empty}; conditions kept: {conditions}; other .cbl files skipped: {otherKinds}");
        foreach ((string what, int count) in histogram)
        {
            output.WriteLine($"  noted x{count}: {what}");
        }
    }

    private static int CountConditions(Paperbunkr.Data.Entities.SmartListConditionGroup group) =>
        group.Conditions.Count + group.ChildGroups.Sum(CountConditions);
}
