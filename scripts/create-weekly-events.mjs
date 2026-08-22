#!/usr/bin/env node
// Post the coming weekend's two Raid-Helper events (Saturday + Sunday) by
// calling the Raid-Helper API directly. Driven by
// .github/workflows/weekly-raid-events.yml on a weekly cron, and runnable by
// hand (workflow_dispatch) to test.
//
// It talks to the same Raid-Helper server the backend proxies, with the same
// kind of token: the server API key from the `/apikey` Discord command. That
// key both reads signups (what the app already does) and creates events, so
// there is nothing new to authorise — just expose it to the Action as a secret.
//
// The raid start is sent as an absolute unix timestamp (Raid-Helper's `date`
// field accepts one, and then no `time` is needed). That pins the event to a
// real instant, so the time is exactly what we ask for in UTC regardless of the
// Discord server's own Raid-Helper timezone — and it sidesteps the DD-MM vs
// MM-DD date-format ambiguity of the string form entirely.
//
// Config is entirely environment-driven so no private ids live in the repo:
//   RAIDHELPER_API_KEY   (secret, required)  /apikey server token
//   RH_SERVER_ID         guild id            (defaults to the wooback server)
//   RH_SAT_CHANNEL_ID    (required)          channel the Saturday event posts in
//   RH_SUN_CHANNEL_ID    (required)          channel the Sunday event posts in
//   RH_LEADER_ID         (required)          Discord user id shown as leader
//   RH_TEMPLATE_ID       (optional)          Raid-Helper template id
//   RH_SAT_TIME / RH_SUN_TIME   "HH:MM" UTC  start times (default 13:30)
//   RH_SAT_TITLE / RH_SUN_TITLE             event titles
//   RH_DESCRIPTION       (optional)          shared description (defaults to title)

const API = 'https://raid-helper.dev/api';

function need(name) {
  const v = process.env[name];
  if (!v) {
    console.error(`Missing required env ${name}`);
    process.exit(1);
  }
  return v;
}

// "HH:MM" → [hours, minutes], rejecting anything out of range.
function parseHM(name, fallback) {
  const raw = (process.env[name] || fallback).trim();
  const m = /^(\d{1,2}):(\d{2})$/.exec(raw);
  const h = m && Number(m[1]);
  const min = m && Number(m[2]);
  if (!m || h > 23 || min > 59) {
    console.error(`${name} must be "HH:MM" (24h UTC), got "${raw}"`);
    process.exit(1);
  }
  return [h, min];
}

// Unix seconds for HH:MM UTC on the day `addDays` from today. Whole-day shifts
// read back cleanly through getUTC*, so there is no DST edge to worry about.
function utcTimestamp(addDays, [h, min]) {
  const base = new Date(Date.now() + addDays * 86_400_000);
  return Math.floor(Date.UTC(base.getUTCFullYear(), base.getUTCMonth(), base.getUTCDate(), h, min, 0) / 1000);
}

async function createEvent(serverId, channelId, apiKey, body, label) {
  let res;
  try {
    res = await fetch(`${API}/v4/servers/${serverId}/channels/${channelId}/event`, {
      method: 'POST',
      headers: { Authorization: apiKey, 'Content-Type': 'application/json; charset=utf-8' },
      body: JSON.stringify(body),
    });
  } catch (err) {
    console.error(`${label}: request failed — ${err.message}`);
    process.exit(1);
  }
  const text = await res.text();
  if (!res.ok) {
    console.error(`${label}: Raid-Helper returned HTTP ${res.status} — ${text}`);
    process.exit(1);
  }
  console.log(`${label}: created (${new Date(body.date * 1000).toISOString()}) — ${text}`);
}

const apiKey = need('RAIDHELPER_API_KEY');
const serverId = process.env.RH_SERVER_ID || '1462481995119722649';
const leaderId = need('RH_LEADER_ID');
const templateId = process.env.RH_TEMPLATE_ID || '';

// Target next week's weekend, not this one: the run posts the Saturday and
// Sunday of the calendar week *after* it fires. The cron runs Saturday morning,
// so this gives a full week of signup lead time rather than creating that same
// day's raid. Adding 7 to the coming Saturday keeps that true from any run day.
const today = new Date().getUTCDay(); // 0=Sun … 6=Sat
const satOffset = ((6 - today + 7) % 7) + 7;
const sunOffset = satOffset + 1;

const days = [
  {
    label: 'Saturday',
    channelId: need('RH_SAT_CHANNEL_ID'),
    date: String(utcTimestamp(satOffset, parseHM('RH_SAT_TIME', '13:30'))),
    title: process.env.RH_SAT_TITLE || 'Saturday Raid',
  },
  {
    label: 'Sunday',
    channelId: need('RH_SUN_CHANNEL_ID'),
    date: String(utcTimestamp(sunOffset, parseHM('RH_SUN_TIME', '13:30'))),
    title: process.env.RH_SUN_TITLE || 'Sunday Raid',
  },
];

for (const d of days) {
  const body = {
    leaderId,
    channelId: d.channelId,
    title: d.title,
    description: process.env.RH_DESCRIPTION || d.title,
    date: d.date,
  };
  if (templateId) body.templateId = templateId;
  // Sequential on purpose: two ordered log lines, and one clear failure point.
  // eslint-disable-next-line no-await-in-loop
  await createEvent(serverId, d.channelId, apiKey, body, d.label);
}
