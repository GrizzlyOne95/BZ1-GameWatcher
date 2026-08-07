import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { EMPTY, Subject, catchError, exhaustMap, takeUntil } from 'rxjs';
import { environment } from '../../../environments/environment';
import { SiteNavComponent } from '../../components/site-nav/site-nav.component';
import { ActivityRange, ActivityResponse, ActivitySample } from '../../models/activity';
import { BZ98Service } from '../../services/bz98.service';
import { visibilityAwareTimer } from '../../services/visibility-polling';

@Component({
    selector: 'app-activity',
    imports: [CommonModule, SiteNavComponent],
    templateUrl: './activity.component.html',
    styleUrl: './activity.component.scss'
})
export class ActivityComponent implements OnInit, OnDestroy {
    private readonly destroyed = new Subject<void>();

    readonly ranges: { value: ActivityRange; label: string }[] = [
        { value: '24h', label: '24 hours' },
        { value: '7d', label: '7 days' },
        { value: '30d', label: '30 days' }
    ];

    selectedRange: ActivityRange = '24h';
    activity: ActivityResponse | null = null;
    loading = true;
    loadFailed = false;

    readonly chartWidth = 1000;
    readonly chartHeight = 240;
    readonly chartPadding = 22;

    constructor(private readonly bz98Service: BZ98Service) {
    }

    ngOnInit(): void {
        const activeIntervalMs = Math.max(60_000, environment.lobbyRefreshIntervalMs);
        visibilityAwareTimer(activeIntervalMs, 5 * 60_000)
            .pipe(
                exhaustMap(() => this.bz98Service.getActivity(this.selectedRange).pipe(
                    catchError((error: unknown) => {
                        console.error('Failed to refresh activity history.', error);
                        this.loadFailed = true;
                        this.loading = false;
                        return EMPTY;
                    })
                )),
                takeUntil(this.destroyed)
            )
            .subscribe(activity => {
                this.activity = activity;
                this.loadFailed = false;
                this.loading = false;
            });
    }

    ngOnDestroy(): void {
        this.destroyed.next();
        this.destroyed.complete();
    }

    selectRange(range: ActivityRange): void {
        if (range === this.selectedRange) {
            return;
        }

        this.selectedRange = range;
        this.loading = true;
        this.bz98Service.getActivity(range)
            .pipe(
                takeUntil(this.destroyed),
                catchError((error: unknown) => {
                    console.error('Failed to change activity range.', error);
                    this.loadFailed = true;
                    this.loading = false;
                    return EMPTY;
                })
            )
            .subscribe(activity => {
                this.activity = activity;
                this.loadFailed = false;
                this.loading = false;
            });
    }

    get playerPoints(): string {
        return this.chartPoints(this.activity?.samples ?? [], sample => sample.playersOnline);
    }

    get gamePoints(): string {
        return this.chartPoints(this.activity?.samples ?? [], sample => sample.activeGames);
    }

    get chartMaximum(): number {
        const samples = this.activity?.samples ?? [];
        return Math.max(
            1,
            ...samples.map(sample => sample.playersOnline),
            ...samples.map(sample => sample.activeGames)
        );
    }

    formatDateTime(value: string | null | undefined): string {
        if (!value) {
            return 'Not available yet';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        return new Intl.DateTimeFormat(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: 'numeric',
            minute: '2-digit',
            timeZoneName: 'short'
        }).format(date);
    }

    formatAge(value: string | null | undefined): string {
        if (!value) {
            return 'Waiting for lobby data';
        }

        const ageSeconds = Math.max(0, Math.round((Date.now() - new Date(value).getTime()) / 1000));
        if (ageSeconds < 60) {
            return `${ageSeconds}s ago`;
        }
        if (ageSeconds < 3600) {
            return `${Math.floor(ageSeconds / 60)}m ago`;
        }
        return `${Math.floor(ageSeconds / 3600)}h ago`;
    }

    private chartPoints(samples: ActivitySample[], selector: (sample: ActivitySample) => number): string {
        if (samples.length === 0) {
            return '';
        }

        const usableWidth = this.chartWidth - this.chartPadding * 2;
        const usableHeight = this.chartHeight - this.chartPadding * 2;
        const maximum = this.chartMaximum;
        const denominator = Math.max(1, samples.length - 1);

        return samples.map((sample, index) => {
            const x = this.chartPadding + (index / denominator) * usableWidth;
            const y = this.chartHeight - this.chartPadding - (selector(sample) / maximum) * usableHeight;
            return `${x.toFixed(1)},${y.toFixed(1)}`;
        }).join(' ');
    }
}
