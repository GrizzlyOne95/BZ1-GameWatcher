import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { ActivityResponse } from '../../models/activity';
import { ActivityComponent } from './activity.component';

const ACTIVITY_URL = `${environment.apiUrl}activity`;

function activityResponse(overrides: Partial<ActivityResponse> = {}): ActivityResponse {
    return {
        range: '24h',
        requestedSinceUtc: '2026-08-06T00:00:00Z',
        historyStartedUtc: '2026-08-06T00:00:00Z',
        lastHistoricalSampleUtc: '2026-08-06T01:00:00Z',
        lobbyDataUpdatedUtc: '2026-08-06T01:00:30Z',
        historyStorage: 'memory',
        durableHistory: false,
        current: {
            timeUtc: '2026-08-06T01:00:30Z',
            playersOnline: 3,
            activeGames: 2,
            gamesInProgress: 1,
            waitingRoomUsers: 2
        },
        summary: {
            peakPlayers: 5,
            averagePlayers: 2.5,
            peakActiveGames: 3,
            historicalSampleCount: 2
        },
        samples: [
            {
                timeUtc: '2026-08-06T00:00:00Z',
                playersOnline: 1,
                activeGames: 1,
                gamesInProgress: 0,
                waitingRoomUsers: 2
            },
            {
                timeUtc: '2026-08-06T01:00:00Z',
                playersOnline: 5,
                activeGames: 3,
                gamesInProgress: 2,
                waitingRoomUsers: 4
            }
        ],
        ...overrides
    };
}

describe('ActivityComponent', () => {
    let fixture: ComponentFixture<ActivityComponent>;
    let httpMock: HttpTestingController;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [ActivityComponent],
            providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
        }).compileComponents();

        fixture = TestBed.createComponent(ActivityComponent);
        httpMock = TestBed.inject(HttpTestingController);
    });

    function load(response = activityResponse()): void {
        fixture.detectChanges();
        tick();
        const request = httpMock.expectOne(req => req.url === ACTIVITY_URL && req.params.get('range') === '24h');
        request.flush(response);
        fixture.detectChanges();
    }

    function teardown(): void {
        fixture.destroy();
        discardPeriodicTasks();
        httpMock.verify();
    }

    it('renders current activity, storage state, and summary metrics', fakeAsync(() => {
        load();

        const text = fixture.nativeElement.textContent as string;
        const exportLink = fixture.nativeElement.querySelector('a[href="/api/activity/export"]') as HTMLAnchorElement | null;
        expect(text).toContain('Players online');
        expect(text).toContain('Peak players');
        expect(text).toContain('Average players');
        expect(text).toContain('History is not currently durable.');
        expect(text).toContain('Samples are process-local');
        expect(exportLink).not.toBeNull();
        expect(fixture.componentInstance.playerPoints.length).toBeGreaterThan(0);

        teardown();
    }));

    it('explains file-backed history that is not on durable storage', fakeAsync(() => {
        load(activityResponse({ historyStorage: 'file', durableHistory: false }));

        expect(fixture.nativeElement.textContent).toContain('Samples are written to a local file');

        teardown();
    }));

    it('requests a new range when the visitor changes the history window', fakeAsync(() => {
        load();

        fixture.componentInstance.selectRange('7d');
        const request = httpMock.expectOne(req => req.url === ACTIVITY_URL && req.params.get('range') === '7d');
        request.flush(activityResponse({ range: '7d' }));
        fixture.detectChanges();

        expect(fixture.componentInstance.selectedRange).toBe('7d');
        expect(fixture.componentInstance.activity?.range).toBe('7d');

        teardown();
    }));

    it('shows a warm-up message until two history samples exist', fakeAsync(() => {
        load(activityResponse({ samples: [activityResponse().samples[0]] }));

        expect(fixture.nativeElement.textContent).toContain('Activity history is warming up.');

        teardown();
    }));
});
