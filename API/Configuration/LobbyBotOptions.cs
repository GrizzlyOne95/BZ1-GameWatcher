namespace BZAPI.Configuration;

/// <summary>
/// Opt-in automation for a community chat lobby. All values can be supplied through the standard
/// ASP.NET configuration providers, including Render environment variables such as
/// <c>LobbyBot__Enabled</c> and <c>LobbyBot__LobbyName</c>.
/// </summary>
public sealed class LobbyBotOptions
{
    public const string SectionName = "LobbyBot";

    public bool Enabled { get; set; }
    public string PlayerName { get; set; } = "BZ1 Game Watcher";
    public string LobbyName { get; set; } = string.Empty;
    public bool AutoClaim { get; set; }
    public int MemberLimit { get; set; } = 20000;
    public string WelcomeMessage { get; set; } = string.Empty;
    public TimeSpan WelcomeCooldown { get; set; } = TimeSpan.FromMinutes(1);
    public string AnnouncementMessage { get; set; } = string.Empty;
    public TimeSpan AnnouncementInterval { get; set; } = TimeSpan.FromMinutes(5);
}
