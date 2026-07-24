using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Data;
using WoobackVash.Api.Models;

namespace WoobackVash.Api.Api;

/// <summary>
/// Per-item roll mutes for the loot-prio page. An officer can say "this character does not
/// roll on this item"; the page then drops that character from that one item everywhere it
/// lists candidates (the tier list, the text copy, the Gargul export) and from a member's
/// personal "what can I roll on" section. See <see cref="LootPrioExclusion"/>.
///
/// Reads are open to any signed-in member, since the mutes shape what everyone sees on the
/// (now member-visible) prio page; writes are officer-only.
/// </summary>
public static class LootPrioEndpoints
{
    public record ExclusionInput(Guid CharacterId, string ItemName, long? ItemId, string? Reason);

    public static void MapLootPrioEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/loot-prio");

        // Every mute in force, so the page can grey out and drop the muted characters.
        group.MapGet("/exclusions", async (HttpContext ctx, SessionTokenService tokens) =>
        {
            var (_, error) = ctx.RequireSession(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var rows = await db.LootPrioExclusions.AsNoTracking()
                .OrderBy(x => x.ItemName)
                .Select(x => new
                {
                    characterId = x.CharacterId,
                    characterName = x.Character != null ? x.Character.Name : null,
                    itemName = x.ItemName
                })
                .ToListAsync();
            return Results.Json(rows);
        });

        // Mute a character on an item. Idempotent: the unique (character, item) index means a
        // repeat is an upsert, not a duplicate.
        group.MapPost("/exclusions", async (HttpContext ctx, SessionTokenService tokens, ExclusionInput input) =>
        {
            var (session, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var name = (input.ItemName ?? "").Trim().ToLowerInvariant();
            if (input.CharacterId == Guid.Empty || name.Length == 0)
                return Results.Json(new { error = "bad_request", detail = "characterId and itemName are required." },
                    statusCode: 400);

            var ch = await db.Characters.FirstOrDefaultAsync(c => c.Id == input.CharacterId);
            if (ch is null) return Results.NotFound(new { error = "not_found", detail = "No such character." });

            var existing = await db.LootPrioExclusions
                .FirstOrDefaultAsync(x => x.CharacterId == input.CharacterId && x.ItemName == name);
            if (existing is null)
            {
                db.LootPrioExclusions.Add(new LootPrioExclusion
                {
                    CharacterId = input.CharacterId,
                    ItemName = name,
                    ItemId = input.ItemId,
                    SetByUid = session!.Uid,
                    Reason = input.Reason
                });
                await db.SaveChangesAsync();
            }
            return Results.Json(new { ok = true });
        });

        // Lift a mute. Keyed by (characterId, itemName) — the same pair that set it.
        group.MapDelete("/exclusions", async (HttpContext ctx, SessionTokenService tokens) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var name = ctx.Request.Query["itemName"].ToString().Trim().ToLowerInvariant();
            if (!Guid.TryParse(ctx.Request.Query["characterId"], out var characterId) || name.Length == 0)
                return Results.Json(new { error = "bad_request", detail = "characterId and itemName are required." },
                    statusCode: 400);

            var existing = await db.LootPrioExclusions
                .FirstOrDefaultAsync(x => x.CharacterId == characterId && x.ItemName == name);
            if (existing is not null)
            {
                db.LootPrioExclusions.Remove(existing);
                await db.SaveChangesAsync();
            }
            return Results.Json(new { ok = true });
        });
    }

    private static IResult DbUnavailable() =>
        Results.Json(new { error = "unavailable", detail = "Persistence is not configured." }, statusCode: 503);
}
