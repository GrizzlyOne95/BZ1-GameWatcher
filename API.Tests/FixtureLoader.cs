using BZAPI.Models;
using BZAPI.Protocol;
using Newtonsoft.Json;

namespace BZAPI.Tests;

internal static class FixtureLoader
{
    public static string ProtocolDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Protocol");

    public static BZ98Lobby LoadLobby(string fileName, bool normalize = true)
    {
        var path = Path.Combine(ProtocolDirectory, fileName);
        var json = File.ReadAllText(path);
        var lobby = JsonConvert.DeserializeObject<BZ98Lobby>(json)
            ?? throw new InvalidDataException($"Fixture {fileName} did not deserialize to a lobby.");

        if (normalize)
        {
            BZ98ProtocolParser.NormalizeLobby(lobby);
        }

        return lobby;
    }
}
