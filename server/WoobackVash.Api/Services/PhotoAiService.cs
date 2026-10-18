using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using WoobackVash.Api.Config;

namespace WoobackVash.Api.Services;

/// <summary>
/// The photobook's two OpenAI calls. <see cref="ModerateAsync"/> uses the free moderation
/// endpoint on the image and caption together; <see cref="SuggestTagsAsync"/> asks a small
/// vision model to pick tags from <see cref="PhotoTags"/> and write a line of alt text.
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
    public record ModerationVerdict(bool Checked, bool Flagged, string? Reason, List<CategoryScore> Top)
    {
        public double MaxScore => Top.Count == 0 ? 0 : Top[0].Score;
    }

    public record TagSuggestion(List<string> Tags, string AltText);

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
        if (input.Count == 0) return new ModerationVerdict(true, false, null, []);

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
            if (flagged)
            {
                var hit = result["categories"]!.AsObject()
                    .Where(kv => kv.Value?.GetValue<bool>() == true)
                    .Select(kv => kv.Key);
                reason = "Flagged: " + string.Join(", ", hit);
            }
            else if (scores.Count > 0 && scores[0].Score >= _opts.ReviewThreshold)
            {
                reason = $"Borderline: {scores[0].Category} scored {scores[0].Score:0.00}.";
            }
            return new ModerationVerdict(true, flagged, reason, scores);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected moderation response shape.");
            return Unchecked("The moderation check returned something unexpected.");
        }
    }

    public async Task<TagSuggestion> SuggestTagsAsync(byte[] jpeg, string caption, CancellationToken ct = default)
    {
        var none = new TagSuggestion([], "");
        if (!Configured || string.IsNullOrWhiteSpace(_opts.TaggingModel)) return none;

        var vocabulary = string.Join("\n", PhotoTags.All.Select(t => $"- {t.Id}: {t.Label}"));
        var prompt =
            "This is a screenshot or photo a member of a World of Warcraft Classic guild uploaded to the guild's photobook." +
            (string.IsNullOrWhiteSpace(caption) ? "" : $" Their caption: \"{caption}\".") +
            $"\n\nPick between 1 and {PhotoTags.MaxPerPhoto} tags that clearly apply, from this list only:\n{vocabulary}\n\n" +
            "Also write one plain sentence describing what's in the picture, for use as alt text.";

        var enumValues = new JsonArray(PhotoTags.All.Select(t => (JsonNode)JsonValue.Create(t.Id)!).ToArray());
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
                        // Low detail: one small tile, a fraction of the tokens, and plenty to tag by.
                        ["image_url"] = new JsonObject { ["url"] = DataUrl(jpeg), ["detail"] = "low" }
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
                            ["description"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("tags", "description"),
                        ["additionalProperties"] = false
                    }
                }
            }
        };
        // The reasoning models spend output tokens thinking first; tagging needs none of it.
        // Older chat models reject the parameter, so it's only sent where it's understood.
        if (_opts.TaggingModel.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
            body["reasoning_effort"] = "minimal";

        var json = await PostAsync("/chat/completions", body, ct);
        if (json is null) return none;
        try
        {
            var content = json["choices"]![0]!["message"]!["content"]!.GetValue<string>();
            var parsed = JsonNode.Parse(content)!;
            var tags = parsed["tags"]!.AsArray().Select(t => t?.GetValue<string>() ?? "");
            var alt = parsed["description"]?.GetValue<string>()?.Trim() ?? "";
            return new TagSuggestion(PhotoTags.Clean(tags), alt.Length > 300 ? alt[..300] : alt);
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

    private static ModerationVerdict Unchecked(string reason) => new(false, false, reason, []);

    private static string DataUrl(byte[] jpeg) => "data:image/jpeg;base64," + Convert.ToBase64String(jpeg);
}
