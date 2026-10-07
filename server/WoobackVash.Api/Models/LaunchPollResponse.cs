namespace WoobackVash.Api.Models;

/// <summary>
/// One member's answers to the WoW Forever launch poll (<c>launch.html</c>): their main and
/// spec, alts, professions, whether they'll be 60 when raids open, and which raid group they
/// want. One row per Discord user — like <see cref="PollResponse"/> it is a snapshot of intent,
/// so a resubmission upserts on <see cref="Uid"/>. Rows only leave the server through the
/// officer-gated <c>GET /api/launch-poll/responses</c>; a member only ever reads back their own.
///
/// The split into <see cref="Choices"/> and <see cref="Text"/> is the same as
/// <see cref="GuildApplication"/>'s, and for the same reason: the question set lives in
/// <c>launch-questions.js</c> and the backend stores whatever ids it is sent. Both are jsonb
/// (see AppDbContext config).
/// </summary>
public class LaunchPollResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The member's Discord user id — the identity of the response, unique.</summary>
    public string Uid { get; set; } = "";

    /// <summary>The member's Discord display name when they last saved; what the review page shows.</summary>
    public string Name { get; set; } = "";

    /// <summary>Choice answers as jsonb: <c>{ questionId: [value, …] }</c>.</summary>
    public string Choices { get; set; } = "{}";

    /// <summary>Free-text answers as jsonb: <c>{ questionId: "…" }</c>. Blank answers are dropped.</summary>
    public string Text { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
