namespace WoobackVash.Api.Services;

/// <summary>
/// Where a photobook photo was taken, and which boss it shows: a fixed list of raids with their
/// bosses, dungeons, battlegrounds, cities, zones and world bosses. Like <see cref="PhotoTags"/>,
/// the tagging model may only pick from these (they're enums in its JSON schema), so "Hyjal
/// Summit" never fragments into "Hyjal" or "hyjal summit".
///
/// The guild plays World of Warcraft: Forever, a new expansion that keeps most of Classic's
/// world. This is Classic's dungeons, battlegrounds, cities and zones, plus Forever's new ones
/// and its launch raids, from the pre-launch coverage of October 2026 (Warcraft Wiki's Forever
/// page). Classic raids other than Onyxia's Lair aren't in Forever's list, so they're left out.
/// Hyjal Summit's boss list is incomplete. A place missing from here comes back from the model as
/// "none", never as the nearest match, so a gap costs a filter, not a wrong label. Use each name
/// exactly as the game shows it on screen — the model matches on it.
///
/// It tracks the current expansion (see CLAUDE.md). An id that disappears is kept raw on photos
/// already tagged with it, and the page shows it as-is.
/// </summary>
public static class PhotoPlaces
{
    public record Boss(string Id, string Label);
    public record Place(string Id, string Label, string Kind, Boss[] Bosses);

    /// <summary>The kinds, in the order the page groups them.</summary>
    public static readonly (string Id, string Label)[] Kinds =
    [
        ("raid", "Raids"), ("world", "World bosses"), ("dungeon", "Dungeons"),
        ("battleground", "Battlegrounds"), ("city", "Cities"), ("zone", "Zones"),
    ];

    private static Place P(string id, string label, string kind, params (string Id, string Label)[] bosses) =>
        new(id, label, kind, bosses.Select(b => new Boss(b.Id, b.Label)).ToArray());

