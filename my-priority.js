/* ───────────────────────── What can I roll on ─────────────────────────
   The member-facing twin of loot-prio.js. No Raid-Helper signup: it runs the
   signed-in user's *own* roster characters against a raid's loot sheet and shows,
   per boss, every item — marking the ones they can roll on (their named prio tier,
   or open MS > OS) — plus a "What you have prio on" summary grouped by tier, the
   same one loot-prio puts up top.

   All the sheet reading, parsing, class/spec matching and character shaping is in
   loot-sheet.js (shared with loot-prio.js); RH, itemLink, whEsc, loadWowhead,
   sessionPayload and isOfficer are in menu.js. This file is only the page: state,
   data load, eligibility marking and rendering. Nothing is saved on the server —
   the whole page is derived from the sheet, the roster and the gear/loot history;
   only the picked raid and the built list persist, in localStorage. */

const STORE_KEY = 'vashj_loot_rolls';

let picked = { raid: RAID_TABS[0].key };
let sections = [];        // the parsed sheet: [{ name, items:[…] }]
let members = [];         // GET /api/members — to find "me" and build the View-as roster
let myChars = [];         // candidate-shaped objects for the signed-in user's own characters
let pickerRoster = [];    // officer View-as options: [{ id, label, chars }] for every member
let viewAs = { kind:'self' }; // whose rolls the page shows: self | member
let charPick = '';         // characterId of the one character shown, when the view has more than one
let equipped = [];        // GET /api/items/list — who is wearing what, for the HAS check
let awards = [];          // GET /api/loot/history — kept only so a cached build round-trips
let exclusions = new Map(); // itemNameLower → Set(characterId) — officers' per-item roll mutes
let exclusionNames = new Map(); // characterId → name, for a cached build's round-trip
let itemIds = new Map();   // itemNameLower → wowhead id, resolved so every item tooltips
let onlyMine = false;      // the "only what I can roll on" toggle
let cachedAt = 0;         // when the shown build was fetched (ms) — for the "as of" note

function setStatus(msg, isErr){
  const el = document.getElementById('rollsStatus');
  el.textContent = msg || '';
  el.style.color = isErr ? 'var(--amber)' : 'var(--text-dim)';
}

function reportError(err, notFoundMsg){
  console.error('Loot rolls call failed:', err);
  if(err.status === 401){ setStatus('Session expired — signing you out…', true); setTimeout(logout, 1200); return; }
  if(err.status === 403){ setStatus('Unauthorized.', true); return; }
  if(err.status === 404){ setStatus(notFoundMsg, true); return; }
  setStatus(err.message + ' See the browser console.', true);
}

/* ───────────────────────── Per-item mutes ─────────────────────────
   An officer can mute a character on an item over on loot-prio; this page reads
   those mutes so a muted character drops out of their own roll list. Same shape as
   loot-prio's — rows of { characterId, itemName }, indexed by the sheet's item name
   (lowercased). This page never sets them, so there is no ✕ affordance. */
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

/* ───────────────────────── Who is "me" ─────────────────────────
   The signed-in user's own characters — all of them, straight off the roster with
   no signup to narrow by. buildCharsFor (loot-sheet.js) shapes them so the same
   spec matcher ranks them. An officer can point the page at someone else with the
   View-as picker (activeViewChars); this stays the default. */
function buildMyChars(memberList){
  const pl = (typeof sessionPayload === 'function') ? sessionPayload() : null;
  const uid = pl && pl.uid ? String(pl.uid) : '';
  if(!uid) return [];
  return buildCharsFor((memberList || []).find(m => String(m.discordUserId || '') === uid) || null);
}

function buildPickerRoster(memberList){
  return (memberList || []).map(m => ({
    id: String(m.discordUserId || ''),
    label: m.nickname || m.displayName || m.discordUsername || m.name || 'member',
    chars: buildCharsFor(m)
  })).filter(r => r.chars.length);
}

/* The character roster the page is currently about: the signed-in user's own, or an
   officer's View-as pick. One character is then chosen from it (activeViewChars) — a
   member with more than one character picks which with the Character dropdown. */
