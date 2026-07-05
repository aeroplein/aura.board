using System.Globalization;
using System.Text.Json;
using DigitalVisionBoard.Models;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Options;

namespace DigitalVisionBoard.Services
{
    public record AdvancedEmailValidationResult(bool IsValid, string? Error);

    public class AdvancedEmailValidationException : InvalidOperationException
    {
        public AdvancedEmailValidationException(string message)
            : base(message)
        {
        }
    }

    public class AdvancedEmailValidationSettings
    {
        public bool Enabled { get; set; } = true;
        public bool RequireMxRecord { get; set; } = true;
        public bool BlockDisposableDomains { get; set; } = true;
        public int DnsTimeoutSeconds { get; set; } = 5;
        public string? DisposableDomainsUrl { get; set; }
        public List<string> BlockedDomains { get; set; } = new();
        public List<string> DisposableDomains { get; set; } = new();
    }

    public interface IAdvancedEmailValidator
    {
        Task<AdvancedEmailValidationResult> ValidateAsync(string email, CancellationToken cancellationToken = default);
    }

    public class AdvancedEmailValidator : IAdvancedEmailValidator
    {
        private static readonly HashSet<string> DefaultDisposableDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "10minutemail.com",
            "10minutemail.net",
            "20minutemail.com",
            "anonbox.net",
            "dispostable.com",
            "fakeinbox.com",
            "fakemail.net",
            "getnada.com",
            "grr.la",
            "guerrillamail.com",
            "guerrillamail.net",
            "maildrop.cc",
            "mailinator.com",
            "mailnesia.com",
            "moakt.com",
            "sharklasers.com",
            "temp-mail.org",
            "tempmail.com",
            "throwawaymail.com",
            "trashmail.com",
            "yopmail.com"
        };

        private readonly AdvancedEmailValidationSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AdvancedEmailValidator> _logger;
        private readonly Lazy<LookupClient> _lookupClient;
        private HashSet<string>? _cachedDisposableDomains;
        private DateTimeOffset _cacheExpiresAt = DateTimeOffset.MinValue;

        public AdvancedEmailValidator(
            IOptions<AdvancedEmailValidationSettings> options,
            IHttpClientFactory httpClientFactory,
            ILogger<AdvancedEmailValidator> logger)
        {
            _settings = options.Value;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _lookupClient = new Lazy<LookupClient>(() => new LookupClient(new LookupClientOptions
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(1, _settings.DnsTimeoutSeconds)),
                UseCache = true
            }));
        }

        public async Task<AdvancedEmailValidationResult> ValidateAsync(string email, CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
            {
                return new AdvancedEmailValidationResult(true, null);
            }

            if (!StrictEmailValidator.IsValid(email))
            {
                return new AdvancedEmailValidationResult(false, "Please use a valid email address.");
            }

            var domain = ExtractAsciiDomain(email);
            if (string.IsNullOrWhiteSpace(domain))
            {
                return new AdvancedEmailValidationResult(false, "Please use a valid email address.");
            }

            if (IsBlockedDomain(domain))
            {
                return new AdvancedEmailValidationResult(false, "Please use a different email address.");
            }

            if (_settings.RequireMxRecord && !await HasMxRecordAsync(domain, cancellationToken))
            {
                return new AdvancedEmailValidationResult(false, "Please use a different email address.");
            }

            if (_settings.BlockDisposableDomains && await IsDisposableDomainAsync(domain, cancellationToken))
            {
                return new AdvancedEmailValidationResult(false, "Please use a permanent email address.");
            }

            return new AdvancedEmailValidationResult(true, null);
        }

        private async Task<bool> HasMxRecordAsync(string domain, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _lookupClient.Value.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);
                return result.Answers.MxRecords().Any(mx => !string.IsNullOrWhiteSpace(mx.Exchange.Value));
            }
            catch (Exception ex) when (ex is DnsResponseException or OperationCanceledException or TimeoutException)
            {
                _logger.LogWarning(ex, "MX lookup failed for email domain {Domain}", domain);
                return false;
            }
        }

        private async Task<bool> IsDisposableDomainAsync(string domain, CancellationToken cancellationToken)
        {
            var domains = await GetDisposableDomainsAsync(cancellationToken);
            return domains.Contains(domain);
        }

        private bool IsBlockedDomain(string domain)
        {
            return _settings.BlockedDomains.Any(blockedDomain =>
                string.Equals(
                    blockedDomain.Trim().TrimStart('@').TrimEnd('.'),
                    domain,
                    StringComparison.OrdinalIgnoreCase));
        }

        private async Task<HashSet<string>> GetDisposableDomainsAsync(CancellationToken cancellationToken)
        {
            if (_cachedDisposableDomains != null && _cacheExpiresAt > DateTimeOffset.UtcNow)
            {
                return _cachedDisposableDomains;
            }

            var domains = new HashSet<string>(DefaultDisposableDomains, StringComparer.OrdinalIgnoreCase);
            foreach (var configuredDomain in _settings.DisposableDomains)
            {
                AddDomain(domains, configuredDomain);
            }

            if (!string.IsNullOrWhiteSpace(_settings.DisposableDomainsUrl))
            {
                await LoadRemoteDisposableDomainsAsync(domains, cancellationToken);
            }

            _cachedDisposableDomains = domains;
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddHours(12);
            return domains;
        }

        private async Task LoadRemoteDisposableDomainsAsync(HashSet<string> domains, CancellationToken cancellationToken)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient();
                using var response = await client.GetAsync(_settings.DisposableDomainsUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Disposable email domain list returned status {StatusCode}", response.StatusCode);
                    return;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var remoteDomains = await JsonSerializer.DeserializeAsync<List<string>>(stream, cancellationToken: cancellationToken);
                if (remoteDomains == null)
                {
                    return;
                }

                foreach (var domain in remoteDomains)
                {
                    AddDomain(domains, domain);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                _logger.LogWarning(ex, "Disposable email domain list could not be loaded. Built-in list remains active.");
            }
        }

        private static string ExtractAsciiDomain(string email)
        {
            var domain = email.Trim().Split('@').LastOrDefault();
            if (string.IsNullOrWhiteSpace(domain))
            {
                return string.Empty;
            }

            return new IdnMapping().GetAscii(domain.Trim().TrimEnd('.')).ToLowerInvariant();
        }

        private static void AddDomain(HashSet<string> domains, string? domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return;
            }

            domains.Add(domain.Trim().TrimStart('@').TrimEnd('.').ToLowerInvariant());
        }
    }
}
