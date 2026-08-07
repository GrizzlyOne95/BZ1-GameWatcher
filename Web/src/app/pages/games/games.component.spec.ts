import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { BZ98Lobby, BZ98User } from '../../models/bz98-lobby-info';
import { GamesComponent } from './games.component';

const LOBBIES_URL = `${environment.apiUrl}BZ98Lobby`;
const TIME_ZONE_STORAGE_KEY = 'bz98-display-time-zone';

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
        hasPassword: null,
        host: null,
        memberLimit: 10,
        metaData: null,
        stats: null,
        owner: null,
        userCount: 0,
        users: {},
        directJoinUrl: null,
        recentChat: [],
        ...overrides
    };
}

describe('GamesComponent', () => {
    let fixture: ComponentFixture<GamesComponent>;
    let httpMock: HttpTestingController;

    beforeEach(async () => {
        localStorage.removeItem(TIME_ZONE_STORAGE_KEY);

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

        expect(fixture.componentInstance.BZ98Lobbies[0].parsedStats).toBeNull();
        expect(fixture.componentInstance.BZ98Lobbies[0].stats).toBeNull();

        teardown();
    }));

    it('parses all documented game settings fields while preserving API stats', fakeAsync(() => {
        const apiStats = {
            mapFile: 'api-map.bzn',
            crc32: 'API',
            mod: 'api-mod',
            attributes: null
        };

        load([
            lobby({
                metaData: {
                    gameSettings: '78*bunker.bzn*ABC123*2299335165*1*0*1*180*5*8*1*25*0*'
                } as never,
                stats: apiStats
            })
        ]);

        const view = fixture.componentInstance.BZ98Lobbies[0];

        expect(view.parsedStats?.metaDataVersion).toBe(78);
        expect(view.parsedStats?.mapFile).toBe('bunker.bzn');
        expect(view.parsedStats?.crc32).toBe('ABC123');
        expect(view.parsedStats?.mod).toBe('2299335165');
        expect(view.parsedStats?.syncJoin).toBeTrue();
        expect(view.parsedStats?.timeLimit).toBe(180);
        expect(view.parsedStats?.playerLimit).toBe(8);
        expect(view.parsedStats?.killLimit).toBe(25);
        expect(view.parsedStats?.attributes?.satellite).toBeFalse();
        expect(view.parsedStats?.attributes?.barracks).toBeTrue();
        expect(view.parsedStats?.attributes?.lives).toBe('5');
        expect(view.parsedStats?.attributes?.sniper).toBeTrue();
        expect(view.parsedStats?.attributes?.splinter).toBeFalse();
        expect(view.apiStats).toEqual(apiStats);
        expect(view.stats).toBe(view.parsedStats);

        teardown();
    }));

    it('uses authType as the authoritative platform classification', () => {
        const component = fixture.componentInstance;

        expect(component.userPlatform(user({ authType: 'web', isGOG: true }))).toBe('Web');
        expect(component.userPlatform(user({ authType: 'gog', isSteam: true }))).toBe('GOG');
        expect(component.userPlatform(user({ authType: 'steam', isSteam: false }))).toBe('Steam');
        expect(component.userPlatform(user({ authType: 'custom' }))).toBe('custom');
    });

    it('does not silently convert unknown booleans or launch state to false', () => {
        const component = fixture.componentInstance;
        const unknownLobby = lobby({ metaData: { launched: null } as never });
        const endedLobby = lobby({ metaData: { launched: '1', gameEnded: '1' } as never });

        expect(component.yesNo(null)).toBe('Not reported');
        expect(component.launchStatus(unknownLobby as never)).toBe('Not reported');
        expect(component.launchStatus(endedLobby as never)).toBe('Ended');
    });

    it('builds a Steam Workshop link for numeric mod IDs', () => {
        expect(fixture.componentInstance.workshopUrl('2299335165'))
            .toBe('https://steamcommunity.com/sharedfiles/filedetails/?id=2299335165');
    });

    it('does not link stock or local mod labels', () => {
        expect(fixture.componentInstance.workshopUrl('stock')).toBeNull();
        expect(fixture.componentInstance.workshopUrl('')).toBeNull();
        expect(fixture.componentInstance.workshopUrl(null)).toBeNull();
    });

    it('shows an explicit mod link for a Workshop lobby', fakeAsync(() => {
        load([
            lobby({
                stats: {
                    mapFile: 'cell.bzn',
                    crc32: '20530842',
                    mod: '2299335165',
                    attributes: null
                }
            })
        ]);

        const modLink = [...fixture.nativeElement.querySelectorAll('a')]
            .find((anchor: HTMLAnchorElement) => anchor.textContent?.trim() === 'Link to mod') as HTMLAnchorElement | undefined;

        expect(modLink?.href).toContain('steamcommunity.com/sharedfiles/filedetails/?id=2299335165');

        teardown();
    }));

    it('shows a friendly name while preserving a known vehicle ODF code', () => {
        expect(fixture.componentInstance.vehicleLabel('bvrmpa')).toBe('Red Devil (bvrmpa)');
        expect(fixture.componentInstance.stockVehicle('BVRMPA.ODF')?.maxHealth).toBe(1800);
    });

    it('leaves unknown and modded vehicle codes unchanged', () => {
        expect(fixture.componentInstance.vehicleLabel('custom_tank')).toBe('custom_tank');
        expect(fixture.componentInstance.stockVehicle('custom_tank')).toBeNull();
        expect(fixture.componentInstance.vehicleLabel(null)).toBe('Not reported');
    });

    it('renders expandable ODF details and an attributed thumbnail for a known vehicle', fakeAsync(() => {
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
        const thumbnail = fixture.nativeElement.querySelector('img.vehicle-thumbnail') as HTMLImageElement | null;

        expect(text).toContain('Red Devil (bvrmpa)');
        expect(text).toContain('Known stock craft details: Red Devil');
        expect(thumbnail).not.toBeNull();
        expect(thumbnail?.alt).toBe('Red Devil craft render');
        expect(thumbnail?.closest('a')?.getAttribute('href')).toContain('battlezone.fandom.com');

        teardown();
    }));

    it('shows a Web chat-lobby owner, recent read-only chat, and no game-only metadata', fakeAsync(() => {
        const bridgeOwner = user({
            authType: 'web',
            id: 'B1000002',
            name: '!BRIDGE',
            isGOG: false,
            isSteam: false
        });

        load([
            lobby({
                id: 1004,
                isChat: true,
                owner: 'B1000002',
                host: bridgeOwner,
                userCount: 1,
                users: { B1000002: bridgeOwner },
                recentChat: [{
                    author: 'PilotOne',
                    speakerId: 'S123',
                    text: 'Anyone up for a game?',
                    timeUtc: '2026-08-07T02:00:00Z'
                }],
                metaData: {
                    gameVersion: '2.2.301',
                    gameType: '1',
                    launched: null,
                    name: 'default',
                    nextMid: null,
                    userCount: '1',
                    userPack: '!BRIDGE',
                    gameSettings: '*'
                }
            })
        ]);

        const component = fixture.componentInstance;
        const chatLobby = component.BZ98ChatLobbies[0];
        const pageText = fixture.nativeElement.textContent as string;

        expect(component.ownerDisplayName(chatLobby)).toBe('!BRIDGE');
        expect(component.userPlatform(bridgeOwner)).toBe('Web');
        expect(pageText).toContain('Owner:');
        expect(pageText).toContain('Last chat');
        expect(pageText).toContain('Recent chat');
        expect(pageText).toContain('PilotOne');
        expect(pageText).toContain('Anyone up for a game?');
        expect(pageText).toContain('Web visitors cannot send messages into Battlezone');
        expect(pageText).not.toContain('Lobby metadata');

        teardown();
    }));

    it('shows only the latest five chat lines by default while retaining older messages on demand', fakeAsync(() => {
        const recentChat = Array.from({ length: 7 }, (_, index) => ({
            author: `Pilot${index + 1}`,
            speakerId: `S${index + 1}`,
            text: `Message ${index + 1}`,
            timeUtc: `2026-08-07T02:0${index}:00Z`
        }));

        load([
            lobby({
                id: 1004,
                isChat: true,
                recentChat
            })
        ]);

        const previewLines = fixture.nativeElement.querySelectorAll('.chat-preview > .chat-lines > .chat-line') as NodeListOf<HTMLElement>;
        const historySummary = fixture.nativeElement.querySelector('.chat-history-details summary') as HTMLElement | null;

        expect(previewLines.length).toBe(5);
        expect(previewLines[0].textContent).toContain('Message 3');
        expect(previewLines[4].textContent).toContain('Message 7');
        expect(historySummary?.textContent).toContain('Show 2 older retained messages');

        teardown();
    }));

    it('shows password status without exposing a password value', fakeAsync(() => {
        load([
            lobby({ id: 42, hasPassword: true })
        ]);

        const pageText = fixture.nativeElement.textContent as string;
        expect(pageText).toContain('Passworded');
        expect(pageText).not.toContain('super-secret-password');

        teardown();
    }));

    it('formats timestamps in the selected time zone and persists the choice', () => {
        const component = fixture.componentInstance;
        const zone = component.timeZoneOptions.includes('America/New_York')
            ? 'America/New_York'
            : component.timeZoneOptions[0];
        const select = document.createElement('select');
        select.add(new Option(zone, zone));
        select.value = zone;

        component.selectTimeZone({ currentTarget: select } as unknown as Event);

        const expected = new Intl.DateTimeFormat(undefined, {
            year: 'numeric',
            month: 'short',
            day: 'numeric',
            hour: 'numeric',
            minute: '2-digit',
            second: '2-digit',
            timeZoneName: 'short',
            timeZone: zone
        }).format(new Date('2026-01-01T17:00:00Z'));

        expect(component.formatDateTime('2026-01-01T17:00:00Z')).toBe(expected);
        expect(localStorage.getItem(TIME_ZONE_STORAGE_KEY)).toBe(zone);
    });

    it('keeps polling after a failed request', fakeAsync(() => {
        fixture.detectChanges();
        tick();
        httpMock.expectOne(LOBBIES_URL).flush('boom', { status: 500, statusText: 'Server Error' });
        fixture.detectChanges();

        expect(fixture.componentInstance.loadFailed).toBeTrue();

        tick(environment.lobbyRefreshIntervalMs);
        httpMock.expectOne(LOBBIES_URL).flush([lobby({ id: 7 })]);
        fixture.detectChanges();

        expect(fixture.componentInstance.loadFailed).toBeFalse();
        expect(fixture.componentInstance.BZ98Lobbies.map(l => l.id)).toEqual([7]);

        teardown();
    }));
});