function viewRoster(){
  if(isOfficer() && viewAs.kind === 'member'){
    const r = pickerRoster.find(x => x.id === String(viewAs.id));
    if(r) return { chars:r.chars, label:r.label, self:false };
  }
  return { chars: myChars, label:'you', self:true };
}

// The one character the page shows: the picked one if it is still in the current
// roster, else the main, else the first. With a single character there is nothing to
// pick — it is that one.
function selectedChar(chars){
  if(!chars || !chars.length) return null;
  return chars.find(c => String(c.characterId) === String(charPick))
      || chars.find(c => c.isMain)
      || chars[0];
}

function activeViewChars(){
  const roster = viewRoster();
  const sel = selectedChar(roster.chars);
  return { chars: sel ? [sel] : [], label: roster.label, self: roster.self,
           roster: roster.chars, selected: sel };
}

/* ───────────────────────── HAS: what a character already holds ─────────────────
   Lifted from loot-prio.js so an item a character already owns drops out of their
   personal prio list, exactly as it does there. A tier token is held as its
   redeemed piece, matched by id against what the character wears. */
let wornByChar = new Map();  // charNameLower → Set(item id) worn

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

// One insertion, deletion or substitution apart — the shape a hand-typed item name
// in the sheet gets wrong. Same as loot-prio's, so the gear/loot row for an item
// still matches a slightly mis-typed sheet name.
function oneTypoApart(a, b){
  if(a === b) return true;
  if(Math.abs(a.length - b.length) > 1) return false;
  let head = 0;
  while(head < a.length && head < b.length && a[head] === b[head]) head++;
  let tail = 0;
  while(tail < a.length - head && tail < b.length - head &&
        a[a.length - 1 - tail] === b[b.length - 1 - tail]) tail++;
  return (a.length - head - tail) <= 1 && (b.length - head - tail) <= 1;
}

// The gear/loot row for an item name, by exact then substring then one-typo match —
// carries the item's real id and who is wearing it. No candidate stamping here (this
// page has no signup candidates); HAS is decided per character by myHasItem.
function buildLookup(rows){
  rows = rows || [];
  const exact = new Map();
  rows.forEach(r => exact.set(String(r.name || '').toLowerCase(), r));
  return function(itemName){
    const lower = String(itemName).toLowerCase();
    return exact.get(lower)
        || rows.find(r => String(r.name || '').toLowerCase().includes(lower))
        || rows.find(r => oneTypoApart(String(r.name || '').toLowerCase(), lower))
        || null;
  };
}

function itemIdFor(item, row){
  return (row && row.id) || itemIds.get(item.name.toLowerCase()) || null;
}

// Every distinct item name on the built raid → its id, so the links tooltip. Uses
// the shared resolveItemIds (loot-sheet.js); best-effort, falls back to gear/loot
// ids on failure.
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

/* ───────────────────────── Building ───────────────────────── */
async function build(){
  const raidKey = document.getElementById('raidPick').value;
  const raid = raidTab(raidKey);
  setStatus(`Loading the roster and the ${raid.label} sheet…`);

  let tabs, exclRows;
  try {
    // Gear and loot degrade to empty rather than failing the build — the list is
    // still useful without the HAS check.
    [members, tabs, equipped, awards, exclRows] = await Promise.all([
      apiGet('/api/members').then(r => r.json()),
      fetchRaidTabs(raid),
      apiGet('/api/items/list').then(r => r.json()).catch(() => []),
      apiGet('/api/loot/history').then(r => r.json()).catch(() => []),
      apiGet('/api/loot-prio/exclusions?raid=' + encodeURIComponent(raidKey)).then(r => r.json()).catch(() => [])
    ]);
  } catch(err){
    reportError(err, 'The sheet could not be read.');
    return;
  }

  exclusions = buildExclusions(exclRows);
  exclusionNames = new Map((exclRows || [])
    .filter(r => r.characterId && r.characterName)
    .map(r => [String(r.characterId), r.characterName]));

  myChars = buildMyChars(members);
  pickerRoster = buildPickerRoster(members);
  unknownTokens = [];
  sections = tabs.reduce(
    (all, tab, i) => all.concat(parseRaidTab(tab.grid, raid.tabs[i].section, raid.tabs[i].tierSlot)), []);
  picked = { raid: raidKey };

  const items = sections.reduce((n, s) => n + s.items.length, 0);
  if(!items){ setStatus(`The ${raid.label} sheet parsed to no items — has its layout changed?`, true); return; }

  itemIds = await resolveAllItemIds();

  cachedAt = Date.now();
  save();
  render();

  const noteParts = [];
  if(tabs.some(t => !t.colored))
    noteParts.push('read without cell colours, so "Resto" and "Holy" stay ambiguous');
  if(!myChars.length)
    noteParts.push('you have no characters on the roster — sign a character up or ask an officer to link one');
  setStatus(`${items} items across ${sections.length} sections of ${raid.label}` +
            (noteParts.length ? ` — ${noteParts.join(', ')}.` : '.'));
}