    public static readonly Place[] All =
    [
        // Raids, with their bosses. Forever's launch raids are its own two plus Onyxia's Lair; the
        // other Classic raids aren't on its list (a "revamped iconic raid" is on the roadmap for
        // summer 2027 — add it back here when it arrives).
        P("onyxias-lair", "Onyxia's Lair", "raid", ("onyxia", "Onyxia")),
        P("barrow-deeps", "Barrow Deeps", "raid",
            ("deepscar-matriarch", "Deepscar Matriarch"), ("khalith", "Khalith the Dreadspinner"), ("amethrax", "Amethrax"),
            ("ravus-and-darlissa", "Ravus and Darlissa"), ("elder-tangleclaw", "Elder Tangleclaw"), ("well-of-sorrow", "Well of Sorrow"),
            ("dellynar-songwood", "Del'lynar Songwood"), ("sonya-darkhallow", "Sonya Darkhallow")),
        // Reportedly 13 encounters; these are the ones named so far. A boss missing here comes back
        // as "none", so add the rest as they're published.
        P("hyjal-summit", "Hyjal Summit", "raid",
            ("bandalar", "Bandalar"), ("time-lost-battalion", "Time-Lost Battalion"), ("old-gloomlurker", "Old Gloomlurker"),
            ("kathris", "Kathris the Haunted"), ("elder-minderel", "Elder Minderel"), ("council-of-thorns", "Council of Thorns"),
            ("the-wild-king", "The Wild King"), ("ancient-of-decay", "Ancient of Decay"), ("sylvestris-dusksong", "Sylvestris Dusksong"),
            ("nythus", "Nythus the Dreambound")),

        // World bosses: out in the open world, so they share one "place".
        P("world-bosses", "World bosses", "world",
            ("azuregos", "Azuregos"), ("kazzak", "Lord Kazzak"), ("ysondre", "Ysondre"), ("lethon", "Lethon"),
            ("emeriss", "Emeriss"), ("taerar", "Taerar")),

        // Dungeons. No bosses listed — the tags carry "boss kill" for these.
        P("ragefire-chasm", "Ragefire Chasm", "dungeon"),
        P("wailing-caverns", "Wailing Caverns", "dungeon"),
        P("deadmines", "The Deadmines", "dungeon"),
        P("shadowfang-keep", "Shadowfang Keep", "dungeon"),
        P("blackfathom-deeps", "Blackfathom Deeps", "dungeon"),
        P("stockade", "The Stockade", "dungeon"),
        P("gnomeregan", "Gnomeregan", "dungeon"),
        P("razorfen-kraul", "Razorfen Kraul", "dungeon"),
        P("scarlet-monastery", "Scarlet Monastery", "dungeon"),
        P("razorfen-downs", "Razorfen Downs", "dungeon"),
        P("uldaman", "Uldaman", "dungeon"),
        P("zul-farrak", "Zul'Farrak", "dungeon"),
        P("maraudon", "Maraudon", "dungeon"),
        P("sunken-temple", "Sunken Temple", "dungeon"),
        P("blackrock-depths", "Blackrock Depths", "dungeon"),
        P("lower-blackrock-spire", "Lower Blackrock Spire", "dungeon"),
        P("upper-blackrock-spire", "Upper Blackrock Spire", "dungeon"),
        P("dire-maul", "Dire Maul", "dungeon"),
        P("scholomance", "Scholomance", "dungeon"),
        P("stratholme", "Stratholme", "dungeon"),
        // Forever's new dungeons, by level. Spelled as Warcraft Wiki has them; sources differ on
        // a few ("Hall of Thanes", "Alcaz Island Prison", "Krol'Dok"), so check against the game.
        P("hall-of-thanes", "The Hall of Thanes", "dungeon"),
        P("ruins-of-lordaeron", "Ruins of Lordaeron", "dungeon"),
        P("excavation-site", "Excavation Site: Wetlands", "dungeon"),
        P("city-of-dalaran", "City of Dalaran", "dungeon"),
        P("drowned-city", "The Drowned City", "dungeon"),
        P("kroldok-stronghold", "Krol'dok Stronghold", "dungeon"),
        P("alcaz-prison", "Alcaz Prison", "dungeon"),
        P("blackmaw-hold", "Blackmaw Hold", "dungeon"),
        P("shapers-terrace", "Shaper's Terrace", "dungeon"),

        P("warsong-gulch", "Warsong Gulch", "battleground"),
        P("arathi-basin", "Arathi Basin", "battleground"),
        P("alterac-valley", "Alterac Valley", "battleground"),
        P("darkspear-islands", "Darkspear Islands", "battleground"),

        P("stormwind", "Stormwind City", "city"),
        P("ironforge", "Ironforge", "city"),
        P("darnassus", "Darnassus", "city"),
        P("orgrimmar", "Orgrimmar", "city"),
        P("thunder-bluff", "Thunder Bluff", "city"),
        P("undercity", "Undercity", "city"),

        // Eastern Kingdoms.
        P("elwynn-forest", "Elwynn Forest", "zone"), P("dun-morogh", "Dun Morogh", "zone"),
        P("tirisfal-glades", "Tirisfal Glades", "zone"), P("westfall", "Westfall", "zone"),
        P("loch-modan", "Loch Modan", "zone"), P("silverpine-forest", "Silverpine Forest", "zone"),
        P("redridge-mountains", "Redridge Mountains", "zone"), P("duskwood", "Duskwood", "zone"),
        P("wetlands", "Wetlands", "zone"), P("hillsbrad-foothills", "Hillsbrad Foothills", "zone"),
        P("alterac-mountains", "Alterac Mountains", "zone"), P("arathi-highlands", "Arathi Highlands", "zone"),
        P("stranglethorn-vale", "Stranglethorn Vale", "zone"), P("badlands", "Badlands", "zone"),
        P("swamp-of-sorrows", "Swamp of Sorrows", "zone"), P("the-hinterlands", "The Hinterlands", "zone"),
        P("searing-gorge", "Searing Gorge", "zone"), P("burning-steppes", "Burning Steppes", "zone"),
        P("blasted-lands", "Blasted Lands", "zone"), P("deadwind-pass", "Deadwind Pass", "zone"),
        P("western-plaguelands", "Western Plaguelands", "zone"), P("eastern-plaguelands", "Eastern Plaguelands", "zone"),
        // Kalimdor.
        P("durotar", "Durotar", "zone"), P("mulgore", "Mulgore", "zone"), P("teldrassil", "Teldrassil", "zone"),
        P("darkshore", "Darkshore", "zone"), P("the-barrens", "The Barrens", "zone"),
        P("stonetalon-mountains", "Stonetalon Mountains", "zone"), P("ashenvale", "Ashenvale", "zone"),
        P("thousand-needles", "Thousand Needles", "zone"), P("desolace", "Desolace", "zone"),
        P("dustwallow-marsh", "Dustwallow Marsh", "zone"), P("feralas", "Feralas", "zone"),
        P("tanaris", "Tanaris", "zone"), P("azshara", "Azshara", "zone"), P("felwood", "Felwood", "zone"),
        P("ungoro-crater", "Un'Goro Crater", "zone"), P("silithus", "Silithus", "zone"),
        P("winterspring", "Winterspring", "zone"), P("moonglade", "Moonglade", "zone"),
        // Forever's new zones. Zephras Isle is the Skyborne elves' starting area.
        P("mount-hyjal", "Mount Hyjal", "zone"), P("riverglades", "Riverglades", "zone"),
        P("shendralas", "Shen'dralas", "zone"), P("zephras-isle", "Zephras Isle", "zone"),
    ];

    public static readonly Dictionary<string, Place> ById =
        All.ToDictionary(p => p.Id, StringComparer.Ordinal);

    /// <summary>Boss id → the boss and the place it's fought in.</summary>
    public static readonly Dictionary<string, (Boss Boss, Place Place)> BossById =
        All.SelectMany(p => p.Bosses.Select(b => (b, p))).ToDictionary(x => x.b.Id, x => (x.b, x.p), StringComparer.Ordinal);

    /// <summary>
    /// Known ids only, and consistent: a boss decides its own place (Sonya Darkhallow is always Barrow
    /// Deeps), and a boss without a recognised place still fills it in. Unknown ids become null.
    /// </summary>
    public static (string? Place, string? Boss) Clean(string? place, string? boss)
    {
        if (!string.IsNullOrEmpty(boss) && BossById.TryGetValue(boss, out var b))
            return (b.Place.Id, b.Boss.Id);
        return (!string.IsNullOrEmpty(place) && ById.ContainsKey(place) ? place : null, null);
    }
}
