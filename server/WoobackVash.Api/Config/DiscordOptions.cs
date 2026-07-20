namespace WoobackVash.Api.Config;

/// <summary>
/// Discord OAuth + guild gate configuration. Non-secret values live in
/// appsettings ("Discord" section); ClientSecret comes from env / user-secrets.
/// Ported from the constants in the old raidhelper-proxy.worker.js.
/// </summary>
public class DiscordOptions
{
    public const string SectionName = "Discord";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string GuildId { get; set; } = "";

    /// <summary>Any one of these Discord role ids grants officer access.</summary>
    public string[] OfficerRoleIds { get; set; } = Array.Empty<string>();

    /// <summary>Broader home-page access (officers implicitly have it too).</summary>
    public string HomeRoleId { get; set; } = "";

    public string Scopes { get; set; } = "identify guilds.members.read";

    /// <summary>This API's own public base URL; the OAuth redirect_uri is ApiBase + /auth/callback.</summary>
    public string ApiBase { get; set; } = "";

    /// <summary>Where the static site is served (GitHub Pages). Login redirects back here.</summary>
    public string AppBase { get; set; } = "https://wooback.info";

    public string RedirectUri => ApiBase.TrimEnd('/') + "/auth/callback";
}
