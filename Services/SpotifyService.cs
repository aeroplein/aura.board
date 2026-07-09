using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DigitalVisionBoard.Models;

namespace DigitalVisionBoard.Services
{
    public class SpotifyService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SpotifyService> _logger;
        private string? _accessToken;
        private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

        public SpotifyService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<SpotifyService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<List<SpotifyTrackSearchResult>> SearchTracksAsync(string query, CancellationToken cancellationToken = default)
        {
            var cleanedQuery = query.Trim();
            if (cleanedQuery.Length < 2)
            {
                return new List<SpotifyTrackSearchResult>();
            }

            var token = await GetAccessTokenAsync(cancellationToken);
            var client = _httpClientFactory.CreateClient();
            var requestUrl =
                "https://api.spotify.com/v1/search" +
                $"?q={Uri.EscapeDataString(cleanedQuery)}" +
                "&type=track" +
                "&market=US" +
                "&limit=8";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Spotify search failed with status {StatusCode}.", response.StatusCode);
                throw new InvalidOperationException("Spotify search failed. Please try again in a moment.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("tracks", out var tracks) ||
                !tracks.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
            {
                return new List<SpotifyTrackSearchResult>();
            }

            var results = new List<SpotifyTrackSearchResult>();
            foreach (var item in items.EnumerateArray())
            {
                var id = GetString(item, "id");
                var name = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var artist = GetFirstArtistName(item) ?? "Unknown artist";
                var album = GetAlbumName(item);
                var imageUrl = GetAlbumImageUrl(item);
                var spotifyUrl = GetSpotifyUrl(item) ?? $"https://open.spotify.com/track/{id}";

                results.Add(new SpotifyTrackSearchResult(
                    id,
                    name,
                    artist,
                    album,
                    imageUrl,
                    spotifyUrl,
                    $"https://open.spotify.com/embed/track/{id}?utm_source=generator"
                ));
            }

            return results;
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken;
            }

            var clientId = FirstConfigured("Spotify:ClientId", "SPOTIFY_CLIENT_ID");
            var clientSecret = FirstConfigured("Spotify:ClientSecret", "SPOTIFY_CLIENT_SECRET");

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Spotify API credentials are not configured.");
            }

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://accounts.spotify.com/api/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Spotify token request failed with status {StatusCode}.", response.StatusCode);
                throw new InvalidOperationException("Spotify API credentials were rejected.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            _accessToken = GetString(document.RootElement, "access_token")
                ?? throw new InvalidOperationException("Spotify token response did not include an access token.");
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresElement)
                ? expiresElement.GetInt32()
                : 3600;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn));

            return _accessToken;
        }

        private string? FirstConfigured(string configurationKey, string environmentKey)
        {
            var configured = _configuration[configurationKey];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var fromEnvironment = Environment.GetEnvironmentVariable(environmentKey);
            return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment;
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static string? GetSpotifyUrl(JsonElement item)
        {
            return item.TryGetProperty("external_urls", out var externalUrls)
                ? GetString(externalUrls, "spotify")
                : null;
        }

        private static string? GetFirstArtistName(JsonElement item)
        {
            if (!item.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var firstArtist = artists.EnumerateArray().FirstOrDefault();
            return firstArtist.ValueKind == JsonValueKind.Object ? GetString(firstArtist, "name") : null;
        }

        private static string? GetAlbumName(JsonElement item)
        {
            return item.TryGetProperty("album", out var album) ? GetString(album, "name") : null;
        }

        private static string? GetAlbumImageUrl(JsonElement item)
        {
            if (!item.TryGetProperty("album", out var album) ||
                !album.TryGetProperty("images", out var images) ||
                images.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var image in images.EnumerateArray())
            {
                var url = GetString(image, "url");
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }

            return null;
        }
    }
}
