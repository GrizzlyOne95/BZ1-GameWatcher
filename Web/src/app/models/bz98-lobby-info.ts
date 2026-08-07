/**
 * A lobby exactly as returned by the API.
 *
 * Player IP, WAN and LAN addresses are deliberately absent — the API never publishes them.
 */
export interface BZ98Lobby {
    id: number;
    clientVersion: string | null;
    createdTime: string;
    isChat: boolean;
    isLocked: boolean;
    isPrivate: boolean;
    hasPassword?: boolean | null;
    host: BZ98User | null;
    memberLimit: number;
    metaData: BZ98MetaData | null;
    stats: BZ98LobbyData | null;
    workshop?: BZ98WorkshopItem | null;
    map?: BZ98MapMetadata | null;
    owner: string | null;
    userCount: number;
    users: Record<string, BZ98User>;
    directJoinUrl: string | null;
    recentChat?: BZ98ChatMessage[];
}

/** A lobby prepared for display, with users flattened and game-settings data preserved separately. */
export interface BZ98LobbyView extends Omit<BZ98Lobby, 'users' | 'stats' | 'recentChat'> {
    users: BZ98User[];
    recentChat: BZ98ChatMessage[];
    oddTeamUsers: BZ98User[];
    evenTeamUsers: BZ98User[];
    unassignedTeamUsers: BZ98User[];

    /** The API's reported stats object, without transformation. */
    apiStats: BZ98LobbyData | null;

    /** Stats parsed from the raw metadata game-settings string. */
    parsedStats: BZ98LobbyData | null;

    /** Parsed settings when available, otherwise the API's reported stats. */
    stats: BZ98LobbyData | null;
}

export interface BZ98WorkshopItem {
    publishedFileId: string;
    title: string;
    previewUrl: string | null;
    creatorSteamId: string | null;
    creatorProfileUrl: string | null;
    workshopUrl: string;
    updatedUtc: string | null;
    subscriptions: number | null;
}

/** Optional public metadata resolved for a lobby's map/mod pair. */
export interface BZ98MapMetadata {
    mapFile: string;
    modId: string;
    isStock: boolean;
    title: string | null;
    imageUrl: string | null;
    description: string | null;
    minPlayers: number | null;
    maxPlayers: number | null;
    typeCode: string | null;
    typeLabel: string | null;
    modeCode: string | null;
    modeLabel: string | null;
    customTypeCode: string | null;
    customTypeName: string | null;
}

export interface BZ98ChatMessage {
    author: string | null;
    speakerId: string | null;
    text: string;
    timeUtc: string;
}

export interface BZ98MetaData {
    gameVersion: string | null;
    gameSettings: string | null;
    gameType: string | null;
    launched: string | null;
    gameEnded?: string | null;
    name: string | null;
    rawName?: string | null;
    nextMid: string | null;
    userCount: string | null;
    userPack: string | null;
}

export interface BZ98User {
    authType: string | null;
    clientVersion: string | null;
    id: string | null;
    isAdmin: boolean;
    isAuth: boolean;
    isBB: boolean;
    isDangerous: boolean;
    isInLounge: boolean;
    isGOG: boolean;
    isTest: boolean;
    isSteam: boolean;
    lobby: number;
    metaData: BZ98UserMetaData | null;
    name: string | null;
    stats: BZ98LobbyData | null;
    steamCleanId: string | null;
    steamImgUri: string | null;
}

export interface BZ98UserMetaData {
    clientsConnected: string | null;
    friendId: string | null;
    knownPlayers: string | null;
    launched: string | null;
    miniId: string | null;
    ready: string | null;
    team: string | null;
    vehicle: string | null;
    communityPatch?: string | null;
    communityPatchShim?: string | null;
}

export interface BZ98LobbyData {
    mapFile: string | null;
    crc32: string | null;
    mod: string | null;
    metaDataVersion?: number | null;
    syncJoin?: boolean | null;
    timeLimit?: number | null;
    playerLimit?: number | null;
    killLimit?: number | null;
    attributes: BZ98LobbyDataAttributes | null;
}

export interface BZ98LobbyDataAttributes {
    lives: string | null;
    satellite: boolean | null;
    barracks: boolean | null;
    sniper: boolean | null;
    splinter: boolean | null;
}
