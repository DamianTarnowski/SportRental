using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SportRental.Admin.Components.Shared;

namespace SportRental.Admin.Tests.Components;

public class RentalReminderActionTests : TestContext
{
    public RentalReminderActionTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void BeforeSending_RendersVisibleOutlinedBell()
    {
        var cut = RenderAction(wasSent: false);

        var button = cut.Find("button[data-testid='return-reminder-action']");
        button.GetAttribute("class").Should().Contain("mud-button-outlined-warning");
        button.GetAttribute("class").Should().NotContain("rs-return-reminder-action-sent");
        button.QuerySelector("svg path").Should().NotBeNull("dzwonek musi pozostać rzeczywistą ikoną SVG");
    }

    [Fact]
    public void AfterSending_RendersVisibleFilledBellOnAccessibleGreenSurface()
    {
        var cut = RenderAction(wasSent: true);

        var button = cut.Find("button[data-testid='return-reminder-action']");
        button.GetAttribute("class").Should().Contain("mud-button-filled-success");
        button.GetAttribute("class").Should().Contain("rs-return-reminder-action-sent");
        button.GetAttribute("style").Should().Contain("background-color: #1B5E20 !important");
        button.GetAttribute("style").Should().Contain("color: #FFFFFF !important");
        button.QuerySelector("svg path").Should().NotBeNull("stan po wysłaniu nie może być pustym kwadratem");

        ContrastRatio("#FFFFFF", "#1B5E20").Should().BeGreaterThanOrEqualTo(7d);
    }

    private IRenderedFragment RenderAction(bool wasSent) => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<RentalReminderAction>(1);
        builder.AddAttribute(2, nameof(RentalReminderAction.WasSent), wasSent);
        builder.AddAttribute(3, nameof(RentalReminderAction.Tooltip), "Wyślij przypomnienie SMS o zwrocie");
        builder.CloseComponent();
    });

    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static double RelativeLuminance(string hex)
    {
        var channels = new[] { 1, 3, 5 }
            .Select(index => Convert.ToInt32(hex.Substring(index, 2), 16) / 255d)
            .Select(channel => channel <= 0.04045d
                ? channel / 12.92d
                : Math.Pow((channel + 0.055d) / 1.055d, 2.4d))
            .ToArray();

        return (0.2126d * channels[0]) + (0.7152d * channels[1]) + (0.0722d * channels[2]);
    }
}
