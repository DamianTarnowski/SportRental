using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class TenantUser
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [MaxLength(256)]
        public string? DisplayName { get; set; }

        [MaxLength(64)]
        public string Role { get; set; } = "Employee"; // Owner | Employee
    }
}





