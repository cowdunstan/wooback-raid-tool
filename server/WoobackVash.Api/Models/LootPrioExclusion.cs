namespace WoobackVash.Api.Models;

/// <summary>
/// An officer's decision to mute one <see cref="Character"/> on one item in the loot-prio
/// list: "this character does not roll on this item". Unlike <see cref="Character.Ignored"/>
/// (which drops a character everywhere), this is per-item — the sheet still hands the
/// character prio on everything else.
///
/// Keyed by the sheet's own item name (lowercased) rather than a wowhead id: the loot sheet
/// is written in names, and the prio page keys items by name throughout, so a name is what
/// the officer is actually muting. <see cref="ItemId"/> is carried along when known, for a
/// later id-based feature, but is not the identity.
/// </summary>
public class LootPrioExclusion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The character being muted for this item.</summary>
    public Guid CharacterId { get; set; }
    public Character? Character { get; set; }

    /// <summary>The sheet's item name, lowercased and trimmed — the identity of the mute.</summary>
    public required string ItemName { get; set; }

    /// <summary>The wowhead item id, when the page happened to have resolved it. Not the key.</summary>
    public long? ItemId { get; set; }

    /// <summary>Discord user id of the officer who set the mute, for an audit trail.</summary>
    public string? SetByUid { get; set; }

    public string? Reason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
