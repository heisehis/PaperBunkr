using System.Diagnostics;
using Paperbunkr.Data.Gcd;

// dotnet run --project tools/Paperbunkr.GcdExtract -c Release -- <dump.db> <outDir> [--url <release asset url>] [--date yyyy-mm-dd]
//
// Turns the Grand Comics Database SQLite dump (downloaded by a logged-in GCD user from https://www.comics.org/download/) into
// gcd.sqlite, gcd-<date>.zip and gcd-data.json (docs/superpowers/specs/2026-09-27-gcd-data-design.md §1). Publish it in the data repo,
// https://github.com/heisehis/paperbunkr-gcd-data: a release tagged gcd-<date> with the zip, then its gcd-data.json replaced (the app
// checks for updates there). Copy gcd-data.json to this repo's root too, so the next app build knows it. The data is CC BY-SA 4.0.

if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("usage: Paperbunkr.GcdExtract <dump.db> <outDir> [--url <release asset url>] [--date yyyy-mm-dd]");
    return 1;
}

string dump = Path.GetFullPath(args[0]);
string outDir = Path.GetFullPath(args[1]);
string? url = Option("--url");
string? date = Option("--date");

if (!File.Exists(dump))
{
    Console.Error.WriteLine($"No dump at {dump}");
    return 2;
}

var clock = Stopwatch.StartNew();
var manifest = GcdExtractor.Build(dump, outDir, url, date, message => Console.WriteLine($"[{clock.Elapsed:mm\\:ss}] {message}"));
long extractBytes = new FileInfo(Path.Combine(outDir, GcdExtractor.ExtractFileName)).Length;

Console.WriteLine();
Console.WriteLine($"Dump date     {manifest.DumpDate}");
Console.WriteLine($"Extract       {extractBytes / 1024.0 / 1024.0:F1} MB");
Console.WriteLine($"Zip           {manifest.SizeBytes / 1024.0 / 1024.0:F1} MB  gcd-{manifest.DumpDate}.zip");
Console.WriteLine($"SHA-256       {manifest.Sha256}");
Console.WriteLine($"Manifest url  {manifest.Url}");
Console.WriteLine($"Done in {clock.Elapsed:mm\\:ss}. Attach the zip to a gcd-<date> release in heisehis/paperbunkr-gcd-data, then update gcd-data.json there and here.");
return 0;

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
