using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class ProductCategory
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        public int SortOrder { get; set; } = 0;
        public bool IsDeleted { get; set; } = false;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        // Navigation properties
        public Tenant? Tenant { get; set; }
        public ICollection<Product>? Products { get; set; }
    }

    public static class ProductCategoryDefaults
    {
        public static readonly Dictionary<int, string> DefaultTypes = new Dictionary<int, string>
        {
            { 0, "Deskorolka" },
            { 1, "Snowboard" },
            { 2, "Narty" },
            { 3, "Buty" },
            { 4, "Kask" },
            { 5, "Kijki" },
            { 6, "Kurtka" },
            { 7, "Spodnie" },
            { 8, "Gogle" },
            { 9, "Rękawice" },
            { 10, "Rowery" },
            { 11, "Inne" }
        };
    }
}
