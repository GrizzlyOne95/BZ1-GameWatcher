const CACHE_VERSION = 'bz98-game-watcher-v1';
const SHELL_CACHE = `${CACHE_VERSION}:shell`;
const RUNTIME_CACHE = `${CACHE_VERSION}:runtime`;

// Keep the precache intentionally small. Live lobby/activity data is never placed in Cache Storage.
const SHELL_ASSETS = [
  '/',
  '/games',
  '/manifest.json',
  '/favicon.ico',
  '/poster.jpg',
  '/steam-icon.png'
];

self.addEventListener('install', event => {
  event.waitUntil((async () => {
    const cache = await caches.open(SHELL_CACHE);
    await Promise.allSettled(SHELL_ASSETS.map(asset => cache.add(asset)));
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    const names = await caches.keys();
    await Promise.all(
      names
        .filter(name => name.startsWith('bz98-game-watcher-') && name !== SHELL_CACHE && name !== RUNTIME_CACHE)
        .map(name => caches.delete(name))
    );
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET') {
    return;
  }

  const url = new URL(request.url);

  // Never cache API responses. Lobby state, chat, health, and activity must always come from the
  // live API rather than a service-worker snapshot. Cross-origin map/Workshop/avatar images are
  // also left to the browser's normal HTTP cache instead of being retained by this worker.
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/')) {
    return;
  }

  if (request.mode === 'navigate') {
    event.respondWith(networkFirstNavigation(request));
    return;
  }

  const isShellAsset = SHELL_ASSETS.includes(url.pathname);
  const isBuildAsset = request.destination === 'script' ||
    request.destination === 'style' ||
    request.destination === 'font';

  if (isShellAsset || isBuildAsset) {
    event.respondWith(cacheFirstStatic(request));
  }
});

async function networkFirstNavigation(request) {
  try {
    const response = await fetch(request);
    if (response.ok) {
      const cache = await caches.open(SHELL_CACHE);
      await cache.put('/', response.clone());
    }
    return response;
  } catch {
    return (await caches.match(request)) ||
      (await caches.match('/')) ||
      new Response('Battlezone 98 Redux Game Watcher is offline.', {
        status: 503,
        headers: { 'Content-Type': 'text/plain; charset=utf-8' }
      });
  }
}

async function cacheFirstStatic(request) {
  const cached = await caches.match(request);
  if (cached) {
    return cached;
  }

  const response = await fetch(request);
  if (response.ok) {
    const cache = await caches.open(RUNTIME_CACHE);
    await cache.put(request, response.clone());
  }
  return response;
}
