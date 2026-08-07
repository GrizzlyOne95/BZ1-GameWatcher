import { Component } from '@angular/core';
import { SiteNavComponent } from '../../components/site-nav/site-nav.component';

@Component({
    selector: 'app-about',
    imports: [SiteNavComponent],
    templateUrl: './about.component.html',
    styleUrl: './about.component.scss'
})
export class AboutComponent {
}
