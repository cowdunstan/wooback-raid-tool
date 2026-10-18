namespace WoobackVash.Api.Models;

/// <summary>
/// One photobook upload (<c>photobook.html</c>). The metadata lives here and the image bytes in
/// <see cref="PhotoImage"/>, a separate table, so listing the book never drags megabytes of
/// JPEG out of Postgres.
///
/// A photo the moderation check flags outright is refused and never stored, so this table only
/// ever holds <see cref="PhotoStatus.Approved"/> photos and <see cref="PhotoStatus.Pending"/> ones
/// waiting on an officer: borderline scores, or no moderation verdict at all (no API key, or
/// OpenAI was down).
/// </summary>
public class Photo
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The uploader's Discord user id and display name at upload time.</summary>
    public string UploaderUid { get; set; } = "";
    public string UploaderName { get; set; } = "";

    public string Caption { get; set; } = "";

    /// <summary>Tag ids from <c>PhotoTags</c>, as a jsonb array. AI-suggested, then hand-edited.</summary>
    public string Tags { get; set; } = "[]";

    /// <summary>The tagging model's one-sentence description, used as the image's alt text.</summary>
    public string AltText { get; set; } = "";

    public string Status { get; set; } = PhotoStatus.Pending;

    /// <summary>
    /// What moderation said, as jsonb: <c>{ checked, flagged, reason, top: [{ category, score }] }</c>.
    /// Kept so the officer reviewing a held photo can see why it was held.
    /// </summary>
    public string Moderation { get; set; } = "{}";

    public int Width { get; set; }
    public int Height { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PhotoImage? Image { get; set; }
}

/// <summary>The bytes of a <see cref="Photo"/>: the full image and its grid thumbnail, both JPEG.</summary>
public class PhotoImage
{
    public Guid PhotoId { get; set; }
    public byte[] Full { get; set; } = [];
    public byte[] Thumb { get; set; } = [];
}

public static class PhotoStatus
{
    public const string Approved = "approved";
    public const string Pending = "pending";
}
