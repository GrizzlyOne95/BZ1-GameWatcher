import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { FontAwesomeModule } from '@fortawesome/angular-fontawesome';
import { faYoutube } from '@fortawesome/free-brands-svg-icons';
import { faGlobe } from '@fortawesome/free-solid-svg-icons';
import { environment } from '../../../environments/environment';
import { WatcherStatusComponent } from '../watcher-status/watcher-status.component';

/**
 * Site header. Previously this markup was duplicated in every page template, with the active
 * link maintained by hand and an href alongside routerLink that forced a full page reload.
 */
@Component({
    selector: 'app-site-nav',
    imports: [FontAwesomeModule, RouterLink, RouterLinkActive, WatcherStatusComponent],
    templateUrl: './site-nav.component.html'
})
export class SiteNavComponent {
    readonly faYouTube = faYoutube;
    readonly faCommunity = faGlobe;
    readonly youTubeUrl = environment.youTubeUrl;
    readonly communitySiteUrl = environment.communitySiteUrl;
}
