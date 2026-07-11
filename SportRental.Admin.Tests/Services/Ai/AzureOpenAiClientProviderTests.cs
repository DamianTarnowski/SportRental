using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using SportRental.Admin.Services.Ai;
using SportRental.Admin.Services.Chat;

namespace SportRental.Admin.Tests.Services.Ai;

public class AzureOpenAiClientProviderTests
{
    [Fact]
    public void MissingConfiguration_DisablesProviderWithoutThrowing()
    {
        var provider = CreateProvider(new Dictionary<string, string?>());

        provider.IsConfigured.Should().BeFalse();
        provider.Client.Should().BeNull();
    }

    [Fact]
    public void ValidConfiguration_CreatesClientWithoutMakingNetworkRequest()
    {
        var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["OpenAI:Endpoint"] = "https://example.openai.azure.com/",
            ["OpenAI:ApiKey"] = "test-only-key"
        });

        provider.IsConfigured.Should().BeTrue();
        provider.Client.Should().NotBeNull();
    }

    [Fact]
    public async Task ChatWithoutConfiguration_ReturnsUnavailableResult()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>());
        var provider = new AzureOpenAiClientProvider(
            configuration,
            NullLogger<AzureOpenAiClientProvider>.Instance);
        var service = new OpenAiChatService(
            provider,
            configuration,
            NullLogger<OpenAiChatService>.Instance);

        var result = await service.ChatAsync(
            "system",
            Array.Empty<ChatTurn>(),
            Array.Empty<ChatTool>(),
            (_, _) => Task.FromResult("unused"));

        result.Error.Should().Be("openai_not_configured");
        result.Content.Should().Contain("nie jest skonfigurowany");
    }

    private static AzureOpenAiClientProvider CreateProvider(Dictionary<string, string?> values)
    {
        return new AzureOpenAiClientProvider(
            BuildConfiguration(values),
            NullLogger<AzureOpenAiClientProvider>.Instance);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
