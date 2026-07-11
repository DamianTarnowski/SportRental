window.rentSpotHomeLayout = (() => {
    let scrollHandler = null;
    let componentRef = null;

    function detachScroll() {
        if (scrollHandler) {
            window.removeEventListener('scroll', scrollHandler);
        }
        scrollHandler = null;
        componentRef = null;
    }

    function attachScroll(dotNetRef) {
        detachScroll();
        componentRef = dotNetRef;
        scrollHandler = () => {
            componentRef?.invokeMethodAsync('SetScrolled', window.scrollY > 50);
        };
        window.addEventListener('scroll', scrollHandler, { passive: true });
        scrollHandler();
    }

    return { attachScroll, detachScroll };
})();
