import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faCirclePlay, faComputer, faLink, faLock, faLockOpen, faMessage, faPlayCircle, faUser } from '@fortawesome/free-solid-svg-icons';
import { EMPTY, Subject, catchError, exhaustMap, takeUntil } from 'rxjs';
import { environment } from '../../../environments/environment';
import { StockVehicleDefinition, findStockVehicle } from '../../data/stock-vehicles';
import { SiteNavComponent } from '../../components/site-nav/site-nav.component';
import { BZ98ChatMessage, BZ98Lobby, BZ98LobbyData, BZ98LobbyView, BZ98User } from '../../models/bz98-lobby-info';
import { BZ98Service } from '../../services/bz98.service';
import { buildSteamJoinUrl } from '../../services/steam-join';
import { visibilityAwareTimer } from '../../services/visibility-polling';

/** Minimum fields needed to decode the original core portion of the '*' settings tuple. */
const GAME_SETTINGS_MIN_FIELD_COUNT = 9;
const TIME_ZONE_STORAGE_KEY = 'bz98-display-time-zone';
const FALLBACK_TIME_ZONES = [
    'Pacific/Honolulu',
    'America/Anchorage',
    'America/Los_Angeles',
    'America/Denver',
    'America/Chicago',
    'America/New_York',
    'America/Halifax',
    'America/Sao_Paulo',
    'Atlantic/Reykjavik',
    'Europe/London',
    'Europe/Paris',
    'Europe/Berlin',
    'Europe/Helsinki',
    'Europe/Moscow',
    'Africa/Johannesburg',
    'Asia/Dubai',
    'Asia/Kolkata',
    'Asia/Bangkok',
    'Asia/Singapore',
    'Asia/Shanghai',
    'Asia/Tokyo',
    'Australia/Perth',
    'Australia/Adelaide',
    'Australia/Sydney',
    'Pacific/Auckland'
] as const;

@Component({
    selector: 'app-games',
    imports: [CommonModule, FontAwesomeModule, SiteNavComponent],
    templateUrl: './games.component.html',
    styleUrl: './games.component.scss'
})
export class GamesComponent implements OnInit, OnDestroy {
    private readonly destroyed = new Subject<void>();

    faLink = faLink;
    faUser = faUser;
    faComputer = faComputer;
    faMessage = faMessage;
    faPlayCircle = faPlayCircle;
    faLock = faLock;
    faLockOpen = faLockOpen;
    faCirclePlay = faCirclePlay;

    BZ98Lobbies: BZ98LobbyView[] = [];
    BZ98ChatLobbies: BZ98LobbyView[] = [];

    /** True once a response has been received, so the page can tell "empty" from "still loading". */
    hasLoaded = false;

    /** Set when the most recent refresh failed, so the page can say so instead of going blank. */
    loadFailed = false;

    /** Local timestamp of the most recent successful browser refresh. */
    lastRefreshedAt: Date | null = null;

    /** Browser-local zone remains the default until a visitor explicitly chooses another one. */
    readonly browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Browser local time';
    readonly timeZoneOptions = this.getSupportedTimeZones();
    selectedTimeZone = this.getStoredTimeZone();
    timeZonePickerOpen = false;

    constructor(private readonly bz98Service: BZ98Service) {
    }

    ngOnInit(): void {
        // Visible game lists stay close to real time. Background tabs back off to one request per
        // minute and refresh immediately when the visitor returns.
        visibilityAwareTimer(environment.lobbyRefreshIntervalMs, 60_000)
            .pipe(
                exhaustMap(() => this.bz98Service.getBZ98Lobbies().pipe(
                    catchError((error: unknown) => {
                        // Without this the polling subscription died on the first failed request
                        // and the page silently stopped updating for good.
                        console.error('Failed to refresh lobby data.', error);
                        this.loadFailed = true;
                        this.hasLoaded = true;
                        return EMPTY;
                    })
                )),
                takeUntil(this.destroyed)
            )
            .subscribe(lobbies => this.applyLobbies(lobbies));
    }

    ngOnDestroy(): void {
        this.destroyed.next();
        this.destroyed.complete();
    }

    joinGame(lobby: BZ98LobbyView): void {
        window.location.href = lobby.directJoinUrl || buildSteamJoinUrl(lobby.id);
    }

