export const environment = {
    production: true,
    apiUrl: '/api/',
    /** How often the lobby list is refreshed, in milliseconds. */
    lobbyRefreshIntervalMs: 3000,
    youTubeUrl: 'https://www.youtube.com/@battlezonecommunity',
    /** Partner community hub opened from the header and after copying a game share link. */
    communitySiteUrl: 'https://battlezonecommunity.com',
    /** Battlezone 98 Redux on Steam. */
    steamAppId: '301650',
    /** Second segment of the steam://rungame URL; required by Steam to resolve the launch. */
    steamRunGameOwnerId: '76561198104781489'
};
