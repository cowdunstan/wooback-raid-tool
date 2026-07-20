namespace WoobackVash.Api.Config;

/// <summary>
/// Warcraft Logs (v2 API) config. Credentials are secrets (env / user-secrets).
/// Non-secret guild identity + host are ported from raidhelper-proxy.worker.js.
/// The guild lives on the Fresh (Classic Anniversary) realm, so we talk to
/// fresh.warcraftlogs.com — a Fresh guild is not visible on the www API.
/// </summary>
public class WarcraftLogsOptions
{
    public const string SectionName = "WarcraftLogs";

    public string OAuthUrl { get; set; } = "https://www.warcraftlogs.com/oauth/token";
    public string Host { get; set; } = "https://fresh.warcraftlogs.com";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    public string GuildName { get; set; } = "wooback";
    public string GuildServer { get; set; } = "dreamscythe";
    public string GuildRegion { get; set; } = "US";

    /// <summary>How long an assembled report list stays fresh (seconds). The list
    /// only changes when a raid is logged (a few times a week), so a long window
    /// is safe and keeps us well under the v2 hourly points budget. Default 30 min.</summary>
    public int CacheTtlSeconds { get; set; } = 1800;

    /// <summary>How many reports to show (newest-first). Fetched in a single page,
    /// so this is also the whole per-refresh cost against the v2 points budget.</summary>
    public int ReportLimit { get; set; } = 25;

    /// <summary>Per-request timeout (seconds) for each call to Warcraft Logs. When
    /// WCL throttles us it stalls connections rather than replying fast, so without
    /// this a page walk can hang for minutes; keep it short so we fail over to the
    /// cached copy quickly instead of leaving the browser spinning.</summary>
    public int RequestTimeoutSeconds { get; set; } = 12;

    public string GraphQlUrl => Host.TrimEnd('/') + "/api/v2/client";
}
