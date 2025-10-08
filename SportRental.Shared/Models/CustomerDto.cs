using System.ComponentModel.DataAnnotations;

namespace SportRental.Shared.Models
{
    public class CustomerDto
    {
        public Guid Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;
        
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        [Phone]
        public string PhoneNumber { get; set; } = string.Empty;
        
        [StringLength(200)]
        public string? Address { get; set; }
        
        [StringLength(50)]
        public string? DocumentNumber { get; set; }
        
        public string? Notes { get; set; }
    }
    
    public class CreateCustomerRequest
    {
        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;
        
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
        
        [Required]
        [Phone]
        public string PhoneNumber { get; set; } = string.Empty;
        
        [StringLength(200)]
        public string? Address { get; set; }
        
        [StringLength(50)]
        public string? DocumentNumber { get; set; }
        
        public string? Notes { get; set; }
    }
}