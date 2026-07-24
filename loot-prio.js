/* ───────────────────────── Loot prio ─────────────────────────
   One Raid-Helper signup + one raid of the guild loot sheets → who holds prio
   on every item that drops tonight.

   The sheet already answers "which spec wants this item, in what order"
   (Cuffs of Devastation → Arcane > Balance > Ele > Destro). The signup answers
   "which specs are actually here". This page is the join: it reads the sheet's
   spec tokens, matches them against the characters signed up, and prints an
   ordered candidate list per item per boss.

   Nothing is saved on the server — the whole page is derived from the signup, the
   sheet, the roster links, and the gear/loot history we already store. Only the
   picked event and raid persist, in localStorage, so a refresh mid-raid is cheap.

   The shared Raid-Helper plumbing (event fetching, class tables, spec inference)
   is RH in menu.js, which board.html and groups.html use too. */

const STORE_KEY = 'vashj_loot_prio';

/* What each raid is made of, in the sheets sheet.html embeds, read through the
   backend's /sheet/loot proxy (Google sends no CORS header, so the browser can't
   fetch the exports itself).

   The guild raids two phases at once and keeps a document per phase, laid out
   differently, so a raid names its `doc` and the tabs that make it up:

     • P3 (?doc=p3) puts a whole raid on one tab and banners each boss inside it,
       so one tab with no `section` — parseRaidTab reads the headings off the
       boss rows. Its other tabs (tier-set TLDR, shadow-res crafting, BIS
       sources) aren't boss/item grids, so they aren't offered.
     • P2 (?doc=p2) is a tab per boss with no banner row, so every tab carries the
       `section` it should be filed under. Trash is a tab like any other, and the
       Tier Sets tab is appended to both raids because tokens drop in both.

   A P2 boss tab names its tier drop by the bare token set ("Vanquished Defender")
   and leaves the slot implicit — the boss is the slot. `tierSlot` is that slot, so
   the boss row can read the real item ("Gloves of the Vanquished Defender") rather
   than the token. The Tier Sets tab needs none: it banners the slot itself. */
const RAID_TABS = [
  { key:'bt', label:'Black Temple (P3)', doc:'p3', tabs:[{ gid:'1226096003' }] },
  { key:'mh', label:'Mount Hyjal (P3)',  doc:'p3', tabs:[{ gid:'1714599159' }] },

  { key:'ssc', label:'Serpentshrine Cavern (P2)', doc:'p2', tabs:[
      { gid:'324929599',  section:'Hydross the Unstable' },
      { gid:'8551419',    section:'The Lurker Below' },
      { gid:'420793116',  section:'Leotheras the Blind',       tierSlot:'Gloves' },
      { gid:'152003569',  section:'Fathom-Lord Karathress',    tierSlot:'Leggings' },
      { gid:'821616370',  section:'Morogrim Tidewalker' },
      { gid:'1241401949', section:'Lady Vashj',                tierSlot:'Helm' },
      { gid:'1405398559', section:'Serpentshrine Cavern trash' },
      { gid:'649478556',  section:'Tier sets' }
  ] },
  { key:'tk', label:'Tempest Keep (P2)', doc:'p2', tabs:[
      { gid:'1032392127', section:'Al’ar' },
      { gid:'1216905087', section:'Void Reaver',               tierSlot:'Pauldrons' },
      { gid:'2111072609', section:'High Astromancer Solarian' },
      { gid:'1959598972', section:'Kael’thas Sunstrider',      tierSlot:'Chestguard' },
      { gid:'1626986966', section:'Tempest Keep trash' },
      { gid:'649478556',  section:'Tier sets' }
  ] }
];

// The phase each raid's picker option is grouped under.
const PHASE_LABELS = { p3:'Phase 3', p2:'Phase 2' };

/* Sheet token → the specs it means. A list, because two of the guild's tokens are
   genuinely ambiguous and expanding them is more honest than guessing:
     • "Resto" is the druid or the shaman,
     • "Holy" is the paladin per the sheet's own legend — but rows like
       "Holy > Resto" on a cloth piece plainly mean the priest, so both are
       offered and the note column settles it. "Holy Priest" is exact.

   One table serves both phases. P2 writes the same specs out in full where P3
   abbreviates ("Retribution" for "Ret", "Feral Tank" for "Bear"), and those
   phrases are longer than every P3 key — and an exact match is tried before any
   substring — so adding them changes nothing about how P3 reads.

   `spec` null means any spec of that class. `role` narrows a token to one side of
   a spec Raid-Helper doesn't split (bear vs cat both sign up "feral"), and only
   applies when the candidate's role is known.

   Spec names are the ones RH.mapSignups produces: lowercased, letters only. */
const SPEC_TOKENS = {
  'holy priest':    [{ cls:'priest',  spec:'holy' }],
  // P2's word for the healing side of the class, either spec of it.
  'healing priest': [{ cls:'priest',  spec:'holy' }, { cls:'priest', spec:'discipline' }],
  'shadow':       [{ cls:'priest',  spec:'shadow' }],
  'disc':         [{ cls:'priest',  spec:'discipline' }],
  'discipline':   [{ cls:'priest',  spec:'discipline' }],

  'resto':        [{ cls:'druid',   spec:'restoration' }, { cls:'shaman', spec:'restoration' }],
  'resto druid':  [{ cls:'druid',   spec:'restoration' }],
  'balance':      [{ cls:'druid',   spec:'balance' }],
  'bear':         [{ cls:'druid',   spec:'guardian' }, { cls:'druid', spec:'feral', role:'tank' }],
  'feral tank':   [{ cls:'druid',   spec:'guardian' }, { cls:'druid', spec:'feral', role:'tank' }],
  'cat':          [{ cls:'druid',   spec:'feral', role:'dps' }],
  'feral dps':    [{ cls:'druid',   spec:'feral', role:'dps' }],
  'feral':        [{ cls:'druid',   spec:'feral' }],

  'dps warrior':  [{ cls:'warrior', spec:'arms' }, { cls:'warrior', spec:'fury' }],
  'arms':         [{ cls:'warrior', spec:'arms' }],
  '2h arms':      [{ cls:'warrior', spec:'arms' }],
  'fury':         [{ cls:'warrior', spec:'fury' }],
  'prot warrior': [{ cls:'warrior', spec:'protection' }],
  // A bare class name means the dps side of it — the sheet always spells a tank
  // out as "Prot Warrior" / "Prot". Reached by rows like "No Talon Warrior".
  'warrior':      [{ cls:'warrior', spec:'arms' }, { cls:'warrior', spec:'fury' }],

  'rogue':        [{ cls:'rogue',   spec:null }],

  'holy':         [{ cls:'paladin', spec:'holy' }, { cls:'priest', spec:'holy' }],
  'holy paladin': [{ cls:'paladin', spec:'holy' }],
  'prot':         [{ cls:'paladin', spec:'protection' }],
  'prot paladin': [{ cls:'paladin', spec:'protection' }],
  'ret':          [{ cls:'paladin', spec:'retribution' }],
  'retribution':  [{ cls:'paladin', spec:'retribution' }],

  'enh':          [{ cls:'shaman',  spec:'enhancement' }],
  'enhancement':  [{ cls:'shaman',  spec:'enhancement' }],
  'ele':          [{ cls:'shaman',  spec:'elemental' }],
  'elemental':    [{ cls:'shaman',  spec:'elemental' }],
  'resto shaman': [{ cls:'shaman',  spec:'restoration' }],

  'survival':     [{ cls:'hunter',  spec:'survival' }],
  'bm':           [{ cls:'hunter',  spec:'beastmastery' }],
  'bm hunter':    [{ cls:'hunter',  spec:'beastmastery' }],
  'marksmanship': [{ cls:'hunter',  spec:'marksmanship' }],
  'hunter':       [{ cls:'hunter',  spec:null }],

  'destro':       [{ cls:'warlock', spec:'destruction' }],
  'destruction':  [{ cls:'warlock', spec:'destruction' }],
  // P2 spells out which school the destro lock is stacking. Same spec either way
  // — the distinction is which caster gear it wants, not who may roll.
  'shadow destruction': [{ cls:'warlock', spec:'destruction' }],
  'fire destruction':   [{ cls:'warlock', spec:'destruction' }],
  'affliction':   [{ cls:'warlock', spec:'affliction' }],
  'demonology':   [{ cls:'warlock', spec:'demonology' }],

  'arcane':       [{ cls:'mage',    spec:'arcane' }],
  'fire':         [{ cls:'mage',    spec:'fire' }],
  'frost':        [{ cls:'mage',    spec:'frost' }],

  // Two P2 tokens that name a job rather than a spec. "Spellfire Caster" is the
  // tailored cloth set, worn by the mage and the warlock alike, so it lists both
  // and renders with the ambiguity marker for an officer to settle.
  'healers':          [{ cls:'paladin', spec:'holy' }, { cls:'priest', spec:'holy' },
                       { cls:'priest',  spec:'discipline' }, { cls:'druid', spec:'restoration' },
                       { cls:'shaman',  spec:'restoration' }],
  'spellfire caster': [{ cls:'mage',    spec:'fire' }, { cls:'mage', spec:'arcane' },
                       { cls:'warlock', spec:'destruction' }, { cls:'warlock', spec:'affliction' }],

  // Whole-class fallbacks, for a row that names a class where a spec belongs.
  'priest':       [{ cls:'priest',  spec:null }],
  'druid':        [{ cls:'druid',   spec:null }],
  'paladin':      [{ cls:'paladin', spec:null }],
  'shaman':       [{ cls:'shaman',  spec:null }],
  'mage':         [{ cls:'mage',    spec:null }],
  'warlock':      [{ cls:'warlock', spec:null }]
};

// Longest first, so "prot warrior" wins over "prot" and "holy priest" over "holy".
const TOKEN_KEYS = Object.keys(SPEC_TOKENS).sort((a, b) => b.length - a.length);

// The operators between two tokens. ">" opens the next tier; "=" (and the sheet's
// occasional ">=" / "=>") keeps both on the same one.
const OPERATORS = { '>':'next', '=':'same', '>=':'same', '=>':'same', '≥':'same' };

// "Main spec over off spec": no named prio, everyone signed up may roll.
const OPEN_ROLL = /^ms\s*>\s*os$/i;

// A win inside this window counts toward the "has been winning lately" tally.
const RECENT_DAYS = 28;

