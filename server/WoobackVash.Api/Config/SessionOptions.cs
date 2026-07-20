namespace WoobackVash.Api.Config;

/// <summary>
/// Session-token signing config. Secret comes from env / user-secrets. Reuse the
/// SAME value the old Worker used (SESSION_SECRET) so tokens minted before and
/// after the cutover both verify — nobody gets logged out.
/// </summary>
public class SessionSigningOptions
{
    public const string SectionName = "Session";

    public string Secret { get; set; } = "";

    /// <summary>Session lifetime in seconds (default 6h, matching the Worker).</summary>
    public int TtlSeconds { get; set; } = 6 * 60 * 60;
}
