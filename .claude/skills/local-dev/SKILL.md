---
name: local-dev
description: Bring the wooback stack up locally — Postgres, the .NET API on 8080, and the static frontend on 5173 — so a change can be exercised in a real browser. Use when asked to run or start the app, try something locally, reproduce a bug, or verify a UI change end to end.
---

# Local development

Four things: a Postgres container, user-secrets, two servers, a health check.

## 1. Postgres

The dev connection string is fixed in
`server/WoobackVash.Api/appsettings.Development.json`
(`Host=localhost;Port=5432;Database=wooback;Username=postgres;Password=postgres`).
This container matches it exactly:

```
docker start wv-pg
```

If it doesn't exist yet:

```
docker run -d --name wv-pg -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=wooback -p 5432:5432 postgres:16
```

## 2. Secrets

Check which are set — **absolute `--project` path**, a relative one has failed here:

```powershell
$p = 'C:\Users\user\git\wooback-vash\server\WoobackVash.Api'
dotnet user-secrets list --project $p | ForEach-Object { ($_ -split ' = ')[0] }
```

That form prints key names only. Plain `dotnet user-secrets list` prints **values in
plaintext** — only do that when you need one, and never paste a secret into chat, a
file, or a commit.

| Secret | Unlocks |
|---|---|
| `Session:Secret` | Sessions. Needed for the `session-token` skill. |
| `Discord:ClientSecret` | OAuth login. |
| `Discord:BotToken` | *Import from Discord* on the roster. |
| `WarcraftLogs:ClientId` / `:ClientSecret` | Logs list, attendance import, gear snapshots. |
| `Blizzard:ClientId` / `:ClientSecret` | Guild sync, item-name resolution. |
| `RaidHelper:Token` | Raid-Helper signups (board, groups, loot prio). |

`Session:Secret` and `Discord:ClientSecret` are the minimum to boot and sign in.
Set one with `dotnet user-secrets set "Blizzard:ClientId" "…" --project $p` — note
the **colon** form locally, against the `Blizzard__ClientId` double-underscore form
used for Fly secrets.

## 3. Servers

Use `preview_start`, never Bash. `.claude/launch.json` already defines both:

- `preview_start {name: "api"}` — `dotnet run`, `http://localhost:8080`. EF migrations
  apply on startup, so the schema appears on first boot.
- `preview_start {name: "frontend"}` — `npx serve`, `http://localhost:5173`.

## 4. Check it came up

- `http://localhost:8080/readyz` → `{"db":"ok"}` (it reports the DB error detail when
  it isn't).
- `preview_logs {serverId, level: "error"}` on the api server for startup failures.

## 5. Sign in

Browse `http://localhost:5173/index.html`. `menu.js` routes `localhost` and `127.0.0.1`
to `http://localhost:8080`, and 5173 is already in the dev `AllowedOrigins`.

Real Discord login only works if `http://localhost:8080/auth/callback` is registered as
a redirect on the Discord application. When it isn't — the usual case — **use the
`session-token` skill** to mint a token and inject it. That is the normal path, and it
is also the only way to test the non-officer tier.

## Troubleshooting

- **`readyz` reports a DB error** — the container isn't up, or an old `wv-pg` is
  running with different credentials. `docker rm -f wv-pg` and recreate.
- **Docker builds fail on HTTPS / NuGet restore** — AVG intercepts TLS, so containers
  don't trust the chain. The root CA lives in `server/ca-certs/`; see its README.
- **CORS errors in the console** — the page is being served from an origin that isn't
  in `AllowedOrigins` (only 5173, 127.0.0.1:5173 and 8080 are). Use 5173, not a
  `file://` path or another port.
- **"Port 5173 is in use by another chat's dev server"** — another session already has
  the frontend up. Just use it: navigate to `http://localhost:5173/…`. Do **not** set
  `autoPort: true` in `.claude/launch.json` to dodge this; the port is fixed by
  `AllowedOrigins`, and a reassigned port fails CORS on every API call. Same for 8080,
  which the Discord redirect and `menu.js` both hardcode.
