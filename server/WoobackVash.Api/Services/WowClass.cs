namespace WoobackVash.Api.Services;

/// <summary>
/// WoW playable-class id → the lower-case class string the rest of the app keys on
/// (matching the Warcraft Logs subType, e.g. "warrior", "deathknight"). Both the
/// attendance import (from a log's winner class) and the guild-roster import (from the
/// Blizzard roster's class id) speak these ids, so the mapping lives in one place.
/// </summary>
public static class WowClass
{
    public static string? Name(int id) => id switch
    {
        1 => "warrior", 2 => "paladin", 3 => "hunter", 4 => "rogue",
        5 => "priest", 6 => "deathknight", 7 => "shaman", 8 => "mage",
        9 => "warlock", 10 => "monk", 11 => "druid", 12 => "demonhunter",
        _ => null
    };
}