/* ───────────────────────── Tier tokens ─────────────────────────
   A tier token drops as one item and is redeemed into a class-specific piece: the
   sheet names the *token* ("Chestguard of the Forgotten Conqueror"), but a raider
   who won it wears the *piece* ("Lightbringer Chestguard"), which is all the gear
   snapshots ever see. So the HAS/exclusion check below would never fire on a token
   — the redeemed piece has a different name and id. This table bridges the two: for
   each token, the item ids of every class piece it turns into, so wearing the
   upgrade counts as holding the token (redeemed = HAS).

   Ids are TBC's (Blizzard's static classicann item table), verified against
   Wowhead. A class's list is every *spec* variant of that slot — a paladin token
   redeems into the holy, prot and ret helm alike — because any of them means the
   raider has taken the token. Only the five token slots appear (head, shoulder,
   chest, hands, legs); the token name carries the slot, so keys are the full item
   name lowercased, matching how the sheet writes P3 and how P2 is expanded below. */
const TIER_TOKENS = {
  'helm of the vanquished champion': { paladin:[29061,29068,29073], rogue:[30146], shaman:[30171,30190] },
  'helm of the vanquished defender': { warrior:[30115,30120], priest:[29049,29058,30152,30161], druid:[30219,30228,30233] },
  'helm of the vanquished hero': { hunter:[30141], mage:[30206], warlock:[30212] },
  'helm of the forgotten conqueror': { paladin:[30987,30988,30989], priest:[31063,31064], warlock:[31051] },
  'helm of the forgotten protector': { warrior:[30972,30974], hunter:[31003], shaman:[31012,31014,31015] },
  'helm of the forgotten vanquisher': { rogue:[31027], mage:[31056], druid:[31037,31039,31040] },
  'pauldrons of the vanquished champion': { paladin:[29064,29070,29075], rogue:[30149], shaman:[30173,30194] },
  'pauldrons of the vanquished defender': { warrior:[30117,30122], priest:[29054,29060,30154,30163], druid:[30221,30230,30235] },
  'pauldrons of the vanquished hero': { hunter:[30143], mage:[30210], warlock:[30215] },
  'pauldrons of the forgotten conqueror': { paladin:[30996,30997,30998], priest:[31069,31070], warlock:[31054] },
  'pauldrons of the forgotten protector': { warrior:[30979,30980], hunter:[31006], shaman:[31022,31023,31024] },
  'pauldrons of the forgotten vanquisher': { rogue:[31030], mage:[31059], druid:[31047,31048,31049] },
  'chestguard of the vanquished champion': { paladin:[29062,29066,29071], rogue:[30144], shaman:[30169,30185] },
  'chestguard of the vanquished defender': { warrior:[30113,30118], priest:[29050,29056,30150,30159], druid:[30216,30222,30231] },
  'chestguard of the vanquished hero': { hunter:[30139], mage:[30196], warlock:[30214] },
  'chestguard of the forgotten conqueror': { paladin:[30990,30991,30992], priest:[31065,31066], warlock:[31052] },
  'chestguard of the forgotten protector': { warrior:[30975,30976], hunter:[31004], shaman:[31016,31017,31018] },
  'chestguard of the forgotten vanquisher': { rogue:[31028], mage:[31057], druid:[31041,31042,31043] },
  'leggings of the vanquished champion': { paladin:[29063,29069,29074], rogue:[30148], shaman:[30172,30192] },
  'leggings of the vanquished defender': { warrior:[30116,30121], priest:[29053,29059,30153,30162], druid:[30220,30229,30234] },
  'leggings of the vanquished hero': { hunter:[30142], mage:[30207], warlock:[30213] },
  'leggings of the forgotten conqueror': { paladin:[30993,30994,30995], priest:[31067,31068], warlock:[31053] },
  'leggings of the forgotten protector': { warrior:[30977,30978], hunter:[31005], shaman:[31019,31020,31021] },
  'leggings of the forgotten vanquisher': { rogue:[31029], mage:[31058], druid:[31044,31045,31046] },
  'gloves of the vanquished champion': { paladin:[29065,29067,29072], rogue:[30145], shaman:[30170,30189] },
  'gloves of the vanquished defender': { warrior:[30114,30119], priest:[29055,29057,30151,30160], druid:[30217,30223,30232] },
  'gloves of the vanquished hero': { hunter:[30140], mage:[30205], warlock:[30211] },
  'gloves of the forgotten conqueror': { paladin:[30982,30983,30985], priest:[31060,31061], warlock:[31050] },
  'gloves of the forgotten protector': { warrior:[30969,30970], hunter:[31001], shaman:[31007,31008,31011] },
  'gloves of the forgotten vanquisher': { rogue:[31026], mage:[31055], druid:[31032,31034,31035] }
};

// The token map for an item name, or null. Item names arrive already lowercased for
// P3; P2 is expanded to the same full name before it is parsed (see parseRaidTab).
function tierToken(name){ return TIER_TOKENS[String(name || '').toLowerCase()] || null; }

/* P2's shared Tier sets tab names its tokens *bare* under a slot banner — section
   "Tier sets — Helms", item just "Vanquished Champion" — so the slot noun that
   would make it a real item name lives only in the banner. This maps the banner
   word to the noun the token item name uses, so the five slots stop collapsing to
   one lookup and each expands to its canonical name. The noun is the same across
   T5 and T6. */
const TIER_SLOT_NOUNS = {
  'helms':'Helm', 'shoulder':'Pauldrons', 'chest':'Chestguard',
  'pants':'Leggings', 'gloves':'Gloves'
};

// The canonical token item name for a slot noun ("Helm") + bare token label
// ("Vanquished Champion"), when the two name a token the table knows; null
// otherwise, so only real tokens are rewritten. A boss tab already has the noun in
// its `tierSlot`; the Tier sets tab has only the banner word, which tierItemName
// translates first.
function tierItemForSlot(noun, label){
  if(!noun) return null;
  const full = `${noun} of the ${String(label || '').trim()}`;
  return tierToken(full) ? full : null;
}

// As above, but from the Tier sets tab's slot banner ("Helms", "Pants", …), whose
// word differs from the noun the item name uses.
function tierItemName(slotWord, label){
  return tierItemForSlot(TIER_SLOT_NOUNS[String(slotWord || '').toLowerCase()], label);
}

let picked = { eventId:'', eventTitle:'', raid:RAID_TABS[0].key };
let candidates = [];      // everyone signed up, as { name, cls, spec, ... }
let sections = [];        // the parsed sheet: [{ name, items:[…] }]
let unknownTokens = [];   // sheet tokens the table above doesn't know
let hideEmpty = true;
let equipped = [];        // GET /api/items/list — who is wearing what
let awards = [];          // GET /api/loot/history — every award, for WON and the tally
let members = [];         // GET /api/members — kept so the personal section can find "me"
let myChars = [];         // candidate-shaped objects for the signed-in user's own characters
let exclusions = new Map(); // itemNameLower → Set(characterId) — officers' per-item roll mutes
let exclusionNames = new Map(); // characterId → name, so the muted line can name a character not signed up
let itemIds = new Map();   // itemNameLower → wowhead id, resolved so every item tooltips (not just ones in our gear/loot data)
let cachedAt = 0;         // when the shown build was fetched (ms) — for the "as of" note

function setStatus(msg, isErr){
  const el = document.getElementById('prioStatus');
  el.textContent = msg || '';
  el.style.color = isErr ? 'var(--amber)' : 'var(--text-dim)';
}

function raidTab(key){ return RAID_TABS.find(r => r.key === key) || RAID_TABS[0]; }

/* ───────────────────────── Reading the sheet ─────────────────────────
   Two views of a tab, both reduced to the same grid of { text, bg } cells, so the
   row walking below neither knows nor cares which one it got.

   The embedded view is the one worth having: the sheet fills every spec token
   with that class's colour, and that fill is the *only* thing separating the two
   tokens the guild writes ambiguously — "Resto" is the druid in orange and the
   shaman in blue, "Holy" the priest in white and the paladin in pink. The CSV
   export carries no formatting at all, so on that path those stay ambiguous and
   the page says so. */

// The class each of the sheets' fills means. These are the sheets' own hexes,
// which are close to but not identical with RH.CLASS_COLORS (they use #ff7c0a for
// druid where menu.js has #FF7D0A), so matching is nearest-colour with a tight
// cutoff rather than equality — a re-typed fill a shade off still lands, an
// unrelated colour still misses. Both phases fill alike bar the warlock, where P2
// picked a purple far enough from P3's to need naming separately.
const SHEET_CLASS_FILLS = {
  '#c69b6d':'warrior', '#f48cba':'paladin', '#aad372':'hunter', '#fff468':'rogue',
  '#ffffff':'priest',  '#0070dd':'shaman',  '#3fc7eb':'mage',   '#8788ee':'warlock',
  '#8e7cc3':'warlock', '#ff7c0a':'druid'
};

// Fills that are page furniture, not a class: the row banding, the grey on the
// operator cells, the header greys (P2 bands its slot headings in #999999). White
// is deliberately absent — it is the priest, and it is also the default cell
// background, which is why a token cell is only ever read for colour when it
// holds a token.
const NEUTRAL_FILLS = ['#f3f3f3', '#d9d9d9', '#666666', '#999999', '#bdbdbd', '#efefef', '#cccccc'];

