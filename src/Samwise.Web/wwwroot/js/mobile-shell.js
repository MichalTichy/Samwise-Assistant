// Phone shell behavior. Closes the "More" sheet after navigating: enhanced navigation keeps the
// layout DOM, so an open popover would otherwise stay open over the next page.
(() => {
    const hideSheet = () => {
        const sheet = document.getElementById("mobile-more-sheet");
        if (sheet?.matches(":popover-open")) {
            sheet.hidePopover();
        }
    };

    document.addEventListener("click", event => {
        if (event.target instanceof Element && event.target.closest(".mobile-sheet a")) {
            hideSheet();
        }
    });

    window.Blazor?.addEventListener?.("enhancedload", hideSheet);

    // Keeping the service worker registered lets the Home Screen app receive Web Push.
    if ("serviceWorker" in navigator) {
        navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => { });
    }
})();
