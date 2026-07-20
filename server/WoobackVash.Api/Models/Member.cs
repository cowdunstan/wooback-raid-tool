namespace WoobackVash.Api.Models;

/// <summary>
/// One row per Discord user. Upserted at OAuth login (Phase 1) keyed on
/// <see cref="DiscordUserId"/>. Owns the player's WoW characters (main + alts).
/// </summary>
public class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Discord snowflake — the stable identity we key on.</summary>
    public required string DiscordUserId { get; set; }

    /// <summary>Discord username at last login (display only; can change).</summary>
    public string? DiscordUsername { get; set; }

    /// <summary>Preferred display name (global_name, falls back to username).</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// The member's per-server nickname in the guild (Discord's <c>nick</c>), when
    /// they have one set. Captured at login and by the roster import; preferred over
    /// <see cref="DisplayName"/> in the UI so the roster reads like the Discord
    /// member list. Null when the member has no server nickname.
    /// </summary>
    public string? Nickname { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the member last signed in via Discord OAuth. Null means "never signed
    /// in" — e.g. a row seeded by the roster import from the Discord member list.
    /// Set only on login, so the roster can distinguish real logins from imports.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; set; }

    public List<Character> Characters { get; set; } = new();
}
