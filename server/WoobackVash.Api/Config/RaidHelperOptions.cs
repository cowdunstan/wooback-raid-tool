namespace WoobackVash.Api.Config;

/// <summary>
/// Raid-Helper proxy config. The API token is a secret (env / user-secrets); it is
/// attached server-side so it never reaches the browser — same as the Worker's
/// RH_TOKEN. Ported from raidhelper-proxy.worker.js.
/// </summary>
public class RaidHelperOptions
{
    public const string SectionName = "RaidHelper";

    public string ApiBase { get; set; } = "https://raid-helper.xyz/api";

    /// <summary>Raid-Helper API token, sent verbatim as the Authorization header.</summary>
    public string Token { get; set; } = "";
}
