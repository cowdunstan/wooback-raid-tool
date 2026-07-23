namespace WoobackVash.Api.Models;

/// <summary>
/// A guild-roster character the last Blizzard sync found on the roster but that we
/// hold no <see cref="Character"/> for — a staging row an officer can turn into a real
/// character from the roster page. This is a scratch table, not a record: every sync
/// clears it and repopulates it from the current roster, so it only ever describes the
/// most recent pull. Importing a row creates the character and deletes the row.
/// </summary>
public class RosterImportCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Character name as Blizzard spells it — the key, one per realm.</summary>
    public required string Name { get; set; }

    /// <summary>Realm slug from the roster (the guild is single-realm, so usually the
    /// one realm). Copied onto the character on import.</summary>
    public string? RealmSlug { get; set; }

    /// <summary>Guild rank from the roster (0 = guild master), stamped on import.</summary>
    public int Rank { get; set; }

    /// <summary>Character level from the roster — shown so officers can tell a main
    /// from a fresh alt before importing.</summary>
    public int Level { get; set; }

    /// <summary>Class resolved from the roster's class id to the lower-case name the
    /// rest of the app keys on ("warrior"), or null when unknown.</summary>
    public string? Class { get; set; }

    /// <summary>When the sync that produced this row ran.</summary>
    public DateTimeOffset StagedAt { get; set; } = DateTimeOffset.UtcNow;
}
