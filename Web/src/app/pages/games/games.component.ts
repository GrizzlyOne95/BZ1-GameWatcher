import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faCirclePlay, faComputer, faLink, faLock, faLockOpen, faMessage, faPlayCircle, faUser } from '@fortawesome/free-solid-svg-icons';
import { EMPTY, Subject, catchError, exhaustMap, takeUntil, timer } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SiteNavComponent } from '../../components/site-nav/site-nav.component';
import { BZ98Lobby, BZ98LobbyData, BZ98LobbyView, BZ98User } from '../../models/bz98-lobby-info';
import { BZ98Service } from '../../services/bz98.service';
import { buildSteamJoinUrl } from '../../services/steam-join';

/** Number of '*'-separated fields a game settings string must have to be parsable. */
const GAME_SETTINGS_FIELD_COUNT = 9;

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

    constructor(private readonly bz98Service: BZ98Service) {
    }

    ngOnInit(): void {
        // exhaustMap rather than a bare setInterval: if a refresh is slow, later ticks are skipped
        // instead of stacking up more in-flight requests.
        timer(0, environment.lobbyRefreshIntervalMs)
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

    async shareToDiscord(lobby: BZ98LobbyView): Promise<void> {
        const shareText =
            `${lobby.userCount}/${lobby.memberLimit} ${window.location.origin}/join/${lobby.id} @BZ1 Expert @BZ1 Novice`;

        await this.copyToClipboard(shareText);

        window.location.href = environment.discordShareChannelUrl;
    }

    trackLobby(_index: number, lobby: BZ98LobbyView): number {
        return lobby.id;
    }

    trackUser(index: number, user: BZ98User): string | number {
        return user.id || user.steamCleanId || user.name || index;
    }

    display(value: string | number | null | undefined): string {
        if (value === null || value === undefined || value === '') {
            return 'Not reported';
        }

        return String(value);
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
        return value ? 'Yes' : 'No';
    }

    gameTypeLabel(gameType: string | null | undefined): string {
        switch (gameType) {
            case '0':
                return 'MPI';
            case '1':
                return 'Strategy';
            default:
                return this.display(gameType);
        }
    }

    launchStatus(lobby: BZ98LobbyView): string {
        return lobby.metaData?.launched === '1' ? 'In progress' : 'In lobby';
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

    userPlatform(user: BZ98User): string {
        if (user.isSteam) {
            return 'Steam';
        }

        if (user.isGOG) {
            return 'GOG';
        }

        if (user.isBB) {
            return 'Battlezone';
        }

        return this.display(user.authType);
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
     * Game settings arrive as a '*'-separated string. Returns null when the string is missing or
     * too short rather than producing an object full of undefined fields.
     */
    private parseGameSettings(settings: string | null | undefined): BZ98LobbyData | null {
        if (!settings) {
            return null;
        }

        const parts = settings.split('*');

        if (parts.length < GAME_SETTINGS_FIELD_COUNT) {
            return null;
        }

        return {
            mapFile: parts[1],
            crc32: parts[2],
            mod: parts[3],
            attributes: {
                lives: parts[8],
                satellite: Boolean(Number(parts[4])),
                barracks: Boolean(Number(parts[5])),
                sniper: Boolean(Number(parts[6])),
                splinter: Boolean(Number(parts[7]))
            }
        };
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
