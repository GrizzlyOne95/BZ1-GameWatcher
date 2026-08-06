import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { BZ98Lobby, BZ98User } from '../../models/bz98-lobby-info';
import { GamesComponent } from './games.component';

const LOBBIES_URL = `${environment.apiUrl}BZ98Lobby`;

function user(overrides: Partial<BZ98User>): BZ98User {
    return {
        authType: null,
        clientVersion: null,
        id: null,
        isAdmin: false,
        isAuth: false,
        isBB: false,
        isDangerous: false,
        isInLounge: false,
        isGOG: false,
        isTest: false,
        isSteam: false,
        lobby: 0,
        metaData: null,
        name: null,
        stats: null,
        steamCleanId: null,
        steamImgUri: null,
        ...overrides
    };
}

function lobby(overrides: Partial<BZ98Lobby>): BZ98Lobby {
    return {
        id: 1,
        clientVersion: null,
        createdTime: '2024-01-01T00:00:00+00:00',
        isChat: false,
        isLocked: false,
        isPrivate: false,
        host: null,
        memberLimit: 10,
        metaData: null,
        stats: null,
        owner: null,
        userCount: 0,
        users: {},
        directJoinUrl: null,
        ...overrides
    };
}

describe('GamesComponent', () => {
    let fixture: ComponentFixture<GamesComponent>;
    let httpMock: HttpTestingController;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [GamesComponent],
            providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
        }).compileComponents();

        fixture = TestBed.createComponent(GamesComponent);
        httpMock = TestBed.inject(HttpTestingController);
    });

    function load(lobbies: BZ98Lobby[]): void {
        fixture.detectChanges();
        tick();
        httpMock.expectOne(LOBBIES_URL).flush(lobbies);
        fixture.detectChanges();
    }

    function teardown(): void {
        fixture.destroy();
        discardPeriodicTasks();
        httpMock.verify();
    }

    it('separates chat lobbies from game lobbies', fakeAsync(() => {
        load([
            lobby({ id: 1, isChat: false }),
            lobby({ id: 2, isChat: true })
        ]);

        expect(fixture.componentInstance.BZ98Lobbies.map(l => l.id)).toEqual([1]);
        expect(fixture.componentInstance.BZ98ChatLobbies.map(l => l.id)).toEqual([2]);

        teardown();
    }));

    it('splits users into odd, even, and unassigned team columns', fakeAsync(() => {
        load([
            lobby({
                users: {
                    S1: user({ name: 'odd', metaData: { team: '1' } as never }),
                    S2: user({ name: 'even', metaData: { team: '2' } as never }),
                    S3: user({ name: 'unassigned', metaData: null })
                }
            })
        ]);

        const view = fixture.componentInstance.BZ98Lobbies[0];

        expect(view.oddTeamUsers.map(u => u.name)).toEqual(['odd']);
        expect(view.evenTeamUsers.map(u => u.name)).toEqual(['even']);
        expect(view.unassignedTeamUsers.map(u => u.name)).toEqual(['unassigned']);

        teardown();
    }));

    it('ignores game settings that are too short to parse', fakeAsync(() => {
        load([
            lobby({ metaData: { gameSettings: '*' } as never, stats: null })
        ]);

        // Previously this produced a stats object full of undefined fields.
        expect(fixture.componentInstance.BZ98Lobbies[0].parsedStats).toBeNull();
        expect(fixture.componentInstance.BZ98Lobbies[0].stats).toBeNull();

        teardown();
    }));

    it('parses a full game settings string while preserving API stats', fakeAsync(() => {
        const apiStats = {
            mapFile: 'api-map.bzn',
            crc32: 'API',
            mod: 'api-mod',
            attributes: null
        };

        load([
            lobby({
                metaData: { gameSettings: 'x*bunker.bzn*ABC123*stock*1*0*1*0*5' } as never,
                stats: apiStats
            })
        ]);

        const view = fixture.componentInstance.BZ98Lobbies[0];

        expect(view.parsedStats?.mapFile).toBe('bunker.bzn');
        expect(view.parsedStats?.crc32).toBe('ABC123');
        expect(view.parsedStats?.attributes?.satellite).toBeTrue();
        expect(view.parsedStats?.attributes?.barracks).toBeFalse();
        expect(view.parsedStats?.attributes?.lives).toBe('5');
        expect(view.apiStats).toEqual(apiStats);
        expect(view.stats).toBe(view.parsedStats);

        teardown();
    }));

    it('builds a Steam Workshop link for numeric mod IDs', () => {
        expect(fixture.componentInstance.workshopUrl('2299335165'))
            .toBe('https://steamcommunity.com/sharedfiles/filedetails/?id=2299335165');
    });

    it('does not link stock or local mod labels', () => {
        expect(fixture.componentInstance.workshopUrl('stock')).toBeNull();
        expect(fixture.componentInstance.workshopUrl('')).toBeNull();
        expect(fixture.componentInstance.workshopUrl(null)).toBeNull();
    });

    it('shows a friendly name while preserving a known vehicle ODF code', () => {
        expect(fixture.componentInstance.vehicleLabel('bvrmpa')).toBe('Red Devil (bvrmpa)');
        expect(fixture.componentInstance.stockVehicle('BVRMPA.ODF')?.maxHealth).toBe(1800);
    });

    it('leaves unknown and modded vehicle codes unchanged', () => {
        expect(fixture.componentInstance.vehicleLabel('custom_tank')).toBe('custom_tank');
        expect(fixture.componentInstance.stockVehicle('custom_tank')).toBeNull();
        expect(fixture.componentInstance.vehicleLabel(null)).toBe('Not reported');
    });

    it('renders expandable ODF details for a known vehicle', fakeAsync(() => {
        load([
            lobby({
                userCount: 1,
                users: {
                    S1: user({
                        name: 'Pilot',
                        metaData: { team: '1', vehicle: 'bvrmpa' } as never
                    })
                }
            })
        ]);

        const text = fixture.nativeElement.textContent as string;
        expect(text).toContain('Red Devil (bvrmpa)');
        expect(text).toContain('Known stock craft details: Red Devil');

        teardown();
    }));

    it('keeps polling after a failed request', fakeAsync(() => {
        fixture.detectChanges();
        tick();
        httpMock.expectOne(LOBBIES_URL).flush('boom', { status: 500, statusText: 'Server Error' });
        fixture.detectChanges();

        expect(fixture.componentInstance.loadFailed).toBeTrue();

        // The previous implementation lost its subscription on the first error and never
        // recovered; the next tick must still issue a request.
        tick(environment.lobbyRefreshIntervalMs);
        httpMock.expectOne(LOBBIES_URL).flush([lobby({ id: 7 })]);
        fixture.detectChanges();

        expect(fixture.componentInstance.loadFailed).toBeFalse();
        expect(fixture.componentInstance.BZ98Lobbies.map(l => l.id)).toEqual([7]);

        teardown();
    }));
});
