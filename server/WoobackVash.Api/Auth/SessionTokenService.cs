using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WoobackVash.Api.Config;

namespace WoobackVash.Api.Auth;

/// <summary>
/// The signed-in session, encoded into the token the frontend stores.
/// Property names/shape match the old Worker payload exactly:
/// <c>{ uid, name, officer, exp }</c>. The pages decode this client-side and read
/// <c>exp</c> / <c>officer</c>, so the shape must not drift.
/// </summary>
public record SessionPayload
{
    [JsonPropertyName("uid")] public string Uid { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("officer")] public bool Officer { get; init; }
    [JsonPropertyName("exp")] public long Exp { get; init; }
}

/// <summary>
/// Mints and verifies session tokens in the Worker's format:
/// <c>base64url(payloadJSON) + "." + base64url(HMAC-SHA256(base64url(payloadJSON)))</c>.
/// The HMAC is computed over the already-encoded body segment (not the raw JSON),
/// matching signSession() / verifySession() in raidhelper-proxy.worker.js.
/// </summary>
public class SessionTokenService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Compact, like JS JSON.stringify. (Formatting is irrelevant to verification
        // since we HMAC the encoded body string, but keep it tidy for the frontend.)
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly byte[] _secret;
    private readonly int _ttlSeconds;

    public SessionTokenService(IOptions<SessionSigningOptions> options)
    {
        _secret = Encoding.UTF8.GetBytes(options.Value.Secret ?? "");
        _ttlSeconds = options.Value.TtlSeconds;
    }

    /// <summary>Seconds since the Unix epoch, now.</summary>
    public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Builds a session for a user, stamping exp = now + TTL.</summary>
    public string Sign(string uid, string name, bool officer)
        => Sign(new SessionPayload
        {
            Uid = uid,
            Name = name,
            Officer = officer,
            Exp = NowUnix() + _ttlSeconds
        });

    public string Sign(SessionPayload payload)
    {
        var body = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOpts));
        var sig = Base64UrlEncode(Hmac(Encoding.UTF8.GetBytes(body)));
        return body + "." + sig;
    }

    /// <summary>
    /// Verifies signature and expiry; returns the payload or null. Used by the
    /// gated proxy endpoints (Phase 2) — safe to have now.
    /// </summary>
    public SessionPayload? Verify(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return null;

        var body = token[..dot];
        byte[] presentedSig;
        try { presentedSig = Base64UrlDecode(token[(dot + 1)..]); }
        catch { return null; }

        var expectedSig = Hmac(Encoding.UTF8.GetBytes(body));
        if (!CryptographicOperations.FixedTimeEquals(presentedSig, expectedSig)) return null;

        SessionPayload? payload;
        try { payload = JsonSerializer.Deserialize<SessionPayload>(Base64UrlDecode(body)); }
        catch { return null; }
        if (payload is null || payload.Exp < NowUnix()) return null;
        return payload;
    }

    private byte[] Hmac(byte[] message)
    {
        using var h = new HMACSHA256(_secret);
        return h.ComputeHash(message);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