// Rebuild from the network again — the toolbar's Refresh.
function refreshBuild(){
  if(!sections.length){ setStatus('Pick a raid and show its items first.', true); return; }
  build();
}

function onRaidChange(){
  // Picking a different raid rebuilds straight away when a list is already up;
  // otherwise the button does it.
  if(sections.length) build();
}

function toggleOnlyMine(el){
  onlyMine = !!el.checked;
  save();
  if(sections.length) render();
}

/* ───────────────────────── "What you have prio on" ─────────────────────────
   Lifted from loot-prio.js unchanged bar the section not needing a signup: the
   viewer's characters run against the parsed sheet and every item they hold *named*
   prio on is pulled out, grouped by tier, with the classes ahead of them named.
   Open MS > OS is deliberately left out here — that is the boss list's job below. */
// The sheet ranked nobody for this item: MS > OS outright, or no chain to walk (a
// mount, a trained recipe). Named prio can't apply, so the personal section skips it.
function isOpenItem(item){
  return item.openRoll || !item.tiers.length;
}

function myPrioForItem(item, keys, row, chars){
  if(isOpenItem(item) || item.openRoll) return [];
  const excludedIds = excludedIdsForKeys(keys);
  const out = [];
  (chars || []).forEach(mc => {
    if(isExcluded(mc, excludedIds)) return;
    if(myHasItem(mc, item, row)) return;
    let found = -1;
    for(let i = 0; i < item.tiers.length && found < 0; i++){
      if(item.tiers[i].tokens.some(tok => tok.specs && tok.specs.some(s => matchesSpec(mc, s)))) found = i;
    }
    if(found < 0) return;
    const ahead = [];
    for(let i = 0; i < found; i++){
      item.tiers[i].tokens.forEach(tok => {
        if(ahead.some(a => a.label === tok.label)) return;
        const classes = tok.specs ? new Set(tok.specs.map(s => s.cls)) : new Set();
        ahead.push({ label: tok.label, cls: classes.size === 1 ? [...classes][0] : '' });
      });
    }
    out.push({ char: mc, rank: found + 1, ahead });
  });
  return out;
}

// The spec a character was detected as — what its prio is computed from. Spec comes
// through lowercased and letters-only (buildCharsFor), so it is just capitalised for
// display; a character the logs never gave a spec is flagged so a member can see the
// gap rather than wonder why they hold no prio.
function detectedSpecLabel(mc){
  const cap = s => s ? s.charAt(0).toUpperCase() + s.slice(1) : '';
  const cls = cap(mc.cls);
  if(mc.spec) return cap(mc.spec) + (cls ? ' ' + cls : '');
  return cls ? cls + ' — no spec detected' : 'no spec detected';
}

/* The "Detected as" line: every one of the viewer's characters with the spec its prio
   was worked out from, plus the note to take a wrong one to an officer. Shown for all
   the characters, not just the ones holding prio, so a mis-detected spec is visible
   even when it is the reason a character ends up with none. */
