using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class Product
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        [MaxLength(256)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Sku { get; set; } = string.Empty;

        public string? Category { get; set; }
        
        [MaxLength(100)]
        public string? Producer { get; set; }
        
        [MaxLength(100)]
        public string? Model { get; set; }
        
        [MaxLength(100)]
        public string? SerialNumber { get; set; }
        
        [MaxLength(1000)]
        public string? Description { get; set; }
        
        public int Type { get; set; } = 11; // Default to "Inne"
        
        public string? ImageUrl { get; set; }
        public string? ImageBasePath { get; set; } // np. images/products/{tenant}/{product}/v1
        public string? ImageAlt { get; set; }
        
        public decimal DailyPrice { get; set; }
        public bool IsActive { get; set; } = true;
        public bool Available { get; set; } = true;
        public bool Disabled { get; set; } = false;
        public bool Rented { get; set; } = false;
        
        public int AvailableQuantity { get; set; } = 1;
        public int HowManyRented { get; set; } = 0;
        
        [MaxLength(500)]
        public string? QrCode { get; set; }
        
        // GPS Coordinates
        public double? Lat { get; set; } = 0;
        public double? Lon { get; set; } = 0;
        
        public Guid? CategoryId { get; set; }
        public string? UserId { get; set; }
        public bool IsDeleted { get; set; } = false;
        
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }
        
        // Navigation properties
        public ProductCategory? ProductCategory { get; set; }
    }
}


