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
                // Fallback details if key is missing (matches server.js fallback)
                return Ok(new
                {
                    analysis = $"We analyzed your board \"{request.Title}\". It looks like you're setting up some wonderful intentions! Set up your Gemini API Key in Settings to get bespoke structured design grids and quotes. Here are universal starting options to expand your vision:",
                    suggestedColorPalette = new List<string> { "#6366f1", "#10b981", "#f59e0b", "#ec4899" },
                    recommendedItems = new List<object>
                    {
                        new
                        {
                            type = "quote",
                            title = "Daily Action Catalyst",
                            content = "Continuous improvement is better than delayed perfection. Expand your board with clear milestones! 🌟",
                            color = "bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800",
                            width = 25,
                            height = 25
                        },
                        new
                        {
                            type = "note",
                            title = "Growth Intentions Checklist",
                            content = "1. Identify one micro-habit you can start today\n2. Block out 15 minutes in your calendar\n3. Share this board with a collaborator",
                            color = "bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800",
                            width = 25,
                            height = 30
                        },
                        new
                        {
                            type = "image",
                            title = "Focus Mindset Symbol",
                            content = "https://images.unsplash.com/photo-1519681393784-d120267933ba?w=800",
                            caption = "Visualizing structured growth and personal development.",
                            width = 30,
                            height = 35
                        }
                    }
                });
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
3. Generate exactly 3 fresh, highly tailored recommendations the user can add to their canvas:
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
                        responseSchema = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                analysis = new { type = "STRING" },
                                suggestedColorPalette = new
                                {
                                    type = "ARRAY",
                                    items = new { type = "STRING" }
                                },
                                recommendedItems = new
                                {
                                    type = "ARRAY",
                                    items = new
                                    {
                                        type = "OBJECT",
                                        properties = new
                                        {
                                            type = new { type = "STRING", description = "Must be 'quote', 'note', 'text', or 'image'" },
                                            title = new { type = "STRING" },
                                            content = new { type = "STRING", description = "The content payload" },
                                            caption = new { type = "STRING" },
                                            color = new { type = "STRING" },
                                            width = new { type = "INTEGER" },
                                            height = new { type = "INTEGER" }
                                        },
                                        required = new[] { "type", "title", "content", "width", "height" }
                                    }
                                }
                            },
                            required = new[] { "analysis", "suggestedColorPalette", "recommendedItems" }
                        }
                    }
                };

                var responseJson = await CallGeminiApiAsync(apiKey, requestBody);
                using var doc = JsonDocument.Parse(responseJson);
                var responseText = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrEmpty(responseText))
                {
                    throw new Exception("Empty response text returned from Gemini API.");
                }

                // Parse the inner JSON text returned from the model
                var result = JsonSerializer.Deserialize<JsonElement>(responseText);
                
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
                        responseSchema = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                theme = new { type = "STRING" },
                                description = new { type = "STRING" },
                                quote = new { type = "STRING" },
                                colorPalette = new
                                {
                                    type = "ARRAY",
                                    items = new { type = "STRING" }
                                },
                                suggestedItems = new
                                {
                                    type = "ARRAY",
                                    items = new
                                    {
                                        type = "OBJECT",
                                        properties = new
                                        {
                                            type = new { type = "STRING" },
                                            title = new { type = "STRING" },
                                            content = new { type = "STRING" },
                                            caption = new { type = "STRING" },
                                            color = new { type = "STRING" },
                                            width = new { type = "INTEGER" },
                                            height = new { type = "INTEGER" }
                                        },
                                        required = new[] { "type", "title", "content", "width", "height" }
                                    }
                                }
                            },
                            required = new[] { "theme", "description", "quote", "colorPalette", "suggestedItems" }
                        }
                    }
                };

                var responseJson = await CallGeminiApiAsync(apiKey, requestBody);
                using var doc = JsonDocument.Parse(responseJson);
                var responseText = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (string.IsNullOrEmpty(responseText))
                {
                    throw new Exception("Empty response text returned from Gemini API.");
                }

                var result = JsonSerializer.Deserialize<JsonElement>(responseText);
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

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Explicit build header matching node studio client
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "aistudio-build");

            var response = await _httpClient.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini HTTP Error: {response.StatusCode} - {errorContent}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        private static object BuildRecommendationFallback(RecommendationsRequest request, string guidance)
        {
            return new
            {
                analysis = $"We analyzed your board \"{request.Title}\". {guidance} Here are starter recommendations you can add right now.",
                suggestedColorPalette = new List<string> { "#c4b5fd", "#a5b4fc", "#f8fafc", "#fbcfe8" },
                recommendedItems = new List<object>
                {
                    new
                    {
                        type = "quote",
                        title = "Daily Action Catalyst",
                        content = "Continuous improvement is better than delayed perfection.",
                        color = "bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800",
                        width = 25,
                        height = 25
                    },
                    new
                    {
                        type = "note",
                        title = "Growth Intentions Checklist",
                        content = "[ ] Identify one micro-habit you can start today\n[ ] Block out 15 minutes in your calendar\n[ ] Share this board with a collaborator",
                        color = "bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800",
                        width = 25,
                        height = 30
                    },
                    new
                    {
                        type = "image",
                        title = "Focus Mindset Symbol",
                        content = "focused workspace soft light growth mindset",
                        caption = "A visual anchor for structured growth and personal momentum.",
                        width = 30,
                        height = 35
                    }
                }
            };
        }

        private static object BuildInspirationFallback(InspirationRequest request, string guidance)
        {
            var theme = string.IsNullOrWhiteSpace(request.Theme) ? "dream board" : request.Theme.Trim();
            var seed = theme.ToLowerInvariant().Aggregate(theme.Length, (sum, ch) => sum + ch);
            var paletteSets = new[]
            {
                new List<string> { "#f8fafc", "#ecfdf5", "#fffbeb", "#fff1f2" },
                new List<string> { "#faf5ff", "#e0f2fe", "#fef3c7", "#dcfce7" },
                new List<string> { "#f5f3ff", "#cffafe", "#fce7f3", "#f7fee7" },
                new List<string> { "#eef2ff", "#f0fdfa", "#fff7ed", "#fdf2f8" }
            };
            var tones = new[] { "soft", "focused", "playful", "minimal", "bright", "calm" };
            var verbs = new[] { "shape", "curate", "build", "anchor", "sketch", "refine" };
            var objects = new[] { "workspace", "mood board", "daily ritual", "visual system", "inspiration corner", "planning desk" };
            var palette = paletteSets[seed % paletteSets.Length];
            var tone = tones[seed % tones.Length];
            var verb = verbs[(seed + 2) % verbs.Length];
            var focalObject = objects[(seed + 4) % objects.Length];

            return new
            {
                theme,
                description = $"Your vision sparks here. {guidance} Aura prepared a local {tone} concept kit for this theme.",
                quote = $"Small details make the vision feel real: {verb} one {focalObject} at a time.",
                colorPalette = palette,
                suggestedItems = new List<object>
                {
                    new
                    {
                        type = "quote",
                        title = $"{theme} Mantra",
                        content = $"Design the next version of \"{theme}\" through one visible detail, one useful habit, and one space that makes starting easy.",
                        color = "bg-emerald-50 dark:bg-emerald-950 border-emerald-200 dark:border-emerald-800",
                        width = 30,
                        height = 25
                    },
                    new
                    {
                        type = "note",
                        title = $"{theme} Actions",
                        content = $"[ ] Save three references for the {tone} look\n[ ] Choose one color and one material cue\n[ ] Add a tiny daily ritual that belongs in this vision",
                        color = "bg-indigo-50 dark:bg-indigo-950 border-indigo-200 dark:border-indigo-800",
                        width = 25,
                        height = 30
                    },
                    new
                    {
                        type = "image",
                        title = $"{tone} {focalObject}",
                        content = $"{theme} {tone} {focalObject} natural light aesthetic",
                        caption = $"A visual direction for {theme}.",
                        width = 40,
                        height = 45
                    }
                }
            };
        }

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
