import { Routes } from '@angular/router';
import { AboutComponent } from './pages/about/about.component';
import { ActivityComponent } from './pages/activity/activity.component';
import { GamesComponent } from './pages/games/games.component';
import { JoinGameComponent } from './pages/join-game/join-game.component';
import { LobbyDetailComponent } from './pages/lobby-detail/lobby-detail.component';
import { UnitDatabaseComponent } from './pages/unit-database/unit-database.component';

export const routes: Routes = [
    {
        path: 'games',
        component: GamesComponent
    },
    {
        path: 'activity',
        component: ActivityComponent
    },
    {
        path: 'units',
        component: UnitDatabaseComponent
    },
    {
        path: 'about',
        component: AboutComponent
    },
    {
        path: 'unit-database',
        redirectTo: '/units',
        pathMatch: 'full'
    },
    {
        path: 'lobby/:lobbyId',
        component: LobbyDetailComponent
    },
    {
        path: 'join/:lobbyId',
        component: JoinGameComponent
    },
    {
        path: '',
        redirectTo: '/games',
        pathMatch: 'full'
    },
    {
        path: '**',
        redirectTo: '/games',
        pathMatch: 'full'
    }
];
