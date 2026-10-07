/* ───────────────────────── WoW Forever launch poll questions ─────────────────────────
   The one question list behind the members' launch poll (launch.html) and the officer review
   of it (launch-responses.html), so the two can never drift. The backend (/api/launch-poll) is
   generic, like the application form's: it stores whatever question and option ids it is sent.

   `class` uses the same option ids as the application form and the WoW Forever poll, so the
   three line up. The roles here are deliberately coarser than those (tank / healer / DPS, no
   melee-vs-ranged split): that is all a raid group needs to know at launch.

   Choice types (radio, checkbox) are sent as `choices: { id: [value, …] }`; free-text types
   (text, textarea) as `text: { id: "…" }`. A question with `details` renders each option
   full-width with that line of explanation under it; `max` on a checkbox caps the picks. */

const LAUNCH_CLASSES = [
  ['warrior','Warrior'], ['paladin','Paladin'], ['hunter','Hunter'], ['rogue','Rogue'],
  ['priest','Priest'], ['shaman','Shaman'], ['mage','Mage'], ['warlock','Warlock'],
  ['druid','Druid']
];
const LAUNCH_ROLES = [ ['tank','Tank'], ['healer','Healer'], ['dps','DPS'] ];

const LAUNCH_SECTIONS = [
  { title:'Your main', questions:[
    { id:'class', type:'radio', req:true, q:'Main class', options: LAUNCH_CLASSES },
    { id:'spec', type:'radio', req:true, q:'Main spec', options: LAUNCH_ROLES },
    { id:'offspec', type:'radio', req:true, q:'Off spec', hint:'What you could swap to if the raid needs it.',
      options: LAUNCH_ROLES.concat([ ['none','No off spec'] ]) },
    { id:'charName', type:'text', max:64, q:'Character name', hint:'If you know it already.' }
  ]},

  { title:'Alts and professions', questions:[
    { id:'alts', type:'text', max:200, q:'Alts?', hint:'Any you plan to level, with class and role — e.g. "Priest healer". Leave blank for none.' },
    { id:'professions', type:'checkbox', q:'Professions on your main', hint:'Pick up to two, or leave blank if you have not decided.', max:2, options:[
      ['alchemy','Alchemy'], ['blacksmithing','Blacksmithing'], ['enchanting','Enchanting'],
      ['engineering','Engineering'], ['herbalism','Herbalism'], ['leatherworking','Leatherworking'],
      ['mining','Mining'], ['skinning','Skinning'], ['tailoring','Tailoring'] ] }
  ]},

  { title:'Raiding', questions:[
    { id:'sixty', type:'radio', req:true, q:'Do you think you will be 60 when raids open on 9 December?', options:[
      ['yes','Yes'], ['no','No'], ['unsure','Unsure'] ] },
    { id:'group', type:'radio', req:true, q:'Which raid group do you want to be in?', options:[
      ['sweaty','Sweaty parsing group'], ['semi','Semi-hardcore group'] ],
      details:{
        sweaty:'About 5 hours a day, Saturday and Sunday mornings. Minimum performance requirements and a bench policy.',
        semi:'If you sign up for Saturday or Sunday, show up — with consumes, and knowing the fights.'
      } },
    { id:'anythingElse', type:'textarea', q:'Anything else?' }
  ]}
];

const LAUNCH_TEXT_TYPES = ['text', 'textarea'];
const LAUNCH_QUESTIONS = LAUNCH_SECTIONS.reduce(function(all, s){ return all.concat(s.questions); }, []);
