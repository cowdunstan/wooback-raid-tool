/* ───────────────────────── WoW Forever application questions ─────────────────────────
   The one question list behind the public form (apply.html) and the officer review page
   (applications.html), so the two can never drift. The backend (/api/applications) is
   generic, like the poll's: it stores whatever question and option ids it is sent.

   Where a question also appears in the members' WoW Forever poll (legacy/forever.html,
   QUESTIONS) — class, role, race, focus, commitment — the question id and option ids match
   that poll's, so an officer can line an applicant up against the guild's own answers.
   Change one, change the other. (Race is trimmed to Alliance here: we're going Alliance.)

   Choice types (radio, checkbox, select, range) are sent as `choices: { id: [value, …] }`;
   free-text types (text, url, textarea) as `text: { id: "…" }`. */

const APPLY_SECTIONS = [
  { title:'About you', questions:[
    { id:'discord', type:'text', req:true, max:64, q:'Discord username',
      hint:'So an officer can message you.' },
    { id:'charName', type:'text', max:64, q:'Character name you plan to use',
      hint:'If you know it already.' },
    { id:'timezone', type:'select', req:true, q:'Where do you play from?', options:[
      ['na_east','NA East'], ['na_west','NA West'], ['eu','EU'], ['oce','OCE'], ['other','Other'] ] }
  ]},

  { title:'Your WoW Forever plans', blurb:"We're going Alliance.", questions:[
    { id:'class', type:'radio', req:true, q:'Which class are you planning?', options:[
      ['warrior','Warrior'], ['paladin','Paladin'], ['hunter','Hunter'], ['rogue','Rogue'],
      ['priest','Priest'], ['shaman','Shaman'], ['mage','Mage'], ['warlock','Warlock'],
      ['druid','Druid'], ['unsure','Not sure'] ] },
    { id:'role', type:'radio', req:true, q:'What role do you want to play?', options:[
      ['tank','Tank'], ['melee','Melee DPS'], ['ranged','Ranged DPS'], ['healer','Healer'],
      ['unsure','Not sure'] ] },
    { id:'offRole', type:'radio', q:'Willing to play another role if the raid needs it?', options:[
      ['yes','Yes'], ['maybe','Maybe'], ['no','No'] ] },
    { id:'race', type:'radio', q:'Which race are you leaning toward?', options:[
      ['human','Human'], ['dwarf','Dwarf'], ['nightelf','Night Elf'], ['gnome','Gnome'],
      ['skyborne','Skyborne'], ['nopref','No preference'] ] },
    { id:'focus', type:'checkbox', q:'What are you most looking forward to?', hint:'Pick any.', options:[
      ['raiding','Raiding'], ['pvp','PvP / Battlegrounds'], ['leveling','Leveling & questing'],
      ['dungeons','5-man dungeons'], ['professions','Professions & economy'],
      ['social','Just hanging out'] ] },
    { id:'commitment', type:'radio', req:true, q:'How much do you expect to play?', options:[
      ['hardcore','Hardcore (most days)'], ['regular','Regular (a few nights)'],
      ['casual','Casual (weekends)'], ['whenever','Whenever I can'] ] },
    { id:'launch', type:'radio', q:'What are your launch plans?', options:[
      ['dayone','Leveling hard from day one'], ['steady','Steady pace'],
      ['later','Coming along later'] ] }
  ]},

  { title:'Availability', questions:[
    { id:'raidNights', type:'checkbox', req:true, q:'Which evenings can you usually raid?',
      hint:'Our main raid starts at 9:30pm EST. Tick every evening you could usually make it.', options:[
      ['mon','Mon'], ['tue','Tue'], ['wed','Wed'], ['thu','Thu'], ['fri','Fri'],
      ['sat','Sat'], ['sun','Sun'] ] },
    { id:'attendance', type:'radio', q:'Realistically, how many raids would you make?', options:[
      ['nearly_all','Nearly every raid'], ['most','Most of them'], ['half','About half'],
      ['varies','It varies'] ] },
    { id:'voice', type:'radio', q:'Voice chat in raids', options:[
      ['talk','Happy to talk'], ['listen','Listen only'], ['no','Rather not'] ] }
  ]},

  { title:'Raid experience', blurb:"If you're here to raid. Skip this part if you're not.", questions:[
    { id:'experience', type:'checkbox', q:'Where have you raided before?', hint:'Pick any.', options:[
      ['classic','Vanilla / Classic Era'], ['tbc','TBC'], ['wrath','Wrath'],
      ['retail_heroic','Cata+ / retail heroic'], ['retail_mythic','Retail mythic'],
      ['none','None yet'] ] },
    { id:'logs', type:'url', q:'Warcraft Logs link', hint:'Any character, any version.' },
    { id:'sweaty', type:'range', q:'How sweaty do you expect to be?', min:1, max:5, labels:{
      1:'Here for the vibes', 2:'Relaxed', 3:'Want the clear, no stress',
      4:'Pushing for good parses', 5:'Parsing every pull' } }
  ]},

  { title:'The vibe', questions:[
    { id:'aboutYou', type:'textarea', req:true, q:'Tell us a bit about yourself',
      hint:'What you enjoy in WoW, and what you want from a guild.' },
    { id:'anythingElse', type:'textarea', q:'Anything else?' }
  ]}
];

const APPLY_TEXT_TYPES = ['text', 'url', 'textarea'];
const APPLY_QUESTIONS = APPLY_SECTIONS.reduce(function(all, s){ return all.concat(s.questions); }, []);
