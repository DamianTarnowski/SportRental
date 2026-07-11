using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using SportRental.Client.Pages;
using SportRental.Shared.Models;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Pages;

public sealed class ReviewsPaginationTests : TestContext
{
    private const int PageSize = 20;
    private readonly Mock<IApiService> _apiService = new();

    public ReviewsPaginationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_apiService.Object);
    }

    [Fact]
    public void LoadMore_RemainsAvailableUntilAllReviewsAreLoaded()
    {
        _apiService.Setup(x => x.GetReviewSummaryAsync())
            .ReturnsAsync(new ReviewSummaryDto { Count = 45 });
        _apiService.Setup(x => x.GetReviewsAsync(1, PageSize))
            .ReturnsAsync(CreateReviews(1, PageSize));
        _apiService.Setup(x => x.GetReviewsAsync(2, PageSize))
            .ReturnsAsync(CreateReviews(21, PageSize));
        _apiService.Setup(x => x.GetReviewsAsync(3, PageSize))
            .ReturnsAsync(CreateReviews(41, 5));

        var component = RenderComponent<Reviews>();

        component.WaitForAssertion(() => FindLoadMoreButtons(component).Should().ContainSingle());

        FindLoadMoreButtons(component).Single().Click();
        component.WaitForAssertion(() =>
        {
            component.Markup.Should().Contain("Klient 40");
            FindLoadMoreButtons(component).Should().ContainSingle();
        });

        FindLoadMoreButtons(component).Single().Click();
        component.WaitForAssertion(() =>
        {
            component.Markup.Should().Contain("Klient 45");
            FindLoadMoreButtons(component).Should().BeEmpty();
        });

        _apiService.Verify(x => x.GetReviewsAsync(1, PageSize), Times.Once);
        _apiService.Verify(x => x.GetReviewsAsync(2, PageSize), Times.Once);
        _apiService.Verify(x => x.GetReviewsAsync(3, PageSize), Times.Once);
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> FindLoadMoreButtons(
        IRenderedComponent<Reviews> component) =>
        component.FindAll("button")
            .Where(button => button.TextContent.Contains("Pokaż więcej", StringComparison.Ordinal))
            .ToList();

    private static List<RentalReviewDto> CreateReviews(int firstNumber, int count) =>
        Enumerable.Range(firstNumber, count)
            .Select(number => new RentalReviewDto
            {
                Id = Guid.NewGuid(),
                RentalId = Guid.NewGuid(),
                CustomerName = $"Klient {number}",
                QualityScore = 8,
                PriceScore = 8,
                ServiceScore = 8,
                AverageScore = 8,
                CreatedAtUtc = DateTime.UtcNow
            })
            .ToList();
}
