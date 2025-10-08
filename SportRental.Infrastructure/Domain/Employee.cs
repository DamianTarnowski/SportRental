using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class Employee
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(200)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string City { get; set; } = string.Empty;

        [Required]
        [Phone]
        [MaxLength(20)]
        public string Telephone { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Comment { get; set; }

        [Required]
        [MaxLength(50)]
        public string Position { get; set; } = "Pracownik";

        [Required]
        public EmployeeRole Role { get; set; } = EmployeeRole.Pracownik;

        public int AllRentalsNumber { get; set; } = 0;
        public Guid? UserId { get; set; }
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        // Navigation properties
        public Tenant? Tenant { get; set; }
        public EmployeePermissions? Permissions { get; set; }
    }

    public enum EmployeeRole
    {
        Pracownik = 0,
        Kierownik = 1,
        Manager = 2
    }
}