function parseHexColor(s){
  const m = String(s || '').trim().match(/^#?([0-9a-f]{6})$/i);
  if(m) return [parseInt(m[1].slice(0,2),16), parseInt(m[1].slice(2,4),16), parseInt(m[1].slice(4,6),16)];
  const rgb = String(s || '').match(/rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/i);
  return rgb ? [ +rgb[1], +rgb[2], +rgb[3] ] : null;
}

function colorDistance(a, b){
  return Math.sqrt((a[0]-b[0])**2 + (a[1]-b[1])**2 + (a[2]-b[2])**2);
}

// The class a cell fill means, or '' for a neutral/unknown one.
function fillClass(bg){
  const rgb = parseHexColor(bg);
  if(!rgb) return '';
  if(NEUTRAL_FILLS.some(n => colorDistance(rgb, parseHexColor(n)) < 12)) return '';
  let best = '', bestD = Infinity;
  Object.keys(SHEET_CLASS_FILLS).forEach(hex => {
    const d = colorDistance(rgb, parseHexColor(hex));
    if(d < bestD){ bestD = d; best = SHEET_CLASS_FILLS[hex]; }
  });
  return bestD < 40 ? best : '';
}

/* The embedded view, as a grid. Parsed with DOMParser rather than by hand: it is
   real HTML, and the browser already has a parser for it.

   Two shape details it has to undo to line the grid up with the CSV's columns:
   Google prefixes every row with a row-number header cell, and it merges the
   boss banner rows with colspan. Expanding the spans and dropping the row number
   leaves column indices identical to the CSV's, so one row walker serves both. */
function parseHtmlGrid(html){
  const doc = new DOMParser().parseFromString(html, 'text/html');
  const table = doc.querySelector('table');
  if(!table) throw new Error('The sheet view had no table in it.');
  fillRules = null;                       // this document's rules, not the last one's

  return [...table.rows].map(tr => {
    const cells = [];
    [...tr.cells].forEach((td, i) => {
      // The leading row-number cell is Google's, not the sheet's.
      if(i === 0 && td.tagName === 'TH') return;
      const text = (td.textContent || '').replace(/ /g, ' ').trim();
      const cell = { text, bg: cellFill(td, doc) };
      const span = Math.max(1, parseInt(td.getAttribute('colspan') || '1', 10) || 1);
      cells.push(cell);
      for(let s = 1; s < span; s++) cells.push({ text:'', bg:cell.bg });
    });
    return cells;
  });
}

/* A cell's background. The embed puts the fills in a stylesheet and references
   them by class, so the rules have to be read out of the document's own <style>
   blocks — a parsed document has no layout, so getComputedStyle is not available. */
let fillRules = null;
function cellFill(td, doc){
  if(!fillRules){
    fillRules = {};
    [...doc.querySelectorAll('style')].forEach(s => {
      const css = s.textContent || '';
      for(const m of css.matchAll(/\.([A-Za-z0-9_-]+)\s*\{([^}]*)\}/g)){
        const bg = m[2].match(/background-color\s*:\s*([^;]+)/i);
        if(bg) fillRules[m[1]] = bg[1].trim();
      }
    });
  }
  const inline = (td.getAttribute('style') || '').match(/background-color\s*:\s*([^;]+)/i);
  if(inline) return inline[1].trim();
  const names = (td.getAttribute('class') || '').split(/\s+/);
  for(const n of names) if(fillRules[n]) return fillRules[n];
  return '';
}

/* The plain CSV export, as the same grid with no colour on any cell.
   Note cells contain commas and quotes ("Kinda bad, maybe Bear threat"), so it
   has to be parsed properly rather than split on commas. */
function parseCsvGrid(text){
  return parseCsv(text).map(row => row.map(text => ({ text: String(text || '').trim(), bg:'' })));
}

function parseCsv(text){
  const rows = [];
  let row = [], cell = '', quoted = false;
  const src = String(text).replace(/\r\n/g, '\n').replace(/\r/g, '\n');

  for(let i = 0; i < src.length; i++){
    const c = src[i];
    if(quoted){
      if(c === '"'){
        if(src[i + 1] === '"'){ cell += '"'; i++; }   // "" is one literal quote
        else quoted = false;
      } else cell += c;
      continue;
    }
    if(c === '"'){ quoted = true; continue; }
    if(c === ','){ row.push(cell); cell = ''; continue; }
    if(c === '\n'){ row.push(cell); rows.push(row); row = []; cell = ''; continue; }
    cell += c;
  }
  row.push(cell);
  rows.push(row);
  return rows;
}

/* ───────────────────────── The sheets ─────────────────────────
   Every tab of either phase repeats one shape:

     Rage Winterchill,,,,…                      <- boss: col A set, B and C empty
     Item Name,Bias,,,,…                        <- header, skipped
     Cuffs of Devastation,Arcane,>,Balance,>,Ele,>,Destro,,Arcane does not get…
     ,,,,,,,,,,,"Shaman gets shield, …"         <- a note continuing the row above

   P3 banners its bosses that way inside one tab per raid. P2 gives each boss its
   own tab and so has no banner rows at all — RAID_TABS names the section instead,
   and what banner rows P2 does have are subdivisions (the Tier Sets tab bands
   "Helms", "Gloves", …), which land under the tab's name.

   P3's tabs also carry a far-right **legend** column — the list of canonical
   tokens the sheet is written in, one per row, starting on the header row. It
   isn't part of any item, so it has to be found and excluded: left in, it
   reappears as a stray note ("Guise of the Tidal Lurker … Resto") on whichever
   item row happens to sit beside it. That is also why boss rows are detected on
   cols B *and* C being empty rather than "everything after A" — the legend puts
   text on some boss rows, which a whole-row emptiness test would trip over. */

// The header row that opens a tab, if it has one. Col A is not the tell: P3 writes
// "Item Name" there but P2's tabs have "Item Name/o ", "\", "bb", "ww" and other
// leftovers. Col B says "Bias" on every tab of both.
function isHeaderRow(row){
  return /^bias$/i.test(txt(row[1])) || /^item name$/i.test(txt(row[0]));
}

// The column the legend starts in, so notes can stop before it. It is the only
// thing on the header row past the Bias column; P2 has no legend, so Infinity.
function legendColumn(rows){
  const header = rows.find(isHeaderRow);
  if(!header) return Infinity;
  for(let i = 2; i < header.length; i++) if(txt(header[i])) return i;
  return Infinity;
}

function txt(cell){ return cell ? String(cell.text || '').trim() : ''; }

function cellsFrom(row, i, end){
  return row.slice(i, end).map(txt);
}

/* The specs a sheet cell means, or null when the table doesn't know it.

   Two things narrow it. The text handles the qualifiers that appear in the real
   sheet — "Destro*", "Fire Mage", "BM with CVoS", "Arms 4 Piece", "No Talon
   Rogue", "Cat/Bear". The cell's **fill** then settles which class was meant,
   which is the whole reason the page prefers the embedded view: "Resto" in
   druid-orange is the druid and "Resto" in shaman-blue is the shaman, and the
   text alone cannot tell you which. A fill that isn't a class colour, or the CSV
   path where there is no fill at all, leaves the token as broad as it was. */
/* A sentence, not a token. The token match below is a substring one — it has to
   be, to read "No Talon Rogue" and "BM with CVoS" — and a sentence will always
   contain some spec's name somewhere ("…DPS Warriors and Ret Paladins. Use your
   own discretion" on P2's Verdant Sphere), which would otherwise make the whole
   paragraph tier 1. Nothing the sheets write as a token runs past four words or
   carries punctuation. */
function isProse(text){
  return /[,;.]/.test(text) || text.trim().split(/\s+/).length > 4;
}

function tokenSpecs(raw, bg){
  const text = String(raw).toLowerCase().replace(/\*/g, ' ');
  if(isProse(text)) return null;
  const parts = text.split('/').map(s => s.trim()).filter(Boolean);
  const specs = [];
  let matched = false;

  parts.forEach(part => {
    const key = TOKEN_KEYS.find(k => part === k) || TOKEN_KEYS.find(k => part.includes(k));
    if(!key) return;
    matched = true;
    SPEC_TOKENS[key].forEach(s => {
      if(!specs.some(x => x.cls === s.cls && x.spec === s.spec && x.role === s.role)) specs.push(s);
    });
  });
  if(!matched) return null;

  // Only ever narrows: a fill that agrees with nothing in the token is a fill we
  // have misread, and dropping every candidate on that basis would be worse than
  // ignoring it.
  const cls = fillClass(bg);
  if(cls){
    const narrowed = specs.filter(s => s.cls === cls);
    if(narrowed.length) return narrowed;
  }
  return specs;
}

/* Walk the Bias chain of one row into `item`: token, operator, token, … Anything
   that is neither ends the chain, and it plus every non-empty cell after it is a
   note. Starts at col B, which is where both phases put the first token. */
function walkChain(row, item, legend, unknown){
  let i = 1;
  let tier = null;
  while(i < row.length && i < legend){
    const raw = txt(row[i]);
    if(!raw) break;

    // "Shadow > Destro > MS > OS": the chain names a couple of specs and then
    // opens up. That is the end of it either way.
    if(OPEN_ROLL.test(raw)){ item.openTail = true; i += 1; break; }

    const specs = tokenSpecs(raw, row[i] && row[i].bg);
    const op = OPERATORS[txt(row[i + 1])];

    if(!specs){
      // Something the spec table doesn't know. In the Bias column that is
      // most likely prose, so only take it when an operator follows and the
      // sheet is plainly writing a chain ("No T5 Rings > Bear > Hunter");
      // otherwise let it fall through to the notes. Past the first operator
      // there is no such doubt — we are mid-chain, and the last link
      // ("Xat > Chankles = Doopey") has no operator after it either.
      // A null `specs` is resolved against the raiders' names at render time,
      // and only reported if it matches nothing at all.
      if(i === 1 && !op) break;
      unknown.push({ item:item.name, token:raw });
    }

    if(!tier){ tier = { tokens:[] }; item.tiers.push(tier); }
    tier.tokens.push({ label:raw, specs });

    if(!op) { i += 1; break; }
    if(op === 'next') tier = null;
    i += 2;
  }
  item.notes.push(...cellsFrom(row, i, legend).filter(Boolean));
}

/* One tab, as sections of items. `forcedSection` is the boss the whole tab is
   about — P2's shape, where the tab is the boss and nothing inside it says so.
   Given one, banner rows read as subdivisions of it ("Tier sets — Helms") rather
   than as bosses in their own right. `tierSlot`, when set, is the slot this boss's
   tier token drops in, so a bare token gains its real item name. */
