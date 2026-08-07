using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace BZAPI.Tests;

public sealed partial class FixtureSafetyTests
{
    private static readonly HashSet<string> NetworkPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ip",
        "ipaddress",
        "wan",
        "wanaddress",
        "lan",
        "lanaddress",
        "lanaddresses"
    };

    private static readonly HashSet<string> SecretPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwd",
        "token",
        "authtoken",
        "authkey",
        "secret"
    };

    [Fact]
    public void Protocol_fixtures_contain_only_documentation_network_addresses_and_no_secrets()
    {
        var violations = new List<string>();
        var files = Directory.GetFiles(FixtureLoader.ProtocolDirectory, "*.json", SearchOption.AllDirectories);

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            InspectElement(document.RootElement, Path.GetFileName(file), "$", violations);
        }

        Assert.True(
            violations.Count == 0,
            "Unsafe data found in sanitized protocol fixtures:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(violation => "- " + violation)));
    }

    private static void InspectElement(
        JsonElement element,
        string fileName,
        string path,
        ICollection<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var propertyPath = path + "." + property.Name;
                    var normalizedName = NormalizePropertyName(property.Name);

                    if (SecretPropertyNames.Contains(normalizedName) && ContainsNonEmptyValue(property.Value))
                    {
                        violations.Add($"{fileName} {propertyPath} contains a secret/password-like value.");
                    }

                    if (NetworkPropertyNames.Contains(normalizedName))
                    {
                        ValidateNetworkProperty(property.Value, fileName, propertyPath, violations);
                    }

                    InspectElement(property.Value, fileName, propertyPath, violations);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    InspectElement(item, fileName, $"{path}[{index}]", violations);
                    index++;
                }
                break;

            case JsonValueKind.String:
                InspectString(element.GetString(), fileName, path, violations);
                break;
        }
    }

    private static void ValidateNetworkProperty(
        JsonElement element,
        string fileName,
        string path,
        ICollection<string> violations)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                return;

            case JsonValueKind.String:
                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (!TryParseIp(value, out var address) || !IsDocumentationAddress(address))
                {
                    violations.Add($"{fileName} {path} must be empty or an RFC documentation address.");
                }
                return;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String ||
                        !TryParseIp(item.GetString(), out var arrayAddress) ||
                        !IsDocumentationAddress(arrayAddress))
                    {
                        violations.Add($"{fileName} {path} contains a non-documentation network value.");
                    }
                }
                return;

            default:
                violations.Add($"{fileName} {path} contains an unexpected network-address value shape.");
                return;
        }
    }

    private static void InspectString(
        string? value,
        string fileName,
        string path,
        ICollection<string> violations)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // Do not feed arbitrary numeric/version strings to IPAddress.TryParse: it accepts legacy
        // shorthand forms such as "1" and "2.0.185" as IPv4. Generic fixture scanning starts from
        // syntactic dotted-quad/IPv6 candidates instead; explicitly named network properties are
        // still parsed strictly by ValidateNetworkProperty above.
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            TryParseIp(uri.Host, out var hostAddress) &&
            !IsDocumentationAddress(hostAddress))
        {
            violations.Add($"{fileName} {path} contains a URL with non-documentation IP host {uri.Host}.");
        }

        foreach (Match match in Ipv4CandidateRegex().Matches(value))
        {
            if (TryParseIp(match.Value, out var address) && !IsDocumentationAddress(address))
            {
                violations.Add($"{fileName} {path} contains non-documentation IPv4 address {match.Value}.");
            }
        }

        foreach (Match match in Ipv6CandidateRegex().Matches(value))
        {
            if (TryParseIp(match.Value, out var address) &&
                address.AddressFamily == AddressFamily.InterNetworkV6 &&
                !IsDocumentationAddress(address))
            {
                violations.Add($"{fileName} {path} contains non-documentation IPv6 address {match.Value}.");
            }
        }
    }

    private static bool TryParseIp(string? value, out IPAddress address)
    {
        var candidate = value?.Trim().Trim('[', ']');
        if (candidate is not null && IPAddress.TryParse(candidate, out var parsed) && parsed is not null)
        {
            address = parsed;
            return true;
        }

        address = IPAddress.None;
        return false;
    }

    private static bool IsDocumentationAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes is [192, 0, 2, _] or
                [198, 51, 100, _] or
                [203, 0, 113, _];
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // RFC 3849: 2001:db8::/32.
            return bytes.Length == 16 &&
                bytes[0] == 0x20 && bytes[1] == 0x01 &&
                bytes[2] == 0x0d && bytes[3] == 0xb8;
        }

        return false;
    }

    private static bool ContainsNonEmptyValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => false,
        JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => true
    };

    private static string NormalizePropertyName(string propertyName) =>
        new(propertyName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"(?<![0-9])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4CandidateRegex();

    [GeneratedRegex(@"(?<![0-9A-Fa-f:])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?![0-9A-Fa-f:])", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6CandidateRegex();
}