    async shareToCommunity(lobby: BZ98LobbyView): Promise<void> {
        const shareText =
            `${lobby.userCount}/${lobby.memberLimit} ${window.location.origin}/join/${lobby.id} @BZ1 Expert @BZ1 Novice`;

        await this.copyToClipboard(shareText);

        window.location.href = environment.communitySiteUrl;
    }

    toggleTimeZonePicker(): void {
        this.timeZonePickerOpen = !this.timeZonePickerOpen;
    }

    selectTimeZone(event: Event): void {
        const requestedTimeZone = (event.currentTarget as HTMLSelectElement | null)?.value ?? '';
        this.selectedTimeZone = this.timeZoneOptions.includes(requestedTimeZone) ? requestedTimeZone : '';

        try {
            if (this.selectedTimeZone) {
                localStorage.setItem(TIME_ZONE_STORAGE_KEY, this.selectedTimeZone);
            } else {
                localStorage.removeItem(TIME_ZONE_STORAGE_KEY);
            }
        } catch {
            // Private browsing and hardened browsers may disallow storage. The current selection
            // still remains active for the life of this page.
        }
    }

    get timeZoneButtonLabel(): string {
        return this.selectedTimeZone
            ? this.selectedTimeZone.replaceAll('_', ' ')
            : 'Add time zone';
    }

    get activeTimeZoneLabel(): string {
        return this.selectedTimeZone
            ? this.selectedTimeZone.replaceAll('_', ' ')
            : `Local (${this.browserTimeZone.replaceAll('_', ' ')})`;
    }

    formatDateTime(value: string | Date | null | undefined): string {
        return this.formatTimestamp(value, true);
    }

    formatTime(value: string | Date | null | undefined): string {
        return this.formatTimestamp(value, false);
    }

    ownerUser(lobby: BZ98LobbyView): BZ98User | null {
        const candidates = [lobby.host, ...lobby.users]
            .filter((user): user is BZ98User => Boolean(user));

        if (lobby.owner) {
            const exactOwner = candidates.find(user => user.id === lobby.owner);
            if (exactOwner) {
                return exactOwner;
            }
        }

        // The API's host snapshot is the most useful fallback when a lobby's public user list is
        // empty, which is common for persistent chat/bridge lobbies.
        return lobby.host ?? null;
    }

    ownerDisplayName(lobby: BZ98LobbyView): string {
        const owner = this.ownerUser(lobby);
        const name = owner?.name?.trim();

        if (name) {
            return name;
        }

        if (owner?.steamCleanId) {
            return 'Steam profile';
        }

        return this.display(lobby.owner);
    }

    ownerSteamProfileUrl(lobby: BZ98LobbyView): string | null {
        const steamId = this.ownerUser(lobby)?.steamCleanId?.trim();
        return steamId ? `https://steamcommunity.com/profiles/${steamId}/` : null;
    }

    trackLobby(_index: number, lobby: BZ98LobbyView): number {
        return lobby.id;
    }

    trackUser(index: number, user: BZ98User): string | number {
        return user.id || user.steamCleanId || user.name || index;
    }

    trackChat(index: number, message: BZ98ChatMessage): string {
        return `${message.timeUtc}|${message.speakerId ?? message.author ?? ''}|${index}`;
    }

    display(value: string | number | null | undefined): string {
        if (value === null || value === undefined || value === '') {
            return 'Not reported';
        }

        return String(value);
    }

    /** Resolve a lobby-reported ODF code without guessing when the craft is modded or unknown. */
    stockVehicle(code: string | null | undefined): StockVehicleDefinition | null {
        return findStockVehicle(code);
    }

    /** Keep the raw ODF code visible even when a friendly stock name is available. */
    vehicleLabel(code: string | null | undefined): string {
        const normalizedCode = code?.trim();
        if (!normalizedCode) {
            return 'Not reported';
        }

        const vehicle = findStockVehicle(normalizedCode);
        return vehicle ? `${vehicle.unitName} (${normalizedCode})` : normalizedCode;
    }

    /** A failed third-party thumbnail should disappear instead of leaving a broken-image icon. */
    hideBrokenImage(event: Event): void {
        const image = event.currentTarget as HTMLImageElement | null;
        if (image) {
            image.hidden = true;
        }
    }

    /**
     * Battlezone reports the Steam Workshop published-file ID in the mod field for Workshop games.
     * Non-numeric values such as stock/local mod labels are deliberately left as plain text.
     */
    workshopUrl(mod: string | null | undefined): string | null {
        const publishedFileId = mod?.trim();

        if (!publishedFileId || !/^[1-9]\d*$/.test(publishedFileId)) {
            return null;
        }

        return `https://steamcommunity.com/sharedfiles/filedetails/?id=${publishedFileId}`;
    }

