export interface LobbyConnectionHealth {
    state: 'starting' | 'connected' | 'disconnected' | string;
    isConnected: boolean;
    lastConnectedUtc: string | null;
    lastDisconnectedUtc: string | null;
    lastMessageUtc: string | null;
}

export interface WatcherHealth {
    status: string;
    lobbyCount: number;
    lastUpdatedUtc: string | null;
    lobbyConnection: LobbyConnectionHealth;
    activityHistoryStartedUtc: string | null;
    activityLastSampleUtc: string | null;
    activityStorage: string;
    activityDurable: boolean;
}
