import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faArrowLeft, faCopy, faLink, faLock, faLockOpen, faPlay, faUser } from '@fortawesome/free-solid-svg-icons';
import { EMPTY, Subject, catchError, exhaustMap, takeUntil } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SiteNavComponent } from '../../components/site-nav/site-nav.component';
import { BZ98ChatMessage, BZ98Lobby, BZ98User } from '../../models/bz98-lobby-info';
import { BZ98Service } from '../../services/bz98.service';
import { buildSteamJoinUrl } from '../../services/steam-join';
import { visibilityAwareTimer } from '../../services/visibility-polling';

const TIME_ZONE_STORAGE_KEY = 'bz98-display-time-zone';

@Component({
    selector: 'app-lobby-detail',
    imports: [CommonModule, FontAwesomeModule, RouterLink, SiteNavComponent],
    templateUrl: './lobby-detail.component.html',
    styleUrl: './lobby-detail.component.scss'
})
export class LobbyDetailComponent implements OnInit, OnDestroy {
    private readonly destroyed = new Subject<void>();
    private shareResetTimer?: ReturnType<typeof setTimeout>;

    readonly faArrowLeft = faArrowLeft;
    readonly faCopy = faCopy;
    readonly faLink = faLink;
    readonly faLock = faLock;
    readonly faLockOpen = faLockOpen;
    readonly faPlay = faPlay;
    readonly faUser = faUser;

    lobbyId: number | null = null;
    lobby: BZ98Lobby | null = null;
    loading = true;
    loadFailed = false;
    notFound = false;
    shareCopied = false;

    private readonly selectedTimeZone = this.getStoredTimeZone();

    constructor(
        private readonly route: ActivatedRoute,
        private readonly router: Router,
        private readonly bz98Service: BZ98Service) {
    }

    ngOnInit(): void {
        const rawLobbyId = this.route.snapshot.paramMap.get('lobbyId');
        if (rawLobbyId === null || !/^\d+$/.test(rawLobbyId)) {
            void this.router.navigate(['/games']);
            return;
        }

        this.lobbyId = Number(rawLobbyId);

        visibilityAwareTimer(environment.lobbyRefreshIntervalMs, 60_000)
            .pipe(
                exhaustMap(() => this.bz98Service.getBZ98Lobby(rawLobbyId).pipe(
                    catchError((error: unknown) => {
                        this.loading = false;
                        if (error instanceof HttpErrorResponse && error.status === 404) {
                            this.notFound = true;
                            this.loadFailed = false;
                        } else {
                            this.loadFailed = true;
                        }
                        return EMPTY;
                    })
                )),
                takeUntil(this.destroyed)
            )
            .subscribe(lobby => {
                this.lobby = lobby;
                this.loading = false;
                this.loadFailed = false;
                this.notFound = false;
            });
    }

    ngOnDestroy(): void {
        this.destroyed.next();
        this.destroyed.complete();
        if (this.shareResetTimer !== undefined) {
            clearTimeout(this.shareResetTimer);
        }
    }

    get users(): BZ98User[] {
        return Object.values(this.lobby?.users ?? {});
    }

    get canJoin(): boolean {
        const lobby = this.lobby;
        return Boolean(lobby && !lobby.isChat && !lobby.isLocked && !lobby.isPrivate);
    }

    get platformSummary(): { label: string; count: number }[] {
        const counts = new Map<string, number>();
        for (const user of this.users) {
            const label = this.userPlatform(user);
            counts.set(label, (counts.get(label) ?? 0) + 1);
        }

        return [...counts.entries()]
            .map(([label, count]) => ({ label, count }))
            .sort((a, b) => b.count - a.count || a.label.localeCompare(b.label));
    }

    ownerUser(lobby: BZ98Lobby): BZ98User | null {
        const users = Object.values(lobby.users ?? {});
        if (lobby.owner) {
            const exactOwner = users.find(user => user.id === lobby.owner);
            if (exactOwner) {
                return exactOwner;
            }

            if (lobby.host?.id === lobby.owner) {
                return lobby.host;
            }
        }

        return lobby.host ?? null;
    }

    ownerDisplayName(lobby: BZ98Lobby): string {
        const owner = this.ownerUser(lobby);
        const reportedName = owner?.name?.trim();
        return reportedName || lobby.owner || 'Not reported';
    }

    ownerSteamProfileUrl(lobby: BZ98Lobby): string | null {
        const steamId = this.ownerUser(lobby)?.steamCleanId?.trim();
        return steamId ? `https://steamcommunity.com/profiles/${steamId}/` : null;
    }

    userPlatform(user: BZ98User): string {
        switch (user.authType?.trim().toLowerCase()) {
            case 'steam':
                return 'Steam';
            case 'gog':
                return 'GOG';
            case 'web':
                return 'Web';
            default:
                return user.authType?.trim() || 'Not reported';
        }
    }

