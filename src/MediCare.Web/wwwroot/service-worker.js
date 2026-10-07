// MediCare Progressive Web App (PWA) Service Worker
const CACHE_NAME = 'medicare-cache-v1';
const STATIC_ASSETS = [
    '/',
    '/manifest.json',
    '/css/site.css',
    '/images/pwa-icon-192.png',
    '/images/pwa-icon-512.png',
    '/lib/bootstrap/dist/css/bootstrap.min.css',
    '/lib/bootstrap/dist/js/bootstrap.bundle.min.js'
];

// Install Event - Pre-cache core shell
self.addEventListener('install', (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => {
            return cache.addAll(STATIC_ASSETS).catch((err) => {
                console.warn('PWA: Failed to cache some static assets during install', err);
            });
        })
    );
    self.skipWaiting();
});

// Activate Event - Clean up obsolete caches
self.addEventListener('activate', (event) => {
    event.waitUntil(
        caches.keys().then((keys) => {
            return Promise.all(
                keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))
            );
        })
    );
    self.clients.claim();
});

// Fetch Event - Network First with Cache Fallback for navigation, Cache First for static
self.addEventListener('fetch', (event) => {
    const request = event.request;

    // Skip non-GET requests or browser extension URLs
    if (request.method !== 'GET' || !request.url.startsWith(self.location.origin)) {
        return;
    }

    // Static Assets: Cache First
    if (request.url.includes('/css/') || request.url.includes('/images/') || request.url.includes('/lib/')) {
        event.respondWith(
            caches.match(request).then((cachedResponse) => {
                if (cachedResponse) {
                    return cachedResponse;
                }
                return fetch(request).then((networkResponse) => {
                    if (networkResponse && networkResponse.status === 200) {
                        const copy = networkResponse.clone();
                        caches.open(CACHE_NAME).then((cache) => cache.put(request, copy));
                    }
                    return networkResponse;
                });
            })
        );
        return;
    }

    // Navigation / HTML pages: Network First with fallback
    event.respondWith(
        fetch(request)
            .then((networkResponse) => {
                return networkResponse;
            })
            .catch(() => {
                return caches.match(request).then((cachedResponse) => {
                    if (cachedResponse) {
                        return cachedResponse;
                    }
                    return caches.match('/');
                });
            })
    );
});
