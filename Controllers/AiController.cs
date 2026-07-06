using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Models;
using DigitalVisionBoard.Services;

namespace DigitalVisionBoard.Controllers
{
    [Route("api")]
    public class AiController : BaseApiController
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AiController> _logger;

        public AiController(AppDbContext context, AuthService authService, IHttpClientFactory httpClientFactory, ILogger<AiController> logger)
            : base(context, authService)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
        }

        [HttpPost("board/recommendations")]
        public async Task<IActionResult> GetRecommendations([FromBody] RecommendationsRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey) || apiKey == "MY_GEMINI_API_KEY")
            {
                _logger.LogWarning("Gemini API key is not configured; returning local board recommendation fallback for user {UserId}", user.Id);
                return Ok(BuildRecommendationFallback(request, "Gemini is unavailable, so Aura built a local set that still matches this board's mood."));
            }

            try
            {
                string itemsContext = string.Join("\n", request.Items.Select(it =>
                    $"- [{it.Type.ToUpper()}] \"{it.Title}\": {it.Content} {(string.IsNullOrEmpty(it.Caption) ? "" : $"(Caption: {it.Caption})")}"
                ));

                string promptText = $@"You are an expert personal alignment coach, visual consultant, and aesthetic coordinator.
You will analyze the current elements on a user's Digital Vision Board to recommend new, highly relevant inspirational content (quotes, actionable tasks, color palettes, and image search concepts) to help the user expand their vision.

Board Context:
- Title: ""{request.Title}""
- Description: ""{request.Description ?? "None"}""
- Category: ""{request.Category ?? "General"}""

Current items on this board:
{(string.IsNullOrEmpty(itemsContext) ? "No items added yet." : itemsContext)}

Your task is to:
1. Provide an inspiring, encouraging analysis paragraph summarizing the core themes/moods observed on this board and explain how the recommended elements will help them expand their vision. (Make it max 3 sentences, warm and hyper-focused).
2. Recommend 3 to 4 hex color codes that extend or beautifully contrast with the board's vibe.
3. Generate exactly 3 fresh, highly tailored recommendations the user can add to their canvas. Avoid generic productivity advice, hustle language, and vague self-improvement slogans. If the board is about lifestyle, home, travel, fitness, money, study, creativity, or personal wellbeing, make the content feel native to that theme:
   - 1 quote element (deeply aligned quote text in ""content"" and the person who said it in ""caption"")
   - 1 note element (a 3-step actionable, specific checklist task list relevant to the current board's themes; every checklist step must be on its own line)
   - 1 image element (with a highly descriptive search query for Unsplash in ""content"" to represent the target vision, and an aesthetic photo motivation caption in ""caption"")

Return a JSON object matching this schema exactly:
{{
  ""analysis"": ""the analysis paragraph"",
  ""suggestedColorPalette"": [""#hex1"", ""#hex2"", ""#hex3""],
  ""recommendedItems"": [
     {{
        ""type"": ""quote"" | ""note"" | ""text"" | ""image"",
        ""title"": ""Short title label of the recommendation"",
        ""content"": ""For quote type: write the quote only, without author. For note type: exactly 3 actionable checklist lines separated by newline characters, formatted like '[ ] First step\n[ ] Second step\n[ ] Third step'. For image type: a highly descriptive keyword search concept to look up (e.g. 'cozy modern log cabin fireplace morning')"",
        ""caption"": ""For quote type: the quote author. For image type: a short tagline motivation caption (optional)"",
        ""color"": ""bg-indigo-50 border-indigo-200"" | ""bg-amber-50 border-amber-200"" | ""bg-rose-50 border-rose-200"" | ""bg-emerald-50 border-emerald-200"" | ""bg-cyan-50 border-cyan-200"",
        ""width"": 25,
        ""height"": 28
     }}
  ]
}}
Ensure valid JSON output. Output nothing else.";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = promptText } } }
                    },
                    generationConfig = new
                    {
                        responseMimeType = "application/json",
                        temperature = 0.8
                    }
                };

                var responseJson = await CallGeminiApiAsync(apiKey, requestBody);
                var responseText = ExtractGeminiResponseText(responseJson);

                if (string.IsNullOrEmpty(responseText))
                {
                    throw new Exception("Empty response text returned from Gemini API.");
                }

                var result = ParseModelJsonObject(responseText);
                
                // Preserve image keywords so the client can resolve them to stable photo URLs.
                var outputData = MapRecommendedItems(result);
                return Content(JsonSerializer.Serialize(outputData), "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini board recommendation API failed.");
                return Ok(BuildRecommendationFallback(request, "Live AI suggestions are unavailable, so Aura prepared a local starter set instead."));
            }
        }

        [HttpPost("inspiration")]
        public async Task<IActionResult> GetInspiration([FromBody] InspirationRequest request)
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
            {
                return Unauthorized(new { error = "Unauthorized: Invalid or expired token." });
            }

            string? apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            if (string.IsNullOrEmpty(apiKey) || apiKey == "MY_GEMINI_API_KEY")
            {
                _logger.LogWarning("Gemini API key is not configured; returning local inspiration fallback for user {UserId}", user.Id);
                // Fallback details if key is missing (matches server.js fallback)
                return Ok(new
                {
                    theme = request.Theme,
                    description = "Your vision sparks here! Connect your Gemini API Key in Settings to get bespoke structured design grids and quotes.",
                    quote = "Action is the foundational key to all success. – Pablo Picasso",
                    colorPalette = new List<string> { "#f8fafc", "#ecfdf5", "#fffbeb", "#fff1f2" },
                    suggestedItems = new List<object>
                    {
                        new
                        {
                            type = "quote",
                            title = "Aspiration Statement",
                            content = $"Build an amazing path toward: \"{request.Theme}\". Visualise every single milestone.",
                            color = "bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800",
                            width = 30,
                            height = 25
                        },
                        new
                        {
                            type = "note",
                            title = "Key Intentions",
                            content = "1. Commit to 15m focus\n2. Design the mood map\n3. Capture reference images",
                            color = "bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800",
                            width = 25,
                            height = 30
                        },
                        new
                        {
                            type = "image",
                            title = "Theme Focal Point",
                            content = "https://images.unsplash.com/photo-1498050108023-c5249f4df085?w=800",
                            caption = $"Inspiration search term for dream matching: {request.Theme}",
                            width = 40,
                            height = 45
                        }
                    }
                });
            }

            try
            {
                string promptText = $@"Analyze this vision board theme: ""{request.Theme}"". Generate a structured JSON response to help the user populate their digital vision board.
Create a response matching this schema:
{{
  ""theme"": ""the exact title provided"",
  ""description"": ""A encouraging, descriptive paragraph summarizing the emotional target and aesthetic visual directions of this theme (2-3 sentences max)"",
  ""quote"": ""A powerful quote (with author if possible) that fits the theme"",
  ""colorPalette"": [""3 to 4 hexadecimal primary color hex codes fits the mood style""],
  ""suggestedItems"": [
     {{
        ""type"": ""quote"" | ""note"" | ""text"" | ""image"",
        ""title"": ""Short title label"",
        ""content"": ""For type 'quote': quote text only, without author. For 'note': exactly 3 actionable checklist lines separated by newline characters, formatted like '[ ] First step\n[ ] Second step\n[ ] Third step'. For 'text': descriptive insight. For 'image': a highly descriptive keyword matching this aspect of the theme to lookup (e.g., 'cozy scandinavian cabin interior')"",
        ""caption"": ""For quote type: the quote author. For image type: brief 1-sentence photo motivation caption description (optional)"",
        ""color"": ""bg-indigo-50 border-indigo-200"" | ""bg-amber-50 border-amber-200"" | ""bg-rose-50 border-rose-200"" | ""bg-emerald-50 border-emerald-200"" | ""bg-cyan-50 border-cyan-200"",
        ""width"": 30,
        ""height"": 25
     }}
  ]
}}
Include exactly 3 to 4 suggested items mapping quote, note, and image prompts. Ensure valid, beautifully structured JSON. Do not include markdown wraps or anything except the raw JSON.";

                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = promptText } } }
                    },
                    generationConfig = new
                    {
                        responseMimeType = "application/json",
                        temperature = 0.8
                    }
                };

                var responseJson = await CallGeminiApiAsync(apiKey, requestBody);
                var responseText = ExtractGeminiResponseText(responseJson);

                if (string.IsNullOrEmpty(responseText))
                {
                    throw new Exception("Empty response text returned from Gemini API.");
                }

                var result = ParseModelJsonObject(responseText);
                var outputData = MapSuggestedItems(result);

                return Content(JsonSerializer.Serialize(outputData), "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini inspiration API failed.");
                return Ok(BuildInspirationFallback(request, "Live AI generation is unavailable, so Aura prepared local inspiration instead."));
            }
        }

        // Native HTTP Call to Google Gemini API Endpoint
        private async Task<string> CallGeminiApiAsync(string apiKey, object payload)
        {
            var model = Environment.GetEnvironmentVariable("GEMINI_MODEL");
            if (string.IsNullOrWhiteSpace(model))
            {
                model = "gemini-2.5-flash";
            }

            model = model.Trim();
            if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                model = model["models/".Length..];
            }

            var json = JsonSerializer.Serialize(payload);
            var modelsToTry = GetGeminiModelsToTry(model);

            // Explicit build header matching node studio client
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "aistudio-build");

            Exception? lastException = null;
            foreach (var modelToTry in modelsToTry)
            {
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelToTry}:generateContent?key={apiKey}";
                    var response = await _httpClient.PostAsync(url, content);

                    if (response.IsSuccessStatusCode)
                    {
                        if (!string.Equals(modelToTry, model, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Gemini request succeeded with fallback model {Model}", modelToTry);
                        }

                        return await response.Content.ReadAsStringAsync();
                    }

                    lastException = new Exception($"Gemini HTTP Error: {response.StatusCode} using {modelToTry}");
                    if (!IsTransientGeminiStatus(response.StatusCode))
                    {
                        throw lastException;
                    }

                    if (attempt < 2)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt));
                    }
                }
            }

            throw lastException ?? new Exception("Gemini HTTP Error: no model attempts were made.");
        }

        private static List<string> GetGeminiModelsToTry(string primaryModel)
        {
            var models = new List<string> { primaryModel };
            var fallbackModel = Environment.GetEnvironmentVariable("GEMINI_FALLBACK_MODEL");
            if (!string.IsNullOrWhiteSpace(fallbackModel))
            {
                models.Add(NormalizeGeminiModelName(fallbackModel));
            }

            models.Add("gemini-2.5-flash-lite");
            return models
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeGeminiModelName(string model)
        {
            model = model.Trim();
            return model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                ? model["models/".Length..]
                : model;
        }

        private static bool IsTransientGeminiStatus(System.Net.HttpStatusCode statusCode)
        {
            return statusCode == System.Net.HttpStatusCode.RequestTimeout ||
                statusCode == System.Net.HttpStatusCode.TooManyRequests ||
                statusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                statusCode == System.Net.HttpStatusCode.GatewayTimeout;
        }

        private static string? ExtractGeminiResponseText(string responseJson)
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.ValueKind == JsonValueKind.Array &&
                candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array)
                {
                    var textParts = parts.EnumerateArray()
                        .Where(part => part.TryGetProperty("text", out _))
                        .Select(part => part.GetProperty("text").GetString())
                        .Where(text => !string.IsNullOrWhiteSpace(text));

                    return string.Join("", textParts);
                }
            }

            return root.ValueKind == JsonValueKind.Object ? root.GetRawText() : null;
        }

        private static JsonElement ParseModelJsonObject(string responseText)
        {
            var cleaned = responseText.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                cleaned = cleaned.Trim('`').Trim();
                if (cleaned.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[4..].Trim();
                }
            }

            var firstBrace = cleaned.IndexOf('{');
            var lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleaned = cleaned[firstBrace..(lastBrace + 1)];
            }

            using var doc = JsonDocument.Parse(cleaned);
            return doc.RootElement.Clone();
        }

        private static object BuildRecommendationFallback(RecommendationsRequest request, string guidance)
        {
            var theme = BuildThemeProfile(request.Title, request.Description, request.Category);

            return new
            {
                analysis = $"We analyzed \"{request.Title}\". {guidance} These cards are tuned for {theme.Summary}, so they should feel more like material for this board than generic advice.",
                suggestedColorPalette = theme.Palette,
                recommendedItems = new List<object>
                {
                    new
                    {
                        type = "quote",
                        title = theme.QuoteTitle,
                        content = theme.Quote,
                        caption = theme.QuoteCaption,
                        color = theme.QuoteColor,
                        width = 30,
                        height = 25
                    },
                    new
                    {
                        type = "note",
                        title = theme.NoteTitle,
                        content = theme.Note,
                        color = theme.NoteColor,
                        width = 30,
                        height = 30
                    },
                    new
                    {
                        type = "image",
                        title = theme.ImageTitle,
                        content = theme.ImageQuery,
                        caption = theme.ImageCaption,
                        width = 38,
                        height = 35
                    }
                }
            };
        }

        private static object BuildInspirationFallback(InspirationRequest request, string guidance)
        {
            var theme = string.IsNullOrWhiteSpace(request.Theme) ? "dream board" : request.Theme.Trim();
            var profile = BuildThemeProfile(theme, null, null);
            var seed = theme.ToLowerInvariant().Aggregate(theme.Length, (sum, ch) => sum + ch);
            var tones = new[] { "cinematic", "soft", "warm", "clean", "dreamy", "grounded" };
            var palette = profile.Palette;
            var tone = tones[seed % tones.Length];

            return new
            {
                theme,
                description = $"Your vision sparks here. {guidance} Aura prepared a local {tone} kit for {profile.Summary}.",
                quote = profile.Quote,
                colorPalette = palette,
                suggestedItems = new List<object>
                {
                    new
                    {
                        type = "quote",
                        title = profile.QuoteTitle,
                        content = profile.Quote,
                        caption = profile.QuoteCaption,
                        color = profile.QuoteColor,
                        width = 30,
                        height = 25
                    },
                    new
                    {
                        type = "note",
                        title = profile.NoteTitle,
                        content = profile.Note,
                        color = profile.NoteColor,
                        width = 30,
                        height = 30
                    },
                    new
                    {
                        type = "image",
                        title = profile.ImageTitle,
                        content = profile.ImageQuery,
                        caption = profile.ImageCaption,
                        width = 40,
                        height = 45
                    }
                }
            };
        }

        private static ThemeProfile BuildThemeProfile(string? title, string? description, string? category)
        {
            var combined = $"{title} {description} {category}".ToLowerInvariant();
            var displayTitle = string.IsNullOrWhiteSpace(title) ? "this board" : title.Trim();

            if (ContainsAny(combined, "love", "life", "personal", "glow", "wellbeing", "confidence", "dream"))
            {
                return new ThemeProfile(
                    "personal lifestyle, confidence, and a softer daily atmosphere",
                    new List<string> { "#f8c8dc", "#c8b6ff", "#fff7ed", "#f3e8ff" },
                    "Soft Life Cue",
                    "I am allowed to become the calmest, clearest version of myself.",
                    "Aura lifestyle mantra",
                    "Mood Reset Checklist",
                    "[ ] Add one image that represents the energy you want to wake up with\n[ ] Add one texture, color, or room detail that makes the board feel personal\n[ ] Add one small ritual that supports this version of your day",
                    "Soft Lifestyle Anchor",
                    "soft morning apartment journal flowers coffee pastel natural light lifestyle",
                    "A visual cue for a calmer, more intentional daily atmosphere.",
                    "bg-rose-50 dark:bg-pink-950 border-rose-200 dark:border-pink-800",
                    "bg-purple-50 dark:bg-purple-950 border-purple-200 dark:border-purple-800"
                );
            }

            if (ContainsAny(combined, "travel", "city", "country", "explore", "trip", "global", "beach", "mountain"))
            {
                return new ThemeProfile(
                    "travel, movement, and collecting new memories",
                    new List<string> { "#bfdbfe", "#fde68a", "#bbf7d0", "#f8fafc" },
                    "Next Stamp Energy",
                    "The world gets bigger every time I say yes to the next doorway.",
                    "Aura travel mantra",
                    "Trip Texture Checklist",
                    "[ ] Add one city or landscape you want to wake up in\n[ ] Add one food, sound, or street detail from that place\n[ ] Add the first tiny planning step that makes it real",
                    "Destination Mood",
                    "sunlit european street cafe travel morning film photography",
                    "A visual anchor for the version of you who keeps moving.",
                    "bg-blue-50 dark:bg-indigo-950 border-blue-200 dark:border-indigo-800",
                    "bg-amber-50 dark:bg-amber-950 border-amber-200 dark:border-amber-800"
                );
            }

            if (ContainsAny(combined, "fitness", "run", "gym", "triathlon", "ironman", "health", "body", "strength"))
            {
                return new ThemeProfile(
                    "discipline, body confidence, and athletic momentum",
                    new List<string> { "#dcfce7", "#cffafe", "#f8fafc", "#e0e7ff" },
                    "Strong Body Cue",
                    "I do not need perfect motivation; I need one kept promise.",
                    "Aura training mantra",
                    "Training Board Add-ons",
                    "[ ] Add one image of the exact environment you train in\n[ ] Add one measurable milestone for this month\n[ ] Add one recovery ritual that makes consistency sustainable",
                    "Training Atmosphere",
                    "morning run athletic training sunlight minimalist wellness aesthetic",
                    "A clean visual cue for strength that feels repeatable.",
                    "bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800",
                    "bg-cyan-50 dark:bg-cyan-950 border-cyan-200 dark:border-cyan-800"
                );
            }

            if (ContainsAny(combined, "money", "finance", "career", "business", "job", "study", "school", "exam", "portfolio"))
            {
                return new ThemeProfile(
                    "ambition, stability, and building a future with receipts",
                    new List<string> { "#fef3c7", "#dbeafe", "#ecfdf5", "#faf5ff" },
                    "Future Proof",
                    "I am building evidence, not waiting for permission.",
                    "Aura ambition mantra",
                    "Proof Stack",
                    "[ ] Add one visible symbol of the role or life you are building\n[ ] Add one concrete outcome you can finish this week\n[ ] Add one reminder of why this future matters to you",
                    "Ambition Desk",
                    "clean desk laptop notebook morning light career goals aesthetic",
                    "A visual cue for focused, credible momentum.",
                    "bg-amber-50 dark:bg-amber-950 border-amber-200 dark:border-amber-800",
                    "bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800"
                );
            }

            return new ThemeProfile(
                $"the mood and intention behind \"{displayTitle}\"",
                new List<string> { "#c4b5fd", "#a5b4fc", "#f8fafc", "#fbcfe8" },
                "Board Mantra",
                $"I can make \"{displayTitle}\" visible through one honest detail at a time.",
                "Aura affirmation",
                "Make It Specific",
                "[ ] Add one object that belongs in this exact vision\n[ ] Add one sentence that explains why it matters\n[ ] Add one image with a clear place, texture, or mood",
                "Mood Anchor",
                $"{displayTitle} aesthetic natural light mood board detail",
                "A specific visual anchor for this board's atmosphere.",
                "bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800",
                "bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800"
            );
        }

        private static bool ContainsAny(string source, params string[] terms)
        {
            return terms.Any(source.Contains);
        }

        private sealed record ThemeProfile(
            string Summary,
            List<string> Palette,
            string QuoteTitle,
            string Quote,
            string QuoteCaption,
            string NoteTitle,
            string Note,
            string ImageTitle,
            string ImageQuery,
            string ImageCaption,
            string QuoteColor,
            string NoteColor
        );

        // Maps recommended items while preserving image keywords for the client resolver
        private static object MapRecommendedItems(JsonElement element)
        {
            var analysis = element.GetProperty("analysis").GetString();
            var colors = element.GetProperty("suggestedColorPalette").EnumerateArray().Select(x => x.GetString()).ToList();
            var itemsList = new List<object>();

            foreach (var item in element.GetProperty("recommendedItems").EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                var content = item.GetProperty("content").GetString() ?? "";
                var caption = item.TryGetProperty("caption", out var capVal) ? capVal.GetString() : "";
                var title = item.GetProperty("title").GetString();
                var color = item.TryGetProperty("color", out var colVal) ? colVal.GetString() : "";
                var width = item.GetProperty("width").GetInt32();
                var height = item.GetProperty("height").GetInt32();

                itemsList.Add(new
                {
                    type,
                    title,
                    content,
                    caption,
                    color,
                    width,
                    height
                });
            }

            return new
            {
                analysis,
                suggestedColorPalette = colors,
                recommendedItems = itemsList
            };
        }

        // Maps suggested items while preserving image keywords for the client resolver
        private static object MapSuggestedItems(JsonElement element)
        {
            var theme = element.GetProperty("theme").GetString();
            var description = element.GetProperty("description").GetString();
            var quote = element.GetProperty("quote").GetString();
            var colors = element.GetProperty("colorPalette").EnumerateArray().Select(x => x.GetString()).ToList();
            var itemsList = new List<object>();

            foreach (var item in element.GetProperty("suggestedItems").EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                var content = item.GetProperty("content").GetString() ?? "";
                var caption = item.TryGetProperty("caption", out var capVal) ? capVal.GetString() : "";
                var title = item.GetProperty("title").GetString();
                var color = item.TryGetProperty("color", out var colVal) ? colVal.GetString() : "";
                var width = item.GetProperty("width").GetInt32();
                var height = item.GetProperty("height").GetInt32();

                itemsList.Add(new
                {
                    type,
                    title,
                    content,
                    caption,
                    color,
                    width,
                    height
                });
            }

            return new
            {
                theme,
                description,
                quote,
                colorPalette = colors,
                suggestedItems = itemsList
            };
        }
    }
}
