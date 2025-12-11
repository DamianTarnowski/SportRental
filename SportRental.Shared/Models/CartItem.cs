using System.ComponentModel.DataAnnotations;

namespace SportRental.Shared.Models;

public class CartItem
{
    public required Guid ProductId { get; set; }
    public required string ProductName { get; set; }
    public string? ProductImageUrl { get; set; }
    public decimal DailyPrice { get; set; }
    public decimal? HourlyPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime StartDate { get; set; } = DateTime.Today.AddDays(1);
    public DateTime EndDate { get; set; } = DateTime.Today.AddDays(2);

    // Typ wynajmu (godzinowy/dzienny)
    public RentalTypeDto RentalType { get; set; } = RentalTypeDto.Daily;
    public int? HoursRented { get; set; }

    // Reservation hold metadata (managed by CartService)
    public Guid? HoldId { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }

    public int TotalDays => Math.Max(1, (EndDate - StartDate).Days);
    public decimal TotalPrice => RentalType == RentalTypeDto.Hourly && HourlyPrice.HasValue && HoursRented.HasValue
        ? HourlyPrice.Value * Quantity * HoursRented.Value
        : DailyPrice * Quantity * TotalDays;
}

public class Cart
{
    public List<CartItem> Items { get; set; } = new();
    public decimal TotalAmount => Items.Sum(i => i.TotalPrice);
    public int TotalItems => Items.Sum(i => i.Quantity);
    
    public void AddItem(ProductDto product, int quantity = 1, DateTime? startDate = null, DateTime? endDate = null)
    {
        var existingItem = Items.FirstOrDefault(i => i.ProductId == product.Id);
        if (existingItem != null)
        {
            existingItem.Quantity += quantity;
            if (existingItem.ProductImageUrl is null && !string.IsNullOrWhiteSpace(product.FullImageUrl ?? product.ImageUrl))
            {
                existingItem.ProductImageUrl = product.FullImageUrl ?? product.ImageUrl;
            }
            if (startDate.HasValue && endDate.HasValue && endDate > startDate)
            {
                existingItem.StartDate = startDate.Value;
                existingItem.EndDate = endDate.Value;
            }
        }
        else
        {
            Items.Add(new CartItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductImageUrl = product.FullImageUrl ?? product.ImageUrl,
                DailyPrice = product.DailyPrice,
                HourlyPrice = product.HourlyPrice,
                Quantity = quantity,
                StartDate = startDate ?? DateTime.Today.AddDays(1),
                EndDate = endDate ?? DateTime.Today.AddDays(2)
            });
        }
    }
    
    public void RemoveItem(Guid productId)
    {
        Items.RemoveAll(i => i.ProductId == productId);
    }
    
    public void UpdateQuantity(Guid productId, int quantity)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            if (quantity <= 0)
                RemoveItem(productId);
            else
                item.Quantity = quantity;
        }
    }
    
    public void Clear()
    {
        Items.Clear();
    }
}
