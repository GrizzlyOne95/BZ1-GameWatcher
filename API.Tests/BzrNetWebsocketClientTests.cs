using BZAPI.Websocket;
using Xunit;

namespace API.Tests;

public sealed class BzrNetWebsocketClientTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyProxyConfigurationUsesDirectSocket(string? value)
    {
        Assert.Null(BzrNetWebsocketClient.ParseProxyUri(value));
    }

    [Theory]
    [InlineData("socks5://127.0.0.1:40000")]
    [InlineData("http://proxy.example:8080")]
    [InlineData("https://proxy.example:8443")]
    public void SupportedProxyConfigurationIsAccepted(string value)
    {
        Assert.Equal(value, BzrNetWebsocketClient.ParseProxyUri(value)?.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("127.0.0.1:40000")]
    [InlineData("ftp://proxy.example:21")]
    [InlineData("socks5://proxy.example")]
    public void InvalidProxyConfigurationFailsClosed(string value)
    {
        Assert.Throws<InvalidOperationException>(() => BzrNetWebsocketClient.ParseProxyUri(value));
    }
}
