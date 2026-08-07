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

export interface ActivityResponse {
    range: ActivityRange;
    requestedSinceUtc: string;
    historyStartedUtc: string | null;
    lastHistoricalSampleUtc: string | null;
    lobbyDataUpdatedUtc: string | null;
    historyStorage: 'memory' | 'file' | string;
    durableHistory: boolean;
    current: ActivitySample | null;
    summary: ActivitySummary;
    samples: ActivitySample[];
}
