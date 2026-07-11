using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SportRental.Client.Components;

namespace SportRental.Client.Tests.Components;

public sealed class CustomErrorBoundaryTests : TestContext
{
    [Fact]
    public void ErrorView_HidesExceptionDetailsByDefault()
    {
        var component = RenderComponent<CustomErrorBoundary>(parameters => parameters
            .AddChildContent<ThrowingComponent>());

        component.Markup.Should().Contain("Wystąpił nieoczekiwany błąd");
        component.Markup.Should().NotContain("sensitive error details");
        component.Markup.Should().NotContain("Szczegóły błędu");
        component.Markup.Should().NotContain("powiadomiony");
    }

    [Fact]
    public void ErrorView_ShowsExceptionDetailsOnlyWhenExplicitlyEnabled()
    {
        var component = RenderComponent<CustomErrorBoundary>(parameters => parameters
            .Add(parameter => parameter.ShowDetails, true)
            .AddChildContent<ThrowingComponent>());

        component.Markup.Should().Contain("Szczegóły błędu");
        component.Markup.Should().Contain("sensitive error details");
    }

    private sealed class ThrowingComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => throw new InvalidOperationException("sensitive error details");
    }
}
