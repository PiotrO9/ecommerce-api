namespace E_commerce_backend.Services.Email;

public sealed class MailtrapEmailSender(HttpClient httpClient) : IEmailSender
{
    public async Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("api/send", new
        {
            from = new { email = message.FromEmail, name = message.FromName },
            to = new[] { new { email = message.ToEmail } },
            subject = message.Subject,
            category = message.Category,
            text = message.Text
        }, cancellationToken);
        return response.IsSuccessStatusCode;
    }
}
