using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportRental.MediaStorage.Data;
using SportRental.MediaStorage.Options;
using Xunit;

namespace SportRental.MediaStorage.Tests.Services;

public class MediaStorageEndpointsTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly WebApplicationFactory<global::Program> _factory;

    public MediaStorageEndpointsTests(WebApplicationFactory<global::Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.Configure<StorageOptions>(options =>
                {
                    options.RootPath = Path.Combine(Path.GetTempPath(), "media-storage-tests", Guid.NewGuid().ToString());
                    options.PublicBaseUrl = "https://localhost:7002";
                });
                services.Configure<SecurityOptions>(options =>
                {
                    options.ApiKeys = new[] { "test-key" };
                });

                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(MediaStorageDbContext));
                if (dbContextDescriptor is not null)
                {
                    services.Remove(dbContextDescriptor);
                }

                var optionsDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<MediaStorageDbContext>));
                if (optionsDescriptor is not null)
                {
                    services.Remove(optionsDescriptor);
                }

                var dbDirectory = Path.Combine(Path.GetTempPath(), "media-storage-tests", "db");
                Directory.CreateDirectory(dbDirectory);
                var databasePath = Path.Combine(dbDirectory, $"{Guid.NewGuid():N}.db");
                var connectionString = $"Data Source={databasePath}";

                services.AddDbContext<MediaStorageDbContext>(options =>
                {
                    options.UseSqlite(connectionString);
                });
            });
        });
    }

    [Fact]
    public async Task UploadAndDownloadRoundTrip()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");

        await using var stream = new MemoryStream(new byte[] { 4, 5, 6, 7 });
        using var content = new MultipartFormDataContent
        {
            { new StringContent(Guid.Empty.ToString()), "tenantId" },
            { new StringContent("images/test/file.png"), "path" },
            { new StreamContent(stream) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") } }, "file", "file.png" }
        };

        var uploadResponse = await client.PostAsync("/api/files", content);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        uploaded.Should().NotBeNull();

        var metadataResponse = await client.GetAsync($"/api/files/{uploaded!.Id}");
        metadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var download = await client.GetAsync(uploaded.DownloadUrl);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(4, 5, 6, 7);
    }

    private record UploadResult(Guid Id, string DownloadUrl, string RelativePath);
}
