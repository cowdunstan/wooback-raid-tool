using System.Text;

namespace WoobackVash.Api.Services;

/// <summary>
/// Which of the guild's two raids an item belongs to — <c>"P2"</c> (Serpentshrine Cavern /
/// Tempest Keep) or <c>"P3"</c> (Black Temple / Mount Hyjal), or <c>null</c> for anything the
/// sheets don't list. An award records no phase, and item level can't recover it — SSC/TK's
/// final bosses (Lady Vashj, Kael'thas) drop ilvl-141 gear that BT/MH sits on too, so the two
/// overlap exactly at the line. The authority on which item drops where is the guild's loot
/// sheets, so these sets are their item column, lifted once and baked in, matched on a folded
/// name so the apostrophes and hyphens Gargul and the sheet punctuate differently don't split
/// "Kael'thas" from "Kaelthas". Baked rather than fetched because a raid's drop table never
/// changes — only a new tier does, and that is a deploy either way. **Regenerate from the
/// sheets (the docs named in LootSheet:Docs) whenever the guild changes raids.**
///
/// Shared by the loot-stats phase toggle (which awards feed the hall of shame) and the
/// character sheet (the P2/P3 tag on each item).
/// </summary>
public static class LootPhases
{
    /// <summary>"P2", "P3", or null when neither set lists the item.</summary>
    public static string? Of(string? itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        var key = NameKey(itemName);
        if (P2Items.Contains(key)) return "P2";
        if (P3Items.Contains(key)) return "P3";
        return null;
    }

    private static readonly HashSet<string> P2Items = new(StringComparer.Ordinal)
    {
        "ancestral ring of conquest", "arcanite steam pistol", "ashes of alar", "band of alar",
        "band of the ranger general", "band of the vigilant", "band of vile aggression",
        "bands of the celestial archer", "bark gloves of ancient wisdom",
        "belt of one hundred deaths", "blackfathom warbands", "bloodsea brigands vest",
        "boots of courage unending", "boots of effortless striking", "boots of the resillient",
        "boots of the shifting nightmare", "bracers of eradication", "brighthelm of justice",
        "choker of animalistic fury", "claw of the phoenix", "cobra lash boots",
        "coral band of the revived", "coral barbed shoulderpads", "cord of screaming terrors",
        "cowl of the grand engineer", "crown of the sun", "earring of soulful meditation",
        "ethereum life staff", "fang of the leviathan", "fang of vashj",
        "fathom brooch of the tidewalker", "fathomstone", "fel reavers piston",
        "fel steel warhelm", "fire cord of the magus", "fire crest breastplate",
        "frayed tether of the drowned", "gauntlets of the sun king", "girdle of fallen stars",
        "girdle of the invulnerable", "girdle of the righteous path",
        "girdle of the tidal call", "girdle of zaetar", "glorious gauntlets of crestfall",
        "gloves of the searing grip", "glowing breastplate of truth",
        "gnarled chestpiece of the ancients", "greaves of the bloodwarder",
        "grove bands of remulos", "heartrazor", "idol of the crescent goddess",
        "illidari shoulderpads", "krakken heart breastplate", "leggings of murderous intent",
        "libram of absolute truth", "lightfathom scepter", "living root of the wildheart",
        "luminescent rod of the naaru", "mallet of the tides", "mantle of the elven kings",
        "mantle of the tireless tracker", "mindstorm wristbands", "netherbane",
        "orca hide boots", "pauldrons of the argent sentinel", "pauldrons of the wardancer",
        "pendant of the lost ages", "pendant of the perilous", "phoenix ring of rebirth",
        "phoenix wing cloak", "prism of inner calm", "ranger generals chestguard",
        "razor scale battlecloak", "ring of endless coils", "ring of lethality",
        "ring of sundered souls", "robe of hateful echoes", "rod of the sun king",
        "royal cloak of the sunstriders", "royal gauntlets of silvermoon", "runetotems mantle",
        "scarab of displacement", "serpent coil braid", "serpent spine longbow",
        "serpentshrine shuriken", "seventh ring of the tirisfalen",
        "sextant of unstable currents", "shoulderpads of the stranger", "solarions sapphire",
        "soul strider boots", "spyglass of the hidden fleet", "star soul breeches",
        "star strider boots", "sunhawk leggings", "sunshower light cloak",
        "talisman of the sun king", "talon of alar", "talon of azshara",
        "talon of the phoenix", "tempest strider boots", "thalassian wildercloak",
        "the nexus key", "the seal of danzalar", "tome of fiery redemption",
        "totem of the maelstrom", "trousers of the astromancer", "true aim stalker bands",
        "tsunami talisman", "twinblade of the phoenix", "vambraces of ending",
        "vanquished champion", "vanquished defender", "vanquished hero",
        "velvet boots of the guardian", "verdant sphere", "vestments of the sea witch",
        "void reaver greaves", "void star talisman", "wand of the forgotten star",
        "warboots of obliteration", "warp spring coil", "wildfury greatstaff", "world breaker",
        "worldstorm gauntlets", "wraps of purification", "wristguards of determination",
    };

