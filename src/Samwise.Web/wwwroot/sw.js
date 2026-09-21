// Samwise service worker. It exists only for Web Push and Home Screen installation: Blazor Server
// needs a live connection, so nothing is cached for offline use.

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));

self.addEventListener("push", event => {
    let payload = {};
    try {
        payload = event.data ? event.data.json() : {};
    } catch {
        payload = { body: event.data?.text() };
    }

    const title = payload.title || "Samwise";
    event.waitUntil(self.registration.showNotification(title, {
        body: payload.body || "",
        tag: payload.tag || undefined,
        icon: "/icons/icon-192.png",
        badge: "/icons/icon-192.png",
        data: { url: payload.url || "/board" }
    }));
});

self.addEventListener("notificationclick", event => {
    event.notification.close();
    const target = new URL(event.notification.data?.url || "/board", self.location.origin).href;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
        for (const client of windows) {
            if (new URL(client.url).origin === self.location.origin && "focus" in client) {
                await client.focus();
                if ("navigate" in client) {
                    await client.navigate(target);
                }
                return;
            }
        }

        await self.clients.openWindow(target);
    })());
});
