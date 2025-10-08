namespace SportRental.Infrastructure.Domain
{
    public class ContractTemplate
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}




