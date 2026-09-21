// Closes the phone "More" sheet after navigating. Enhanced navigation keeps the layout DOM,
// so an open popover would otherwise stay open over the next page.
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
})();
