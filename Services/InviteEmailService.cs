using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using DigitalVisionBoard.Models;

namespace DigitalVisionBoard.Services
{
    public record InviteEmailResult(bool IsConfigured, int SentCount, List<string> Recipients, string? Error);

    public interface IInviteEmailService
    {
        Task<InviteEmailResult> SendBoardInviteAsync(Board board, User sender, IEnumerable<string> recipients, string appBaseUrl);
    }

    public class InviteEmailService : IInviteEmailService
    {
        private readonly IConfiguration _configuration;

        public InviteEmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<InviteEmailResult> SendBoardInviteAsync(Board board, User sender, IEnumerable<string> recipients, string appBaseUrl)
        {
            var host = GetSetting("SMTP_HOST");
            var from = GetSetting("SMTP_FROM") ?? GetSetting("SMTP_USERNAME");
            var username = GetSetting("SMTP_USERNAME");
            var password = GetSetting("SMTP_PASSWORD");
            var portText = GetSetting("SMTP_PORT") ?? "587";
            var useSslText = GetSetting("SMTP_USE_SSL") ?? "true";

            var cleanRecipients = recipients
                .Select(email => email.Trim().ToLowerInvariant())
                .Where(email => !string.IsNullOrWhiteSpace(email))
                .Distinct()
                .ToList();

            if (cleanRecipients.Count == 0)
            {
                return new InviteEmailResult(true, 0, cleanRecipients, "No collaborator emails were provided.");
            }

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new InviteEmailResult(false, 0, cleanRecipients, "SMTP settings are not configured.");
            }

            if (!int.TryParse(portText, out var port))
            {
                port = 587;
            }

            var boardUrl = $"{appBaseUrl.TrimEnd('/')}/?board={Uri.EscapeDataString(board.Id.ToString())}";
            var subject = $"Invitation to collaborate on {board.Title}";
            var body = $"""
                Hello,

                {sender.Name} invited you to collaborate on the Aura Board workspace "{board.Title}".

                Open the board:
                {boardUrl}

                Sign in or register with this email address to access the shared board.
                """;

            try
            {
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = bool.TryParse(useSslText, out var useSsl) ? useSsl : true,
                    Credentials = new NetworkCredential(username, password)
                };

                foreach (var recipient in cleanRecipients)
                {
                    using var message = new MailMessage(from, recipient, subject, body)
                    {
                        IsBodyHtml = false
                    };

                    await client.SendMailAsync(message);
                }

                return new InviteEmailResult(true, cleanRecipients.Count, cleanRecipients, null);
            }
            catch (Exception ex)
            {
                return new InviteEmailResult(true, 0, cleanRecipients, ex.Message);
            }
        }

        private string? GetSetting(string key)
        {
            return Environment.GetEnvironmentVariable(key) ?? _configuration[key];
        }
    }
}
