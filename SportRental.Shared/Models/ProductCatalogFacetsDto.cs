namespace SportRental.Shared.Models;

public sealed class ProductCatalogFacetsDto
{
    public List<string> Categories { get; set; } = [];
    public List<ProductCatalogLocationFacetDto> Locations { get; set; } = [];
    public List<ProductCatalogTenantFacetDto> Tenants { get; set; } = [];
    public int TotalCount { get; set; }
    public int AvailableCount { get; set; }
    public decimal MinimumPrice { get; set; }
    public decimal MaximumPrice { get; set; }
    public decimal AveragePrice { get; set; }
}

public sealed class ProductCatalogLocationFacetDto
{
    public string? City { get; set; }
    public string? Voivodeship { get; set; }
}

public sealed class ProductCatalogTenantFacetDto
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
}
