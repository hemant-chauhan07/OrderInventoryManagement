using System.ComponentModel.DataAnnotations;

namespace OrderInventory.Application.DTOs.Auth
{
    public class RegisterRequestDto
    {
        [Required]
        [StringLength(150, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        [StringLength(128)]
        public string Password { get; set; } = string.Empty;
    }
}
