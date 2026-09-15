using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Config;
using WoobackVash.Api.Data;
using WoobackVash.Api.Models;

namespace WoobackVash.Api.Api;

/// <summary>
/// The WoW Forever recruitment form behind <c>apply.html</c>, and the officer review of it on
/// <c>applications.html</c>.
///
/// <c>POST /api/applications</c> is the one write on the API that takes <b>no session</b>: an
/// applicant isn't in the Discord yet. What stands in for auth is a per-IP rate limit (the
/// <see cref="RateLimitPolicy"/>, registered in Program.cs), a honeypot field, and the same kind
/// of size bounds the poll uses. Reading and deleting applications is officer-gated.
///
/// As with the poll, the backend is question-agnostic — <c>apply-questions.js</c> owns the
/// question set, and this stores whatever ids it is sent. The only answer it insists on is the
/// Discord username, because without it an officer can't reply; every other required field is
/// checked by the form alone.
/// </summary>
public static class ApplicationEndpoints
{
    public const string RateLimitPolicy = "applications";

    /// <summary><c>Website</c> is the honeypot: hidden from people, filled in by naive bots.</summary>
    public record ApplicationInput(
        string? Discord,
        Dictionary<string, List<string>>? Choices,
        Dictionary<string, string>? Text,
        string? Website);

    // Generous headroom over the ~20 questions the form actually has.
    private const int MaxKeys = 40;
    private const int MaxOptionsPerQuestion = 20;
    private const int MaxKeyLength = 64;
    private const int MaxHandleLength = 64;
    private const int MaxTextLength = 4000;

    public static void MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/applications");

        // Submit an application. Anonymous by design — see the class remarks.
        group.MapPost("", async (
            HttpContext ctx,
            ApplicationInput input,
            IOptions<RecruitmentOptions> recruitment,
            IOptions<DiscordOptions> discord,
            IHttpClientFactory httpFactory,
            ILoggerFactory loggers) =>
        {
            // A filled honeypot gets the same reply as a real submission, so a bot learns nothing.
            if (!string.IsNullOrWhiteSpace(input.Website))
                return Results.Json(new { ok = true });

            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var handle = input.Discord?.Trim() ?? "";
            if (handle.Length == 0) return BadRequest("Your Discord username is required.");
            if (handle.Length > MaxHandleLength) return BadRequest("That Discord username is too long.");

            var choices = input.Choices ?? new Dictionary<string, List<string>>();
            var text = input.Text ?? new Dictionary<string, string>();
            if (choices.Count + text.Count > MaxKeys)
                return BadRequest("Too many answers.");

            foreach (var (qId, options) in choices)
            {
                if (qId.Length == 0 || qId.Length > MaxKeyLength)
                    return BadRequest("A question id is missing or too long.");
                if (options is null || options.Count > MaxOptionsPerQuestion)
                    return BadRequest("Too many options selected for one question.");
                if (options.Any(o => o is null || o.Length > MaxKeyLength))
                    return BadRequest("An option id is too long.");
            }

            var cleanText = new Dictionary<string, string>();
            foreach (var (qId, value) in text)
            {
                if (qId.Length == 0 || qId.Length > MaxKeyLength)
                    return BadRequest("A question id is missing or too long.");
                var trimmed = value?.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.Length > MaxTextLength)
                    return BadRequest($"An answer is too long (the limit is {MaxTextLength} characters).");
                cleanText[qId] = trimmed;
            }

            var row = new GuildApplication
            {
                DiscordHandle = handle,
                Choices = JsonSerializer.Serialize(choices),
                Text = JsonSerializer.Serialize(cleanText),
                SubmittedAt = DateTimeOffset.UtcNow
            };
            db.GuildApplications.Add(row);
            await db.SaveChangesAsync();

            // Ping the officers. Fire-and-forget: the application is already saved, so a Discord
            // outage or a bad webhook URL is logged and never turned into an error for the applicant.
            var hook = recruitment.Value.DiscordWebhookUrl;
            if (!string.IsNullOrWhiteSpace(hook))
            {
                var payload = WebhookPayload(row, choices, cleanText, discord.Value.AppBase);
                var logger = loggers.CreateLogger(typeof(ApplicationEndpoints));
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var client = httpFactory.CreateClient();
                        client.Timeout = TimeSpan.FromSeconds(10);
                        using var res = await client.PostAsJsonAsync(hook, payload);
                        if (!res.IsSuccessStatusCode)
                            logger.LogWarning("Application webhook returned HTTP {Status}.", (int)res.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Application webhook failed.");
                    }
                });
            }

            return Results.Json(new { ok = true });
        }).RequireRateLimiting(RateLimitPolicy);

        // Every application, newest first. Officers only — this is the one door applicants'
        // answers leave by.
        group.MapGet("", async (HttpContext ctx, SessionTokenService tokens) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var rows = await db.GuildApplications.AsNoTracking()
                .OrderByDescending(a => a.SubmittedAt)
                .ToListAsync();

            var applications = rows.Select(r => new
            {
                id = r.Id,
                discord = r.DiscordHandle,
                choices = Deserialize<Dictionary<string, List<string>>>(r.Choices),
                text = Deserialize<Dictionary<string, string>>(r.Text),
                submittedAt = r.SubmittedAt
            }).ToList();

            return Results.Json(new { total = applications.Count, applications });
        });

        // Remove one — spam, a duplicate, or someone who has been dealt with. Officers only.
        group.MapDelete("/{id:guid}", async (HttpContext ctx, SessionTokenService tokens, Guid id) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var deleted = await db.GuildApplications.Where(a => a.Id == id).ExecuteDeleteAsync();
            return deleted == 0
                ? Results.Json(new { error = "not_found", detail = "No such application." }, statusCode: 404)
                : Results.Json(new { ok = true });
        });
    }

    // A short summary only: the full answers stay behind the officer gate, and a Discord channel
    // is a wider audience than the review page. `allowed_mentions.parse = []` means nothing the
    // applicant typed can ping @everyone, a role, or a user.
    private static object WebhookPayload(
        GuildApplication row,
        Dictionary<string, List<string>> choices,
        Dictionary<string, string> text,
        string appBase)
    {
        var fields = new List<object>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields.Add(new { name, value = Clip(value, 1000), inline = true });
        }
        string? Picked(string qId) =>
            choices.TryGetValue(qId, out var v) && v.Count > 0 ? string.Join(", ", v) : null;

        Add("Character", text.GetValueOrDefault("charName"));
        Add("Class", Picked("class"));
        Add("Role", Picked("role"));
        Add("Plays", Picked("commitment"));
        Add("Sweaty", Picked("sweaty") is { } s ? s + "/5" : null);
        Add("Raid nights", Picked("raidNights"));
        Add("Timezone", Picked("timezone"));

        return new
        {
            username = "wooback recruitment",
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = Clip("New application: " + row.DiscordHandle, 250),
                    url = appBase.TrimEnd('/') + "/applications.html",
                    color = 0xE8B84D,
                    fields,
                    timestamp = row.SubmittedAt.ToString("o")
                }
            }
        };
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    // A stored blob is trusted (we wrote it), but a malformed row must never sink the whole list.
    private static T Deserialize<T>(string json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(json) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    private static IResult BadRequest(string detail) =>
        Results.Json(new { error = "bad_request", detail }, statusCode: 400);

    private static IResult DbUnavailable() =>
        Results.Json(new { error = "unavailable", detail = "Persistence is not configured." }, statusCode: 503);
}
