using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.App.Services.Sharing;

/// <summary>
/// Finds active networks Windows has classed as <em>Public</em> (docs/superpowers/specs/2026-09-19-remote-library-
/// sharing-design.md §6/§12): the classification Windows gives cafés and airports. Sharing there would offer the
/// library to strangers on that network, so the Sharing settings warn (they never block - a user who knows the
/// network is fine can carry on). Best effort: no PowerShell, a timeout or any error simply means "no warning".
/// </summary>
public static class NetworkProfileProbe
{
    private const string Command = "Get-NetConnectionProfile | ForEach-Object { $_.Name + '|' + $_.NetworkCategory }";

    /// <summary>Names of networks reported as Public in <paramref name="output"/> (one <c>Name|Category</c> per line). Pure, for testing.</summary>
    public static IReadOnlyList<string> ParsePublicNetworks(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return Array.Empty<string>();
        }

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Select(line => line.LastIndexOf('|') is int bar and > 0 ? (Name: line[..bar].Trim(), Category: line[(bar + 1)..].Trim()) : default)
            .Where(x => !string.IsNullOrEmpty(x.Name) && x.Category.Equals("Public", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The user-facing sentence for the Sharing settings, or null when there is nothing to warn about.</summary>
    public static string? WarningFor(IReadOnlyList<string> publicNetworks) => publicNetworks.Count == 0
        ? null
        : $"Windows treats {(publicNetworks.Count == 1 ? "this network" : "these networks")} as Public: {string.Join(", ", publicNetworks)}. " +
          "Sharing here would let strangers on that network see your library's name and try your password. Switch it to Private in Windows network settings, or turn sharing off.";

    public static async Task<IReadOnlyList<string>> GetPublicNetworksAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{Command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return Array.Empty<string>();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(8));
            Task<string> read = process.StandardOutput.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return ParsePublicNetworks(await read.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                return Array.Empty<string>();
            }
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }
}
