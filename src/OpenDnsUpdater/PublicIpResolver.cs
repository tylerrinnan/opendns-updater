using System.Net;
using DnsClient;

namespace OpenDnsUpdater;

/// <summary>
/// Figures out the machine's current public IP address as cheaply as possible.
///
/// Primary method: a single DNS query for "myip.opendns.com" sent directly to
/// OpenDNS's own resolvers (208.67.222.222 / 208.67.220.220). Those resolvers answer
/// that name with whatever source IP the query arrived from — it's OpenDNS's own
/// mechanism for exactly this purpose, so it costs one UDP round trip and adds no
/// third-party dependency. If that's blocked (e.g. a network that filters outbound
/// DNS to arbitrary servers), fall back to a plain HTTPS IP-echo service.
/// </summary>
internal static class PublicIpResolver
{
    private static readonly IPAddress[] OpenDnsResolvers =
    {
        IPAddress.Parse("208.67.222.222"),
        IPAddress.Parse("208.67.220.220"),
    };

    private static readonly string[] HttpFallbacks =
    {
        "https://api.ipify.org",
        "https://checkip.amazonaws.com",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    public static async Task<IPAddress?> GetPublicIpAsync(CancellationToken ct)
    {
        var viaDns = await TryDnsTrickAsync(ct);
        if (viaDns is not null) return viaDns;

        return await TryHttpFallbacksAsync(ct);
    }

    private static async Task<IPAddress?> TryDnsTrickAsync(CancellationToken ct)
    {
        try
        {
            var options = new LookupClientOptions(OpenDnsResolvers)
            {
                Timeout = TimeSpan.FromSeconds(4),
                Retries = 1,
                UseCache = false,
                ThrowDnsErrors = false,
            };
            var client = new LookupClient(options);
            var result = await client.QueryAsync("myip.opendns.com", QueryType.A, cancellationToken: ct);
            var record = result.Answers.ARecords().FirstOrDefault();
            return record?.Address;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"OpenDNS resolver IP lookup failed, will try HTTPS fallback: {ex.Message}");
            return null;
        }
    }

    private static async Task<IPAddress?> TryHttpFallbacksAsync(CancellationToken ct)
    {
        foreach (var url in HttpFallbacks)
        {
            try
            {
                var text = (await Http.GetStringAsync(url, ct)).Trim();
                if (IPAddress.TryParse(text, out var ip)) return ip;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Public IP fallback '{url}' failed: {ex.Message}");
            }
        }

        return null;
    }
}
