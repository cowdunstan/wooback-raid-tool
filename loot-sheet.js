/* ───────────────────────── Loot sheet (shared) ─────────────────────────
   The guild loot sheets, fetched through the backend /sheet/loot proxy and parsed
   in the browser into bosses → items → prio tiers, plus the class/spec eligibility
   matcher and the roster-character shaping both loot pages need.

   This is the *pure* half of what used to live in loot-prio.js — no page DOM, no
   signup, no per-page state. It depends only on menu.js (API_BASE, the RH class
   tables, RH.headers). loot-prio.js and my-priority.js both load it and add their
   own rendering on top: loot-prio joins it to a Raid-Helper signup; my-priority runs
   the signed-in user's own characters against it. Kept as plain globals (no module
   system here) so both page scripts can call these directly once this file loads
   before them. */

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
  // Set names the sheet sometimes writes for the bear instead of the spec.
  'warden':       [{ cls:'druid',   spec:'guardian' }, { cls:'druid', spec:'feral', role:'tank' }],
  'guardian':     [{ cls:'druid',   spec:'guardian' }, { cls:'druid', spec:'feral', role:'tank' }],
  'cat':          [{ cls:'druid',   spec:'feral', role:'dps' }],
  'feral dps':    [{ cls:'druid',   spec:'feral', role:'dps' }],
  'feral':        [{ cls:'druid',   spec:'feral' }],

  'dps warrior':  [{ cls:'warrior', spec:'arms' }, { cls:'warrior', spec:'fury' }],
  'arms':         [{ cls:'warrior', spec:'arms' }],
  '2h arms':      [{ cls:'warrior', spec:'arms' }],
  'fury':         [{ cls:'warrior', spec:'fury' }],
  'prot warrior': [{ cls:'warrior', spec:'protection' }],
  'gladiator':    [{ cls:'warrior', spec:'protection' }],  // set name for the prot warrior
  // A bare class name means the dps side of it — the sheet always spells a tank
  // out as "Prot Warrior" / "Prot". Reached by rows like "No Talon Warrior".
  'warrior':      [{ cls:'warrior', spec:'arms' }, { cls:'warrior', spec:'fury' }],

  'rogue':        [{ cls:'rogue',   spec:null }],

  'holy':         [{ cls:'paladin', spec:'holy' }, { cls:'priest', spec:'holy' }],
  'holy paladin': [{ cls:'paladin', spec:'holy' }],
  'prot':         [{ cls:'paladin', spec:'protection' }],
  'prot paladin': [{ cls:'paladin', spec:'protection' }],
  'justicar':     [{ cls:'paladin', spec:'protection' }],  // set name for the prot paladin
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
  'helm of the vanquished champion': { paladin:[29061,29068,29073], rogue:[30146], shaman:[30171,30190,30166] },
  'helm of the vanquished defender': { warrior:[30115,30120], priest:[30152,30161], druid:[30219,30228,30233] },
  'helm of the vanquished hero': { hunter:[30141], mage:[30206], warlock:[30212] },
  'helm of the forgotten conqueror': { paladin:[30987,30988,30989], priest:[31063,31064], warlock:[31051] },
  'helm of the forgotten protector': { warrior:[30972,30974], hunter:[31003], shaman:[31012,31014,31015] },
  'helm of the forgotten vanquisher': { rogue:[31027], mage:[31056], druid:[31037,31039,31040] },
  'pauldrons of the vanquished champion': { paladin:[29064,29070,29075], rogue:[30149], shaman:[30173,30194,30168] },
  'pauldrons of the vanquished defender': { warrior:[30117,30122], priest:[30154,30163], druid:[30221,30230,30235] },
  'pauldrons of the vanquished hero': { hunter:[30143], mage:[30210], warlock:[30215] },
  'pauldrons of the forgotten conqueror': { paladin:[30996,30997,30998], priest:[31069,31070], warlock:[31054] },
  'pauldrons of the forgotten protector': { warrior:[30979,30980], hunter:[31006], shaman:[31022,31023,31024] },
  'pauldrons of the forgotten vanquisher': { rogue:[31030], mage:[31059], druid:[31047,31048,31049] },
  'chestguard of the vanquished champion': { paladin:[29062,29066,29071], rogue:[30144], shaman:[30169,30185,30164] },
  'chestguard of the vanquished defender': { warrior:[30113,30118], priest:[30150,30159], druid:[30216,30222,30231] },
  'chestguard of the vanquished hero': { hunter:[30139], mage:[30196], warlock:[30214] },
  'chestguard of the forgotten conqueror': { paladin:[30990,30991,30992], priest:[31065,31066], warlock:[31052] },
  'chestguard of the forgotten protector': { warrior:[30975,30976], hunter:[31004], shaman:[31016,31017,31018] },
  'chestguard of the forgotten vanquisher': { rogue:[31028], mage:[31057], druid:[31041,31042,31043] },
  'leggings of the vanquished champion': { paladin:[29063,29069,29074], rogue:[30148], shaman:[30172,30192,30167] },
  'leggings of the vanquished defender': { warrior:[30116,30121], priest:[30153,30162], druid:[30220,30229,30234] },
  'leggings of the vanquished hero': { hunter:[30142], mage:[30207], warlock:[30213] },
  'leggings of the forgotten conqueror': { paladin:[30993,30994,30995], priest:[31067,31068], warlock:[31053] },
  'leggings of the forgotten protector': { warrior:[30977,30978], hunter:[31005], shaman:[31019,31020,31021] },
  'leggings of the forgotten vanquisher': { rogue:[31029], mage:[31058], druid:[31044,31045,31046] },
  'gloves of the vanquished champion': { paladin:[29065,29067,29072], rogue:[30145], shaman:[30170,30189,30165] },
  'gloves of the vanquished defender': { warrior:[30114,30119], priest:[30151,30160], druid:[30217,30223,30232] },
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

let unknownTokens = [];   // sheet tokens the table above doesn't know

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

// Does this candidate satisfy one of a token's specs? A candidate with no spec at
// all still matches on class — better to over-list them, flagged, than to drop a
// raider who signed up class-only.
function matchesSpec(cand, spec){
  if(!cand.cls || cand.cls !== spec.cls) return false;
  if(spec.spec && cand.spec && cand.spec !== spec.spec) return false;
  if(spec.role && cand.role && cand.role !== spec.role) return false;
  return true;
}

/* One member's characters, shaped like signup candidates so the same spec matcher
   ranks them. The roster from /api/members already drops ignored characters. */
function buildCharsFor(member){
  if(!member) return [];
  return (member.characters || []).map(ch => {
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
