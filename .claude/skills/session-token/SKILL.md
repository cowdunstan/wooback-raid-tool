---
name: session-token
description: Mint a signed wooback session token locally and inject it into the browser, so pages can be exercised without going through Discord OAuth. Use when you need to sign in locally, or to check how a page behaves for an officer versus a non-officer (read-only) session.
---

# Local session tokens

Discord OAuth usually isn't wired up for localhost, and it can only ever give you
*your own* tier. Minting a token gets you signed in either way, as either tier.

Requires the API running locally (`local-dev` skill) and `Session:Secret` in
user-secrets.

## Format

From `server/WoobackVash.Api/Auth/SessionTokenService.cs`:

```
base64url(payloadJSON) + "." + base64url(HMAC-SHA256(secret, base64url(payloadJSON)))
```

The HMAC covers the **already-encoded body string**, not the raw JSON. Payload is
`{ uid, name, officer, exp, iat }` (the `SessionPayload` record) — all Unix seconds.
`menu.js` decodes it client-side to read `name`, `officer` and `exp`.

## Mint

Set `$officer` to `$true` or `$false`. The `--project` path must be **absolute**.

```powershell
$p = 'C:\Users\user\git\wooback-vash\server\WoobackVash.Api'
$officer = $true

$sec = (dotnet user-secrets list --project $p | Select-String '^Session:Secret = ').ToString().Substring('Session:Secret = '.Length)
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$payload = "{""uid"":""1"",""name"":""Local Tester"",""officer"":$($officer.ToString().ToLower()),""exp"":$($now + 21600),""iat"":$now}"

function B64Url([byte[]]$b){ [Convert]::ToBase64String($b).Replace('+','-').Replace('/','_').TrimEnd('=') }
$body = B64Url ([Text.Encoding]::UTF8.GetBytes($payload))
$hmac = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($sec))
$sig  = B64Url ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($body)))
"$body.$sig"
```

It prints the token and nothing else — keep it that way, don't echo `$sec`.

For a token tied to a real member, set `uid` to that member's Discord user id (the
`discordUserId` on the roster); pages that resolve "your own main" key off it.

## Inject

`menu.js` reads `localStorage['vashj_session']`. In the browser pane:

```js
localStorage.setItem('vashj_session', '<token>'); location.reload();
```

To sign out, `localStorage.removeItem('vashj_session')` — or call the page's
`logout()`.

## Testing both tiers

Mint one token with `$officer = $true` and one with `$false`, then swap between them
and reload. Officer-gated nav links and buttons are marked `data-officer-only` and
hidden by `menu.js`; pages may add their own read-only handling on top.

Count what actually renders with `getClientRects()`, not computed `display`:

```js
[...document.querySelectorAll('button')].filter(e => e.getClientRects().length).length
```

Computed `display` has reported hidden controls as visible here and produced a wrong
"verified" claim. Check the API side too — a 403 from the endpoint is the real gate;
hiding a control is cosmetic.

## Care

The token is a working credential against whatever backend the secret belongs to.
Don't commit it, don't print the secret, and only ever mint against the **local**
`Session:Secret` — never the production one from Fly.
