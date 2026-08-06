import { Routes } from '@angular/router';
import { GamesComponent } from './pages/games/games.component';
import { JoinGameComponent } from './pages/join-game/join-game.component';
import { UnitDatabaseComponent } from './pages/unit-database/unit-database.component';

export const routes: Routes = [
    {
        path: 'games',
        component: GamesComponent
    },
    {
        path: 'units',
        component: UnitDatabaseComponent
    },
    {
        path: 'unit-database',
        redirectTo: '/units',
        pathMatch: 'full'
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
