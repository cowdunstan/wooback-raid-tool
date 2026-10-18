using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WoobackVash.Api.Auth;
using WoobackVash.Api.Data;
using WoobackVash.Api.Models;
using WoobackVash.Api.Services;

namespace WoobackVash.Api.Api;

/// <summary>
/// The guild photobook behind <c>photobook.html</c>. <b>Officers only for now</b>, every route
/// included, while it's being tried out; opening it to members means swapping
/// <c>RequireOfficer</c> for <c>RequireSession</c> on the reads and the upload, and keeping the
/// review routes (approve, and deleting someone else's photo) on officers.
///
/// An upload is checked before it's stored. The page has already resized the image and
/// re-encoded it as JPEG — which also strips its EXIF, phone GPS included — so the API takes
/// the full image and its thumbnail as base64 JPEG. The image and caption then go to OpenAI
/// moderation together: flagged outright, the upload is refused and the image is not kept — only
/// a <see cref="PhotoRejection"/> saying who tried and why, for the officers' Rejected tab; borderline,
/// or unchecked because moderation couldn't run, it's stored as pending for an officer to
/// approve; otherwise it's published. A photo that gets through is then tagged from
/// <see cref="PhotoTags"/> by a small vision model, and the uploader can correct the tags.
///
/// Images can't go in an <c>&lt;img src&gt;</c> directly, since the routes want a Bearer token,
/// so the page fetches them and shows object URLs.
/// </summary>
public static class PhotoEndpoints
{
    public record PhotoUpload(string? Image, string? Thumb, string? Caption, int Width, int Height);
    public record PhotoEdit(string? Caption, List<string>? Tags);

    private const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxThumbBytes = 512 * 1024;
    private const int MaxCaptionLength = 300;
    private const int MaxDimension = 4096;

    public static void MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/photos");

