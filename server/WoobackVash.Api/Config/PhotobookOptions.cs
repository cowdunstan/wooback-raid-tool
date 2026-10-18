namespace WoobackVash.Api.Config;

/// <summary>
/// Config for the photobook's two OpenAI calls: moderation (free) and tag suggestion (a few
/// hundredths of a cent per photo on a nano model). The API key is a secret, so it lives in
/// user-secrets locally and a Fly secret (<c>Photobook__OpenAiApiKey</c>) in production. Left
/// empty, uploads still work but every photo lands in the officer review queue untagged — nothing
/// is ever published unchecked.
/// </summary>
public class PhotobookOptions
{
    public const string SectionName = "Photobook";

    public string OpenAiApiKey { get; set; } = "";

    public string ApiBase { get; set; } = "https://api.openai.com/v1";

    public string ModerationModel { get; set; } = "omni-moderation-latest";

    /// <summary>The vision model that picks tags from <c>PhotoTags</c>. Empty skips tagging.</summary>
    public string TaggingModel { get; set; } = "gpt-5-nano";

    /// <summary>
    /// The image detail the tagging model sees: "high" (default) or "low". Low is a single 512px
    /// tile — cheaper, but too small to read the minimap's zone name or a boss frame, so the place
    /// and boss picks get much worse.
    /// </summary>
    public string TaggingImageDetail { get; set; } = "high";

    /// <summary>
    /// How much a gpt-5 model reasons before tagging: "minimal", "low", "medium" or "high". More
    /// thinking costs output tokens and about a second. Low by default: in live runs on UI-less
    /// screenshots, minimal named a familiar city for a dungeon it couldn't place and padded the
    /// tags, while low answered "none" and kept to two or three.
    /// </summary>
    public string TaggingReasoningEffort { get; set; } = "low";

    /// <summary>
    /// Any moderation category scoring at or above this — without being flagged outright —
    /// holds the photo for officer review instead of publishing it. A category listed in
    /// <see cref="CategoryReviewThresholds"/> uses its own number instead.
    /// </summary>
    public double ReviewThreshold { get; set; } = 0.2;

    /// <summary>
    /// Per-category overrides of <see cref="ReviewThreshold"/>, keyed by OpenAI's category name
    /// ("violence", "violence/graphic", "sexual", …). Set in appsettings.json, where "violence" is
    /// raised: WoW screenshots are full of skulls, weapons and fights, and a harmless group shot by
    /// some giant skull props scored 0.42 and sat in the review queue. "violence/graphic" (gore)
    /// is a separate category and isn't loosened by it. OpenAI's own "flagged" verdict still
    /// refuses an upload whatever these say.
    /// </summary>
    public Dictionary<string, double> CategoryReviewThresholds { get; set; } = new();

    public double ReviewThresholdFor(string category) =>
        CategoryReviewThresholds.TryGetValue(category, out var t) ? t : ReviewThreshold;

    public int RequestTimeoutSeconds { get; set; } = 30;
}
