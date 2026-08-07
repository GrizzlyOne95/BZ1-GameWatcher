using BZAPI.Configuration;
using BZAPI.Maps;
using BZAPI.Steam;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BZAPI.Tests;

public sealed class ExternalProviderFailureTests
{
    [Fact]
    public async Task Map_metadata_http_failure_returns_null_instead_of_failing_the_lobby_path()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(new ThrowingHandler());
        var provider = new MapMetadataProvider(
            cache,
            new SingleClientFactory(client),
            Options.Create(new MapMetadataOptions
            {
                BaseUrl = "https://example.invalid/bz98r",
                RequestTimeout = TimeSpan.FromSeconds(1)
            }),
            NullLogger<MapMetadataProvider>.Instance);

        var result = await provider.GetMapAsync("dm01.bzn", "0", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Workshop_http_failure_returns_null_instead_of_failing_the_lobby_path()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var client = new HttpClient(new ThrowingHandler());
        var provider = new SteamWorkshopProvider(
            cache,
            new SingleClientFactory(client),
            Options.Create(new SteamOptions
            {
                WorkshopRequestTimeout = TimeSpan.FromSeconds(1)
            }),
            NullLogger<SteamWorkshopProvider>.Instance);

        var result = await provider.GetItemAsync(2898000000, CancellationToken.None);

        Assert.Null(result);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Synthetic fixture test failure."));
    }
}