function detectedRosterHTML(chars, self){
  const chips = chars.map(mc => {
    const color = RH.CLASS_COLORS[mc.cls] || '#7fa89c';
    return `<span class="my-prio-chip"><a class="prio-name" href="character.html?name=${encodeURIComponent(mc.name)}" style="--class-color:${color}">` +
           `${whEsc(mc.name)}<span class="my-prio-spec">${whEsc(detectedSpecLabel(mc))}</span></a>` +
           RH.bisGuidesHTML(mc) + `</span>`;
  }).join('');
  const whose = self ? 'Your' : 'Their';
  const each  = self ? 'each of your characters was' : 'each of their characters was';
  return `<div class="my-prio-detected"><span class="my-prio-detected-label">Detected as</span>${chips}</div>
          <div class="prio-note">${whose} prio here is worked out from the spec ${each} last seen playing, imported from Warcraft Logs and the roster. If one is wrong, reach out to an officer to change it.</div>`;
}

function renderMyPrio(lookup, view){
  const chars = (view && view.chars) || [];
  if(!chars.length) return '';
  const self = !!(view && view.self);
  const who = self ? 'you have' : `${whEsc(view.label)} has`;
  const whose = self ? 'your' : `${whEsc(view.label)}’s`;
  const youd = self ? 'you' : 'they';

  const perChar = new Map();   // character name → [{ name, id, rank, ahead }]
  sections.forEach(section => section.items.forEach(item => {
    const keys = [item.name.toLowerCase()];
    const row = lookup(item.name);
    if(row) keys.push(String(row.name).toLowerCase());
    myPrioForItem(item, keys, row, chars).forEach(h => {
      let arr = perChar.get(h.char.name);
      if(!arr){ arr = []; perChar.set(h.char.name, arr); }
      arr.push({ name: item.name, id: itemIdFor(item, row), rank: h.rank, ahead: h.ahead });
    });
  }));

  const raidLabel = raidTab(picked.raid).label;
  const detected = detectedRosterHTML(chars, self);
  if(!perChar.size){
    return `<section class="prio-boss my-prio">
              <h2>What ${who} prio on</h2>
              <div class="prio-note">Nothing on the ${whEsc(raidLabel)} sheet gives ${whose} characters named prio — anything ${youd}’d roll on here is open MS &gt; OS, listed by boss below.</div>
              ${detected}
            </section>`;
  }

  const clsOf = new Map(chars.map(c => [c.name, c.cls]));
  const blocks = [...perChar.entries()].map(([name, arr]) => {
    const color = RH.CLASS_COLORS[clsOf.get(name)] || '#7fa89c';
    const byTier = new Map();
    arr.forEach(it => {
      let g = byTier.get(it.rank);
      if(!g){ g = []; byTier.set(it.rank, g); }
      g.push(it);
    });
    const groups = [...byTier.entries()].sort((a, b) => a[0] - b[0]).map(([tier, items]) => {
      items.sort((a, b) => a.name.localeCompare(b.name));
      if(tier === 1){
        const links = items.map(it =>
          `<span class="my-prio-item">${itemLink(it.id, it.name)}</span>`).join('');
        return `<div class="my-prio-group">
                  <span class="my-prio-tier">prio ${tier}</span>
                  <div class="my-prio-items">${links}</div>
                </div>`;
      }
      const rows = items.map(it => {
        const specs = (it.ahead || []).map(a => {
          const color = RH.CLASS_COLORS[a.cls];
          return color ? `<span style="color:${color}">${whEsc(a.label)}</span>` : whEsc(a.label);
        }).join('<span class="my-prio-sep">·</span>');
        return `<div class="my-prio-row">
                  <span class="my-prio-item">${itemLink(it.id, it.name)}</span>
                  <span class="my-prio-ahead"><span class="my-prio-behind">behind</span>${specs}</span>
                </div>`;
      }).join('');
      return `<div class="my-prio-group">
                <span class="my-prio-tier">prio ${tier}</span>
                <div class="my-prio-rows">${rows}</div>
              </div>`;
    }).join('');
    return `<div class="my-prio-char">
              <a class="prio-name" href="character.html?name=${encodeURIComponent(name)}" style="--class-color:${color}">${whEsc(name)}</a>
              <div class="my-prio-groups">${groups}</div>
            </div>`;
  }).join('');

  return `<section class="prio-boss my-prio">
            <h2>What ${who} prio on <span class="my-prio-sub">— ${whEsc(raidLabel)}, ${self ? 'your' : 'their'} named prio only (open MS &gt; OS is below)</span></h2>
            ${detected}
            ${blocks}
          </section>`;
}

