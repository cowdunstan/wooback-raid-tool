namespace WoobackVash.Api.Models;

/// <summary>
/// One member's answers to the "WoW Forever" interest poll (<c>forever.html</c>). One row
/// per Discord user — the poll is a snapshot of intent, not a log, so a re-vote upserts on
/// <see cref="Uid"/> rather than piling up. Any signed-in member may vote and everyone sees
/// the aggregate tallies; the raw rows only leave the server through the officer-gated
/// <c>GET /api/poll/detail</c>, so who voted what stays private from the rank and file.
///
/// <see cref="Answers"/> is stored as jsonb (see AppDbContext config) the same way
/// <see cref="BoardLayout.State"/> and <see cref="CharacterGearSnapshot.Items"/> are — a raw
/// JSON string the endpoint serializes/deserializes, so the question set lives entirely in
/// the frontend and the backend stays generic: it tallies whatever keys it finds. The shape
/// is <c>{ questionId: [selectedOptionId, …] }</c> — every answer a list, so single- and
/// multi-select share one form.
/// </summary>
public class PollResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The voter's Discord user id — the identity of the response, unique.</summary>
    public string Uid { get; set; } = "";

    /// <summary>The voter's display name at the time they last voted; for debugging only.</summary>
    public string Name { get; set; } = "";

    /// <summary>Answers as jsonb: <c>{ questionId: [optionId, …] }</c>. See class remarks.</summary>
    public string Answers { get; set; } = "{}";

    /// <summary>Optional free-text "anything else" — kept out of the map so tallies stay clean.</summary>
    public string? Comment { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
