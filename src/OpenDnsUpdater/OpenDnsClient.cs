using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace OpenDnsUpdater;

public enum OpenDnsUpdateStatus
{
    Updated,
    NoChange,
    BadAuth,
    NotYours,
    NoHost,
    Abuse,
    DonatorOnly,
    MalformedRequest,
    TooManyHosts,
    BlockedAgent,
    ServerError,
    UnknownResponse,
    NetworkError,
}

public sealed record OpenDnsUpdateResult(OpenDnsUpdateStatus Status, string RawResponse, string? ConfirmedIp)
{
    /// <summary>True for outcomes that mean OpenDNS's network IP now matches — no action needed.</summary>
    public bool IsSuccess => Status is OpenDnsUpdateStatus.Updated or OpenDnsUpdateStatus.NoChange;
}

/// <summary>
/// Talks to OpenDNS's dynamic-IP update endpoint — the same DynDNS-style API the
/// (now-abandoned) official OpenDNS Updater used: HTTPS GET with HTTP Basic Auth,
/// hostname = your OpenDNS "network label". Endpoint and response codes confirmed
/// live and unchanged as of this writing.
/// </summary>
internal static class OpenDnsClient
{
    private const string Endpoint = "https://updates.opendns.com/nic/update";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenDnsUpdater-Personal/1.0");
        return client;
    }

    public static async Task<OpenDnsUpdateResult> UpdateAsync(
        string email, string password, string networkLabel, IPAddress newIp, CancellationToken ct)
    {
        var url = $"{Endpoint}?hostname={Uri.EscapeDataString(networkLabel)}&myip={newIp}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var authBytes = Encoding.UTF8.GetBytes($"{email}:{password}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));

        try
        {
            using var response = await Http.SendAsync(request, ct);
            var body = (await response.Content.ReadAsStringAsync(ct)).Trim();
            return Parse(body);
        }
        catch (Exception ex)
        {
            return new OpenDnsUpdateResult(OpenDnsUpdateStatus.NetworkError, ex.Message, null);
        }
    }

    private static OpenDnsUpdateResult Parse(string body)
    {
        if (StartsWith(body, "good"))
        {
            var ip = body.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);
            return new OpenDnsUpdateResult(OpenDnsUpdateStatus.Updated, body, ip);
        }
        if (StartsWith(body, "nochg")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.NoChange, body, null);
        if (StartsWith(body, "badauth")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.BadAuth, body, null);
        if (StartsWith(body, "!yours")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.NotYours, body, null);
        if (StartsWith(body, "nohost")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.NoHost, body, null);
        if (StartsWith(body, "abuse")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.Abuse, body, null);
        if (StartsWith(body, "!donator")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.DonatorOnly, body, null);
        if (StartsWith(body, "notfqdn")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.MalformedRequest, body, null);
        if (StartsWith(body, "numhost")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.TooManyHosts, body, null);
        if (StartsWith(body, "badagent")) return new OpenDnsUpdateResult(OpenDnsUpdateStatus.BlockedAgent, body, null);
        if (StartsWith(body, "dnserr") || StartsWith(body, "911"))
            return new OpenDnsUpdateResult(OpenDnsUpdateStatus.ServerError, body, null);

        return new OpenDnsUpdateResult(OpenDnsUpdateStatus.UnknownResponse, body, null);
    }

    private static bool StartsWith(string s, string prefix) => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    // OpenDNS's legacy dynamic-update endpoint has a long-documented quirk where passwords
    // containing certain special characters fail authentication on THIS endpoint specifically,
    // even though the exact same password works fine for logging into the dashboard.
    private static readonly char[] KnownProblemPasswordChars = "^&~`%".ToCharArray();

    /// <summary>Best-effort explanation for a BadAuth result, based on known real-world causes
    /// (special characters this endpoint mishandles, or two-factor auth requiring a separate
    /// update-only password) rather than "your password is wrong", which is often not true.</summary>
    public static string DescribeLikelyBadAuthCause(string password)
    {
        var found = password.Where(c => KnownProblemPasswordChars.Contains(c)).Distinct().ToArray();
        if (found.Length > 0)
        {
            return "Your password contains " + string.Join(", ", found.Select(c => $"'{c}'")) +
                   " — OpenDNS's update API has a long-standing bug rejecting passwords with certain " +
                   "special characters (^ & ~ ` %) even when they're otherwise correct. Try removing " +
                   "those, or use a separate update-only password instead.";
        }

        return "If your account has two-factor authentication enabled, your normal password won't work " +
               "here — request an update-only password from OpenDNS support and use that instead.";
    }
}
