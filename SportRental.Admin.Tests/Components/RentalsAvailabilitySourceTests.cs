using FluentAssertions;

namespace SportRental.Admin.Tests.Components;

public sealed class RentalsAvailabilitySourceTests
{
    [Fact]
    public void Save_GroupsDuplicateProductRowsBeforeCheckingRequestedQuantity()
    {
        var source = LoadRentalsSource();

        source.Should().Contain(".GroupBy(item => item.ProductId)");
        source.Should().Contain("Quantity = group.Sum(item => item.Quantity)");
        source.Should().Contain("item.Quantity,");
        source.Should().Contain("editModel.StartDateUtc");
    }

    [Fact]
    public void EditAvailability_ExcludesCurrentRentalFromReservedMap()
    {
        var source = LoadRentalsSource();

        source.Should().Contain("if (editModel.Id != Guid.Empty)");
        source.Should().Contain("blockingRentals = blockingRentals.Where(rental => rental.Id != editModel.Id);");
        source.Should().Contain(".Join(blockingRentals");
    }

    [Fact]
    public void QuickSkuAndScanner_DoNotAddMoreThanCurrentlyAvailable()
    {
        var source = LoadRentalsSource();

        source.Split("var available = GetAvail(product.Id);").Should().HaveCountGreaterThanOrEqualTo(3);
        source.Should().Contain("qty = available;");
        source.Should().Contain("nie ma wolnych sztuk w wybranym terminie");
    }

    private static string LoadRentalsSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "SportRental.Admin",
                "Components",
                "Pages",
                "Admin",
                "Rentals.razor");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Nie znaleziono pliku Rentals.razor względem katalogu testowego.");
    }
}
