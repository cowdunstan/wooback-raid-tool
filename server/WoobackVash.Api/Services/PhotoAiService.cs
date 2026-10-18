using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WoobackVash.Api.Config;

namespace WoobackVash.Api.Services;

/// <summary>
/// The photobook's two OpenAI calls. <see cref="ModerateAsync"/> uses the free moderation
/// endpoint on the image and caption together; <see cref="SuggestTagsAsync"/> asks a small
/// vision model to pick tags from <see cref="PhotoTags"/>, a place and boss from
/// <see cref="PhotoPlaces"/>, and write a line of alt text.
///
/// Neither ever throws to the caller. A moderation call that can't be made comes back
/// unchecked (<see cref="ModerationVerdict.Checked"/> false), which holds the photo for an
/// officer; a tagging call that fails just leaves the photo untagged.
/// </summary>
public class PhotoAiService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly PhotobookOptions _opts;
    private readonly ILogger<PhotoAiService> _logger;

    public PhotoAiService(IHttpClientFactory httpFactory, IOptions<PhotobookOptions> opts, ILogger<PhotoAiService> logger)
    {
        _httpFactory = httpFactory;
        _opts = opts.Value;
        _logger = logger;
    }

    public record CategoryScore(string Category, double Score);

    /// <param name="Checked">Whether moderation actually ran. False → no verdict, hold for review.</param>
    /// <param name="Flagged">OpenAI's own verdict: the photo is refused.</param>
    /// <param name="Reason">Why it's unchecked, or why it's held — for the officer looking at it.</param>
    /// <param name="Top">The highest-scoring categories, highest first.</param>
    /// <param name="Hit">The categories OpenAI flagged; empty unless <paramref name="Flagged"/>.</param>
    public record ModerationVerdict(bool Checked, bool Flagged, string? Reason, List<CategoryScore> Top, List<string> Hit)
    {
        public double MaxScore => Top.Count == 0 ? 0 : Top[0].Score;
    }

    /// <param name="Ran">Whether the model actually answered. False → nothing was suggested, so the
    /// photo has no AI tags to confirm and is left out of the accuracy numbers.</param>
    /// <param name="Place">A <see cref="PhotoPlaces"/> id, or null for "none".</param>
    /// <param name="Boss">A boss id, or null; when set, <paramref name="Place"/> is its place.</param>
    /// <param name="PlaceEvidence">Which clue the place came from (<see cref="PlaceEvidence"/>), or null with no place.</param>
    public record TagSuggestion(bool Ran, List<string> Tags, string AltText, string? Place, string? Boss, string? PlaceEvidence);

    /// <summary>
    /// The clue a place pick was read from, strongest first. Kept with the photo so the accuracy
    /// numbers can be split by it — the point being to see whether guesses made from the scene
    /// alone, on screenshots with the interface hidden, are as good as ones read off the screen.
    /// </summary>
    public static class PlaceEvidence
    {
        public const string ScreenText = "screen-text";
        public const string NpcName = "npc-name";
        public const string Landmark = "landmark";
        public const string Caption = "caption";
        /// <summary>The place came from a boss pick, with no recognisable clue of its own.</summary>
        public const string Unknown = "unknown";

        public static readonly string[] All = [ScreenText, NpcName, Landmark, Caption];
    }

    private bool Configured => !string.IsNullOrWhiteSpace(_opts.OpenAiApiKey);

    public async Task<ModerationVerdict> ModerateAsync(byte[] jpeg, string caption, CancellationToken ct = default)
    {
        if (!Configured) return Unchecked("Moderation is not configured (no OpenAI API key).");

        var input = new JsonArray();
        if (!string.IsNullOrWhiteSpace(caption))
            input.Add(new JsonObject { ["type"] = "text", ["text"] = caption });
        if (jpeg.Length > 0)
            input.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = DataUrl(jpeg) }
            });
        if (input.Count == 0) return new ModerationVerdict(true, false, null, [], []);

        var body = new JsonObject { ["model"] = _opts.ModerationModel, ["input"] = input };
        var json = await PostAsync("/moderations", body, ct);
        if (json is null) return Unchecked("The moderation check could not be reached.");

        try
        {
            var result = json["results"]![0]!;
            var flagged = result["flagged"]!.GetValue<bool>();
            var scores = result["category_scores"]!.AsObject()
                .Select(kv => new CategoryScore(kv.Key, kv.Value?.GetValue<double>() ?? 0))
                .OrderByDescending(s => s.Score)
                .Take(3)
                .ToList();
            string? reason = null;
            var hit = new List<string>();
            if (flagged)
            {
                hit = result["categories"]!.AsObject()
                    .Where(kv => kv.Value?.GetValue<bool>() == true)
                    .Select(kv => kv.Key)
                    .ToList();
                reason = "Flagged: " + string.Join(", ", hit);
            }
            else if (scores.Count > 0 && scores[0].Score >= _opts.ReviewThreshold)
            {
                reason = $"Borderline: {scores[0].Category} scored {scores[0].Score:0.00}.";
            }
            return new ModerationVerdict(true, flagged, reason, scores, hit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected moderation response shape.");
            return Unchecked("The moderation check returned something unexpected.");
        }
    }

    public async Task<TagSuggestion> SuggestTagsAsync(byte[] jpeg, string caption, CancellationToken ct = default)
    {
        var none = new TagSuggestion(false, [], "", null, null, null);
        if (!Configured || string.IsNullOrWhiteSpace(_opts.TaggingModel)) return none;

        var hasCaption = !string.IsNullOrWhiteSpace(caption);
        var vocabulary = string.Join("\n", PhotoTags.All.Select(t => $"- {t.Id}: {t.Label} — {t.Meaning}"));
        var places = string.Join("\n", PhotoPlaces.All.Select(p => $"- {p.Id}: {p.Label} ({p.Kind})"));
        var bosses = string.Join("\n", PhotoPlaces.All.Where(p => p.Bosses.Length > 0)
            .Select(p => $"{p.Label}: " + string.Join(", ", p.Bosses.Select(b => $"{b.Id} = {b.Label}"))));
        // Many screenshots are taken with the interface hidden — no minimap, no frames — so the
        // place prompt ranks every kind of clue rather than leaning on the minimap, and asks which
        // one it used. That lands in AiPlaceEvidence, so the accuracy tab can show whether the
        // landmark-only guesses hold up as well as the ones read off the screen.
        //
        // Tuned against live runs on two UI-less screenshots. Without tag meanings the model
        // filled all six slots, "UI / addons" and "Real life" included; without a caption it
        // still claimed "caption" as its clue, so that clue is only offered when there is one;
        // and it named a familiar city for a dungeon missing from the list, hence the push to
        // "none" whenever it can't point at a specific clue.
        var clues = new List<string>
        {
            "screen-text: a zone, instance or battleground name shown on screen, such as above the minimap",
            "npc-name: a name floating over a non-player character that gives the place away — \"Stormwind City " +
                "Guard\" means Stormwind City, \"Orgrimmar Grunt\" means Orgrimmar",
            "landmark: a specific building, statue or gate you recognise as belonging to that exact place — not " +
                "just a style of architecture that several places share",
        };
        if (hasCaption) clues.Add("caption: the caption names it");
        var prompt =
            "This is a screenshot or photo a member of the guild <Wooback> uploaded to the guild's photobook. " +
            "The game is World of Warcraft: Forever, a new expansion that keeps most of Classic's world but adds " +
            "new zones and dungeons you won't know." +
            (hasCaption ? $" Their caption: \"{caption}\"." : " There is no caption.") +
            "\n\nThe interface is often hidden, so there may be no minimap, frames or chat — only the scene and the " +
            "names floating over characters. Player names with <Wooback> under them are guild members; a name with " +
            "\"'s Pet\" or \"'s Minion\" under it is a player's pet.\n\n" +
            $"Tags: most photos need two or three, and never more than {PhotoTags.MaxPerPhoto}. Only add a tag " +
            "whose meaning below clearly fits what you can see; a missing tag is better than a wrong one. Use these ids only:\n" +
            $"{vocabulary}\n\n" +
            "Place: say where it was taken, as one place id from the list below, or \"none\". Only pick a place if " +
            "one of these clues points to it, strongest first:\n" +
            string.Join("\n", clues.Select((c, i) => $"{i + 1}. {c}")) + "\n" +
            "If none of those clues applies, the answer is \"none\" — do not guess from the general look of a room " +
            "or a landscape. This may well be one of Forever's new places, which aren't in the list: answer \"none\" " +
            "for those, never the nearest familiar place. A wrong place is worse than none. Give the clue you used " +
            "as place_evidence, or \"none\" with no place.\n" +
            $"{places}\n\n" +
            "Boss: one boss id or \"none\" — only if a boss is plainly being fought, its name floats over it or is in " +
            "a target or boss frame" + (hasCaption ? ", or the caption names it" : "") + ".\n" +
            $"{bosses}\n\n" +
            "Finally, write one plain sentence describing what's in the picture, for use as alt text.";

        static JsonArray Enum(IEnumerable<string> ids) => new(ids.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
        var evidenceValues = Enum(PlaceEvidence.All.Where(e => hasCaption || e != PlaceEvidence.Caption).Append("none"));
        var enumValues = Enum(PhotoTags.All.Select(t => t.Id));
        var placeValues = Enum(PhotoPlaces.All.Select(p => p.Id).Append("none"));
        var bossValues = Enum(PhotoPlaces.BossById.Keys.Append("none"));
        var body = new JsonObject
        {
            ["model"] = _opts.TaggingModel,
            ["max_completion_tokens"] = 1000,
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "text", ["text"] = prompt },
                    new JsonObject
                    {
                        ["type"] = "image_url",
                        // High detail by default: low is one 512px tile, too small to read the
                        // minimap's zone name, a boss frame or the names floating over characters,
                        // which the place and boss picks lean on. On a nano model it still costs
                        // a fraction of a cent.
                        ["image_url"] = new JsonObject { ["url"] = DataUrl(jpeg), ["detail"] = _opts.TaggingImageDetail }
                    })
            }),
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "photo_tags",
                    ["strict"] = true,
                    ["schema"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["tags"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["items"] = new JsonObject { ["type"] = "string", ["enum"] = enumValues }
                            },
                            ["place"] = new JsonObject { ["type"] = "string", ["enum"] = placeValues },
                            ["place_evidence"] = new JsonObject { ["type"] = "string", ["enum"] = evidenceValues },
                            ["boss"] = new JsonObject { ["type"] = "string", ["enum"] = bossValues },
                            ["description"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("tags", "place", "place_evidence", "boss", "description"),
                        ["additionalProperties"] = false
                    }
                }
            }
        };
        // How much the reasoning models think before answering (Photobook:TaggingReasoningEffort).
        // Older chat models reject the parameter, so it's only sent where it's understood.
        if (_opts.TaggingModel.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
            body["reasoning_effort"] = _opts.TaggingReasoningEffort;

        var json = await PostAsync("/chat/completions", body, ct);
        if (json is null) return none;
        try
        {
            var content = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
            // The raw answer, before the cleanup below drops anything; the record of what the model
            // actually said when a pick looks odd.
            _logger.LogInformation("Tagging model replied: {Content}", content);
            var parsed = JsonNode.Parse(content)!;
            var tags = parsed["tags"]!.AsArray().Select(t => t?.GetValue<string>() ?? "");
            var alt = parsed["description"]?.GetValue<string>()?.Trim() ?? "";
            var (place, boss) = PhotoPlaces.Clean(parsed["place"]?.GetValue<string>(), parsed["boss"]?.GetValue<string>());
            // Evidence only means something alongside a place; a boss-implied place came from the boss.
            // A place needs a clue behind it. Without a valid one — none given, or "caption" with
            // no caption — it was a guess, and the prompt says a wrong place is worse than none, so
            // it's dropped. The exception is a place implied by a boss pick, which stands on the boss.
            var evidence = parsed["place_evidence"]?.GetValue<string>();
            var validClue = evidence is not null && PlaceEvidence.All.Contains(evidence)
                && (hasCaption || evidence != PlaceEvidence.Caption);
            if (place is null) evidence = null;
            else if (!validClue && boss is null) { place = null; evidence = null; }
            else if (!validClue) evidence = PlaceEvidence.Unknown;
            return new TagSuggestion(true, PhotoTags.Clean(tags), alt.Length > 300 ? alt[..300] : alt, place, boss, evidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected tagging response shape.");
            return none;
        }
    }

    private async Task<JsonNode?> PostAsync(string path, JsonObject body, CancellationToken ct)
    {
        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(_opts.RequestTimeoutSeconds);
            using var req = new HttpRequestMessage(HttpMethod.Post, _opts.ApiBase.TrimEnd('/') + path)
            {
                Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.OpenAiApiKey);
            using var res = await client.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenAI {Path} returned HTTP {Status}: {Body}", path, (int)res.StatusCode,
                    text.Length > 500 ? text[..500] : text);
                return null;
            }
            return JsonNode.Parse(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "OpenAI {Path} call failed.", path);
            return null;
        }
    }

    private static ModerationVerdict Unchecked(string reason) => new(false, false, reason, [], []);

    private static string DataUrl(byte[] jpeg) => "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
}
