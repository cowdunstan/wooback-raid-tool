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
    /// <summary>
    /// Each tag with the one-line meaning the tagging model is given. Without the meanings it
    /// read the labels loosely — live, it put "UI / addons" and "Real life" on UI-less game
    /// screenshots — so each says what it is, and for the easily-confused ones what it isn't.
    /// </summary>
    public static readonly (string Id, string Label, string Meaning)[] All =
    [
        ("group-shot", "Group shot", "several players posing together for the picture"),
        ("raid", "Raid", "inside a raid instance, or a raid group fighting"),
        ("dungeon", "Dungeon", "inside a five-player dungeon"),
        ("boss-kill", "Boss kill", "a boss being killed or lying dead, or a victory pose over one"),
        ("wipe", "Wipe", "the group lying dead after failing a fight"),
        ("loot", "Loot", "a loot window, or someone showing off an item they just got"),
        ("pvp", "PvP", "fighting other players: a battleground, duel or world PvP"),
        ("world-boss", "World boss", "a world boss out in the open world"),
        ("questing", "Questing", "doing quests: quest givers, quest text or quest objectives"),
        ("levelling", "Levelling milestone", "a level-up, or reaching a level worth celebrating"),
        ("mount", "Mount", "a mount is the point of the picture"),
        ("gear", "Gear & transmog", "showing off armour or how a character looks"),
        ("city", "City", "in a capital city or a town"),
        ("scenery", "Scenery", "the landscape or view is the point, not the people"),
        ("guild-event", "Guild event", "an organised guild occasion, such as a party, race or ceremony — not just players posing together"),
        ("funny", "Funny", "something plainly meant to be comic"),
        ("ui", "UI / addons", "the game's interface itself is the subject: action bars, addons, a damage meter — not names floating over characters"),
        ("irl", "Real life", "a real-world photo of people or things, not a game screenshot"),
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
