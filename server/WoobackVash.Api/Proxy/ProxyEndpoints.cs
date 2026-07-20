using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Config;
using WoobackVash.Api.Services;

namespace WoobackVash.Api.Proxy;

/// <summary>
/// The two gated proxies ported from raidhelper-proxy.worker.js:
///  • /v4/*        Raid-Helper API — officer session required; the RH token is
///                 attached server-side so it never reaches the browser.
///  • /wcl/reports Warcraft Logs report list — any signed-in tier (logs are public).
/// </summary>
public static class ProxyEndpoints
{
    public static void MapProxyEndpoints(this IEndpointRouteBuilder app)
    {
        // Raid-Helper proxy. The frontend only ever calls /v4/*, so scope the
        // catch-all to that prefix (keeps /healthz, /auth/*, /wcl/* out of it).
        app.MapMethods("/v4/{**rest}", new[] { "GET" }, async (
            HttpContext ctx,
            SessionTokenService tokens,
            IOptions<RaidHelperOptions> opt,
            IHttpClientFactory httpFactory) =>
        {
            var session = Authenticate(ctx, tokens);
            if (session is null)
                return Results.Json(new { error = "unauthorized", detail = "Sign-in required." }, statusCode: 401);
            if (!session.Officer)
                return Results.Json(new { error = "forbidden", detail = "Officer access required." }, statusCode: 403);

            var o = opt.Value;
            var target = o.ApiBase.TrimEnd('/') + ctx.Request.Path + ctx.Request.QueryString;

            var http = httpFactory.CreateClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, target);
            // The RH token is sent verbatim (it carries its own scheme, like the
            // Worker's fwd['Authorization'] = env.RH_TOKEN) — bypass validation.
            if (!string.IsNullOrEmpty(o.Token))
                req.Headers.TryAddWithoutValidation("Authorization", o.Token);

            HttpResponseMessage upstream;
            try { upstream = await http.SendAsync(req); }
            catch (Exception err)
            {
                return Results.Json(new { error = "upstream fetch failed", detail = err.Message }, statusCode: 502);
            }

            var body = await upstream.Content.ReadAsStringAsync();
            var contentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json";
            return Results.Text(body, contentType, statusCode: (int)upstream.StatusCode);
        });

        // Warcraft Logs report list — any valid session is enough (public data).
        app.MapGet("/wcl/reports", async (
            HttpContext ctx,
            SessionTokenService tokens,
            WarcraftLogsService wcl) =>
        {
            var session = Authenticate(ctx, tokens);
            if (session is null)
                return Results.Json(new { error = "unauthorized", detail = "Sign-in required." }, statusCode: 401);

            var (status, body) = await wcl.GetReportsAsync();
            return Results.Text(body, "application/json", statusCode: status);
        });
    }

    // Extract and verify the Bearer session token, matching the Worker's
    // /^Bearer\s+(.+)$/i gate.
    private static SessionPayload? Authenticate(HttpContext ctx, SessionTokenService tokens)
    {
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth)) return null;
        const string prefix = "Bearer ";
        if (!auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var token = auth[prefix.Length..].Trim();
        return string.IsNullOrEmpty(token) ? null : tokens.Verify(token);
    }
}
