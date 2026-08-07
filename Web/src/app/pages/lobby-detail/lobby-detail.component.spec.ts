import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { BZ98Lobby, BZ98User } from '../../models/bz98-lobby-info';
import { LobbyDetailComponent } from './lobby-detail.component';

const LOBBY_URL = `${environment.apiUrl}BZ98Lobby/42`;
const HEALTH_URL = `${environment.apiUrl}health`;

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
        lobby: 42,
        metaData: null,
        name: null,
        stats: null,
        steamCleanId: null,
        steamImgUri: null,
        ...overrides
    };
}

function lobby(overrides: Partial<BZ98Lobby> = {}): BZ98Lobby {
    const host = user({
        authType: 'steam',
        id: 'S76561198000000000',
        name: 'HostPilot',
        isSteam: true,
        steamCleanId: '76561198000000000',
        metaData: { team: '1', vehicle: 'avtank' } as never
    });
    const webUser = user({
        authType: 'web',
        id: 'B1000002',
        name: '!BRIDGE',
        metaData: { team: '2', vehicle: null } as never
    });

    return {
        id: 42,
        clientVersion: '2.2.301',
        createdTime: '2026-08-07T02:00:00Z',
        isChat: false,
        isLocked: false,
        isPrivate: false,
        hasPassword: false,
        host,
        memberLimit: 8,
        metaData: {
            gameVersion: '2.2.301',
            gameSettings: '78*bunker.bzn*ABC*2299335165*1*0*1*30*5*8*1*20*0*',
            gameType: '1',
            launched: '0',
            name: 'Friday Night Battle',
            nextMid: null,
            userCount: '2',
            userPack: null
        },
        stats: {
            mapFile: 'bunker.bzn',
            crc32: 'ABC',
            mod: '2299335165',
            metaDataVersion: 78,
            syncJoin: true,
            timeLimit: 30,
            playerLimit: 8,
            killLimit: 20,
            attributes: {
                lives: '5',
                satellite: false,
                barracks: true,
                sniper: true,
                splinter: false
            }
        },
        owner: host.id,
        userCount: 2,
        users: {
            [host.id!]: host,
            [webUser.id!]: webUser
        },
        directJoinUrl: null,
        recentChat: [],
        ...overrides
    };
}

describe('LobbyDetailComponent', () => {
    let fixture: ComponentFixture<LobbyDetailComponent>;
    let httpMock: HttpTestingController;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [LobbyDetailComponent],
            providers: [
                provideRouter([]),
                provideHttpClient(),
                provideHttpClientTesting(),
                {
                    provide: ActivatedRoute,
                    useValue: { snapshot: { paramMap: convertToParamMap({ lobbyId: '42' }) } }
                }
            ]
        }).compileComponents();

        fixture = TestBed.createComponent(LobbyDetailComponent);
        httpMock = TestBed.inject(HttpTestingController);
    });

    function start(): void {
        fixture.detectChanges();
        tick();
        for (const request of httpMock.match(HEALTH_URL)) {
            request.flush({ status: 'ok', lobbyConnection: { state: 'connected', isConnected: true } });
        }
    }

    function teardown(): void {
        fixture.destroy();
        discardPeriodicTasks();
        httpMock.verify();
    }

    it('renders a stable lobby detail view with owner, rules, players, and platform mix', fakeAsync(() => {
        start();
        httpMock.expectOne(LOBBY_URL).flush(lobby());
        fixture.detectChanges();

        const text = fixture.nativeElement.textContent as string;
        expect(text).toContain('bunker.bzn');
        expect(text).toContain('HostPilot');
        expect(text).toContain('Strategy');
        expect(text).toContain('Steam 1');
        expect(text).toContain('Web 1');
        expect(text).toContain('Open Workshop item');
        expect(text).toContain('Join game');
        expect(fixture.componentInstance.ownerDisplayName(fixture.componentInstance.lobby!)).toBe('HostPilot');

        teardown();
    }));

    it('shows a clear closed-lobby state when the current lobby is no longer reported', fakeAsync(() => {
        start();
        httpMock.expectOne(LOBBY_URL).flush('missing', { status: 404, statusText: 'Not Found' });
        fixture.detectChanges();

        expect(fixture.componentInstance.notFound).toBeTrue();
        expect(fixture.nativeElement.textContent).toContain('Lobby no longer listed');
        expect(fixture.nativeElement.textContent).toContain('Lobby 42');

        teardown();
    }));

    it('keeps Web users classified as Web on the detail page', () => {
        expect(fixture.componentInstance.userPlatform(user({ authType: 'web', isGOG: true }))).toBe('Web');
    });
});
