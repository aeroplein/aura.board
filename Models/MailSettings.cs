namespace DigitalVisionBoard.Models
{
    public class MailSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = "Digital Vision Board";
        public bool UseSsl { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 20;
        public string AppBaseUrl { get; set; } = "http://localhost:5000";
    }
}