    private static readonly HashSet<string> P3Items = new(StringComparer.Ordinal)
    {
        "amice of brilliant light", "anetherons noose", "angelistas sash",
        "antonidas aegis of rapt concentration", "apostle of argus", "archbishops slippers",
        "band of devastation", "band of the abyssal lord", "bands of the coming storm",
        "bastion of light", "beast tamers shoulders", "belt of divine guidance",
        "belt of primal majesty", "belt of seething fury", "belt of the crescent moon",
        "bindings of lightning reflexes", "black bow of the betrayer",
        "black featherlight boots", "blade of infamy", "blade of savagery",
        "blessed adamantite bracers", "blessed band of karabor", "blind seers icon",
        "blood cursed shoulderpads", "blood stained pauldrons", "blue suede shoes",
        "boneweave girdle", "boots of oceanic fury", "boots of the divine light",
        "botanists gloves of growth", "boundless agony", "bow stitched leggings",
        "bracers of martydom", "bracers of nimble thought", "bracers of renewed life",
        "bracers of the pathfinder", "bristleblitz striker", "bulwark of azzinoth",
        "cataclysms edge", "chestguard of relentless storms",
        "chestguard of the forgotten conqueror", "chestguard of the forgotten protector",
        "chestguard of the forgotten vanquisher", "choker of endless nightmares",
        "choker of serrated blades", "chronicle of dark secrets", "claw of molten fury",
        "cloak of the illidari council", "cowl of benevolence",
        "cowl of the illidari high lord", "crown of empowered fate",
        "crystal spire of karabor", "cuffs of devastation", "cursed vision of sargeras",
        "dawnsteel bracers", "dawnsteel shoulders", "deadly cuffs",
        "don alejandros money belt", "don rodrigos poncho", "dreadboots of the legion",
        "elunite empowered bracers", "enchanted leather sandals", "eternium shell bracers",
        "faceplate of the impenetrable", "felstone bulwark", "fist of molten fury",
        "fists of mukoa", "flashfire girdle", "focused mana bindings", "forest prowlers helm",
        "furious shackles", "garments of temperance", "gauntlets of enforcement",
        "girdle of hope", "girdle of lordaerons fallen", "girdle of mighty resolve",
        "girdle of stability", "girdle of the lightbearer", "glimmering steel mantle",
        "glory of the defender", "gloves of the forgotten conqueror",
        "gloves of the forgotten protector", "gloves of the forgotten vanquisher",
        "gloves of unfailing faith", "golden links of restoration", "grips of damnation",
        "grips of silent justice", "guise of the tidal lurker", "halberd of desolation",
        "hammer of atonement", "hammer of judgement", "hatefury mantle",
        "heartshatter breastplate", "hellfire encased pendant", "helm of soothing currents",
        "helm of the forgotten conqueror", "helm of the forgotten protector",
        "helm of the forgotten vanquisher", "helm of the illidari shatterer",
        "howling wind bracers", "idol of the white stag", "illidari runeshield",
        "insidious bands", "kazrogals hardened heart", "kilt of immortal nature",
        "leggings of channeled elements", "leggings of devastation",
        "leggings of divine retribution", "leggings of endless rage", "leggings of eternity",
        "leggings of the forgotten conqueror", "leggings of the forgotten protector",
        "leggings of the forgotten vanquisher", "legionkiller", "living earth bindings",
        "living earth shoulders", "madness of the betrayer", "mail of fevered pursuit",
        "mantle of darkness", "mantle of nimble thought", "memento of tyrande",
        "messenger of fate", "midnight chestguard", "mymidons treads",
        "naaru blessed life rod", "nadinas pendant of purity", "naturalists preserving cinch",
        "naturewardens treads", "nether shadow tunic", "nethervoid cloak",
        "pauldrons of abyssal fury", "pauldrons of the forgotten conqueror",
        "pauldrons of the forgotten protector", "pauldrons of the forgotten vanquisher",
        "pearl inlaid boots", "pendant of titans", "pepes shroud of pacification",
        "pillagers gauntlets", "pillar of ferocity", "praetorians legguards",
        "quickstrider moccasins", "razorfury mantle", "rejuvenating bracers",
        "rifle of the stoic guardian", "ring of ancient knowledge", "ring of calming waves",
        "ring of captured storms", "ring of deceitful intent", "rising tide",
        "robe of the shadow council", "robes of rhonin", "saviors grasp",
        "scepter of purification", "shadow walkers cord", "shadowmasters boots",
        "shadowmoon destroyers drape", "shadowmoon insignia", "shady dealers pantaloons",
        "shard of azzinoth", "shoulderpads of renewed life", "shoulders of lightning reflexes",
        "shoulders of the hidden predator", "shroud of forgiveness",
        "shroud of the final stand", "shroud of the highborne", "slippers of the seacaller",
        "softstep boots of tracking", "soul cleaver", "spiritwalker gauntlets",
        "staff of immaculate recovery", "stillwater boots", "stormrage signet ring",
        "sun touched chain leggings", "swiftheal mantle", "swiftheal wraps",
        "swiftsteel bludgeon", "swiftsteel bracers", "swiftsteel shoulders",
        "swiftstrike bracers", "swiftstrike shoulders", "syphon of the nathrezim",
        "tempest of chaos", "the brutalizer", "the maelstroms fury", "the seekers wristguards",
        "the skull of guldan", "the unbreakable will", "the wavemenders mantle",
        "tide stompers greaves", "tome of the lightbringer", "torch of the damned",
        "totem of ancestral guidance", "touch of inspiration", "trackers blade",
        "translucent spellthread necklace", "treads of the den mother",
        "twisted blades of zarak", "unstoppable aggressors ring", "valestalker girdle",
        "veil of turning leaves", "vest of mounting assault", "waistwrap of infinity",
        "wand of prismatic focus", "warglaive of azzinoth", "wraps of precise flight",
        "wristbands of divine influence", "zhardoom greatstaff of the devourer",
    };

    // Fold an item name to a match key: lower-case, letters and digits only, single spaces.
    // Punctuation the sheet and Gargul disagree on (apostrophes, hyphens) drops out, so a
    // baked name and an award's name meet. The P2Items / P3Items sets are already folded.
    private static string NameKey(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (var ch in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) { if (pendingSpace) { sb.Append(' '); pendingSpace = false; } sb.Append(ch); }
            else if (char.IsWhiteSpace(ch) || ch == '-') { if (sb.Length > 0) pendingSpace = true; }
        }
        return sb.ToString();
    }
}