function parseRaidTab(grid, forcedSection, tierSlot){
  const rows = grid;
  const out = [];
  const unknown = [];
  const legend = legendColumn(rows);
  let section = forcedSection ? { name:forcedSection, items:[] } : null;
  let lastItem = null;
  // The current slot banner, when this is P2's Tier sets tab — the noun a bare token
  // row ("Vanquished Champion") needs to become a real item name. Only set under a
  // forcedSection, which is the only place tokens are written slot-first.
  let slotWord = null;
  if(section) out.push(section);

  rows.forEach(row => {
    const a = txt(row[0]);
    const b = txt(row[1]);
    const c = txt(row[2]);

    if(!a && !b && !c){
      // A blank line separates sections, but a late note cell can sit alone on
      // one — those belong to the item above. (A row carrying nothing but a
      // legend entry lands here too, and the legend bound is what drops it.)
      const rest = cellsFrom(row, 3, legend).filter(Boolean);
      if(rest.length && lastItem) lastItem.notes.push(...rest);
      return;
    }

    if(isHeaderRow(row)) return;

    if(a && !b && !c){
      const name = forcedSection ? `${forcedSection} — ${a}` : a;
      section = { name, items:[] };
      out.push(section);
      lastItem = null;
      slotWord = forcedSection ? a : null;
      return;
    }

    if(!a){
      // Col A empty but something further along. Usually a note continuing the
      // item above — but P2 also writes an item whose bias was too long for one
      // cell on the next row ("Verdant Sphere", where col B holds the prose and
      // the chain follows underneath), so a chain that starts here belongs to
      // that item as prio, not as more prose.
      if(!lastItem) return;
      if(!lastItem.tiers.length && !lastItem.openRoll && tokenSpecs(b, row[1] && row[1].bg))
        walkChain(row, lastItem, legend, unknown);
      else
        lastItem.notes.push(...cellsFrom(row, 1, legend).filter(Boolean));
      return;
    }

    if(!section){ section = { name:'Loot', items:[] }; out.push(section); }

    // Both P2 shapes name a tier token bare and leave the slot elsewhere: the Tier
    // sets tab banners it ("Helms" over "Vanquished Champion"), a boss tab implies
    // it (the boss is the slot, its tab's `tierSlot`). Rejoin either into the real
    // item name ("Helm of the Vanquished Champion") so WON, the id resolver and the
    // redeemed-piece HAS all key on a token they know, and the five slots stop
    // collapsing to one lookup.
    const item = { name: (slotWord && tierItemName(slotWord, a))
                      || (tierSlot && tierItemForSlot(tierSlot, a))
                      || a,
                   tiers:[], openRoll:false, openTail:false, notes:[] };

    if(OPEN_ROLL.test(b)){
      item.openRoll = true;
      item.notes.push(...cellsFrom(row, 2, legend).filter(Boolean));
    } else {
      walkChain(row, item, legend, unknown);
    }

    // A note cell that is itself a known token would have been eaten as prio, so
    // anything left that looks like prose stays prose. Nothing to do but keep it.
    section.items.push(item);
    lastItem = item;
  });

  // A raid can be many tabs, so this adds to what the ones before it found —
  // build() clears the list before the first.
  unknownTokens.push(...unknown);
  return out.filter(s => s.items.length);
}

/* ───────────────────────── Who is here ─────────────────────────
   One signup, resolved against the roster the same way groups.js does it: Discord
   user id first (that is who signed up, full stop), character name second (which
   only matches when the roster already knows the character). The roster fills in
   whatever the signup left blank — the class and spec a Warcraft Logs import last
   saw for that character.

   The name Raid-Helper carries isn't taken as gospel. When it matches a roster
   character outright, that character is the one holding prio. When it doesn't —
   a Discord nick, an alt spelled a shade differently, a bare "Warrior" — but the
   Discord id still finds the member, we pick the character of theirs whose class
   matches what they signed up as: that is the character that would actually take
   the loot, and pinning it to a real character is what makes the HAS / WON / recent
   annotations (all keyed on character name) land. The main breaks a tie when more
   than one character of that class is on the roster. */

function buildCandidates(signups, members){
  const byDiscord = new Map();
  const byName = new Map();
  members.forEach(m => {
    if(m.discordUserId) byDiscord.set(String(m.discordUserId), m);
    (m.characters || []).forEach(ch => byName.set(String(ch.name || '').toLowerCase(), m));
  });

  let unlinked = 0, noSpec = 0, byClass = 0;

  candidates = signups.map(s => {
    const member = (s.userId ? byDiscord.get(s.userId) : null) || byName.get(s.name.toLowerCase()) || null;
    const chars = (member && member.characters) || [];
    const signupCls = (s.cls || '').toLowerCase().trim();

    // First the character the roster knows by the signed-up name.
    let known = chars.find(ch => String(ch.name || '').toLowerCase() === s.name.toLowerCase()) || null;

    // Failing that, the member's character whose class matches the signup — the
    // one they'd actually loot on. Main wins a tie; otherwise the first of that
    // class. Only reached when the signup name isn't a roster character.
    let matchedByClass = false;
    if(!known && member && signupCls){
      const ofClass = chars.filter(ch => String(ch.cls || '').toLowerCase().trim() === signupCls);
      known = ofClass.find(ch => ch.isMain) || ofClass[0] || null;
      matchedByClass = !!known;
    }

    // The character resolved to is who holds the prio, so it names the candidate —
    // that is what the loot history and gear tables key on. With nothing resolved,
    // the signup's own name stands.
    const name = known ? known.name : s.name;
    // The signup wins; the roster is the fallback for whatever it left out.
    const cls = (s.cls || (known && known.cls) || '').toLowerCase().trim();
    let spec = (s.spec || (known && known.spec) || '').toLowerCase().replace(/[^a-z]/g, '');

    const c = {
      id: s.id,
      name,
      cls: RH.CLASS_COLORS[cls] ? cls : (RH.SPEC_TO_CLASS[spec] || ''),
      spec,
      status: s.status || 'active',
      characterId: known ? known.id : null,
      member: member ? (member.nickname || member.displayName || member.discordUsername || '') : '',
      unlinked: !member,
      recent: 0,
      won: new Set(),
      has: new Set()
    };
    c.role = RH.isTank(c) ? 'tank' : RH.isHealer(c) ? 'healer' : 'dps';
    if(!member) unlinked++;
    if(!spec) noSpec++;
    if(matchedByClass) byClass++;
    return c;
  });

  const notes = [];
  if(unlinked) notes.push(`${unlinked} not linked to a member`);
  if(byClass) notes.push(`${byClass} matched to a character by class`);
  if(noSpec) notes.push(`${noSpec} with no spec on the signup or in the logs`);
  return notes;
}

// Does this candidate satisfy one of a token's specs? A candidate with no spec at
// all still matches on class — better to over-list them, flagged, than to drop a
// raider who signed up class-only.
function matchesSpec(cand, spec){
  if(!cand.cls || cand.cls !== spec.cls) return false;
  if(spec.spec && cand.spec && cand.spec !== spec.spec) return false;
  if(spec.role && cand.role && cand.role !== spec.role) return false;
  return true;
}

/* ───────────────────────── Per-item mutes ─────────────────────────
   An officer can say "this character does not roll on this item". The mutes come
   from GET /api/loot-prio/exclusions as { characterId, itemName } rows and are
   indexed by the sheet's item name (lowercased), so every candidate walk can drop
   the muted characters for the item it is on. */
const EMPTY_SET = new Set();

function buildExclusions(rows){
  const map = new Map();
  (rows || []).forEach(r => {
    const item = String(r.itemName || '').toLowerCase();
    const cid = String(r.characterId || '');
    if(!item || !cid) return;
    let set = map.get(item);
    if(!set){ set = new Set(); map.set(item, set); }
    set.add(cid);
  });
  return map;
}

// The character ids muted for an item name, or an empty set. `keys` is the item's
// aliases (sheet name plus the database's), so a mute set on either spelling lands.
function excludedIdsForKeys(keys){
  let out = null;
  (keys || []).forEach(k => {
    const set = exclusions.get(String(k || '').toLowerCase());
    if(!set) return;
    if(!out) out = new Set();
    set.forEach(id => out.add(id));
  });
  return out || EMPTY_SET;
}

function isExcluded(cand, excludedIds){
  return !!cand.characterId && excludedIds.has(String(cand.characterId));
}

/* The signed-in user's own characters, shaped like signup candidates so the same
   spec matcher ranks them. Found by the Discord id the session carries — the roster
   from /api/members already drops ignored characters. Drives the personal "what can
   I roll on" section; independent of who is actually signed up tonight. */
function buildMyChars(memberList){
  const pl = (typeof sessionPayload === 'function') ? sessionPayload() : null;
  const uid = pl && pl.uid ? String(pl.uid) : '';
  if(!uid) return [];
  const me = (memberList || []).find(m => String(m.discordUserId || '') === uid);
  if(!me) return [];
  return (me.characters || []).map(ch => {
    const cls  = String(ch.cls || ch.class || '').toLowerCase().trim();
    const spec = String(ch.spec || '').toLowerCase().replace(/[^a-z]/g, '');
    const c = {
      id: 'me-' + ch.id,
      characterId: ch.id,
      name: ch.name,
      cls: RH.CLASS_COLORS[cls] ? cls : (RH.SPEC_TO_CLASS[spec] || ''),
      spec,
      isMain: !!ch.isMain
    };
    c.role = RH.isTank(c) ? 'tank' : RH.isHealer(c) ? 'healer' : 'dps';
    return c;
  }).filter(c => c.cls);
}

/* ───────────────────────── Annotations ─────────────────────────
   Three things an officer wants next to a name, all from data we already keep:
   who is wearing the item already, who has been awarded it before, and who has
   been winning a lot lately. */

/* One insertion, deletion or substitution apart — the shape a hand-typed item
   name in the sheet actually gets wrong ("Bracers of Martydom" for the real
   "Bracers of Martyrdom"). Only ever the last resort after an exact and a
   substring match, and one edit is tight enough that the items which really do
   look alike (Gloves of the Forgotten Conqueror / Protector / Vanquisher) stay
   apart. */
function oneTypoApart(a, b){
  if(a === b) return true;
  if(Math.abs(a.length - b.length) > 1) return false;
  // Longest common prefix and suffix; what is left over is the single edit.
  let head = 0;
  while(head < a.length && head < b.length && a[head] === b[head]) head++;
  let tail = 0;
  while(tail < a.length - head && tail < b.length - head &&
        a[a.length - 1 - tail] === b[b.length - 1 - tail]) tail++;
  return (a.length - head - tail) <= 1 && (b.length - head - tail) <= 1;
}

