namespace SportRental.Shared.Models;

public record DiscountValidateRequest(Guid TenantId, string Code, decimal OrderAmount);

public record DiscountValidateResponse(bool IsValid, decimal DiscountAmount, string? Reason);

public record VoucherValidateRequest(string Code);

public record VoucherValidateResponse(bool IsValid, decimal RemainingBalance, string? Reason);