/* ───────────────────────── The boss list ─────────────────────────
   Every boss, every item, with a badge saying whether the viewer can roll on it and
   the tier they sit in lit up. Eligibility is class/spec off the sheet, plus the
   open-roll rules: an item that names your spec in a tier you can roll (prio N); one
   that is MS > OS, or opens to the room after its named chain (openTail), you can roll
   too; a named chain that never names your spec you cannot; a row the sheet gave no
   chain (a mount, a trained recipe) is left unranked. An item a character already
   holds drops out — you would not roll on gear you have — so eligibility is judged on
   the characters who don't own it, and an item every character owns reads "have this"
   (the same HAS check the personal section uses, tier tokens counted as their
   redeemed piece). */
function rollEligibility(item, row, chars){
  chars = chars || [];
  const eligible = chars.filter(mc => !myHasItem(mc, item, row));
  let rank = -1, who = null;
  for(let i = 0; i < item.tiers.length && rank < 0; i++){
    for(const mc of eligible){
      if(item.tiers[i].tokens.some(tok => tok.specs && tok.specs.some(s => matchesSpec(mc, s)))){
        rank = i; who = mc; break;
      }
    }
  }
  const open = item.openRoll || item.openTail;
  let kind;
  if(rank >= 0)                              kind = 'prio';
  else if(open && eligible.length)          kind = 'open';
  else if(chars.length && !eligible.length) kind = 'has';   // every character already holds it
  else if(!item.tiers.length)               kind = 'unranked';
  else                                       kind = 'none';
  return { kind, rank, who, can: kind === 'prio' || kind === 'open' };
}

function rollItemHTML(item, row, chars){
  const name = itemLink(itemIdFor(item, row), item.name);
  const notes = item.notes.length
    ? `<div class="prio-note">${whEsc(item.notes.join(' · '))}</div>` : '';
  const el = rollEligibility(item, row, chars);

  const badge = el.kind === 'prio'
      ? `<span class="roll-badge can">You roll · prio ${el.rank + 1}${el.who && chars.length > 1 ? ` · ${whEsc(el.who.name)}` : ''}</span>`
    : el.kind === 'open'
      ? '<span class="roll-badge open">You roll · MS &gt; OS</span>'
    : el.kind === 'has'
      ? '<span class="roll-badge has">You have this</span>'
    : el.kind === 'none'
      ? '<span class="roll-badge no">Not your specs</span>'
    : '';   // unranked: no badge, it isn't a roll at all

  let tierHtml;
  if(item.openRoll){
    tierHtml = '<span class="prio-open">MS &gt; OS — open to everyone</span>';
  } else if(!item.tiers.length){
    tierHtml = '<span class="prio-none">No prio on the sheet</span>';
  } else {
    const chain = item.tiers.map((t, i) => {
      const toks = t.tokens.map(tok => {
        const amb = (tok.specs && new Set(tok.specs.map(s => s.cls)).size > 1)
          ? '<span class="prio-amb" title="This token means more than one spec">?</span>' : '';
        return `<span class="prio-token">${whEsc(tok.label)}${amb}</span>`;
      }).join('<span class="prio-eq">=</span>');
      const mine = i === el.rank ? ' is-mine' : '';
      return `<span class="prio-tier${mine}"><span class="prio-rank">${i + 1}</span>${toks}</span>`;
    }).join('<span class="prio-gt">›</span>');
    const tail = item.openTail
      ? `<span class="prio-gt">›</span><span class="prio-tier${el.kind === 'open' ? ' is-mine' : ''}"><span class="prio-rank">${item.tiers.length + 1}</span>` +
        '<span class="prio-open">MS &gt; OS — anyone else</span></span>'
      : '';
    tierHtml = chain + tail;
  }

  return { can: el.can, html:
    `<div class="prio-item roll-item${el.can ? '' : ' is-empty'}">
       <div class="prio-item-name">${name}${badge}</div>
       <div class="prio-tiers">${tierHtml}</div>
       ${notes}
     </div>` };
}