        // The book, newest first: approved photos, or the review queue with ?status=pending.
        // Carries the tag vocabulary so the page never keeps its own copy.
        group.MapGet("", async (HttpContext ctx, SessionTokenService tokens, string? status) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var want = status == PhotoStatus.Pending ? PhotoStatus.Pending : PhotoStatus.Approved;
            var rows = await db.Photos.AsNoTracking()
                .Where(p => p.Status == want)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            var pending = want == PhotoStatus.Pending
                ? rows.Count
                : await db.Photos.CountAsync(p => p.Status == PhotoStatus.Pending);
            var rejected = await db.PhotoRejections.CountAsync();

            return Results.Json(new
            {
                tags = PhotoTags.All.Select(t => new { id = t.Id, label = t.Label }),
                pending,
                rejected,
                photos = rows.Select(Dto)
            });
        });

        // Everything the content check refused, newest first: who, what kind, and why. There is
        // no image to show — a refused upload's bytes are never kept (see PhotoRejection).
        group.MapGet("/rejections", async (HttpContext ctx, SessionTokenService tokens) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var rows = await db.PhotoRejections.AsNoTracking()
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
            return Results.Json(new
            {
                total = rows.Count,
                rejections = rows.Select(r => new
                {
                    id = r.Id,
                    uploaderUid = r.UploaderUid,
                    uploaderName = r.UploaderName,
                    kind = r.Kind,
                    photoId = r.PhotoId,
                    caption = r.Caption,
                    categories = Deserialize<List<string>>(r.Categories),
                    scores = Deserialize<List<Dictionary<string, JsonElement>>>(r.Scores),
                    createdAt = r.CreatedAt
                })
            });
        });

        // Dismiss one once it has been dealt with.
        group.MapDelete("/rejections/{id:guid}", async (HttpContext ctx, SessionTokenService tokens, Guid id) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var deleted = await db.PhotoRejections.Where(r => r.Id == id).ExecuteDeleteAsync();
            return deleted == 0
                ? Results.Json(new { error = "not_found", detail = "No such rejection." }, statusCode: 404)
                : Results.Json(new { ok = true });
        });

        group.MapGet("/{id:guid}/thumb", (HttpContext ctx, SessionTokenService tokens, Guid id) =>
            ImageAsync(ctx, tokens, id, thumb: true));
        group.MapGet("/{id:guid}/full", (HttpContext ctx, SessionTokenService tokens, Guid id) =>
            ImageAsync(ctx, tokens, id, thumb: false));

        // Upload: validate, moderate, store (unless flagged), tag.
        group.MapPost("", async (HttpContext ctx, SessionTokenService tokens, PhotoAiService ai, PhotoUpload input) =>
        {
            var (session, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var full = DecodeJpeg(input.Image, MaxImageBytes);
            var thumb = DecodeJpeg(input.Thumb, MaxThumbBytes);
            if (full is null) return BadRequest($"The image is missing, not a JPEG, or over {MaxImageBytes / (1024 * 1024)} MB.");
            if (thumb is null) return BadRequest("The thumbnail is missing, not a JPEG, or too large.");
            if (input.Width is <= 0 or > MaxDimension || input.Height is <= 0 or > MaxDimension)
                return BadRequest("The image dimensions are out of range.");
            var caption = input.Caption?.Trim() ?? "";
            if (caption.Length > MaxCaptionLength)
                return BadRequest($"The caption is too long (the limit is {MaxCaptionLength} characters).");

            var verdict = await ai.ModerateAsync(full, caption, ctx.RequestAborted);
            if (verdict.Flagged)
            {
                await RecordRejectionAsync(db, session!, PhotoRejectionKind.Upload, null, caption, verdict);
                return Results.Json(new
                {
                    error = "rejected",
                    detail = "This photo didn't pass the content check, so it wasn't saved. " + verdict.Reason
                }, statusCode: 422);
            }

            var held = !verdict.Checked || verdict.Reason is not null;
            var suggestion = await ai.SuggestTagsAsync(full, caption, ctx.RequestAborted);

            var photo = new Photo
            {
                UploaderUid = session!.Uid,
                UploaderName = session.Name,
                Caption = caption,
                Tags = JsonSerializer.Serialize(suggestion.Tags),
                AltText = suggestion.AltText,
                Status = held ? PhotoStatus.Pending : PhotoStatus.Approved,
                Moderation = JsonSerializer.Serialize(new
                {
                    @checked = verdict.Checked,
                    flagged = false,
                    reason = verdict.Reason,
                    top = verdict.Top.Select(s => new { category = s.Category, score = Math.Round(s.Score, 4) })
                }),
                Width = input.Width,
                Height = input.Height,
                CreatedAt = DateTimeOffset.UtcNow,
                Image = new PhotoImage { Full = full, Thumb = thumb }
            };
            db.Photos.Add(photo);
            await db.SaveChangesAsync();
            return Results.Json(Dto(photo));
        });

        // Correct the caption or tags. A new caption goes back through moderation on its own.
        group.MapPut("/{id:guid}", async (HttpContext ctx, SessionTokenService tokens, PhotoAiService ai, Guid id, PhotoEdit input) =>
        {
            var (session, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null) return NotFound();

            if (input.Caption is not null)
            {
                var caption = input.Caption.Trim();
                if (caption.Length > MaxCaptionLength)
                    return BadRequest($"The caption is too long (the limit is {MaxCaptionLength} characters).");
                if (caption != photo.Caption && caption.Length > 0)
                {
                    var verdict = await ai.ModerateAsync([], caption, ctx.RequestAborted);
                    if (verdict.Flagged)
                    {
                        await RecordRejectionAsync(db, session!, PhotoRejectionKind.Caption, photo.Id, caption, verdict);
                        return Results.Json(new { error = "rejected", detail = "That caption didn't pass the content check. " + verdict.Reason },
                            statusCode: 422);
                    }
                }
                photo.Caption = caption;
            }
            if (input.Tags is not null)
                photo.Tags = JsonSerializer.Serialize(PhotoTags.Clean(input.Tags));

            await db.SaveChangesAsync();
            return Results.Json(Dto(photo));
        });

        // Publish a photo moderation held back.
        group.MapPost("/{id:guid}/approve", async (HttpContext ctx, SessionTokenService tokens, Guid id) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var updated = await db.Photos.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PhotoStatus.Approved));
            return updated == 0 ? NotFound() : Results.Json(new { ok = true });
        });

        // Remove a photo and its bytes (the image row cascades).
        group.MapDelete("/{id:guid}", async (HttpContext ctx, SessionTokenService tokens, Guid id) =>
        {
            var (_, error) = ctx.RequireOfficer(tokens);
            if (error is not null) return error;
            var db = ctx.RequestServices.GetService<AppDbContext>();
            if (db is null) return DbUnavailable();

            var deleted = await db.Photos.Where(p => p.Id == id).ExecuteDeleteAsync();
            return deleted == 0 ? NotFound() : Results.Json(new { ok = true });
        });
    }

    // Logged before the 422 goes back. A failure to log never changes what the uploader is told.
    private static async Task RecordRejectionAsync(AppDbContext db, SessionPayload session, string kind, Guid? photoId,
        string caption, PhotoAiService.ModerationVerdict verdict)
    {
        db.PhotoRejections.Add(new PhotoRejection
        {
            UploaderUid = session.Uid,
            UploaderName = session.Name,
            Kind = kind,
            PhotoId = photoId,
            Caption = caption,
            Categories = JsonSerializer.Serialize(verdict.Hit),
            Scores = JsonSerializer.Serialize(verdict.Top.Select(s => new { category = s.Category, score = Math.Round(s.Score, 4) })),
            CreatedAt = DateTimeOffset.UtcNow
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }
    }

    private static async Task<IResult> ImageAsync(HttpContext ctx, SessionTokenService tokens, Guid id, bool thumb)
    {
        var (_, error) = ctx.RequireOfficer(tokens);
        if (error is not null) return error;
        var db = ctx.RequestServices.GetService<AppDbContext>();
        if (db is null) return DbUnavailable();

        var bytes = thumb
            ? await db.PhotoImages.Where(i => i.PhotoId == id).Select(i => i.Thumb).FirstOrDefaultAsync()
            : await db.PhotoImages.Where(i => i.PhotoId == id).Select(i => i.Full).FirstOrDefaultAsync();
        if (bytes is null) return NotFound();
        // A photo's bytes never change under its id, so the browser may keep them — privately,
        // since they sat behind a session.
        ctx.Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return Results.File(bytes, "image/jpeg");
    }

    // Base64 → bytes, accepting a data: URL prefix, and only if it really is a JPEG
    // (starts FF D8 FF) within the size limit. Anything else is null.
    private static byte[]? DecodeJpeg(string? b64, int maxBytes)
    {
        if (string.IsNullOrWhiteSpace(b64)) return null;
        var comma = b64.IndexOf(',');
        if (b64.StartsWith("data:", StringComparison.Ordinal) && comma > 0) b64 = b64[(comma + 1)..];
        if (b64.Length > (maxBytes / 3 + 1) * 4) return null;
        var buffer = new byte[b64.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(b64, buffer, out var n)) return null;
        if (n < 3 || n > maxBytes || buffer[0] != 0xFF || buffer[1] != 0xD8 || buffer[2] != 0xFF) return null;
        return buffer[..n];
    }

    private static object Dto(Photo p) => new
    {
        id = p.Id,
        uploaderUid = p.UploaderUid,
        uploaderName = p.UploaderName,
        caption = p.Caption,
        tags = Deserialize<List<string>>(p.Tags),
        altText = p.AltText,
        status = p.Status,
        moderation = Deserialize<Dictionary<string, JsonElement>>(p.Moderation),
        width = p.Width,
        height = p.Height,
        createdAt = p.CreatedAt
    };

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

    private static IResult NotFound() =>
        Results.Json(new { error = "not_found", detail = "No such photo." }, statusCode: 404);

    private static IResult DbUnavailable() =>
        Results.Json(new { error = "unavailable", detail = "Persistence is not configured." }, statusCode: 503);
}