function annotate(itemRows, loot){
  const cutoff = Date.now() - RECENT_DAYS * 86400000;
  const byName = new Map();
  candidates.forEach(c => byName.set(c.name.toLowerCase(), c));

  candidates.forEach(c => { c.recent = 0; c.won = new Set(); c.has = new Set(); });

  (loot || []).forEach(l => {
    if(l.disenchanted || !l.character) return;
    const c = byName.get(String(l.character).toLowerCase());
    if(!c) return;
    if(l.itemName) c.won.add(String(l.itemName).toLowerCase());
    // Off-spec wins don't say much about how well someone has been doing.
    if(!l.offSpec && Date.parse(l.awardedAt) >= cutoff) c.recent++;
  });

  const rows = itemRows || [];
  const exact = new Map();
  rows.forEach(r => exact.set(String(r.name || '').toLowerCase(), r));

  // Every item id each character is wearing, from the same gear rows. Tier tokens
  // are held as their *redeemed* piece, whose name never matches the token — so the
  // token's HAS is decided by id against this map, not by the name lookup above.
  const wornByChar = new Map();
  rows.forEach(r => {
    const id = Number(r.id);
    if(!id) return;
    (r.equipped || []).forEach(e => {
      const key = String(e.name || '').toLowerCase();
      let ids = wornByChar.get(key);
      if(!ids){ ids = new Set(); wornByChar.set(key, ids); }
      ids.add(id);
    });
  });

  return function lookup(itemName){
    const lower = String(itemName).toLowerCase();
    const row = exact.get(lower) ||
                rows.find(r => String(r.name || '').toLowerCase().includes(lower)) ||
                rows.find(r => oneTypoApart(String(r.name || '').toLowerCase(), lower));
    if(row){
      (row.equipped || []).forEach(e => {
        const c = byName.get(String(e.name || '').toLowerCase());
        if(c) c.has.add(lower);
      });
    }
    // A tier token: anyone wearing a piece it redeems into has already taken it, so
    // mark HAS under the token's own key. Class-keyed — a paladin holding the
    // Conqueror shoulder wears the paladin piece, not the priest's or warlock's.
    const tok = tierToken(lower);
    if(tok){
      candidates.forEach(c => {
        const ids = tok[c.cls];
        if(!ids) return;
        const worn = wornByChar.get(c.name.toLowerCase());
        if(worn && ids.some(id => worn.has(id))) c.has.add(lower);
      });
    }
    return row || null;
  };
}

/* ───────────────────────── Building ───────────────────────── */

function reportError(err, notFoundMsg){
  console.error('Loot prio call failed:', err);
  if(err.status === 401){ setStatus('Session expired — signing you out…', true); setTimeout(logout, 1200); return; }
  if(err.status === 403){ setStatus('Unauthorized — officer access is needed.', true); return; }
  if(err.status === 404){ setStatus(notFoundMsg, true); return; }
  setStatus(err.message + ' See the browser console.', true);
}

async function apiGet(path){
  let res;
  try {
    res = await fetch(API_BASE + path, { headers: RH.headers() });
  } catch(err){
    const e = new Error('Could not reach the API — is the backend up and is this origin allowed?');
    e.cause = err;
    throw e;
  }
  if(!res.ok){
    const e = new Error('The API returned HTTP ' + res.status + '.');
    e.status = res.status;
    throw e;
  }
  return res;
}

/* The tab, as a grid. The embedded view first, because it is the one that keeps
   the cell colours the sheet says class with; the plain CSV export is the
   fallback if that ever stops parsing, and costs only the disambiguation of
   "Resto" and "Holy". `colored` tells the caller which it got, so the page can
   say so rather than quietly guessing. */
async function fetchSheetGrid(doc, gid){
  const q = 'doc=' + encodeURIComponent(doc) + '&gid=' + encodeURIComponent(gid);
  try {
    const html = await (await apiGet('/sheet/loot?' + q)).text();
    const grid = parseHtmlGrid(html);
    if(!grid.length) throw new Error('The embedded sheet view had no rows.');
    return { grid, colored:true };
  } catch(err){
    // A 401/403 is the session, not the view — that must surface as itself.
    if(err.status) throw err;
    console.warn('Falling back to the CSV export of the loot sheet:', err);
    const csv = await (await apiGet('/sheet/loot?format=csv&' + q)).text();
    return { grid: parseCsvGrid(csv), colored:false };
  }
}

/* Every tab a raid is made of, in order. One request per tab — a P2 raid is
   seven of them — but the backend caches each for ten minutes, so a rebuild
   mid-raid costs nothing. */
function fetchRaidTabs(raid){
  return Promise.all(raid.tabs.map(t => fetchSheetGrid(raid.doc, t.gid)));
}

async function loadEvents(){
  setStatus('Loading events…');
  let events;
  try {
    events = await RH.listEvents();
  } catch(err){
    reportError(err, 'Server not found — check RH.SERVER_ID in menu.js.');
    return;
  }
  if(!events.length){
    setStatus(events.total
      ? `No recent or upcoming events (server has ${events.total} total).`
      : 'No events found on that server.', true);
    return;
  }

  const sel = document.getElementById('rhEvent');
  sel.innerHTML = events.map(ev => {
    const id = ev.id || ev.eventId || '';
    const when = RH.fmtEventDate(ev);
    const title = String(ev.title || 'Untitled event').replace(/</g, '&lt;');
    return `<option value="${id}">${when ? when + ' — ' : ''}${title}</option>`;
  }).join('');
  if(picked.eventId && [...sel.options].some(o => o.value === picked.eventId)) sel.value = picked.eventId;
  document.getElementById('rhEventPickRow').style.display = 'flex';
  setStatus(`Found ${events.length} recent/upcoming event${events.length===1?'':'s'} — pick the raid and build.`);
}

async function build(eventIdArg){
  const sel = document.getElementById('rhEvent');
  // Refresh passes the built event id directly, since the picker isn't populated
  // after a cache restore; the button path falls back to the picker's selection.
  const eventId = eventIdArg || (sel && sel.value) || picked.eventId;
  const raidKey = document.getElementById('raidPick').value;
  if(!eventId){ setStatus('Load the events and pick one first.', true); return; }

  const raid = raidTab(raidKey);
  setStatus(`Loading the signup, the roster and the ${raid.label} sheet…`);

  let ev, tabs, exclRows;
  try {
    // Loot history is the member-readable award list (/api/loot is officer-only);
    // both return the same rows. Exclusions and loot degrade to empty rather than
    // failing the build — a member without loot access still gets a prio list.
    [ev, members, tabs, equipped, awards, exclRows] = await Promise.all([
      RH.fetchEvent(eventId),
      apiGet('/api/members').then(r => r.json()),
      fetchRaidTabs(raid),
      apiGet('/api/items/list').then(r => r.json()).catch(() => []),
      apiGet('/api/loot/history').then(r => r.json()).catch(() => []),
      apiGet('/api/loot-prio/exclusions?raid=' + encodeURIComponent(raidKey)).then(r => r.json()).catch(() => [])
    ]);
  } catch(err){
    reportError(err, 'That event is gone (it may have been deleted).');
    return;
  }

  exclusions = buildExclusions(exclRows);
  exclusionNames = new Map((exclRows || [])
    .filter(r => r.characterId && r.characterName)
    .map(r => [String(r.characterId), r.characterName]));
  myChars = buildMyChars(members);

  const signups = RH.mapSignups(ev.signUps || ev.signups || [], null);
  if(!signups.length){ setStatus('That event has no usable signups.', true); return; }

  const rosterNotes = buildCandidates(signups, members);
  unknownTokens = [];
  sections = tabs.reduce(
    (all, tab, i) => all.concat(parseRaidTab(tab.grid, raid.tabs[i].section, raid.tabs[i].tierSlot)), []);
  picked = { eventId, eventTitle: ev.title || 'event ' + eventId, raid: raidKey };

  const items = sections.reduce((n, s) => n + s.items.length, 0);
  if(!items){ setStatus(`The ${raid.label} sheet parsed to no items — has its layout changed?`, true); return; }

  // Resolve an id for every item so all of them tooltip, not just the ones our gear
  // and loot data happens to carry. Best-effort: a failure leaves the DB-derived ids
  // in place rather than blocking the build.
  itemIds = await resolveAllItemIds();

  cachedAt = Date.now();
  save();
  render();

  // A token off the spec table may still be a raider's name (the sheet does that
  // for the Warglaives), and one that matched somebody isn't a problem to report.
  const names = new Set(candidates.map(c => c.name.toLowerCase()));
  const stillUnknown = unknownTokens.filter(u => !names.has(u.token.toLowerCase()));

  const notes = [...rosterNotes];
  if(tabs.some(t => !t.colored))
    notes.push('read without cell colours, so "Resto" and "Holy" stay ambiguous');
  if(stillUnknown.length){
    console.warn('Loot sheet tokens that are neither a spec nor a raider signed up:', stillUnknown);
    notes.push(`${stillUnknown.length} sheet token${stillUnknown.length===1?'':'s'} not recognised — see the console`);
  }
  setStatus(`${candidates.length} signed up · ${items} items across ${sections.length} sections of ${raid.label}` +
            (notes.length ? ` — ${notes.join(', ')}.` : '.'));
}

/* ───────────────────────── Rendering ───────────────────────── */

// `keys` is what this item is called: the sheet's spelling, plus the database's
// where the two differ (the loot log records the real name, the sheet may have
// typed it wrong).
// `had` greys the name out: they already have this item, so prio has moved past
// them (and they are left out of the soft-reserve export).
function candidateHTML(c, keys, had, itemName){
  const color = RH.CLASS_COLORS[c.cls] || '#7fa89c';
  const pills = [];
  if(keys.some(k => c.has.has(k))) pills.push('<span class="stag has">HAS</span>');
  if(keys.some(k => c.won.has(k))) pills.push('<span class="stag won">WON</span>');
  if(!c.spec)             pills.push('<span class="stag tentative">SPEC?</span>');
  if(c.unlinked)          pills.push('<span class="stag unlinked">UNLINKED</span>');
  if(c.status && c.status !== 'active')
    pills.push(`<span class="stag ${c.status}">${c.status === 'tentative' ? 'TENT' : 'BENCH'}</span>`);
  const href = 'character.html?name=' + encodeURIComponent(c.name);
  const cls = 'prio-name' + (had ? ' is-had' : '');
  // An officer can mute a linked character on this one item; the ✕ POSTs the mute.
  const mute = (isOfficer() && c.characterId && itemName)
    ? `<button class="prio-mute" title="Mute ${whEsc(c.name)} on this item" data-cid="${whEsc(String(c.characterId))}" data-item="${whEsc(itemName)}" onclick="ignoreChar(this)">×</button>`
    : '';
  return `<a class="${cls}" href="${href}" style="--class-color:${color}">${whEsc(c.name)}${pills.join('')}</a>${mute}`;
}