    lobbyDisplayName(lobby: BZ98Lobby): string {
        const rawName = lobby.metaData?.name;
        if (!rawName) {
            return lobby.isChat ? `Chat lobby ${lobby.id}` : `Lobby ${lobby.id}`;
        }

        return rawName
            .replace(/^~game~(?:pub|pri)~\*?~/i, '')
            .replace(/^~chat~(?:pub|pri)~~/i, '') || rawName;
    }

    /**
     * The upstream metadata gameType flag is a validity state, not the actual multiplayer mode.
     * Actual Deathmatch/Strategy/MPI/etc. comes from the map metadata service when available.
     */
    gameTypeLabel(value: string | null | undefined): string {
        switch (value) {
            case '0':
                return 'Broken/invalid';
            case '1':
                return 'Valid';
            default:
                return this.display(value);
        }
    }

    mapTitle(lobby: BZ98Lobby): string {
        return lobby.map?.title?.trim() || this.display(lobby.stats?.mapFile);
    }

    mapModeLabel(lobby: BZ98Lobby): string {
        return lobby.map?.modeLabel?.trim() || 'Mode not resolved';
    }

    mapSourceLabel(lobby: BZ98Lobby): string {
        if (lobby.map?.isStock) {
            return 'Stock map';
        }

        const workshopId = lobby.workshop?.publishedFileId || this.numericWorkshopId(lobby.stats?.mod);
        if (workshopId) {
            return `Workshop ${workshopId}`;
        }

        const modId = lobby.map?.modId?.trim() || lobby.stats?.mod?.trim();
        return modId ? `Mod ${modId}` : 'Source not resolved';
    }

    mapPlayerRange(lobby: BZ98Lobby): string {
        const min = lobby.map?.minPlayers;
        const max = lobby.map?.maxPlayers;
        if (min && max) {
            return min === max ? `${min}` : `${min}–${max}`;
        }
        if (min) {
            return `${min}+`;
        }
        if (max) {
            return `Up to ${max}`;
        }
        return 'Not reported';
    }

    launchStatus(lobby: BZ98Lobby): string {
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

    workshopUrl(mod: string | null | undefined): string | null {
        const publishedFileId = this.numericWorkshopId(mod);
        return publishedFileId
            ? `https://steamcommunity.com/sharedfiles/filedetails/?id=${publishedFileId}`
            : null;
    }

    chatAuthor(message: BZ98ChatMessage): string {
        const reported = message.author?.trim();
        if (reported) {
            return reported;
        }

        if (message.speakerId) {
            const user = this.users.find(candidate => candidate.id === message.speakerId);
            return user?.name?.trim() || message.speakerId;
        }

        return 'Unknown';
    }

    hideBrokenImage(event: Event): void {
        const image = event.currentTarget as HTMLImageElement | null;
        if (image) {
            image.hidden = true;
        }
    }

    joinGame(): void {
        if (!this.canJoin || !this.lobby) {
            return;
        }

        window.location.href = this.lobby.directJoinUrl || buildSteamJoinUrl(this.lobby.id);
    }

    async copyLobbyLink(): Promise<void> {
        if (this.lobbyId === null) {
            return;
        }

        const url = `${window.location.origin}/lobby/${this.lobbyId}`;
        try {
            await navigator.clipboard.writeText(url);
        } catch {
            const textArea = document.createElement('textarea');
            textArea.value = url;
            textArea.setAttribute('readonly', '');
            textArea.style.position = 'absolute';
            textArea.style.left = '-9999px';
            document.body.appendChild(textArea);
            textArea.select();
            try {
                document.execCommand('copy');
            } finally {
                document.body.removeChild(textArea);
            }
        }

        this.shareCopied = true;
        if (this.shareResetTimer !== undefined) {
            clearTimeout(this.shareResetTimer);
        }
        this.shareResetTimer = setTimeout(() => this.shareCopied = false, 2000);
    }

    display(value: string | number | null | undefined): string {
        return value === null || value === undefined || value === '' ? 'Not reported' : String(value);
    }

    yesNo(value: boolean | null | undefined): string {
        if (value === null || value === undefined) {
            return 'Not reported';
        }
        return value ? 'Yes' : 'No';
    }

    formatDateTime(value: string | null | undefined): string {
        if (!value) {
            return 'Not reported';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        const options: Intl.DateTimeFormatOptions = {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
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
            delete options.timeZone;
            return new Intl.DateTimeFormat(undefined, options).format(date);
        }
    }

    private numericWorkshopId(value: string | null | undefined): string | null {
        const normalized = value?.trim();
        return normalized && /^[1-9]\d*$/.test(normalized) ? normalized : null;
    }

    private getStoredTimeZone(): string {
        try {
            return localStorage.getItem(TIME_ZONE_STORAGE_KEY) ?? '';
        } catch {
            return '';
        }
    }
}
