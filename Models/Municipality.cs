using System.ComponentModel.DataAnnotations;

namespace BusinessLicensing_Practice.Models;

public class Municipality
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;

    // Stable geographic routing identity; null for administratively added municipalities.
    [StringLength(200)]
    public string? RoutingName { get; set; }
}