// The candidates for one tier, minus anyone a better tier already claimed. That
// dedupe is what stops a chain like "Holy > Resto > Holy > Resto" — the sheet's
// way of ordering two classes that share a token — from listing the same people
// four times. A token whose every match was already claimed is `repeat: true`
// rather than "nobody": it isn't that no one plays it, it's that they are listed
// above. Ties inside a tier break on who has won least lately.
// Everyone a token means. Normally the specs it maps to — but the sheet also
// writes a chain of *names* where the prio is a personal one (the Warglaives go
// "Xat > Chankles = Doopey"), so a token the spec table doesn't know is tried
// against the raiders signed up before it counts as unrecognised.
function tokenMatches(tok, excludedIds){
  const ex = excludedIds || EMPTY_SET;
  if(tok.specs) return candidates.filter(c => !isExcluded(c, ex) && tok.specs.some(s => matchesSpec(c, s)));
  const name = tok.label.toLowerCase();
  return candidates.filter(c => !isExcluded(c, ex) && c.name.toLowerCase() === name);
}

function tierCandidates(tier, taken, excludedIds){
  return tier.tokens.map(tok => {
    const all = tokenMatches(tok, excludedIds);
    const hits = all.filter(c => !taken.has(c.id));
    hits.sort((a, b) => a.recent - b.recent || a.name.localeCompare(b.name));
    hits.forEach(c => taken.add(c.id));
    return {
      label: tok.label,
      // Ambiguous means "more than one class", which is the case an officer has
      // to settle by hand. A token covering two specs of one class ("DPS Warrior"
      // = arms and fury) is not ambiguous, it is just broad.
      ambiguous: !!tok.specs && new Set(tok.specs.map(s => s.cls)).size > 1,
      named: !tok.specs,
      hits,
      repeat: !hits.length && !!all.length
    };
  });
}

/* ───────────────────────── Who actually reserves ─────────────────────────
   The soft-reserve export needs one flat list per item, where the sheet gives an
   ordered chain — so: the top tier reserves it, except that anyone who already
   has the item is not a candidate for it, and a tier where *everyone* already has
   it is skipped entirely and the next one down reserves instead.

   The same walk drives the page, so what an officer reads is exactly what Gargul
   will be told. `settled` is the tier that ended up reserving; the ones above it
   are greyed out with their names struck, so it is obvious why prio moved down. */
// The sheet ranked nobody for this item: it said MS > OS outright, or gave the
// row no chain to walk (P2's "Ashes of Al'ar: Epic Flying Trained Only"). There
// is no plan to make — the export hands these to OPEN_ROLL_NAME instead.
function isOpenItem(item){
  return item.openRoll || !item.tiers.length;
}

function reservePlan(item, keys){
  if(isOpenItem(item)) return null;

  const excludedIds = excludedIdsForKeys(keys);
  const hasAlready = c => keys.some(k => c.has.has(k) || c.won.has(k));
  const taken = new Set();
  const tiers = item.tiers.map(tier => {
    const groups = tierCandidates(tier, taken, excludedIds);
    const free = [], had = [];
    groups.forEach(g => g.hits.forEach(c => (hasAlready(c) ? had : free).push(c)));
    return { groups, free, had };
  });

  const settled = tiers.findIndex(t => t.free.length);
  return { tiers, settled, hasAlready };
}

function itemHTML(item, lookup){
  const row = lookup(item.name);          // also stamps the HAS flags for this item
  const keys = [item.name.toLowerCase()];
  if(row) keys.push(String(row.name).toLowerCase());
  const name = itemLink(itemIdFor(item, row), item.name);
  const notes = item.notes.length
    ? `<div class="prio-note">${whEsc(item.notes.join(' · '))}</div>` : '';
  const muted = mutedHTML(item, keys);

  if(item.openRoll){
    return { empty:false, html:
      `<div class="prio-item">
         <div class="prio-item-name">${name}</div>
         <div class="prio-tiers"><span class="prio-open">MS &gt; OS — open to everyone signed up</span></div>
         ${muted}${notes}
       </div>` };
  }

  // Null when the sheet gave the item no chain at all — P2 does that for the
  // things prio can't rank ("Ashes of Al'ar: Epic Flying Trained Only"), where
  // the row is a name and a note. There is nothing to walk; the tier line below
  // says so instead.
  const plan = reservePlan(item, keys);
  let anyone = false;
  let rank = 0;
  const tiers = !plan ? '' : plan.tiers.map((t, i) => {
    // Every token here already listed above — drop the tier rather than print a
    // rank nobody occupies.
    if(t.groups.every(g => g.repeat)) return '';
    rank++;
    // The tier that ends up reserving, marked so the export and the page agree.
    const reserves = i === plan.settled ? ' is-reserving' : '';
    const body = t.groups.map(g => {
      if(g.repeat) return `<span class="prio-none">${whEsc(g.label)} — listed above</span>`;
      if(!g.hits.length){
        return `<span class="prio-none">${whEsc(g.label)} — ${g.named ? 'not a spec, and nobody here by that name' : 'nobody'}</span>`;
      }
      anyone = true;
      const amb = g.ambiguous ? '<span class="prio-amb" title="This token means more than one spec">?</span>' : '';
      return `<span class="prio-token">${whEsc(g.label)}${amb}</span>` +
             g.hits.map(c => candidateHTML(c, keys, plan.hasAlready(c), item.name)).join('');
    }).join('<span class="prio-eq">=</span>');
    return `<span class="prio-tier${reserves}"><span class="prio-rank">${rank}</span>${body}</span>`;
  }).filter(Boolean).join('<span class="prio-gt">›</span>');

  // "Shadow > Destro > MS > OS" — a named chain that then opens to the room.
  const tail = item.openTail
    ? `<span class="prio-gt">›</span><span class="prio-tier"><span class="prio-rank">${rank + 1}</span>` +
      '<span class="prio-open">MS &gt; OS — anyone else signed up</span></span>'
    : '';

  const tierHtml = item.tiers.length ? tiers + tail
                 : (item.openTail ? '<span class="prio-open">MS &gt; OS — open to everyone signed up</span>'
                                  : '<span class="prio-none">No prio on the sheet</span>');
  return { empty: !anyone && !item.openTail, html:
    `<div class="prio-item${anyone ? '' : ' is-empty'}">
       <div class="prio-item-name">${name}</div>
       <div class="prio-tiers">${tierHtml}</div>
       ${muted}${notes}
     </div>` };
}

/* The officer-only "muted" line under an item: the characters an officer has told
   not to roll on it, each with a ✕ to lift the mute. Empty for members and for any
   item with no mutes. Names come from the exclusion rows, so a muted character need
   not be signed up tonight to be listed and restorable. */
function mutedHTML(item, keys){
  if(!isOfficer()) return '';
  const ids = excludedIdsForKeys(keys);
  if(!ids.size) return '';
  const names = [...ids].map(cid => {
    const nm = exclusionNames.get(String(cid)) || 'character';
    return `<span class="prio-muted-name">${whEsc(nm)}` +
           `<button class="prio-mute" title="Un-mute on this item" data-cid="${whEsc(String(cid))}" data-item="${whEsc(item.name)}" onclick="restoreChar(this)">×</button></span>`;
  }).join('');
  return `<div class="prio-muted">Muted here: ${names}</div>`;
}

function render(){
  const lookup = annotate(equipped, awards);
  wornByChar = buildWornByChar(equipped);
  const host = document.getElementById('prioResult');

  const myHtml = renderMyPrio(lookup);

  let hidden = 0;
  const html = sections.map(section => {
    const items = section.items.map(it => itemHTML(it, lookup));
    const shown = items.filter(i => !(hideEmpty && i.empty));
    hidden += items.length - shown.length;
    if(!shown.length) return '';
    return `<section class="prio-boss">
              <h2>${whEsc(section.name)}</h2>
              ${shown.map(i => i.html).join('')}
            </section>`;
  }).join('');

  host.innerHTML = myHtml +
    (html || '<div class="pool-empty">Nothing to show — try unticking "hide items nobody wants".</div>');
  document.getElementById('prioHiddenNote').textContent =
    hidden ? `${hidden} item${hidden===1?'':'s'} hidden — nobody signed up wants them.` : '';
  document.getElementById('prioHead').textContent =
    `${raidTab(picked.raid).label} — ${picked.eventTitle}`;
  document.getElementById('prioAsOf').textContent = cachedAt
    ? '· as of ' + new Date(cachedAt).toLocaleTimeString([], { hour:'2-digit', minute:'2-digit' })
    : '';
  document.getElementById('prioToolbar').style.display = 'flex';
  loadWowhead();
}

/* ───────────────────────── "What can I roll on" ─────────────────────────
   The signed-in user's own characters run against the same parsed sheet, so a
   member (or an officer checking their own alts) sees, up front, every item on this
   raid they hold *named* prio on. Open MS > OS is deliberately left out — the whole
   point is "where do I actually have priority". Items a character already has, and
   items an officer has muted them on, drop out too. */
let wornByChar = new Map();  // charNameLower → Set(item id) worn, for the HAS check below

function buildWornByChar(rows){
  const m = new Map();
  (rows || []).forEach(r => {
    const id = Number(r.id);
    if(!id) return;
    (r.equipped || []).forEach(e => {
      const key = String(e.name || '').toLowerCase();
      let ids = m.get(key);
      if(!ids){ ids = new Set(); m.set(key, ids); }
      ids.add(id);
    });
  });
  return m;
}

// Does this character already hold the item? The equipped row lists everyone
// wearing it by name; a tier token is instead held as its redeemed piece, matched
// by id against what the character wears (same logic as annotate's HAS).
function myHasItem(mc, item, row){
  if(row && (row.equipped || []).some(e => String(e.name || '').toLowerCase() === mc.name.toLowerCase()))
    return true;
  const tok = tierToken(item.name.toLowerCase());
  if(tok){
    const ids = tok[mc.cls];
    const worn = wornByChar.get(mc.name.toLowerCase());
    if(ids && worn && ids.some(id => worn.has(id))) return true;
  }
  return false;
}

