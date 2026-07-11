using Azure;
using Azure.AI.OpenAI;

namespace SportRental.Admin.Services.Ai;

/// <summary>
/// Keeps Azure OpenAI optional. Missing local secrets must not prevent unrelated
/// Blazor components from being activated.
/// </summary>
public sealed class AzureOpenAiClientProvider
{
    public AzureOpenAIClient? Client { get; }

    public bool IsConfigured => Client is not null;

    public AzureOpenAiClientProvider(
        IConfiguration configuration,
        ILogger<AzureOpenAiClientProvider> logger)
    {
        var endpoint = configuration["OpenAI:Endpoint"];
        var apiKey = configuration["OpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogInformation("Azure OpenAI is not configured; AI features are disabled.");
            return;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || (endpointUri.Scheme != Uri.UriSchemeHttps && endpointUri.Scheme != Uri.UriSchemeHttp))
        {
            logger.LogWarning("Azure OpenAI endpoint is invalid; AI features are disabled.");
            return;
        }

        Client = new AzureOpenAIClient(endpointUri, new AzureKeyCredential(apiKey));
    }
}
