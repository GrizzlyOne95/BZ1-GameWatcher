import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ActivityRange, ActivityResponse } from '../models/activity';
import { BZ98Lobby } from '../models/bz98-lobby-info';
import { WatcherHealth } from '../models/watcher-health';

@Injectable({
    providedIn: 'root'
})
export class BZ98Service {
    constructor(private readonly httpClient: HttpClient) {
    }

    getBZ98Lobbies(): Observable<BZ98Lobby[]> {
        return this.httpClient.get<BZ98Lobby[]>(`${environment.apiUrl}BZ98Lobby`);
    }

    getBZ98Lobby(lobbyId: number | string): Observable<BZ98Lobby> {
        return this.httpClient.get<BZ98Lobby>(`${environment.apiUrl}BZ98Lobby/${lobbyId}`);
    }

    getActivity(range: ActivityRange): Observable<ActivityResponse> {
        return this.httpClient.get<ActivityResponse>(`${environment.apiUrl}activity`, {
            params: { range }
        });
    }

    getHealth(): Observable<WatcherHealth> {
        return this.httpClient.get<WatcherHealth>(`${environment.apiUrl}health`);
    }
}
