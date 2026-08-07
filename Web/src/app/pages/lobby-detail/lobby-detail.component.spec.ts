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
        workshop: {
            publishedFileId: '2299335165',
            title: 'Community Map Pack',
            previewUrl: 'https://example.test/workshop-preview.jpg',
            creatorSteamId: '76561198012345678',
            creatorProfileUrl: 'https://steamcommunity.com/profiles/76561198012345678/',
            workshopUrl: 'https://steamcommunity.com/sharedfiles/filedetails/?id=2299335165',
            updatedUtc: '2026-08-01T12:00:00Z',
            subscriptions: 1234
        },
        map: {
            mapFile: 'bunker.bzn',
            modId: '2299335165',
            isStock: false,
            title: 'Bunker Hill',
            imageUrl: 'https://example.test/map-preview.jpg',
            description: 'A strategy battlefield from the public BZ98R map catalog.',
            minPlayers: 2,
            maxPlayers: 8,
            typeCode: 'S',
            typeLabel: 'Strategy',
            modeCode: 'S',
            modeLabel: 'Strategy',
            customTypeCode: null,
            customTypeName: null
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

    it('renders owner, rules, platforms, Workshop metadata, and recognized map metadata', fakeAsync(() => {
        start();
        httpMock.expectOne(LOBBY_URL).flush(lobby());
        fixture.detectChanges();

        const text = fixture.nativeElement.textContent as string;
        const workshopPreview = fixture.nativeElement.querySelector('img[alt="Community Map Pack Workshop preview"]') as HTMLImageElement | null;
        const mapPreview = fixture.nativeElement.querySelector('img[alt="Bunker Hill map preview"]') as HTMLImageElement | null;
        expect(text).toContain('Bunker Hill');
        expect(text).toContain('bunker.bzn');
        expect(text).toContain('HostPilot');
        expect(text).toContain('Strategy');
        expect(text).toContain('Steam 1');
        expect(text).toContain('Web 1');
        expect(text).toContain('Community Map Pack');
        expect(text).toContain('Workshop 2299335165');
        expect(text).toContain('Players 2–8');
        expect(text).toContain('1234');
        expect(text).toContain('Join game');
        expect(workshopPreview?.src).toContain('workshop-preview.jpg');
        expect(mapPreview?.src).toContain('map-preview.jpg');
        expect(fixture.componentInstance.ownerDisplayName(fixture.componentInstance.lobby!)).toBe('HostPilot');
        expect(fixture.componentInstance.gameTypeLabel('1')).toBe('Valid');

        teardown();
    }));

    it('recognizes stock map metadata without inventing a Workshop source', fakeAsync(() => {
        const stock = lobby({
            workshop: null,
            stats: {
                mapFile: 'crater.bzn',
                crc32: 'STOCK',
                mod: '0',
                attributes: null
            },
            map: {
                mapFile: 'crater.bzn',
                modId: '0',
                isStock: true,
                title: 'The Crater',
                imageUrl: null,
                description: null,
                minPlayers: 2,
                maxPlayers: 6,
                typeCode: 'D',
                typeLabel: 'Deathmatch',
                modeCode: 'D',
                modeLabel: 'Deathmatch',
                customTypeCode: null,
                customTypeName: null
            }
        });

        start();
        httpMock.expectOne(LOBBY_URL).flush(stock);
        fixture.detectChanges();

        expect(fixture.componentInstance.mapSourceLabel(stock)).toBe('Stock map');
        expect(fixture.nativeElement.textContent).toContain('The Crater');
        expect(fixture.nativeElement.textContent).toContain('Stock map');
        expect(fixture.nativeElement.textContent).toContain('Deathmatch');

        teardown();
    }));

    it('falls back to raw map/mod data when optional enrichment is unavailable', fakeAsync(() => {
        start();
        httpMock.expectOne(LOBBY_URL).flush(lobby({ workshop: null, map: null }));
        fixture.detectChanges();

        const text = fixture.nativeElement.textContent as string;
        expect(text).toContain('bunker.bzn');
        expect(text).toContain('Mode not resolved');
        expect(text).toContain('2299335165');
        expect(text).toContain('Open Workshop item');
        expect(text).toContain('No external map metadata was resolved');
        expect(text).not.toContain('Community Map Pack');

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
