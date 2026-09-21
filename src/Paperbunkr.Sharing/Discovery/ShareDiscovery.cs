using System.Net;
using Makaretu.Dns;

namespace Paperbunkr.Sharing.Discovery;

/// <summary>A host found on the local network. Nothing here is trusted: it only pre-fills the Add dialog.</summary>
public sealed record DiscoveredHost(string InstanceId, string DisplayName, int ProtocolVersion, string Address, int Port);

/// <summary>
/// LAN discovery over mDNS / DNS-SD (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §5.1).
/// Manual host:port entry always works; this only saves typing an address. It advertises nothing secret - the
/// TXT record carries the instance id, display name and protocol version, exactly what <c>/v1/hello</c> already
/// shows an unauthenticated visitor - and finding a host grants nothing: the certificate trust prompt and the
/// password still happen afterwards. Advertising exists only while sharing is on.
/// </summary>
public static class ShareDiscovery
{
    public const string ServiceType = "_paperbunkr._tcp";

    internal const string KeyInstance = "id";
    internal const string KeyName = "name";
    internal const string KeyVersion = "v";

    /// <summary>Builds the TXT key/value pairs a host advertises. Public so the contents can be asserted.</summary>
    public static IReadOnlyDictionary<string, string> BuildTxt(string instanceId, string displayName) => new Dictionary<string, string>
    {
        [KeyInstance] = instanceId,
        [KeyName] = displayName,
        [KeyVersion] = ProtocolVersion.Current.ToString(),
    };

    /// <summary>Parses TXT strings ("key=value") into a <see cref="DiscoveredHost"/>, or null when they aren't from a compatible Paperbunkr.</summary>
    public static DiscoveredHost? TryParse(IEnumerable<string> txt, string address, int port)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in txt)
        {
            int eq = entry.IndexOf('=');
            if (eq > 0)
            {
                map[entry[..eq]] = entry[(eq + 1)..];
            }
        }

        if (!map.TryGetValue(KeyInstance, out string? id) || string.IsNullOrWhiteSpace(id)
            || !map.TryGetValue(KeyVersion, out string? v) || !int.TryParse(v, out int version)
            || port is < 1 or > 65535 || string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        map.TryGetValue(KeyName, out string? name);
        return new DiscoveredHost(id, string.IsNullOrWhiteSpace(name) ? address : name, version, address, port);
    }

    /// <summary>Advertises this host until disposed. Never throws: discovery is a convenience, and a failure (no multicast, firewall) just means people type the address.</summary>
    public sealed class Advertiser : IDisposable
    {
        private readonly ServiceDiscovery? _discovery;
        private readonly ServiceProfile? _profile;

        public Advertiser(string instanceId, string displayName, int port)
        {
            try
            {
                _profile = new ServiceProfile(SafeInstanceName(displayName, instanceId), ServiceType, (ushort)port);
                foreach (var pair in BuildTxt(instanceId, displayName))
                {
                    _profile.AddProperty(pair.Key, pair.Value);
                }

                _discovery = new ServiceDiscovery();
                _discovery.Advertise(_profile);
                IsAdvertising = true;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                _discovery?.Dispose();
                _discovery = null;
            }
        }

        public bool IsAdvertising { get; }

        public string? Error { get; }

        public void Dispose()
        {
            try
            {
                if (_profile is not null)
                {
                    _discovery?.Unadvertise(_profile);
                }
            }
            catch (Exception)
            {
            }

            _discovery?.Dispose();
        }

        // DNS labels are limited to 63 bytes and shouldn't contain dots; the instance id disambiguates two hosts sharing a name.
        private static string SafeInstanceName(string displayName, string instanceId)
        {
            string clean = new string(displayName.Where(c => !char.IsControl(c) && c != '.').ToArray()).Trim();
            if (clean.Length == 0)
            {
                clean = "Paperbunkr";
            }

            string suffix = " (" + instanceId[..Math.Min(4, instanceId.Length)] + ")";
            return (clean.Length > 40 ? clean[..40] : clean) + suffix;
        }
    }

    /// <summary>
    /// Browses the network for a few seconds and returns every compatible host seen. Never throws (returns what it found).
    /// </summary>
    public static async Task<IReadOnlyList<DiscoveredHost>> BrowseAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var found = new Dictionary<string, DiscoveredHost>();
        try
        {
            using var mdns = new MulticastService();
            using var discovery = new ServiceDiscovery(mdns);

            discovery.ServiceInstanceDiscovered += (_, e) =>
            {
                // The instance's SRV/TXT/address records arrive in the same or a follow-up message; ask for them explicitly.
                mdns.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
                mdns.SendQuery(e.ServiceInstanceName, type: DnsType.TXT);
            };

            mdns.AnswerReceived += (_, e) =>
            {
                try
                {
                    var records = e.Message.Answers.Concat(e.Message.AdditionalRecords).ToList();
                    foreach (SRVRecord srv in records.OfType<SRVRecord>().Where(r => r.Name.ToString().Contains(ServiceType, StringComparison.OrdinalIgnoreCase)))
                    {
                        var txt = records.OfType<TXTRecord>().Where(t => t.Name == srv.Name).SelectMany(t => t.Strings).ToList();
                        IPAddress? ip = records.OfType<AddressRecord>().Where(a => a.Name == srv.Target).Select(a => a.Address)
                            .OrderBy(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 0 : 1).FirstOrDefault();
                        string address = ip?.ToString() ?? srv.Target.ToString().TrimEnd('.');
                        DiscoveredHost? host = TryParse(txt, address, srv.Port);
                        if (host is not null)
                        {
                            lock (found)
                            {
                                found[host.InstanceId] = host;
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // A malformed packet from someone else on the network is not our problem.
                }
            };

            mdns.Start();
            discovery.QueryServiceInstances(ServiceType);
            await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // No multicast on this machine/network: an empty list is the honest answer.
        }

        lock (found)
        {
            return found.Values.OrderBy(h => h.DisplayName).ToList();
        }
    }
}
