// Web Push helpers for the Notifications page (imported through Blazor JS interop).

const isStandalone = () =>
    window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;

const isSupported = () =>
    "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;

const isAppleMobile = () =>
    /iPhone|iPad|iPod/.test(navigator.userAgent)
    || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);

async function currentSubscription() {
    const registration = await navigator.serviceWorker.getRegistration("/");
    return registration ? await registration.pushManager.getSubscription() : null;
}

export async function getState() {
    const supported = isSupported();
    const subscription = supported ? await currentSubscription() : null;
    return {
        supported,
        standalone: isStandalone(),
        appleMobile: isAppleMobile(),
        permission: "Notification" in window ? Notification.permission : "unsupported",
        endpoint: subscription?.endpoint ?? null
    };
}

function toUint8Array(base64Url) {
    const padding = "=".repeat((4 - base64Url.length % 4) % 4);
    const raw = atob((base64Url + padding).replace(/-/g, "+").replace(/_/g, "/"));
    return Uint8Array.from(raw, character => character.charCodeAt(0));
}

// Must run directly from a tap: iOS only shows the permission prompt for a user gesture.
export async function subscribe(publicKey) {
    if (!isSupported()) {
        throw new Error("This browser does not support Web Push notifications.");
    }

    const permission = await Notification.requestPermission();
    if (permission !== "granted") {
        throw new Error(permission === "denied"
            ? "Notifications are blocked for Samwise. Allow them in the system settings and try again."
            : "Notification permission was not granted.");
    }

    const registration = await navigator.serviceWorker.register("/sw.js", { scope: "/" });
    await navigator.serviceWorker.ready;
    const existing = await registration.pushManager.getSubscription();
    const subscription = existing ?? await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: toUint8Array(publicKey)
    });
    const json = subscription.toJSON();
    return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
}

function describeDevice() {
    const agent = navigator.userAgent;
    const device = /iPhone/.test(agent) ? "iPhone"
        : /iPad/.test(agent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1) ? "iPad"
        : /Android/.test(agent) ? "Android"
        : /Windows/.test(agent) ? "Windows"
        : /Mac/.test(agent) ? "Mac"
        : "Browser";
    const browser = isStandalone() ? "Home Screen app"
        : /Edg\//.test(agent) ? "Edge"
        : /Chrome\//.test(agent) ? "Chrome"
        : /Firefox\//.test(agent) ? "Firefox"
        : /Safari\//.test(agent) ? "Safari"
        : "browser";
    return `${device} · ${browser}`;
}

// Binds the Enable button in JavaScript so the permission request runs inside the tap itself;
// a Blazor Server round trip would lose the user gesture iOS requires.
export function bindSubscribeButton(button, publicKey, dotNet) {
    button.onclick = async () => {
        button.disabled = true;
        try {
            const subscription = await subscribe(publicKey);
            await dotNet.invokeMethodAsync("OnSubscribed", { ...subscription, name: describeDevice() });
        } catch (error) {
            await dotNet.invokeMethodAsync("OnSubscribeFailed", error?.message ?? String(error));
        } finally {
            button.disabled = false;
        }
    };
}

export async function unsubscribe() {
    const subscription = isSupported() ? await currentSubscription() : null;
    if (!subscription) {
        return null;
    }

    const endpoint = subscription.endpoint;
    await subscription.unsubscribe();
    return endpoint;
}