// My characters that hold named prio on one item, each with the tier they sit in.
function myPrioForItem(item, keys, row){
  if(isOpenItem(item) || item.openRoll) return [];
  const excludedIds = excludedIdsForKeys(keys);
  const out = [];
  myChars.forEach(mc => {
    if(isExcluded(mc, excludedIds)) return;
    if(myHasItem(mc, item, row)) return;
    let found = -1;
    for(let i = 0; i < item.tiers.length && found < 0; i++){
      if(item.tiers[i].tokens.some(tok => tok.specs && tok.specs.some(s => matchesSpec(mc, s)))) found = i;
    }
    if(found >= 0) out.push({ char: mc, rank: found + 1 });
  });
  return out;
}

function renderMyPrio(lookup){
  if(!myChars.length) return '';

  const perChar = new Map();   // character name → [{ name, id, rank }]
  sections.forEach(section => section.items.forEach(item => {
    const keys = [item.name.toLowerCase()];
    const row = lookup(item.name);
    if(row) keys.push(String(row.name).toLowerCase());
    myPrioForItem(item, keys, row).forEach(h => {
      let arr = perChar.get(h.char.name);
      if(!arr){ arr = []; perChar.set(h.char.name, arr); }
      arr.push({ name: item.name, id: itemIdFor(item, row), rank: h.rank });
    });
  }));

  const raidLabel = raidTab(picked.raid).label;
  if(!perChar.size){
    return `<section class="prio-boss my-prio">
              <h2>What you can roll on</h2>
              <div class="prio-note">Nothing on the ${whEsc(raidLabel)} sheet gives your characters named prio — anything you'd roll on here is open MS &gt; OS.</div>
            </section>`;
  }

  const clsOf = new Map(myChars.map(c => [c.name, c.cls]));
  const blocks = [...perChar.entries()].map(([name, arr]) => {
    arr.sort((a, b) => a.rank - b.rank || a.name.localeCompare(b.name));
    const color = RH.CLASS_COLORS[clsOf.get(name)] || '#7fa89c';
    const items = arr.map(it =>
      `<span class="my-prio-item">${itemLink(it.id, it.name)}<span class="my-prio-rank">tier ${it.rank}</span></span>`).join('');
    return `<div class="my-prio-char">
              <a class="prio-name" href="character.html?name=${encodeURIComponent(name)}" style="--class-color:${color}">${whEsc(name)}</a>
              <div class="my-prio-items">${items}</div>
            </div>`;
  }).join('');

  return `<section class="prio-boss my-prio">
            <h2>What you can roll on <span class="my-prio-sub">— ${whEsc(raidLabel)}, your prio only (no MS &gt; OS)</span></h2>
            ${blocks}
          </section>`;
}

/* ───────────────────────── Muting a character ─────────────────────────
   The officer ✕ next to a name, and the restore ✕ on the muted line. Both hit the
   backend, update the in-memory index and the cache, then re-render off what is
   already loaded — no rebuild. */
async function apiSend(method, path, body){
  const opts = { method, headers: RH.headers() };
  if(body){
    opts.headers = Object.assign({ 'Content-Type':'application/json' }, RH.headers());
    opts.body = JSON.stringify(body);
  }
  let res;
  try { res = await fetch(API_BASE + path, opts); }
  catch(err){ const e = new Error('Could not reach the API.'); e.cause = err; throw e; }
  if(!res.ok){ const e = new Error('The API returned HTTP ' + res.status + '.'); e.status = res.status; throw e; }
  return res;
}

async function ignoreChar(btn){
  const cid = btn.getAttribute('data-cid');
  const item = btn.getAttribute('data-item');
  if(!cid || !item) return;
  const key = item.toLowerCase();
  try {
    await apiSend('POST', '/api/loot-prio/exclusions', { characterId: cid, raid: picked.raid, itemName: item });
  } catch(err){ reportError(err, 'The mute endpoint is unavailable.'); return; }

  let set = exclusions.get(key);
  if(!set){ set = new Set(); exclusions.set(key, set); }
  set.add(String(cid));
  const known = candidates.concat(myChars).find(c => String(c.characterId) === String(cid));
  if(known) exclusionNames.set(String(cid), known.name);
  save();
  render();
}

async function restoreChar(btn){
  const cid = btn.getAttribute('data-cid');
  const item = btn.getAttribute('data-item');
  if(!cid || !item) return;
  const key = item.toLowerCase();
  try {
    await apiSend('DELETE', '/api/loot-prio/exclusions?characterId=' +
      encodeURIComponent(cid) + '&raid=' + encodeURIComponent(picked.raid) +
      '&itemName=' + encodeURIComponent(item));
  } catch(err){ reportError(err, 'The mute endpoint is unavailable.'); return; }

  const set = exclusions.get(key);
  if(set){ set.delete(String(cid)); if(!set.size) exclusions.delete(key); }
  save();
  render();
}

// Rebuild the shown list from the network again — the toolbar's Refresh.
function refreshBuild(){
  if(!picked.eventId){ setStatus('Load the events and build a list first.', true); return; }
  build(picked.eventId);
}

function toggleHideEmpty(el){
  hideEmpty = !!el.checked;
  save();
  // The gear and loot rows are already in memory, so the filter costs no refetch.
  if(sections.length) render();
}

/* ───────────────────────── Gargul soft-reserve export ─────────────────────────
   Gargul reads a soft-reserve import as base64( zlib( JSON ) ) — see
   Classes/SoftRes.lua, importGargulData: base64 decode, LibDeflate:DecompressZlib,
   JSON decode, then it wants `metadata.id` and a `softreserves` array of
   { name, class, note, plusOnes, items:[{id}] }. Class must be one of Gargul's
   own lowercase names (Data/Constants.lua) or it silently rewrites it to priest —
   ours already are. There is a CSV format too, but Gargul warns that it is
   deprecated, so this builds the current one.

   A soft reserve is flat, and the sheet's prio is not, so reservePlan() decides
   who goes in: the top tier, minus anyone who already has the item, dropping to
   the next tier when that empties a whole one. The sheet's spec token rides along
   as the player's note, so Gargul can show *why* they hold it.

   plusOnes is 0 for everyone — we don't track plus-ones. Gargul notices when that
   collides with plus-ones it already has and asks before overwriting, which is
   what the warning under the button is about. */

const SR_METADATA_ID = 'wooback-loot-prio';

/* An item the sheet gives no prio at all — "MS > OS" in the Bias column, or no
   chain on the row — has no reserver to name, and Gargul only shows an item at
   all if somebody reserved it. So they are reserved by a raider who doesn't
   exist: the list then carries every item that drops, and the ones free to roll
   on say so in the reserver's own name. Nobody is called this in game, so it
   can't collide with a real signup. */
const OPEN_ROLL_NAME = 'MS>OS';

// Item ids, which the sheet doesn't carry. The backend resolves names against
// Blizzard's TBC item table and caches them; anything it can't place comes back
// listed so the export can say so instead of quietly dropping an item.
async function resolveItemIds(names){
  const res = await fetch(API_BASE + '/api/items/resolve', {
    method: 'POST',
    headers: Object.assign({ 'Content-Type':'application/json' }, RH.headers()),
    body: JSON.stringify({ names })
  }).catch(err => { const e = new Error('Could not reach the API.'); e.cause = err; throw e; });

  if(!res.ok){
    const e = new Error('Item lookup returned HTTP ' + res.status + '.');
    e.status = res.status;
    throw e;
  }
  return res.json();
}

// Every distinct item name on the built raid → its id, so the links tooltip. The
// sheet carries no ids, and only some items appear in our gear/loot data, so this
// fills the rest from Blizzard's item table (cached server-side). Best-effort: on
// failure the page keeps the ids it already had from gear/loot.
async function resolveAllItemIds(){
  const names = [], seen = new Set();
  sections.forEach(s => s.items.forEach(it => {
    const k = it.name.toLowerCase();
    if(!seen.has(k)){ seen.add(k); names.push(it.name); }
  }));
  if(!names.length) return new Map();
  try {
    const res = await resolveItemIds(names);
    const map = new Map();
    Object.keys(res.resolved || {}).forEach(n => {
      const id = res.resolved[n];
      if(id) map.set(String(n).toLowerCase(), id);
    });
    return map;
  } catch(err){
    console.warn('Item id resolution failed; tooltips fall back to gear/loot ids only:', err);
    return new Map();
  }
}

// The best id we have for an item: our own gear/loot data first (it is exact), then
// the resolved Blizzard id.
function itemIdFor(item, row){
  return (row && row.id) || itemIds.get(item.name.toLowerCase()) || null;
}

// base64(zlib(bytes)). CompressionStream('deflate') is the zlib wrapper Gargul's
// LibDeflate:DecompressZlib expects — 'deflate-raw' would be the one it rejects.
async function zlibBase64(text){
  if(typeof CompressionStream !== 'function')
    throw new Error('This browser has no CompressionStream, so it can’t build a Gargul import string.');

  const stream = new Blob([new TextEncoder().encode(text)]).stream()
    .pipeThrough(new CompressionStream('deflate'));
  const buf = new Uint8Array(await new Response(stream).arrayBuffer());

  // btoa wants a binary string; chunk it so a big payload can't blow the stack.
  let bin = '';
  for(let i = 0; i < buf.length; i += 0x8000)
    bin += String.fromCharCode.apply(null, buf.subarray(i, i + 0x8000));
  return btoa(bin);
}

// Every item that ends up with a reserver, as { character → [{id, note}] }.
function buildReserves(itemIds){
  const lookup = annotate(equipped, awards);
  const byName = new Map();
  let items = 0, open = 0;
  const unpriced = [];        // reserved by someone, but no id to reserve with

  function reserve(name, cls, id, note){
    if(!byName.has(name)) byName.set(name, { cls, notes:new Set(), ids:new Set() });
    const entry = byName.get(name);
    entry.ids.add(id);
    entry.notes.add(note);
  }

  sections.forEach(section => section.items.forEach(item => {
    const row = lookup(item.name);
    const keys = [item.name.toLowerCase()];
    if(row) keys.push(String(row.name).toLowerCase());

    // Null exactly when isOpenItem(item) — the sheet ranked nobody, so the
    // placeholder holds it. An item that *is* ranked but whose tiers are all
    // people who aren't here (settled < 0) is a different thing and stays out:
    // it has a prio, and saying otherwise in the export would be a lie.
    const plan = reservePlan(item, keys);
    if(plan && plan.settled < 0) return;

    const id = itemIds[item.name] || (row && row.id) || 0;
    if(!id){ unpriced.push(item.name); return; }

    items++;
    if(!plan){
      open++;
      // No class of its own — Gargul rewrites anything it doesn't know to
      // priest, so naming that outright is the same result, said honestly.
      reserve(OPEN_ROLL_NAME, 'priest', id, 'MS > OS');
      return;
    }

    plan.tiers[plan.settled].groups.forEach(g => g.hits.forEach(c => {
      if(plan.hasAlready(c)) return;
      reserve(c.name, c.cls, id, g.label);
    }));
  }));

  return { byName, items, open, unpriced };
}