    yesNo(value: boolean | null | undefined): string {
        if (value === null || value === undefined) {
            return 'Not reported';
        }

        return value ? 'Yes' : 'No';
    }

    /**
     * The lobby metadata gameType field is a validity marker in BZ98R (0 broken, 1 valid), not
     * the actual Deathmatch/Strategy/MPI mode. Actual mode comes from optional map metadata.
     */
    gameTypeLabel(gameType: string | null | undefined): string {
        switch (gameType) {
            case '0':
                return 'Broken/invalid';
            case '1':
                return 'Valid';
            default:
                return this.display(gameType);
        }
    }

    mapTitle(lobby: BZ98LobbyView): string {
        return lobby.map?.title?.trim() || this.display(lobby.stats?.mapFile);
    }

    mapModeLabel(lobby: BZ98LobbyView): string {
        return lobby.map?.modeLabel?.trim() || this.gameTypeLabel(lobby.metaData?.gameType);
    }

    launchStatus(lobby: BZ98LobbyView): string {
        if (lobby.metaData?.gameEnded === '1') {
            return 'Ended';
        }

        if (lobby.metaData?.launched === '1') {
            return 'In progress';
        }

        if (lobby.metaData?.launched === '0') {
            return 'In lobby';
        }

        return 'Not reported';
    }

    lobbyDisplayName(lobby: BZ98LobbyView): string {
        const rawName = lobby.metaData?.name;
        if (!rawName) {
            return lobby.isChat ? `Chat lobby ${lobby.id}` : `Game ${lobby.id}`;
        }

        return rawName
            .replace(/^~game~(?:pub|pri)~\*?~/i, '')
            .replace(/^~chat~(?:pub|pri)~~/i, '') || rawName;
    }

    isHost(lobby: BZ98LobbyView, user: BZ98User): boolean {
        return Boolean(user.id && (user.id === lobby.owner || user.id === lobby.host?.id));
    }

    /** The upstream authType field is authoritative; ID prefixes are only enrichment hints. */
    userPlatform(user: BZ98User): string {
        switch (user.authType?.trim().toLowerCase()) {
            case 'steam':
                return 'Steam';
            case 'gog':
                return 'GOG';
            case 'web':
                return 'Web';
            default:
                return this.display(user.authType);
        }
    }

    chatAuthor(lobby: BZ98LobbyView, message: BZ98ChatMessage): string {
        const reported = message.author?.trim();
        if (reported) {
            return reported;
        }

        if (message.speakerId) {
            const user = lobby.users.find(candidate => candidate.id === message.speakerId);
            if (user?.name?.trim()) {
                return user.name.trim();
            }

            return message.speakerId;
        }

        return 'Unknown';
    }

    private applyLobbies(lobbies: BZ98Lobby[]): void {
        this.hasLoaded = true;
        this.loadFailed = false;
        this.lastRefreshedAt = new Date();

        // The API always returns an array, but a proxy error page or an older API could still
        // deliver something else; treat anything unexpected as "no lobbies" rather than throwing.
        const source = Array.isArray(lobbies) ? lobbies : [];
        const views = source.map(lobby => this.toView(lobby));

        this.BZ98ChatLobbies = views.filter(lobby => lobby.isChat);
        this.BZ98Lobbies = views.filter(lobby => !lobby.isChat);
    }

    private toView(lobby: BZ98Lobby): BZ98LobbyView {
        const users = lobby.users ? Object.values(lobby.users) : [];
        const oddTeamUsers: BZ98User[] = [];
        const evenTeamUsers: BZ98User[] = [];
        const unassignedTeamUsers: BZ98User[] = [];

        for (const user of users) {
            const team = Number(user.metaData?.team);

            if (!Number.isFinite(team) || !user.metaData?.team) {
                unassignedTeamUsers.push(user);
            } else if (team % 2 !== 0) {
                oddTeamUsers.push(user);
            } else {
                evenTeamUsers.push(user);
            }
        }

        const parsedStats = lobby.isChat ? null : this.parseGameSettings(lobby.metaData?.gameSettings);

        return {
            ...lobby,
            recentChat: Array.isArray(lobby.recentChat) ? lobby.recentChat : [],
            users,
            oddTeamUsers,
            evenTeamUsers,
            unassignedTeamUsers,
            apiStats: lobby.stats,
            parsedStats,
            stats: parsedStats ?? lobby.stats
        };
    }

