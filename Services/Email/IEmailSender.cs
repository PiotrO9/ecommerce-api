namespace E_commerce_backend.Services.Email;

public interface IEmailSender
{
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
