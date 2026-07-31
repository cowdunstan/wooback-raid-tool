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


let picked = { eventId:'', eventTitle:'', raid:RAID_TABS[0].key };
let candidates = [];      // everyone signed up, as { name, cls, spec, ... }
let sections = [];        // the parsed sheet: [{ name, items:[…] }]
let hideEmpty = true;
let equipped = [];        // GET /api/items/list — who is wearing what
let awards = [];          // GET /api/loot/history — every award, for WON and the tally
let members = [];         // GET /api/members — kept so the personal section can find "me"
let myChars = [];         // candidate-shaped objects for the signed-in user's own characters
let pickerRoster = [];    // officer View-as options: [{ id, label, chars }] for every member
let viewAs = { kind:'self' }; // whose prio the personal section shows: self | cand | member
let exclusions = new Map(); // itemNameLower → Set(characterId) — officers' per-item roll mutes
let exclusionNames = new Map(); // characterId → name, so the muted line can name a character not signed up
let itemIds = new Map();   // itemNameLower → wowhead id, resolved so every item tooltips (not just ones in our gear/loot data)
let cachedAt = 0;         // when the shown build was fetched (ms) — for the "as of" note

function setStatus(msg, isErr){
  const el = document.getElementById('prioStatus');
  el.textContent = msg || '';
  el.style.color = isErr ? 'var(--amber)' : 'var(--text-dim)';
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

// Which classes can hold a role a raider signs up under — the tank and healer
// buttons on a Raid-Helper signup name the *role*, not the class (a tank row comes
// through as className "Tank"), so this is how a "Tanks" signup finds the paladin,
// warrior or druid it belongs to. Only the narrow roles are listed; melee/ranged are
// any class, so they're left to the class header the signup already carries.
const ROLE_CLASSES = {
  tank:   ['warrior', 'paladin', 'druid'],
  healer: ['priest', 'paladin', 'druid', 'shaman']
};

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
    // Only a class *header* names a class; a status row (Tentative/Bench) carries
    // just a spec, and an ambiguous spec like "protection" is a guess RH already
    // resolved to warrior. So key off the header, and let the roster and spec
    // settle the rest — otherwise a tentative prot paladin lands on warrior prio.
    const named = (s.classNamed || '').toLowerCase().trim();
    const sspec = (s.spec || '').toLowerCase().replace(/[^a-z]/g, '');

    // First the character the roster knows by the signed-up name — but not when
    // that character's class flatly contradicts the class they signed up under. A
    // raider bringing their prot paladin "Teroz" but owning a warrior alt that
    // happens to share the signup name "Tero" must bind to the paladin, not the
    // warrior the name alone would catch. Only a *known* class overrides the name;
    // a character with no logged class still takes it and inherits `named` below.
    let known = chars.find(ch => String(ch.name || '').toLowerCase() === s.name.toLowerCase()) || null;
    if(known){
      const kc = String(known.cls || '').toLowerCase().trim();
      if(named){
        // A class header that flatly disagrees: the name match is a coincidence.
        if(kc && kc !== named) known = null;
      } else if(ROLE_CLASSES[s.pickedRole]){
        // A tank/healer row names the *role*, not the class (RH sends className
        // "Tank"), so `named` is empty and the guard above can't fire. Drop a
        // same-named alt that can't be who they signed up as — a classless "Tero"
        // or a rogue "Tero" when they brought their prot-paladin "Teroz" — so the
        // spec/role match below resolves to the real character. Same idea as the
        // groups tally trusting the signed-up role over a spec/class guess.
        if(!kc || !ROLE_CLASSES[s.pickedRole].includes(kc)) known = null;
      }
    }

    // Failing that, the member's character they'd actually loot on: the one whose
    // class matches the named class, or — when the row named no class, only a spec
    // — the one whose spec matches. Main wins a tie. This is what keeps a tentative
    // "Protection" paladin off the warrior's prio and on the paladin's. Only
    // reached when the signup name isn't itself a roster character.
    let matchedByClass = false;
    if(!known && member){
      const chCls  = ch => String(ch.cls || '').toLowerCase().trim();
      const chSpec = ch => String(ch.spec || '').toLowerCase().replace(/[^a-z]/g, '');
      let pool = named ? chars.filter(ch => chCls(ch) === named)
               : sspec ? chars.filter(ch => chSpec(ch) === sspec)
               : [];
      // With no header and no spec match, a spec that only one class can hold
      // (fury → warrior) may still pick that class's character. An ambiguous one
      // (protection/holy/restoration) must not — that guess is this very bug.
      if(!pool.length && !named && sspec && !RH.AMBIGUOUS_SPECS.has(sspec)){
        const inferred = RH.SPEC_TO_CLASS[sspec];
        if(inferred) pool = chars.filter(ch => chCls(ch) === inferred);
      }
      // Still nothing, but they signed up for a tank/healer role: take the character
      // whose class can serve it (a "Tanks" row → their warrior/paladin/druid), main
      // first. Catches a prot paladin whose spec the roster never recorded, where the
      // ambiguous "protection" above is deliberately not guessed.
      if(!pool.length && ROLE_CLASSES[s.pickedRole]){
        pool = chars.filter(ch => ROLE_CLASSES[s.pickedRole].includes(chCls(ch)));
      }
      known = pool.find(ch => ch.isMain) || pool[0] || null;
      matchedByClass = !!known;
    }

    // The character resolved to is who holds the prio, so it names the candidate —
    // that is what the loot history and gear tables key on. With nothing resolved,
    // the signup's own name stands.
    const name = known ? known.name : s.name;
    // The named class wins; then the resolved character's class; an ambiguous spec
    // is only guessed (SPEC_TO_CLASS, below) when the roster knows this raider not
    // at all. This is the fix's crux: a spec-only signup no longer beats the roster.
    const cls = (named || (known && known.cls) || '').toLowerCase().trim();
    let spec = (sspec || (known && known.spec) || '').toLowerCase().replace(/[^a-z]/g, '');

    // Last-ditch class guess from the spec, for a raider the roster couldn't place.
    // It must honour the same rule as above: an ambiguous spec (protection/holy/
    // restoration) resolves to more than one class, so guessing it — protection to
    // warrior — is exactly how a prot paladin was still landing on warrior prio.
    // Leave `cls` blank there; the spec still carries the role (isTank sees it).
    const guessed = (!RH.AMBIGUOUS_SPECS.has(spec) && RH.SPEC_TO_CLASS[spec]) || '';

    const c = {
      id: s.id,
      name,
      cls: RH.CLASS_COLORS[cls] ? cls : guessed,
      spec,
      status: s.status || 'active',
      characterId: known ? known.id : null,
      member: member ? (member.nickname || member.displayName || member.discordUsername || '') : '',
      unlinked: !member,
      recent: 0,
      won: new Set(),
      has: new Set()
    };
    // The role they signed up under wins — it settles a feral bear tank from a cat,
    // and a shadow priest from a healer, the way the groups tally now does. Fall back
    // to the spec/class classifiers when the signup carried no role.
    c.role = ({ tank:'tank', healer:'healer', melee:'dps', ranged:'dps' })[s.pickedRole]
          || (RH.isTank(c) ? 'tank' : RH.isHealer(c) ? 'healer' : 'dps');
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


/* The signed-in user's own characters that are actually signed up tonight. Found by
   the Discord id the session carries, then narrowed to the roster characters this
   raid's signup resolved to — the personal "what can I roll on" section is about the
   character you brought, not every alt you own. Needs `candidates` already built (see
   buildCandidates). An officer can point that section at someone else with the
   View-as picker (see activeViewChars), but this stays the default. */
function buildMyChars(memberList){
  const pl = (typeof sessionPayload === 'function') ? sessionPayload() : null;
  const uid = pl && pl.uid ? String(pl.uid) : '';
  if(!uid) return [];
  const mine = buildCharsFor((memberList || []).find(m => String(m.discordUserId || '') === uid) || null);
  const signedUp = new Set(candidates.filter(c => c.characterId).map(c => String(c.characterId)));
  return mine.filter(c => signedUp.has(String(c.characterId)));
}

/* The roster as View-as options: every member's label and their characters, shaped
   for the personal section. Officers only — it drives the picker's "Roster" group and
   is what lets view-as survive a reload, since the full members array is never cached. */
function buildPickerRoster(memberList){
  return (memberList || []).map(m => ({
    id: String(m.discordUserId || ''),
    label: m.nickname || m.displayName || m.discordUsername || m.name || 'member',
    chars: buildCharsFor(m)
  })).filter(r => r.chars.length);
}

/* Whose characters the personal section is currently showing, and a label for the
   heading. `self` is the signed-in user; an officer can switch to a raider signed up
   tonight (`cand`) or any member (`member`). */
function activeViewChars(){
  // Only officers get someone else's view. A member can't reach the picker (it is
  // hidden), but a stale pick could still be in their cache from a shared browser —
  // fall back to their own prio rather than showing another raider's.
  if(isOfficer()){
    if(viewAs.kind === 'cand'){
      const c = candidates.find(x => String(x.id) === String(viewAs.id));
      if(c) return { chars:[c], label:c.name, self:false };
    } else if(viewAs.kind === 'member'){
      const r = pickerRoster.find(x => x.id === String(viewAs.id));
      if(r) return { chars:r.chars, label:r.label, self:false };
    }
  }
  return { chars: myChars, label:'you', self:true };
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
  pickerRoster = buildPickerRoster(members);

  const signups = RH.mapSignups(ev.signUps || ev.signups || [], null);
  if(!signups.length){ setStatus('That event has no usable signups.', true); return; }

  const rosterNotes = buildCandidates(signups, members);
  // After buildCandidates: the personal section shows only the character you brought.
  myChars = buildMyChars(members);
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

  populateViewAs();
  const myHtml = renderMyPrio(lookup, activeViewChars());

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

// The given characters that hold named prio on one item, each with the tier they sit
// in. `chars` is whoever the section is currently showing (self, or an officer's pick).
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
    // Everything the sheet ranks above this character on this item — the tokens of
    // every tier ahead of theirs, deduped and in order. It is read off the sheet, so
    // it is the classes and specs with priority, not the raiders who signed up as
    // them. `cls` is the token's class when it names exactly one, so it can be
    // coloured; blank for an ambiguous token ("Resto") or a named-prio one ("Xat").
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

function renderMyPrio(lookup, view){
  const chars = (view && view.chars) || [];
  if(!chars.length) return '';
  const self = !!(view && view.self);
  // Heading and empty-state wording shift when an officer is viewing someone else.
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
  if(!perChar.size){
    return `<section class="prio-boss my-prio">
              <h2>What ${who} prio on</h2>
              <div class="prio-note">Nothing on the ${whEsc(raidLabel)} sheet gives ${whose} characters named prio — anything ${youd}’d roll on here is open MS &gt; OS.</div>
            </section>`;
  }

  const clsOf = new Map(chars.map(c => [c.name, c.cls]));
  const charByName = new Map(chars.map(c => [c.name, c]));
  const blocks = [...perChar.entries()].map(([name, arr]) => {
    const color = RH.CLASS_COLORS[clsOf.get(name)] || '#7fa89c';
    // The items this character holds prio on, bucketed by which tier they sit in —
    // "prio 1" for the top tier, "prio 2" for one behind it, and so on. A tier is a
    // group here rather than a per-item pill, so the ones you're first in line for
    // read together.
    const byTier = new Map();
    arr.forEach(it => {
      let g = byTier.get(it.rank);
      if(!g){ g = []; byTier.set(it.rank, g); }
      g.push(it);
    });
    const groups = [...byTier.entries()].sort((a, b) => a[0] - b[0]).map(([tier, items]) => {
      items.sort((a, b) => a.name.localeCompare(b.name));
      // Prio 1 is the tier you're first in line for — nothing outranks you, so the
      // items just read together inline.
      if(tier === 1){
        const links = items.map(it =>
          `<span class="my-prio-item">${itemLink(it.id, it.name)}</span>`).join('');
        return `<div class="my-prio-group">
                  <span class="my-prio-tier">prio ${tier}</span>
                  <div class="my-prio-items">${links}</div>
                </div>`;
      }
      // Prio 2+: each item on its own line, with the classes and specs that hold
      // priority over this character on it named underneath (from myPrioForItem's
      // `ahead`), coloured by class where the token names just one.
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
              <span class="my-prio-chip"><a class="prio-name" href="character.html?name=${encodeURIComponent(name)}" style="--class-color:${color}">${whEsc(name)}</a>${RH.bisGuidesHTML(charByName.get(name))}</span>
              <div class="my-prio-groups">${groups}</div>
            </div>`;
  }).join('');

  return `<section class="prio-boss my-prio">
            <h2>What ${who} prio on <span class="my-prio-sub">— ${whEsc(raidLabel)}, ${self ? 'your' : 'their'} named prio only (no MS &gt; OS)</span></h2>
            ${blocks}
          </section>`;
}

/* ───────────────────────── Officer "View as" ─────────────────────────
   The officer-only picker that points the personal section at someone else. Two
   groups: the characters signed up tonight (from `candidates`), and the rest of the
   roster (members with no signed-up character, from `pickerRoster`). Rebuilt on every
   render so it tracks the current build and re-selects the active pick. Purely
   client-side — the roster is already member-readable and the prio is public sheet
   data, so this exposes nothing; the picker just carries `data-officer-only` so
   menu.js hides it for members. */
function viewAsValue(){
  if(viewAs.kind === 'cand')   return 'cand:'   + viewAs.id;
  if(viewAs.kind === 'member') return 'member:' + viewAs.id;
  return 'self';
}

function populateViewAs(){
  const sel = document.getElementById('viewAs');
  if(!sel) return;

  const opts = ['<option value="self">Yourself</option>'];

  if(candidates.length){
    opts.push('<optgroup label="In tonight’s raid">');
    candidates.slice().sort((a, b) => a.name.localeCompare(b.name)).forEach(c => {
      const cls = c.cls ? ` (${c.cls})` : '';
      opts.push(`<option value="cand:${whEsc(String(c.id))}">${whEsc(c.name)}${whEsc(cls)}</option>`);
    });
    opts.push('</optgroup>');
  }

  // Anyone already in the raid group is dropped here so a raider isn't listed twice.
  const signed = new Set(candidates.map(c => c.name.toLowerCase()));
  const rest = pickerRoster
    .filter(r => r.id && !r.chars.some(ch => signed.has(ch.name.toLowerCase())))
    .sort((a, b) => a.label.localeCompare(b.label));
  if(rest.length){
    opts.push('<optgroup label="Roster">');
    rest.forEach(r => opts.push(`<option value="member:${whEsc(r.id)}">${whEsc(r.label)}</option>`));
    opts.push('</optgroup>');
  }

  sel.innerHTML = opts.join('');
  // Re-select the active pick; fall back to self if it's no longer offered (e.g. the
  // picked raider dropped off a rebuilt signup).
  sel.value = viewAsValue();
  if(sel.selectedIndex < 0){ viewAs = { kind:'self' }; sel.value = 'self'; }
}

function setViewAs(el){
  const v = el.value || 'self';
  if(v.startsWith('cand:'))        viewAs = { kind:'cand',   id: v.slice(5) };
  else if(v.startsWith('member:')) viewAs = { kind:'member', id: v.slice(7) };
  else                             viewAs = { kind:'self' };
  save();
  render();
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

// Officer-only: refresh the gear snapshot for every signed-up character at once, so
// the HAS pills read off current gear rather than the last night they raided with us.
// The server tries Blizzard's live equipment first and falls back to Warcraft Logs —
// same path as the character sheet's per-character "Refresh gear" — so "Blizzard" here
// means Blizzard-first, not Blizzard-only. Rebuilds the list afterwards so the fresh
// snapshots show through.
async function refreshAllGear(){
  if(!picked.eventId){ setStatus('Load the events and build a list first.', true); return; }
  const ids = [...new Set(candidates.filter(c => c.characterId).map(c => String(c.characterId)))];
  if(!ids.length){ setStatus('No signed-up characters are linked to the roster yet.', true); return; }

  const btn = document.getElementById('prioRefreshGear');
  if(btn) btn.disabled = true;
  setStatus(`Updating gear for ${ids.length} character${ids.length === 1 ? '' : 's'}…`);
  try {
    const res = await apiSend('POST', '/api/characters/sheet/refresh-bulk', { ids }).then(r => r.json());
    await build(picked.eventId);   // re-render with the new snapshots; sets its own status
    const rows = (res && res.results) || [];
    const blizz = rows.filter(r => r.ok && r.source === 'blizzard').length;
    const wcl   = rows.filter(r => r.ok && r.source === 'wcl').length;
    const failed = (res ? res.total - res.updated : 0);
    const parts = [`Updated ${res ? res.updated : 0} of ${res ? res.total : ids.length}`];
    if(blizz) parts.push(`${blizz} from Blizzard`);
    if(wcl)   parts.push(`${wcl} from Warcraft Logs`);
    if(failed) parts.push(`${failed} failed`);
    setStatus(parts.join(' · ') + '.', !!failed);
  } catch(err){
    reportError(err, 'The bulk gear refresh is unavailable.');
  } finally {
    if(btn) btn.disabled = false;
  }
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
    pickerRoster, viewAs,
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
  picked = { eventId:'', eventTitle:'', raid:RAID_TABS[0].key };
  candidates = []; sections = []; unknownTokens = [];
  myChars = []; pickerRoster = []; viewAs = { kind:'self' };
  exclusions = new Map(); exclusionNames = new Map(); itemIds = new Map(); cachedAt = 0;
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
