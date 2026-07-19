# wooback-vash

A Lady Vashj (Phase 2) raid-assignment board, gated behind **officer** access on
the wooback Discord server.

- **`index.html`** — public landing page. "Sign in with Discord" only.
- **`board.html`** — the assignment board (was `index.html`). Only reachable with a
  valid officer session; `app.js` + `styles.css` power it.
- **`raidhelper-proxy.worker.js`** — Cloudflare Worker doing two jobs:
  1. Discord OAuth (`/auth/login`, `/auth/callback`) — verifies the signed-in user
     holds an officer role in the guild, then issues a short-lived HMAC-signed
     session token.
  2. Raid-Helper CORS proxy — every proxied call now requires that session token,
     so roster data is never returned to non-officers.

## How the gate works

1. Landing page sends the user to `<worker>/auth/login`.
2. The Worker redirects to Discord (`identify guilds.members.read` scope — no bot
   needed), then Discord calls back to `<worker>/auth/callback`.
3. The Worker reads the user's roles in the guild via
   `/users/@me/guilds/{guild}/member`. If they hold one of `OFFICER_ROLE_IDS`, it
   mints a signed session and redirects to `board.html#session=…`; otherwise it
   redirects back to the landing page with a "not an officer" message.
4. `board.html` stores the session and attaches it to every Raid-Helper request.
   The Worker rejects any Raid-Helper call without a valid session (`401`).

The client-side check in `board.html` only decides whether to *show* the board;
the real enforcement is the Worker refusing data without a valid signature.

## One-time setup

### 1. Create the Discord application
- https://discord.com/developers/applications → **New Application**.
- **General Information** → copy the **Application ID** → put it in
  `raidhelper-proxy.worker.js` as `DISCORD_CLIENT_ID`.
- **OAuth2** → **Redirects** → add exactly:
  `https://wooback-vash.cowdunstan.workers.dev/auth/callback`
  (must match `WORKER_BASE` + `/auth/callback`).
- **OAuth2** → copy the **Client Secret** (used as a Worker secret below).

### 2. Verify the config constants in `raidhelper-proxy.worker.js`
- `DISCORD_CLIENT_ID` — from step 1.
- `GUILD_ID` — `1462481995119722649` (same as the Raid-Helper server ID).
- `OFFICER_ROLE_IDS` — the three officer role IDs (already filled in).
- `WORKER_BASE` — this Worker's public URL.
- `APP_BASE` — where GitHub Pages serves the site (currently
  `https://cowdunstan.github.io/wooback-vash`). **Confirm this matches your Pages
  URL** — a project page lives under `/<repo>/`.

### 3. Set the Worker secrets (never commit these)
```
npx wrangler secret put RH_TOKEN               # Raid-Helper API token (existing)
npx wrangler secret put DISCORD_CLIENT_SECRET  # from the Discord app
npx wrangler secret put SESSION_SECRET         # any long random string
```
(Or Dashboard → the Worker → Settings → Variables and Secrets → Add → Secret.)

### 4. Deploy
```
npx wrangler deploy
```

## Local note
`ALLOWED_ORIGIN` in the Worker locks the Raid-Helper proxy to the GitHub Pages
origin, so the board's API calls only work from the deployed site (not from a
`localhost` preview).
