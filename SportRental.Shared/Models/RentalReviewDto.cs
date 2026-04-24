using System.ComponentModel.DataAnnotations;

namespace SportRental.Shared.Models
{
    public class CreateRentalReviewRequest
    {
        [Required]
        public Guid RentalId { get; set; }

        [Range(0, 10)]
        public int QualityScore { get; set; }

        [Range(0, 10)]
        public int PriceScore { get; set; }

        [Range(0, 10)]
        public int ServiceScore { get; set; }

        [StringLength(2000)]
        public string? Comment { get; set; }

        /// <summary>Opcjonalne oceny per sprzęt z wynajmu (mogą być pominięte — wtedy tylko główna opinia).</summary>
        public List<CreateRentalItemReviewRequest> ItemReviews { get; set; } = new();
    }

    public class CreateRentalItemReviewRequest
    {
        [Required]
        public Guid RentalItemId { get; set; }

        [Range(0, 10)]
        public int Rating { get; set; }

        [StringLength(1000)]
        public string? Comment { get; set; }
    }

    public class RentalReviewDto
    {
        public Guid Id { get; set; }
        public Guid RentalId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public int QualityScore { get; set; }
        public int PriceScore { get; set; }
        public int ServiceScore { get; set; }
        public double AverageScore { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public List<RentalItemReviewDto> ItemReviews { get; set; } = new();
    }

    public class RentalItemReviewDto
    {
        public Guid Id { get; set; }
        public Guid RentalItemId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string? Comment { get; set; }
    }

    public class ReviewSummaryDto
    {
        public int Count { get; set; }
        public double AverageQuality { get; set; }
        public double AveragePrice { get; set; }
        public double AverageService { get; set; }
        public double AverageOverall { get; set; }
    }

    public class AdminReviewDto
    {
        public Guid Id { get; set; }
        public Guid RentalId { get; set; }
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? CustomerEmail { get; set; }
        public int QualityScore { get; set; }
        public int PriceScore { get; set; }
        public int ServiceScore { get; set; }
        public double AverageScore { get; set; }
        public string? Comment { get; set; }
        public bool IsHidden { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class UpdateReviewVisibilityRequest
    {
        public bool IsHidden { get; set; }
    }
}