/* The Character dropdown: which of the current view's characters the page is about.
   Hidden when there is only one (or none) — there is nothing to pick then. The main
   is listed first and is the default selection. Available to any member, not just
   officers, so it carries no [data-officer-only]. */
function populateCharPick(view){
  const sel = document.getElementById('charPick');
  const field = document.getElementById('charField');
  if(!sel || !field) return;
  const roster = (view && view.roster) || [];
  if(roster.length <= 1){ field.style.display = 'none'; sel.innerHTML = ''; return; }
  field.style.display = '';
  sel.innerHTML = roster.slice()
    .sort((a, b) => (b.isMain ? 1 : 0) - (a.isMain ? 1 : 0) || a.name.localeCompare(b.name))
    .map(c => {
      const spec = c.spec ? ` (${detectedSpecLabel(c)})` : '';
      return `<option value="${whEsc(String(c.characterId))}">${whEsc(c.name)}${whEsc(spec)}${c.isMain ? ' ★' : ''}</option>`;
    }).join('');
  if(view.selected) sel.value = String(view.selected.characterId);
}

function setCharPick(el){
  charPick = el.value || '';
  save();
  render();
}

function render(){
  const view = activeViewChars();
  const chars = view.chars;
  const lookup = buildLookup(equipped);
  wornByChar = buildWornByChar(equipped);

  populateViewAs();
  populateCharPick(view);
  const myHtml = renderMyPrio(lookup, view);

  let hidden = 0;
  const html = sections.map(section => {
    const items = section.items.map(it => rollItemHTML(it, lookup(it.name), chars));
    const shown = items.filter(i => !(onlyMine && !i.can));
    hidden += items.length - shown.length;
    if(!shown.length) return '';
    return `<section class="prio-boss">
              <h2>${whEsc(section.name)}</h2>
              ${shown.map(i => i.html).join('')}
            </section>`;
  }).join('');

  document.getElementById('rollsResult').innerHTML = myHtml +
    (html || '<div class="pool-empty">Nothing to show — try unticking "only what I can roll on".</div>');
  document.getElementById('rollsHiddenNote').textContent =
    (onlyMine && hidden) ? `${hidden} item${hidden===1?'':'s'} hidden — your characters can’t roll on them.` : '';
  document.getElementById('rollsHead').textContent = raidTab(picked.raid).label;
  document.getElementById('rollsAsOf').textContent = cachedAt
    ? '· as of ' + new Date(cachedAt).toLocaleTimeString([], { hour:'2-digit', minute:'2-digit' })
    : '';
  document.getElementById('rollsToolbar').style.display = 'flex';
  loadWowhead();
}

/* ───────────────────────── Officer "View as" ─────────────────────────
   The officer-only picker that points the whole page at another raider — their own
   named-prio summary and their eligibility marks. Members never see it (hidden by
   [data-officer-only]), and it exposes nothing they couldn't already read: the
   roster is member-readable and the prio is public sheet data. */
function viewAsValue(){
  return viewAs.kind === 'member' ? 'member:' + viewAs.id : 'self';
}

function populateViewAs(){
  const sel = document.getElementById('viewAs');
  if(!sel) return;
  const opts = ['<option value="self">Yourself</option>'];
  const rest = pickerRoster.slice().sort((a, b) => a.label.localeCompare(b.label));
  if(rest.length){
    opts.push('<optgroup label="Roster">');
    rest.forEach(r => opts.push(`<option value="member:${whEsc(r.id)}">${whEsc(r.label)}</option>`));
    opts.push('</optgroup>');
  }
  sel.innerHTML = opts.join('');
  sel.value = viewAsValue();
  if(sel.selectedIndex < 0){ viewAs = { kind:'self' }; sel.value = 'self'; }
}

