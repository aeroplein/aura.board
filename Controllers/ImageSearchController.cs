using System.Text.Json;
using DigitalVisionBoard.Data;
using DigitalVisionBoard.Services;
using Microsoft.AspNetCore.Mvc;

namespace DigitalVisionBoard.Controllers
{
    public class ImageSearchController : BaseApiController
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ImageSearchController> _logger;

        public ImageSearchController(
            AppDbContext context,
            AuthService authService,
            IHttpClientFactory httpClientFactory,
            ILogger<ImageSearchController> logger) : base(context, authService)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
        }

        [HttpGet("api/images/search")]
        public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] string? sig, [FromQuery] string? format)
        {
            var cleanedQuery = string.IsNullOrWhiteSpace(query) ? "creative workspace" : query.Trim();
            var accessKey = Environment.GetEnvironmentVariable("UNSPLASH_ACCESS_KEY");
            var wantsJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(accessKey) || accessKey == "YOUR_UNSPLASH_ACCESS_KEY")
            {
                var fallbackUrl = GetFallbackImageUrl(cleanedQuery, sig);
                return wantsJson ? Ok(new { url = fallbackUrl }) : Redirect(fallbackUrl);
            }

            try
            {
                var requestUrl =
                    "https://api.unsplash.com/photos/random" +
                    $"?query={Uri.EscapeDataString(cleanedQuery)}" +
                    "&orientation=landscape" +
                    "&content_filter=high";

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Add("Authorization", $"Client-ID {accessKey}");
                request.Headers.Add("Accept-Version", "v1");

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Unsplash image search failed with status {StatusCode}.", response.StatusCode);
                    return Redirect(GetFallbackImageUrl(cleanedQuery, sig));
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);

                if (document.RootElement.TryGetProperty("urls", out var urls) &&
                    urls.TryGetProperty("regular", out var regularUrl))
                {
                    var imageUrl = regularUrl.GetString();
                    if (!string.IsNullOrWhiteSpace(imageUrl))
                    {
                        return wantsJson ? Ok(new { url = imageUrl }) : Redirect(imageUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unsplash image search failed.");
            }

            var finalFallbackUrl = GetFallbackImageUrl(cleanedQuery, sig);
            return wantsJson ? Ok(new { url = finalFallbackUrl }) : Redirect(finalFallbackUrl);
        }

        private static string GetFallbackImageUrl(string query, string? sig)
        {
            var lower = query.ToLowerInvariant();
            var fallback = lower switch
            {
                var value when ContainsAny(value, "reading", "book", "library", "nook", "sanctuary") =>
                    "https://images.unsplash.com/photo-1495446815901-a7297e633e8d?auto=format&fit=crop&w=1200&q=80",
                var value when ContainsAny(value, "garden", "plant", "green", "sustainable", "urban") =>
                    "https://images.unsplash.com/photo-1466692476868-aef1dfb1e735?auto=format&fit=crop&w=1200&q=80",
                var value when ContainsAny(value, "fitness", "triathlon", "ironman", "endurance", "running") =>
                    "https://images.unsplash.com/photo-1517836357463-d25dfeac3438?auto=format&fit=crop&w=1200&q=80",
                var value when ContainsAny(value, "office", "engineer", "engineering", "developer", "computer", "desk", "tech", "coding") =>
                    "https://images.unsplash.com/photo-1497366811353-6870744d04b2?auto=format&fit=crop&w=1200&q=80",
                var value when ContainsAny(value, "cute", "pastel", "soft", "cozy", "aesthetic") =>
                    "https://images.unsplash.com/photo-1518455027359-f3f8164ba6bd?auto=format&fit=crop&w=1200&q=80",
                var value when ContainsAny(value, "travel", "exploration", "global", "beach", "mountain") =>
                    "https://images.unsplash.com/photo-1500530855697-b586d89ba3ee?auto=format&fit=crop&w=1200&q=80",
                _ => "https://images.unsplash.com/photo-1497366754035-f200968a6e72?auto=format&fit=crop&w=1200&q=80"
            };

            return string.IsNullOrWhiteSpace(sig) ? fallback : $"{fallback}&sig={Uri.EscapeDataString(sig)}";
        }

        private static bool ContainsAny(string value, params string[] terms)
        {
            return terms.Any(value.Contains);
        }
    }
}
