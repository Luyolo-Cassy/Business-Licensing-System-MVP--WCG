using Microsoft.AspNetCore.Identity;

namespace BusinessLicensing_Practice.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = "";

        public string? Municipality { get; set; }
    }
}