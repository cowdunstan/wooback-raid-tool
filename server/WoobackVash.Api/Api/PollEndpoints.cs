using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Data;
using WoobackVash.Api.Models;

namespace WoobackVash.Api.Api;

/// <summary>
/// The "WoW Forever" interest poll behind <c>forever.html</c>. Any signed-in member may vote
/// and everyone sees the aggregate tallies, mirroring how the loot "hall of shame" opens
/// guild-wide data to any tier.
///
/// The backend is deliberately generic: it never hard-codes the questions. A response is a
/// <c>{ questionId: [optionId, …] }</c> map (see <see cref="PollResponse"/>); the GET tallies
/// whatever keys it finds, so adding or changing a question is a frontend-only edit. Tallies
/// are computed here in memory and only counts (plus the free-text comments) are returned —
/// the individual rows never leave the server, so who voted what stays private.
/// </summary>
public static class PollEndpoints
{
    public record PollInput(Dictionary<string, List<string>>? Answers, string? Comment);

    // Bounds so a member can't bloat the jsonb. The poll has ~10 questions with a handful of
    // options each; these are generous headroom, not the expected shape.
    private const int MaxQuestions = 40;
    private const int MaxOptionsPerQuestion = 40;
    private const int MaxKeyLength = 64;
    private const int MaxCommentLength = 2000;

    public static void MapPollEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/poll");

        // The caller's own answers (to pre-fill the form) plus the guild-wide tallies. Open to
        // any signed-in member.
        group.MapGet("", async (HttpContext ctx, SessionTokenService tokens) =>
        {
            var (session, error) = ctx.RequireSession(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var rows = await db.PollResponses.AsNoTracking().ToListAsync();

            // Tally every answer across all responses: results[questionId][optionId] = count.
            var results = new Dictionary<string, Dictionary<string, int>>();
            var comments = new List<string>();
            foreach (var row in rows)
            {
                var answers = Deserialize(row.Answers);
                foreach (var (qId, options) in answers)
                {
                    if (!results.TryGetValue(qId, out var byOption))
                        results[qId] = byOption = new Dictionary<string, int>();
                    foreach (var opt in options)
                        byOption[opt] = byOption.GetValueOrDefault(opt) + 1;
                }
                if (!string.IsNullOrWhiteSpace(row.Comment))
                    comments.Add(row.Comment.Trim());
            }

            var mineRow = rows.FirstOrDefault(r => r.Uid == session!.Uid);
            object? mine = mineRow is null
                ? null
                : new { answers = Deserialize(mineRow.Answers), comment = mineRow.Comment };

            return Results.Json(new { total = rows.Count, mine, results, comments });
        });

        // Cast or change the caller's own vote. Upserts on the unique Uid index.
        group.MapPost("", async (HttpContext ctx, SessionTokenService tokens, PollInput input) =>
        {
            var (session, error) = ctx.RequireSession(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var answers = input.Answers ?? new Dictionary<string, List<string>>();
            if (answers.Count > MaxQuestions)
                return BadRequest("Too many answers.");
            foreach (var (qId, options) in answers)
            {
                if (qId.Length == 0 || qId.Length > MaxKeyLength)
                    return BadRequest("A question id is missing or too long.");
                if (options.Count > MaxOptionsPerQuestion)
                    return BadRequest("Too many options selected for one question.");
                if (options.Any(o => o.Length > MaxKeyLength))
                    return BadRequest("An option id is too long.");
            }

            var comment = input.Comment?.Trim();
            if (comment is not null && comment.Length > MaxCommentLength)
                return BadRequest("Comment is too long.");
            if (string.IsNullOrEmpty(comment)) comment = null;

            var json = JsonSerializer.Serialize(answers);
            var existing = await db.PollResponses.FirstOrDefaultAsync(x => x.Uid == session!.Uid);
            if (existing is null)
            {
                db.PollResponses.Add(new PollResponse
                {
                    Uid = session!.Uid,
                    Name = session.Name,
                    Answers = json,
                    Comment = comment,
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.Name = session!.Name;
                existing.Answers = json;
                existing.Comment = comment;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();
            return Results.Json(new { ok = true });
        });
    }

    // A stored answers blob is trusted (we wrote it), but a malformed or legacy row must never
    // sink the whole tally — treat anything unparseable as no answers.
    private static Dictionary<string, List<string>> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static IResult BadRequest(string detail) =>
        Results.Json(new { error = "bad_request", detail }, statusCode: 400);

    private static IResult DbUnavailable() =>
        Results.Json(new { error = "unavailable", detail = "Persistence is not configured." }, statusCode: 503);
}
