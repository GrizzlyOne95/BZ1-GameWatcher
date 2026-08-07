import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { environment } from './environments/environment';

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));

// Keep local development free from service-worker caching. Production gets an intentionally small
// app-shell worker; it explicitly bypasses /api so live lobby/chat/activity data is never stale.
if (environment.production && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    void navigator.serviceWorker.register('/sw.js', { scope: '/' })
      .catch(error => console.warn('Unable to register the Game Watcher service worker.', error));
  });
}
