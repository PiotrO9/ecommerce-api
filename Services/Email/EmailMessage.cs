namespace E_commerce_backend.Services.Email;

public sealed record EmailMessage(
    string FromEmail,
    string FromName,
    string ToEmail,
    string Subject,
    string Category,
    string Text);
