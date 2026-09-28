namespace BusinessLicensing_Practice.Services.Email;

public sealed record EmailMessage(
    string RecipientAddress,
    string RecipientName,
    string Subject,
    string PlainTextBody,
    string HtmlBody);