function setViewAs(el){
  const v = el.value || 'self';
  viewAs = v.startsWith('member:') ? { kind:'member', id: v.slice(7) } : { kind:'self' };
  save();
  render();
}

/* ───────────────────────── Persistence ─────────────────────────
   The whole built list is cached so a reload comes back instantly. The gear/loot
   rows can be large; if the payload trips the storage quota we keep only the picks,
   so at least the raid and toggle stick. */
function serializeExclusions(){
  return [...exclusions.entries()].map(([k, set]) => [k, [...set]]);
}
function deserializeExclusions(pairs){
  return new Map((pairs || []).map(([k, ids]) => [k, new Set(ids)]));
}

function save(){
  const full = {
    picked, onlyMine, cachedAt,
    sections, equipped, awards, myChars, pickerRoster, viewAs, charPick,
    itemIds: [...itemIds.entries()],
    exclusions: serializeExclusions(),
    exclusionNames: [...exclusionNames.entries()]
  };
  try{ localStorage.setItem(STORE_KEY, JSON.stringify(full)); }
  catch(e){
    try{ localStorage.setItem(STORE_KEY, JSON.stringify({ picked, onlyMine, charPick })); }catch(e2){}
  }
}

// Returns true when a full cached build was restored and rendered.
function restore(){
  let saved = null;
  try{ saved = JSON.parse(localStorage.getItem(STORE_KEY) || 'null'); }catch(e){}
  if(!saved) return false;
  if(saved.picked) picked = Object.assign(picked, saved.picked);
  if(typeof saved.onlyMine === 'boolean') onlyMine = saved.onlyMine;
  if(typeof saved.charPick === 'string') charPick = saved.charPick;
  document.getElementById('onlyMine').checked = onlyMine;
  if(RAID_TABS.some(r => r.key === picked.raid)) document.getElementById('raidPick').value = picked.raid;

  if(saved.sections && saved.sections.length){
    sections = saved.sections;
    equipped = saved.equipped || [];
    awards = saved.awards || [];
    myChars = saved.myChars || [];
    pickerRoster = saved.pickerRoster || [];
    viewAs = (saved.viewAs && saved.viewAs.kind) ? saved.viewAs : { kind:'self' };
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
  picked = { raid: RAID_TABS[0].key };
  sections = []; myChars = []; pickerRoster = []; viewAs = { kind:'self' }; charPick = '';
  equipped = []; awards = [];
  exclusions = new Map(); exclusionNames = new Map(); itemIds = new Map(); cachedAt = 0;
  try{ localStorage.removeItem(STORE_KEY); }catch(e){}
  document.getElementById('raidPick').value = picked.raid;
  document.getElementById('rollsResult').innerHTML = '';
  document.getElementById('rollsHead').textContent = '';
  document.getElementById('rollsAsOf').textContent = '';
  document.getElementById('rollsHiddenNote').textContent = '';
  document.getElementById('rollsToolbar').style.display = 'none';
  setStatus('Cleared. Pick a raid to show its items.');
}

(function(){
  function ready(fn){
    if(document.readyState !== 'loading') fn();
    else document.addEventListener('DOMContentLoaded', fn);
  }
  ready(function(){
    const phases = [...new Set(RAID_TABS.map(r => r.doc))];
    document.getElementById('raidPick').innerHTML = phases.map(doc =>
      `<optgroup label="${whEsc(PHASE_LABELS[doc] || doc)}">` +
      RAID_TABS.filter(r => r.doc === doc)
               .map(r => `<option value="${r.key}">${whEsc(r.label)}</option>`).join('') +
      '</optgroup>').join('');
    const restored = restore();
    setStatus(restored
      ? 'Showing your last build — Refresh to re-read the sheet, or pick another raid.'
      : 'Pick a raid and show its items to see what you can roll on.');
  });
})();
