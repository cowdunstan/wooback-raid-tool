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

    /// <summary>How long an assembled report list stays fresh (seconds).</summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>Cap the page walk so a huge history can't blow the request budget.</summary>
    public int MaxPages { get; set; } = 15;

    public string GraphQlUrl => Host.TrimEnd('/') + "/api/v2/client";
}
