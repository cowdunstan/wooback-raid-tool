namespace WoobackVash.Api.Models;

/// <summary>
/// One application from the public WoW Forever recruitment form (<c>apply.html</c>). The
/// applicant has no Discord session, so there is no identity to upsert on: every submission is
/// its own row, and officers clear out spam or duplicates by hand. Rows only leave the server
/// through the officer-gated <c>GET /api/applications</c>.
///
/// Like <see cref="PollResponse"/>, the backend never hard-codes the questions — the shared
/// <c>apply-questions.js</c> owns them. Answers are split by kind so each stays a simple shape:
/// <see cref="Choices"/> holds picked option ids (radio, checkbox, dropdown, slider) as
/// <c>{ questionId: [value, …] }</c>, the same shape as <see cref="PollResponse.Answers"/>;
/// <see cref="Text"/> holds free text as <c>{ questionId: "…" }</c>. Both are jsonb (see
/// AppDbContext config).
/// </summary>
public class GuildApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The Discord username the applicant typed — how an officer reaches them. Unverified.</summary>
    public string DiscordHandle { get; set; } = "";

    /// <summary>Choice answers as jsonb: <c>{ questionId: [value, …] }</c>. See class remarks.</summary>
    public string Choices { get; set; } = "{}";

    /// <summary>Free-text answers as jsonb: <c>{ questionId: "…" }</c>. Blank answers are dropped.</summary>
    public string Text { get; set; } = "{}";

    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}
