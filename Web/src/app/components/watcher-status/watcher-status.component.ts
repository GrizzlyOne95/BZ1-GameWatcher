import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { EMPTY, Subject, catchError, exhaustMap, takeUntil } from 'rxjs';
import { environment } from '../../../environments/environment';
import { WatcherHealth } from '../../models/watcher-health';
import { BZ98Service } from '../../services/bz98.service';
import { visibilityAwareTimer } from '../../services/visibility-polling';

@Component({
    selector: 'app-watcher-status',
    imports: [CommonModule],
    templateUrl: './watcher-status.component.html',
    styleUrl: './watcher-status.component.scss'
})
export class WatcherStatusComponent implements OnInit, OnDestroy {
    private readonly destroyed = new Subject<void>();

    health: WatcherHealth | null = null;
    loadFailed = false;

    constructor(private readonly bz98Service: BZ98Service) {
    }

    ngOnInit(): void {
        // Production shows status immediately. The small development delay also keeps unrelated
        // component tests that embed the shared nav from acquiring an unexpected health request.
        const initialDelayMs = environment.production ? 0 : 5_000;

        visibilityAwareTimer(15_000, 60_000, initialDelayMs)
            .pipe(
                exhaustMap(() => this.bz98Service.getHealth().pipe(
                    catchError((error: unknown) => {
                        console.error('Failed to refresh watcher health.', error);
                        this.loadFailed = true;
                        return EMPTY;
                    })
                )),
                takeUntil(this.destroyed)
            )
            .subscribe(health => {
                this.health = health;
                this.loadFailed = false;
            });
    }

    ngOnDestroy(): void {
        this.destroyed.next();
        this.destroyed.complete();
    }

    get statusLabel(): string {
        if (this.loadFailed && !this.health) {
            return 'Status unavailable';
        }

        switch (this.health?.lobbyConnection?.state) {
            case 'connected':
                return 'Lobby service connected';
            case 'disconnected':
                return 'Lobby service reconnecting';
            default:
                return 'Connecting to lobby service';
        }
    }

    get statusClass(): string {
        if (this.loadFailed && !this.health) {
            return 'unknown';
        }

        switch (this.health?.lobbyConnection?.state) {
            case 'connected':
                return 'connected';
            case 'disconnected':
                return 'disconnected';
            default:
                return 'starting';
        }
    }

    get detailLabel(): string {
        const connection = this.health?.lobbyConnection;
        if (!connection) {
            return this.loadFailed ? 'Health endpoint unavailable' : 'Starting watcher';
        }

        if (connection.isConnected) {
            return connection.lastMessageUtc
                ? `Server message ${this.formatAge(connection.lastMessageUtc)}`
                : 'Socket connected';
        }

        return connection.lastDisconnectedUtc
            ? `Disconnected ${this.formatAge(connection.lastDisconnectedUtc)}`
            : 'Waiting to connect';
    }

    get tooltip(): string {
        const lastLobbyChange = this.health?.lastUpdatedUtc
            ? `Last lobby-list change: ${this.formatAbsolute(this.health.lastUpdatedUtc)}`
            : 'No lobby snapshot received yet';
        const lastMessage = this.health?.lobbyConnection?.lastMessageUtc
            ? `Last server message: ${this.formatAbsolute(this.health.lobbyConnection.lastMessageUtc)}`
            : 'No server message received yet';
        return `${this.statusLabel}. ${lastMessage}. ${lastLobbyChange}.`;
    }

    private formatAge(value: string): string {
        const time = new Date(value).getTime();
        if (!Number.isFinite(time)) {
            return 'recently';
        }

        const seconds = Math.max(0, Math.floor((Date.now() - time) / 1000));
        if (seconds < 60) {
            return `${seconds}s ago`;
        }
        if (seconds < 3600) {
            return `${Math.floor(seconds / 60)}m ago`;
        }
        return `${Math.floor(seconds / 3600)}h ago`;
    }

    private formatAbsolute(value: string): string {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
    }
}
