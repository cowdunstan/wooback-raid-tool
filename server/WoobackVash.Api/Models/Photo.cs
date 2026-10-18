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

    /// <summary>Where it was taken: a <c>PhotoPlaces</c> id, or null.</summary>
    public string? Place { get; set; }

    /// <summary>The boss it shows: a <c>PhotoPlaces</c> boss id, or null. When set, <see cref="Place"/> is its place.</summary>
    public string? Boss { get; set; }

    // ── What the AI suggested, kept unchanged so it can be scored against what people settle on ──

    /// <summary>Whether the tagging model actually answered for this photo. False → nothing to confirm or score.</summary>
    public bool AiTagged { get; set; }

    /// <summary>The tags the model suggested at upload, as a jsonb array. Never edited afterwards.</summary>
    public string AiTags { get; set; } = "[]";

    public string? AiPlace { get; set; }
    public string? AiBoss { get; set; }

    /// <summary>
    /// Whether a person has looked at the tags, place and boss and saved or accepted them. Until
    /// then the AI's picks show as suggestions; afterwards the photo counts towards the accuracy
    /// numbers, comparing <see cref="AiTags"/> with <see cref="Tags"/> and so on.
    /// </summary>
    public bool TagsConfirmed { get; set; }

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

/// <summary>
/// A record of something the content check refused: an upload, or a caption edit. It is how
/// officers see who tried to post what kind of thing, and how often.
///
/// Deliberately <b>no image</b>. A refused photo is exactly the content that shouldn't sit in
/// the guild's database, and keeping it would mean officers looking at it to review it. The
/// caption, the categories that tripped, and their scores say enough.
/// </summary>
public class PhotoRejection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UploaderUid { get; set; } = "";
    public string UploaderName { get; set; } = "";

    /// <summary><see cref="PhotoRejectionKind"/>: a new upload, or a caption edit on an existing photo.</summary>
    public string Kind { get; set; } = PhotoRejectionKind.Upload;

    /// <summary>The photo whose caption edit was refused; null for a refused upload, which never became one.</summary>
    public Guid? PhotoId { get; set; }

    public string Caption { get; set; } = "";

    /// <summary>The flagged categories, as a jsonb array, e.g. <c>["violence", "violence/graphic"]</c>.</summary>
    public string Categories { get; set; } = "[]";

    /// <summary>The top scores as jsonb: <c>[{ category, score }]</c>, highest first.</summary>
    public string Scores { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class PhotoRejectionKind
{
    public const string Upload = "upload";
    public const string Caption = "caption";
}

public static class PhotoStatus
{
    public const string Approved = "approved";
    public const string Pending = "pending";
}
