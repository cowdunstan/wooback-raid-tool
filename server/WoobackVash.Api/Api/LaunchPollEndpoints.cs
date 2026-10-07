using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Data;
using WoobackVash.Api.Models;

namespace WoobackVash.Api.Api;

/// <summary>
/// The WoW Forever launch poll behind <c>launch.html</c>, and the officer review of it on
/// <c>launch-responses.html</c>. Officers use it to plan the launch raid groups: who is playing
/// what, who will be 60 when raids open, and who wants the parsing group over the semi-hardcore
/// one.
///
/// Any signed-in member answers for themselves, and reads back only their own answers — unlike
/// the WoW Forever interest poll there are no public tallies, because "who wants the sweaty
/// group" is roster planning, not a guild-wide vote. Every member's answers leave the server
/// only through the officer-gated <c>GET /api/launch-poll/responses</c>.
///
/// Question-agnostic in the same way as <see cref="ApplicationEndpoints"/>: <c>launch-questions.js</c>
/// owns the question set and this stores whatever ids it is sent, split into choices and text.
/// </summary>
public static class LaunchPollEndpoints
{
    public record LaunchPollInput(Dictionary<string, List<string>>? Choices, Dictionary<string, string>? Text);

    // Generous headroom over the ~10 questions the form actually has.
    private const int MaxKeys = 40;
    private const int MaxOptionsPerQuestion = 20;
    private const int MaxKeyLength = 64;
    private const int MaxTextLength = 2000;

    public static void MapLaunchPollEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/launch-poll");

        // The caller's own answers, to pre-fill the form, and how many members have answered.
        group.MapGet("", async (HttpContext ctx, SessionTokenService tokens) =>
        {
            var (session, error) = ctx.RequireSession(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var total = await db.LaunchPollResponses.CountAsync();
            var row = await db.LaunchPollResponses.AsNoTracking().FirstOrDefaultAsync(r => r.Uid == session!.Uid);
            object? mine = row is null
                ? null
                : new
                {
                    choices = Deserialize<Dictionary<string, List<string>>>(row.Choices),
                    text = Deserialize<Dictionary<string, string>>(row.Text),
                    updatedAt = row.UpdatedAt
                };
            return Results.Json(new { total, mine });
        });

        // Save or change the caller's own answers. Upserts on the unique Uid index.
        group.MapPost("", async (HttpContext ctx, SessionTokenService tokens, LaunchPollInput input) =>
        {
            var (session, error) = ctx.RequireSession(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

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

            var choicesJson = JsonSerializer.Serialize(choices);
            var textJson = JsonSerializer.Serialize(cleanText);
            var existing = await db.LaunchPollResponses.FirstOrDefaultAsync(x => x.Uid == session!.Uid);
            if (existing is null)
            {
                db.LaunchPollResponses.Add(new LaunchPollResponse
                {
                    Uid = session!.Uid,
                    Name = session.Name,
                    Choices = choicesJson,
                    Text = textJson,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.Name = session!.Name;
                existing.Choices = choicesJson;
                existing.Text = textJson;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Json(new { ok = true });
        });

        // Every member's answers, most recently updated first. Officers only — the one door
        // individual answers leave by. Each response carries the member's TBC main, when they
        // have one linked, because officers recognise people by their main more readily than by
        // a Discord display name.
        group.MapGet("/responses", async (HttpContext ctx, SessionTokenService tokens) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var rows = await db.LaunchPollResponses.AsNoTracking()
                .OrderByDescending(r => r.UpdatedAt)
                .ToListAsync();

            // Discord uid → that member's main, the same lookup as GET /api/poll/detail.
            var mainByUid = await db.Characters.AsNoTracking()
                .Where(c => c.IsMain && !c.Ignored && c.MemberId != null)
                .Join(db.Members.AsNoTracking(),
                    c => c.MemberId, m => m.Id,
                    (c, m) => new { m.DiscordUserId, c.Id, c.Name })
                .ToDictionaryAsync(x => x.DiscordUserId, x => new { x.Id, x.Name }, StringComparer.Ordinal);

            var responses = rows.Select(r =>
            {
                mainByUid.TryGetValue(r.Uid, out var main);
                return new
                {
                    id = r.Id,
                    name = r.Name,
                    mainName = main?.Name,
                    mainId = main?.Id,
                    choices = Deserialize<Dictionary<string, List<string>>>(r.Choices),
                    text = Deserialize<Dictionary<string, string>>(r.Text),
                    updatedAt = r.UpdatedAt
                };
            }).ToList();

            return Results.Json(new { total = responses.Count, responses });
        });

        // Remove one — someone who has left, or a test response. Officers only.
        group.MapDelete("/responses/{id:guid}", async (HttpContext ctx, SessionTokenService tokens, Guid id) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var deleted = await db.LaunchPollResponses.Where(r => r.Id == id).ExecuteDeleteAsync();
            return deleted == 0
                ? Results.Json(new { error = "not_found", detail = "No such response." }, statusCode: 404)
                : Results.Json(new { ok = true });
        });
    }

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
