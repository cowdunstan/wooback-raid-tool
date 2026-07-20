using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WoobackVash.Api.Config;

namespace WoobackVash.Api.Services;

/// <summary>
/// Pulls the guild's Warcraft Logs report list, keeping the API credentials
/// server-side. Ported from handleWclReports in raidhelper-proxy.worker.js:
/// client-credentials OAuth (token cached to expiry), a single-page fetch of the
/// newest reports, a long freshness window (CacheTtlSeconds, default 30 min) to
/// stay under the v2 hourly points budget, and a stale-copy fallback when
/// Warcraft Logs rate-limits us (429). Officers can force a refresh past the TTL.
/// </summary>
public class WarcraftLogsService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly WarcraftLogsOptions _opt;
    private readonly ILogger<WarcraftLogsService> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private long _tokenExpUnix;

    private string? _cachedBody;
    private DateTimeOffset _cachedAt;

    public WarcraftLogsService(
        IHttpClientFactory httpFactory,
        IOptions<WarcraftLogsOptions> opt,
        ILogger<WarcraftLogsService> log)
    {
        _httpFactory = httpFactory;
        _opt = opt.Value;
        _log = log;
    }

    private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private bool CacheFresh() =>
        _cachedBody is not null && (DateTimeOffset.UtcNow - _cachedAt).TotalSeconds < _opt.CacheTtlSeconds;

    /// <summary>Returns (httpStatus, jsonBody). Body is always JSON — either the
    /// report list or an { error, detail } object matching the Worker's shape.
    /// The page shows only the newest logs, so we fetch a single page of
    /// ReportLimit reports (newest-first) — one upstream call per refresh.
    /// <paramref name="forceRefresh"/> (officers only) bypasses the cache TTL.</summary>
    public async Task<(int Status, string Body)> GetReportsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && CacheFresh()) return (200, _cachedBody!);

        if (string.IsNullOrWhiteSpace(_opt.GuildServer) || string.IsNullOrWhiteSpace(_opt.GuildRegion))
            return (501, Err("not_configured", "Warcraft Logs guild is not set on the server yet."));

        await _gate.WaitAsync();
        try
        {
            // Another caller may have refreshed while we waited (a forced refresh
            // always goes to the network, so it never short-circuits here).
            if (!forceRefresh && CacheFresh()) return (200, _cachedBody!);

            var token = await GetTokenAsync();
            if (token is null)
                return (501, Err("not_configured", "Warcraft Logs API credentials are not set on the server yet."));

            const string query =
                "query($name:String!,$server:String!,$region:String!,$limit:Int!){" +
                "reportData{reports(guildName:$name,guildServerSlug:$server,guildServerRegion:$region,limit:$limit,page:1){" +
                "data{code title startTime endTime zone{name} owner{name}}}}}";

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(_opt.RequestTimeoutSeconds);
            var reports = new List<object>();

            using var req = new HttpRequestMessage(HttpMethod.Post, _opt.GraphQlUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var payload = JsonSerializer.Serialize(new
            {
                query,
                variables = new { name = _opt.GuildName, server = _opt.GuildServer, region = _opt.GuildRegion, limit = _opt.ReportLimit }
            });
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            HttpResponseMessage r;
            try
            {
                r = await http.SendAsync(req);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Upstream stalled or hit our per-request timeout — fall back to a
                // prior cached copy rather than leaving the browser spinning.
                _log.LogWarning(ex, "Warcraft Logs request failed/timed out");
                if (_cachedBody is not null) return (200, _cachedBody);
                return (504, Err("upstream_timeout",
                    "Warcraft Logs is not responding right now. Try again in a minute."));
            }

            // Rate limited — the hourly points budget is spent. Serve a stale
            // cached copy if we have one, else ask the caller to wait it out.
            if (r.StatusCode == (HttpStatusCode)429)
            {
                if (_cachedBody is not null) return (200, _cachedBody);
                return (429, Err("rate_limited",
                    "Warcraft Logs is rate-limiting the guild tools right now. Try again in a minute."));
            }
            if (!r.IsSuccessStatusCode)
                return (502, Err("upstream", "Warcraft Logs API returned " + (int)r.StatusCode));

            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            if (!TryGetReportsNode(doc.RootElement, out var node))
            {
                var gqlErr = TryGetGqlError(doc.RootElement);
                return (502, Err("upstream", gqlErr ?? "Unexpected Warcraft Logs response."));
            }

            if (node.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var rep in data.EnumerateArray())
                {
                    var code = GetString(rep, "code");
                    reports.Add(new
                    {
                        code,
                        title = GetString(rep, "title"),
                        startTime = GetLong(rep, "startTime"),
                        endTime = GetLong(rep, "endTime"),
                        zone = GetNestedName(rep, "zone"),
                        owner = GetNestedName(rep, "owner"),
                        url = _opt.Host.TrimEnd('/') + "/reports/" + code
                    });
                }
            }

            var guildUrl = $"{_opt.Host.TrimEnd('/')}/guild/{_opt.GuildRegion.ToLowerInvariant()}/" +
                           $"{_opt.GuildServer}/{Uri.EscapeDataString(_opt.GuildName)}";
            var body = JsonSerializer.Serialize(new { guild = _opt.GuildName, guildUrl, reports });

            _cachedBody = body;
            _cachedAt = DateTimeOffset.UtcNow;
            return (200, body);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Warcraft Logs fetch failed");
            return (502, Err("upstream fetch failed", ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> GetTokenAsync()
    {
        var now = NowUnix();
        if (_token is not null && _tokenExpUnix - 60 > now) return _token;
        if (string.IsNullOrWhiteSpace(_opt.ClientId) || string.IsNullOrWhiteSpace(_opt.ClientSecret)) return null;

        try
        {
            var http = _httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Post, _opt.OAuthUrl);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Content = new StringContent("grant_type=client_credentials", Encoding.UTF8,
                "application/x-www-form-urlencoded");

            var r = await http.SendAsync(req);
            if (!r.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("access_token", out var at) ||
                at.ValueKind != JsonValueKind.String) return null;

            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) &&
                            ei.TryGetInt64(out var v) ? v : 3600;
            _token = at.GetString();
            _tokenExpUnix = now + expiresIn;
            return _token;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Warcraft Logs token request failed");
            return null;
        }
    }

    private static bool TryGetReportsNode(JsonElement root, out JsonElement node)
    {
        node = default;
        if (root.TryGetProperty("data", out var d) &&
            d.TryGetProperty("reportData", out var rd) &&
            rd.TryGetProperty("reports", out var reports) &&
            reports.ValueKind == JsonValueKind.Object)
        {
            node = reports;
            return true;
        }
        return false;
    }

    private static string? TryGetGqlError(JsonElement root) =>
        root.TryGetProperty("errors", out var errs) &&
        errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0 &&
        errs[0].TryGetProperty("message", out var m) ? m.GetString() : null;

    private static string GetString(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    private static long GetLong(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static string GetNestedName(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Object ? GetString(v, "name") : "";

    private static string Err(string error, string detail) =>
        JsonSerializer.Serialize(new { error, detail });
}
