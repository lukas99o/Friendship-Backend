using System.Text;
using System.Text.Json;

namespace Vänskap_Api.Service
{
    public class EmailService : IService.IEmailService
    {
        private readonly HttpClient _httpClient;

        public EmailService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            var apiKey = Environment.GetEnvironmentVariable("BrevoApiKey")
                ?? throw new InvalidOperationException("Brevo API key is not set in environment variables.");

            _httpClient.BaseAddress = new Uri("https://api.brevo.com/");
            _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var payload = new
            {
                sender = new { email = "eventure@lukas99o.com" },
                to = new[] { new { email = toEmail } },
                subject,
                htmlContent = htmlBody
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("v3/smtp/email", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to send email. Status: {response.StatusCode}, Details: {error}");
            }
        }
    }
}