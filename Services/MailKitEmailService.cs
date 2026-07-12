using DigitalVisionBoard.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DigitalVisionBoard.Services
{
    public class MailKitEmailService : IEmailService
    {
        private readonly MailSettings _settings;
        private readonly ILogger<MailKitEmailService> _logger;

        public MailKitEmailService(IOptions<MailSettings> options, ILogger<MailKitEmailService> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            EnsureConfigured();

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder
            {
                HtmlBody = htmlBody
            }.ToMessageBody();

            using var client = new SmtpClient();
            var socketOptions = _settings.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            var timeoutToken = timeoutSource.Token;

            _logger.LogInformation(
                "SMTP verification send starting. Host {Host}; Port {Port}; Security {Security}; RecipientDomain {RecipientDomain}; TimeoutSeconds {TimeoutSeconds}",
                _settings.Host, _settings.Port, socketOptions, GetEmailDomain(to), _settings.TimeoutSeconds);

            try
            {
                await client.ConnectAsync(_settings.Host, _settings.Port, socketOptions, timeoutToken);
                _logger.LogInformation("SMTP connection established. Host {Host}; Port {Port}", _settings.Host, _settings.Port);

                await client.AuthenticateAsync(_settings.Username, _settings.Password, timeoutToken);
                _logger.LogInformation("SMTP authentication succeeded. Host {Host}", _settings.Host);

                await client.SendAsync(message, timeoutToken);
                _logger.LogInformation("SMTP message accepted. Host {Host}; RecipientDomain {RecipientDomain}", _settings.Host, GetEmailDomain(to));

                await client.DisconnectAsync(true, CancellationToken.None);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(
                    "SMTP operation timed out after {TimeoutSeconds} seconds. Host {Host}; Port {Port}; LastStage {LastStage}",
                    _settings.TimeoutSeconds, _settings.Host, _settings.Port, GetLastStage(client));
                throw new TimeoutException($"SMTP operation timed out after {_settings.TimeoutSeconds} seconds.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "SMTP operation failed. Host {Host}; Port {Port}; LastStage {LastStage}; ExceptionType {ExceptionType}",
                    _settings.Host, _settings.Port, GetLastStage(client), ex.GetType().Name);
                throw;
            }
        }

        private void EnsureConfigured()
        {
            if (string.IsNullOrWhiteSpace(_settings.Host) ||
                string.IsNullOrWhiteSpace(_settings.Username) ||
                string.IsNullOrWhiteSpace(_settings.Password) ||
                string.IsNullOrWhiteSpace(_settings.FromEmail))
            {
                throw new InvalidOperationException("MailSettings must include Host, Username, Password, and FromEmail.");
            }
        }

        private static string GetEmailDomain(string email)
        {
            var separator = email.LastIndexOf('@');
            return separator >= 0 && separator < email.Length - 1 ? email[(separator + 1)..] : "invalid";
        }

        private static string GetLastStage(SmtpClient client) => client.IsAuthenticated
            ? "send"
            : client.IsConnected
                ? "authenticate"
                : "connect";
    }
}
