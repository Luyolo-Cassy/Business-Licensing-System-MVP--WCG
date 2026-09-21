namespace BusinessLicensing_Practice.Models;

public class AiSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool DocumentValidationEnabled { get; set; }
}
