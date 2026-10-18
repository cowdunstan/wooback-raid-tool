namespace WoobackVash.Api.Services;

/// <summary>
/// The photobook's fixed tag vocabulary. The tagging model may only pick from these (they're
/// an enum in its JSON schema), and an edit may only set these, so filtering the book by tag
/// never fragments into "raid" / "raiding" / "raid night". <c>photobook.html</c> reads the list
/// from <c>GET /api/photos</c> rather than keeping its own copy.
///
/// Renaming an id orphans it on photos already tagged with it — they keep the raw id, which the
/// page shows as-is — so add freely, but rename only with a data fix.
/// </summary>
public static class PhotoTags
{
    public static readonly (string Id, string Label)[] All =
    [
        ("group-shot", "Group shot"),
        ("raid", "Raid"),
        ("dungeon", "Dungeon"),
        ("boss-kill", "Boss kill"),
        ("wipe", "Wipe"),
        ("loot", "Loot"),
        ("pvp", "PvP"),
        ("world-boss", "World boss"),
        ("questing", "Questing"),
        ("levelling", "Levelling milestone"),
        ("mount", "Mount"),
        ("gear", "Gear & transmog"),
        ("city", "City"),
        ("scenery", "Scenery"),
        ("guild-event", "Guild event"),
        ("funny", "Funny"),
        ("ui", "UI / addons"),
        ("irl", "Real life"),
    ];

    public static readonly HashSet<string> Ids = All.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>The most tags one photo carries — enough to describe it, few enough to filter on.</summary>
    public const int MaxPerPhoto = 6;

    /// <summary>Known ids only, de-duplicated, in vocabulary order, capped.</summary>
    public static List<string> Clean(IEnumerable<string>? tags)
    {
        var wanted = (tags ?? []).ToHashSet(StringComparer.Ordinal);
        return All.Select(t => t.Id).Where(wanted.Contains).Take(MaxPerPhoto).ToList();
    }
}
