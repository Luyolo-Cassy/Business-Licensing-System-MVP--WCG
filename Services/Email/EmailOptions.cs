namespace BusinessLicensing_Practice.Services.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string SmtpSecurity { get; set; } = "StartTls";
    public string SmtpUsername { get; set; } = "";
    public string SmtpAppPassword { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Provincial Business Licensing System";
    public int TimeoutSeconds { get; set; } = 15;
}
