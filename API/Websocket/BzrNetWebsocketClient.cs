using System.Net;
using System.Net.WebSockets;
using BZAPI.Configuration;
using Websocket.Client;

namespace BZAPI.Websocket;

/// <summary>
/// Creates BZRNet clients with a fresh native socket for every connection attempt. A configured
/// proxy is therefore applied consistently to the main watcher and every read-only observer,
/// including library-managed recovery of the main watcher.
/// </summary>
internal static class BzrNetWebsocketClient
{
    private static readonly HashSet<string> SupportedProxySchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        "socks5"
    };

    public static WebsocketClient Create(Uri serverUri, BattlezoneOptions options)
    {
        var proxyUri = ParseProxyUri(options.ProxyUrl);
        if (proxyUri is null)
        {
            return new WebsocketClient(serverUri);
        }

        return new WebsocketClient(serverUri, () => CreateNativeSocket(proxyUri));
    }

    internal static Uri? ParseProxyUri(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return null;
        }

        if (!Uri.TryCreate(configuredValue.Trim(), UriKind.Absolute, out var proxyUri) ||
            !SupportedProxySchemes.Contains(proxyUri.Scheme) ||
            string.IsNullOrWhiteSpace(proxyUri.Host) ||
            proxyUri.Port <= 0)
        {
            throw new InvalidOperationException(
                "Battlezone:ProxyUrl must be an absolute HTTP, HTTPS, or SOCKS5 proxy URI with a port.");
        }

        return proxyUri;
    }

    private static ClientWebSocket CreateNativeSocket(Uri proxyUri)
    {
        var socket = new ClientWebSocket();
        socket.Options.Proxy = new WebProxy(proxyUri);
        return socket;
    }
}
