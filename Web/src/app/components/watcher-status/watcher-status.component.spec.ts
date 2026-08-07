import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { WatcherHealth } from '../../models/watcher-health';
import { WatcherStatusComponent } from './watcher-status.component';

const HEALTH_URL = `${environment.apiUrl}health`;

function health(overrides: Partial<WatcherHealth> = {}): WatcherHealth {
    return {
        status: 'ok',
        lobbyCount: 2,
        lastUpdatedUtc: '2026-08-07T04:00:00Z',
        lobbyConnection: {
            state: 'connected',
            isConnected: true,
            lastConnectedUtc: '2026-08-07T04:00:00Z',
            lastDisconnectedUtc: null,
            lastMessageUtc: new Date().toISOString()
        },
        activityHistoryStartedUtc: null,
        activityLastSampleUtc: null,
        activityStorage: 'memory',
        activityDurable: false,
        ...overrides
    };
}

describe('WatcherStatusComponent', () => {
    let fixture: ComponentFixture<WatcherStatusComponent>;
    let httpMock: HttpTestingController;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [WatcherStatusComponent],
            providers: [provideHttpClient(), provideHttpClientTesting()]
        }).compileComponents();

        fixture = TestBed.createComponent(WatcherStatusComponent);
        httpMock = TestBed.inject(HttpTestingController);
    });

    function beginPoll(): void {
        fixture.detectChanges();
        tick(environment.production ? 0 : 5_000);
    }

    function teardown(): void {
        fixture.destroy();
        discardPeriodicTasks();
        httpMock.verify();
    }

    it('shows a healthy connected websocket separately from lobby-list mutation age', fakeAsync(() => {
        beginPoll();
        httpMock.expectOne(HEALTH_URL).flush(health());
        fixture.detectChanges();

        const text = fixture.nativeElement.textContent as string;
        expect(text).toContain('Lobby service connected');
        expect(text).toContain('Server message');
        expect(fixture.componentInstance.tooltip).toContain('Last lobby-list change:');
        expect(fixture.nativeElement.querySelector('.watcher-status.connected')).not.toBeNull();

        teardown();
    }));

    it('shows reconnecting when the primary lobby websocket is disconnected', fakeAsync(() => {
        beginPoll();
        httpMock.expectOne(HEALTH_URL).flush(health({
            lobbyConnection: {
                state: 'disconnected',
                isConnected: false,
                lastConnectedUtc: '2026-08-07T04:00:00Z',
                lastDisconnectedUtc: new Date().toISOString(),
                lastMessageUtc: '2026-08-07T04:00:00Z'
            }
        }));
        fixture.detectChanges();

        const text = fixture.nativeElement.textContent as string;
        expect(text).toContain('Lobby service reconnecting');
        expect(text).toContain('Disconnected');
        expect(fixture.nativeElement.querySelector('.watcher-status.disconnected')).not.toBeNull();

        teardown();
    }));
});
