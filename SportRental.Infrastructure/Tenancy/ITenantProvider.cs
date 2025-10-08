namespace SportRental.Infrastructure.Tenancy;

public interface ITenantProvider
{
    Guid? GetCurrentTenantId();
}