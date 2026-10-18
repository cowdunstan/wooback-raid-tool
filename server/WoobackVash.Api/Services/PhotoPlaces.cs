namespace WoobackVash.Api.Services;

/// <summary>
/// Where a photobook photo was taken, and which boss it shows: a fixed list of Classic (WoW
/// Forever) raids with their bosses, dungeons, battlegrounds, cities, zones and world bosses.
/// Like <see cref="PhotoTags"/>, the tagging model may only pick from these (they're enums in
/// its JSON schema), so "Molten Core" never fragments into "MC" or "molten core".
///
/// This tracks the current expansion. When the guild moves on, replace it along with
/// <c>WOWHEAD_DOMAIN</c> and <c>LEVEL_CAP</c> (see CLAUDE.md). An id that disappears is kept
/// raw on photos already tagged with it, and the page shows it as-is.
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
        // Raids, with their bosses.
        P("molten-core", "Molten Core", "raid",
            ("lucifron", "Lucifron"), ("magmadar", "Magmadar"), ("gehennas", "Gehennas"), ("garr", "Garr"),
            ("shazzrah", "Shazzrah"), ("baron-geddon", "Baron Geddon"), ("sulfuron-harbinger", "Sulfuron Harbinger"),
            ("golemagg", "Golemagg the Incinerator"), ("majordomo-executus", "Majordomo Executus"), ("ragnaros", "Ragnaros")),
        P("onyxias-lair", "Onyxia's Lair", "raid", ("onyxia", "Onyxia")),
        P("blackwing-lair", "Blackwing Lair", "raid",
            ("razorgore", "Razorgore the Untamed"), ("vaelastrasz", "Vaelastrasz the Corrupt"), ("broodlord-lashlayer", "Broodlord Lashlayer"),
            ("firemaw", "Firemaw"), ("ebonroc", "Ebonroc"), ("flamegor", "Flamegor"), ("chromaggus", "Chromaggus"), ("nefarian", "Nefarian")),
        P("zul-gurub", "Zul'Gurub", "raid",
            ("jeklik", "High Priestess Jeklik"), ("venoxis", "High Priest Venoxis"), ("marli", "High Priestess Mar'li"),
            ("mandokir", "Bloodlord Mandokir"), ("edge-of-madness", "Edge of Madness"), ("gahzranka", "Gahz'ranka"),
            ("thekal", "High Priest Thekal"), ("arlokk", "High Priestess Arlokk"), ("jindo", "Jin'do the Hexxer"), ("hakkar", "Hakkar")),
        P("ruins-of-ahnqiraj", "Ruins of Ahn'Qiraj", "raid",
            ("kurinnaxx", "Kurinnaxx"), ("rajaxx", "General Rajaxx"), ("moam", "Moam"), ("buru", "Buru the Gorger"),
            ("ayamiss", "Ayamiss the Hunter"), ("ossirian", "Ossirian the Unscarred")),
        P("temple-of-ahnqiraj", "Temple of Ahn'Qiraj", "raid",
            ("skeram", "The Prophet Skeram"), ("bug-trio", "Silithid Royalty"), ("sartura", "Battleguard Sartura"),
            ("fankriss", "Fankriss the Unyielding"), ("viscidus", "Viscidus"), ("huhuran", "Princess Huhuran"),
            ("twin-emperors", "Twin Emperors"), ("ouro", "Ouro"), ("cthun", "C'Thun")),
        P("naxxramas", "Naxxramas", "raid",
            ("anubrekhan", "Anub'Rekhan"), ("faerlina", "Grand Widow Faerlina"), ("maexxna", "Maexxna"),
            ("noth", "Noth the Plaguebringer"), ("heigan", "Heigan the Unclean"), ("loatheb", "Loatheb"),
            ("razuvious", "Instructor Razuvious"), ("gothik", "Gothik the Harvester"), ("four-horsemen", "The Four Horsemen"),
            ("patchwerk", "Patchwerk"), ("grobbulus", "Grobbulus"), ("gluth", "Gluth"), ("thaddius", "Thaddius"),
            ("sapphiron", "Sapphiron"), ("kelthuzad", "Kel'Thuzad")),

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

        P("warsong-gulch", "Warsong Gulch", "battleground"),
        P("arathi-basin", "Arathi Basin", "battleground"),
        P("alterac-valley", "Alterac Valley", "battleground"),

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
    ];

    public static readonly Dictionary<string, Place> ById =
        All.ToDictionary(p => p.Id, StringComparer.Ordinal);

    /// <summary>Boss id → the boss and the place it's fought in.</summary>
    public static readonly Dictionary<string, (Boss Boss, Place Place)> BossById =
        All.SelectMany(p => p.Bosses.Select(b => (b, p))).ToDictionary(x => x.b.Id, x => (x.b, x.p), StringComparer.Ordinal);

    /// <summary>
    /// Known ids only, and consistent: a boss decides its own place (Ragnaros is always Molten
    /// Core), and a boss without a recognised place still fills it in. Unknown ids become null.
    /// </summary>
    public static (string? Place, string? Boss) Clean(string? place, string? boss)
    {
        if (!string.IsNullOrEmpty(boss) && BossById.TryGetValue(boss, out var b))
            return (b.Place.Id, b.Boss.Id);
        return (!string.IsNullOrEmpty(place) && ById.ContainsKey(place) ? place : null, null);
    }
}