    /**
     * Decode the public 13-field BZ98 game-settings tuple. Older/partial tuples still expose the
     * fields they contain; omitted values stay null rather than becoming false or zero.
     */
    private parseGameSettings(settings: string | null | undefined): BZ98LobbyData | null {
        if (!settings) {
            return null;
        }

        const parts = settings.split('*');

        if (parts.length < GAME_SETTINGS_MIN_FIELD_COUNT) {
            return null;
        }

        return {
            mapFile: this.part(parts, 1),
            crc32: this.part(parts, 2),
            mod: this.part(parts, 3),
            metaDataVersion: this.integerPart(parts, 0),
            syncJoin: this.booleanPart(parts, 4),
            timeLimit: this.integerPart(parts, 7),
            playerLimit: this.integerPart(parts, 9),
            killLimit: this.integerPart(parts, 11),
            attributes: {
                lives: this.part(parts, 8),
                satellite: this.booleanPart(parts, 5),
                barracks: this.booleanPart(parts, 6),
                sniper: this.booleanPart(parts, 10),
                splinter: this.booleanPart(parts, 12)
            }
        };
    }

    private part(parts: string[], index: number): string | null {
        const value = parts[index]?.trim();
        return value ? value : null;
    }

    private integerPart(parts: string[], index: number): number | null {
        const value = this.part(parts, index);
        if (value === null) {
            return null;
        }

        const parsed = Number.parseInt(value, 10);
        return Number.isFinite(parsed) ? parsed : null;
    }

    private booleanPart(parts: string[], index: number): boolean | null {
        switch (this.part(parts, index)) {
            case '0':
                return false;
            case '1':
                return true;
            default:
                return null;
        }
    }

    private getSupportedTimeZones(): string[] {
        const timeZoneIntl = Intl as typeof Intl & {
            supportedValuesOf?: (key: 'timeZone') => string[];
        };

        try {
            const supported = timeZoneIntl.supportedValuesOf?.('timeZone');
            if (supported?.length) {
                return supported;
            }
        } catch {
            // Fall through to the representative list for older browsers.
        }

        return [...FALLBACK_TIME_ZONES];
    }

    private getStoredTimeZone(): string {
        try {
            const stored = localStorage.getItem(TIME_ZONE_STORAGE_KEY) ?? '';
            return this.timeZoneOptions.includes(stored) ? stored : '';
        } catch {
            return '';
        }
    }

    private formatTimestamp(value: string | Date | null | undefined, includeDate: boolean): string {
        if (!value) {
            return 'Not reported';
        }

        const date = value instanceof Date ? value : new Date(value);
        if (Number.isNaN(date.getTime())) {
            return this.display(typeof value === 'string' ? value : null);
        }

        const options: Intl.DateTimeFormatOptions = includeDate
            ? {
                year: 'numeric',
                month: 'short',
                day: 'numeric',
                hour: 'numeric',
                minute: '2-digit',
                second: '2-digit',
                timeZoneName: 'short'
            }
            : {
                hour: 'numeric',
                minute: '2-digit',
                second: '2-digit',
                timeZoneName: 'short'
            };

        if (this.selectedTimeZone) {
            options.timeZone = this.selectedTimeZone;
        }

        try {
            return new Intl.DateTimeFormat(undefined, options).format(date);
        } catch {
            // A browser can retain a zone name after its time-zone database changes. Falling back
            // to local time is safer than leaving all timestamps blank.
            delete options.timeZone;
            return new Intl.DateTimeFormat(undefined, options).format(date);
        }
    }

    private async copyToClipboard(text: string): Promise<void> {
        try {
            await navigator.clipboard.writeText(text);
            return;
        } catch {
            // The Clipboard API needs a secure context and permission; fall back to the old
            // hidden-textarea trick when it is unavailable.
        }

        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.setAttribute('readonly', '');
        textArea.style.position = 'absolute';
        textArea.style.left = '-9999px';

        document.body.appendChild(textArea);
        textArea.select();

        try {
            document.execCommand('copy');
        } catch (error) {
            console.error('Unable to copy the share link to the clipboard.', error);
        } finally {
            document.body.removeChild(textArea);
        }
    }
}
