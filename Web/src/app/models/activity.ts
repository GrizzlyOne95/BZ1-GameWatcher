export type ActivityRange = '24h' | '7d' | '30d';

export interface ActivitySample {
    timeUtc: string;
    playersOnline: number;
    activeGames: number;
    gamesInProgress: number;
    waitingRoomUsers: number;
}

export interface ActivitySummary {
    peakPlayers: number;
    averagePlayers: number;
    peakActiveGames: number;
    historicalSampleCount: number;
}

export interface LobbyActivityEvent {
    sequence: number;
    timeUtc: string;
    lobbyId: number;
    type: string;
    lobbyName: string | null;
    mapFile: string | null;
    mod: string | null;
    fromCount: number | null;
    toCount: number | null;
    fromValue: string | null;
    toValue: string | null;
}

export interface ActivityResponse {
    range: ActivityRange;
    requestedSinceUtc: string;
    historyStartedUtc: string | null;
    lastHistoricalSampleUtc: string | null;
    lobbyDataUpdatedUtc: string | null;
    historyStorage: 'memory' | 'file' | string;
    durableHistory: boolean;
    eventHistoryStartedUtc: string | null;
    lastEventUtc: string | null;
    eventHistoryStorage: 'memory' | 'file' | string;
    durableEventHistory: boolean;
    current: ActivitySample | null;
    summary: ActivitySummary;
    samples: ActivitySample[];
    recentEvents: LobbyActivityEvent[];
}