async function copyGargulSR(){
  if(!sections.length){ setStatus('Build a prio list first.', true); return; }

  setStatus(`Looking up item ids for ${raidTab(picked.raid).label}…`);

  // Only the items that actually have a reserver need an id — someone off the
  // sheet's chain, or the MS > OS placeholder for the ones it never ranked.
  const wanted = [];
  const lookup = annotate(equipped, awards);
  sections.forEach(s => s.items.forEach(item => {
    const row = lookup(item.name);
    const keys = [item.name.toLowerCase()];
    if(row) keys.push(String(row.name).toLowerCase());
    const plan = reservePlan(item, keys);
    if((!plan || plan.settled >= 0) && !wanted.includes(item.name)) wanted.push(item.name);
  }));
  if(!wanted.length){ setStatus('Nothing to reserve — no item has anyone on prio.', true); return; }

  let resolved;
  try {
    resolved = await resolveItemIds(wanted);
  } catch(err){
    reportError(err, 'The item lookup is unavailable.');
    return;
  }

  const { byName, items, open, unpriced } = buildReserves(resolved.resolved || {});
  if(!byName.size){ setStatus('Nothing to reserve — no item resolved to an id.', true); return; }

  const now = Math.floor(Date.now() / 1000);
  const payload = {
    metadata: {
      id: SR_METADATA_ID + '-' + picked.raid + '-' + (picked.eventId || 'manual'),
      createdAt: now,
      updatedAt: now,
      hidden: false,
      // Gargul falls back to a softres.it URL built from the id when this is
      // absent, which would be a dead link — point at the page that made it.
      url: location.origin + location.pathname,
      discordUrl: '',
      raidStartsAt: now
    },
    softreserves: [...byName.entries()].map(([name, e]) => ({
      name,
      class: e.cls || 'priest',
      note: [...e.notes].join(', '),
      plusOnes: 0,
      items: [...e.ids].map(id => ({ id }))
    })),
    hardreserves: []
  };

  let str;
  try {
    str = await zlibBase64(JSON.stringify(payload));
  } catch(err){
    console.error('Gargul export failed:', err);
    setStatus(err.message, true);
    return;
  }

  showSrExport(str, { raiders: byName.size, items, open, unpriced,
                      unresolved: (resolved.unresolved || []).concat(unpriced) });
}

/* The string, in a box to copy from. Shown rather than silently copied: it is
   long, an officer wants to see it exists, and the clipboard may be blocked. */
function showSrExport(str, stats){
  const box = document.getElementById('srExport');
  const unresolved = [...new Set(stats.unresolved)];
  const open = stats.open
    ? ` ${stats.open} item${stats.open===1?'':'s'} the sheet ranks nobody for are held by <b>${OPEN_ROLL_NAME}</b>, who is not a real raider — they are free to roll on.`
    : '';
  const warn = unresolved.length
    ? `<div class="prio-note">${unresolved.length} item${unresolved.length===1?'':'s'} left out — no item id could be found for ${whEsc(unresolved.slice(0,6).join(', '))}${unresolved.length>6?', …':''}. Fix the spelling on the sheet and rebuild.</div>`
    : '';

  box.innerHTML =
    `<label>Gargul soft-reserve import</label>
     <p class="prio-note">${stats.items} item${stats.items===1?'':'s'} reserved by ${stats.raiders} raider${stats.raiders===1?'':'s'} — the top tier of each, skipping anyone who already has it.
${open}
        Paste into Gargul: <b>/gl softreserves</b> → Import. Gargul will ask before overwriting any PlusOne values it already has; this export carries none, so answer <b>No</b> to keep yours.</p>
     ${warn}
     <textarea id="srExportText" class="sr-text" readonly rows="4"></textarea>
     <div class="sr-actions">
       <button class="btn-ghost" onclick="copySrText()">Copy to clipboard</button>
       <span id="srCopyNote" style="font-size:11px;color:var(--text-dim);"></span>
     </div>`;
  document.getElementById('srExportText').value = str;
  box.style.display = 'block';
  setStatus(`Soft-reserve string built — ${stats.items} items, ${stats.raiders} raiders.`);
}

function copySrText(){
  const el = document.getElementById('srExportText');
  const note = document.getElementById('srCopyNote');
  el.select();
  navigator.clipboard.writeText(el.value)
    .then(() => { note.textContent = 'Copied.'; })
    .catch(() => { note.textContent = 'Clipboard blocked — select the box and copy by hand.'; });
}

/* Plain text for pasting into Discord — the same plan without the markup. */
function copyText(){
  if(!sections.length){ setStatus('Build a prio list first.', true); return; }
  const lines = [`${raidTab(picked.raid).label} — ${picked.eventTitle}`, ''];

  sections.forEach(section => {
    const body = [];
    section.items.forEach(item => {
      if(item.openRoll){
        if(!hideEmpty) body.push(`  ${item.name}: MS > OS`);
        return;
      }
      const taken = new Set();
      const excludedIds = excludedIdsForKeys([item.name.toLowerCase()]);
      const parts = item.tiers.map(tier => tierCandidates(tier, taken, excludedIds)
        .filter(g => g.hits.length)
        .map(g => `${g.label}: ${g.hits.map(c => c.name).join(', ')}`)
        .join(' = ')).filter(Boolean);
      if(item.openTail) parts.push('then MS > OS');
      if(!parts.length && hideEmpty) return;
      body.push(`  ${item.name}: ${parts.join(' > ') || '—'}`);
    });
    if(body.length) lines.push(section.name, ...body, '');
  });

  const text = lines.join('\n');
  navigator.clipboard.writeText(text)
    .then(() => setStatus('Copied the whole prio list to the clipboard.'))
    .catch(() => {
      console.log(text);
      setStatus('Clipboard blocked — the list has been logged to the console instead.', true);
    });
}

/* ───────────────────────── Persistence ─────────────────────────
   The whole built list is cached, not just the picks, so a mid-raid reload comes
   back instantly with no refetch or re-parse. Refresh (or a fresh Build) is how you
   deliberately re-read a sheet or a signup that changed. The candidate HAS/WON sets
   don't survive JSON, but annotate rebuilds them on every render, so that's fine.

   The gear and loot rows can be large; if the full payload trips the storage quota
   we fall back to persisting only the picks, so at least the raid and toggle stick. */

function serializeExclusions(){
  return [...exclusions.entries()].map(([k, set]) => [k, [...set]]);
}
function deserializeExclusions(pairs){
  return new Map((pairs || []).map(([k, ids]) => [k, new Set(ids)]));
}

function save(){
  const full = {
    picked, hideEmpty, cachedAt,
    sections, candidates, unknownTokens, equipped, awards, myChars,
    itemIds: [...itemIds.entries()],
    exclusions: serializeExclusions(),
    exclusionNames: [...exclusionNames.entries()]
  };
  try{ localStorage.setItem(STORE_KEY, JSON.stringify(full)); }
  catch(e){
    try{ localStorage.setItem(STORE_KEY, JSON.stringify({ picked, hideEmpty })); }catch(e2){}
  }
}

// Returns true when a full cached build was restored and rendered.
function restore(){
  let saved = null;
  try{ saved = JSON.parse(localStorage.getItem(STORE_KEY) || 'null'); }catch(e){}
  if(!saved) return false;
  if(saved.picked) picked = Object.assign(picked, saved.picked);
  if(typeof saved.hideEmpty === 'boolean') hideEmpty = saved.hideEmpty;
  document.getElementById('hideEmpty').checked = hideEmpty;
  if(RAID_TABS.some(r => r.key === picked.raid)) document.getElementById('raidPick').value = picked.raid;

  if(saved.sections && saved.sections.length){
    sections = saved.sections;
    candidates = saved.candidates || [];
    unknownTokens = saved.unknownTokens || [];
    equipped = saved.equipped || [];
    awards = saved.awards || [];
    myChars = saved.myChars || [];
    cachedAt = saved.cachedAt || 0;
    itemIds = new Map(saved.itemIds || []);
    exclusions = deserializeExclusions(saved.exclusions);
    exclusionNames = new Map(saved.exclusionNames || []);
    render();
    return true;
  }
  return false;
}

function startOver(){
  picked = { eventId:'', eventTitle:'', raid:RAID_TABS[0].key };
  candidates = []; sections = []; unknownTokens = [];
  myChars = []; exclusions = new Map(); exclusionNames = new Map(); itemIds = new Map(); cachedAt = 0;
  try{ localStorage.removeItem(STORE_KEY); }catch(e){}
  document.getElementById('prioResult').innerHTML = '';
  document.getElementById('prioHead').textContent = '';
  document.getElementById('prioAsOf').textContent = '';
  document.getElementById('prioHiddenNote').textContent = '';
  document.getElementById('prioToolbar').style.display = 'none';
  setStatus('Cleared. Load the events to start again.');
}

(function(){
  function ready(fn){
    if(document.readyState !== 'loading') fn();
    else document.addEventListener('DOMContentLoaded', fn);
  }
  ready(function(){
    // Grouped by phase, because the guild raids two and the picker is otherwise
    // four flat entries with the phase buried in each label.
    const phases = [...new Set(RAID_TABS.map(r => r.doc))];
    document.getElementById('raidPick').innerHTML = phases.map(doc =>
      `<optgroup label="${whEsc(PHASE_LABELS[doc] || doc)}">` +
      RAID_TABS.filter(r => r.doc === doc)
               .map(r => `<option value="${r.key}">${whEsc(r.label)}</option>`).join('') +
      '</optgroup>').join('');
    const restored = restore();
    setStatus(restored
      ? 'Showing your last build — Refresh to re-read the sheet, or Load events to pick another.'
      : 'Load the events, pick tonight’s signup and the raid, then build.');
  });
})();
