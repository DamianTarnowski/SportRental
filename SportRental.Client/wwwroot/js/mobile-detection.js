// Mobile detection helper for Blazor WASM
(function() {
    let dotNetRef = null;
    let mediaQuery = null;
    
    const MOBILE_BREAKPOINT = 768;
    
    function checkMobile() {
        return window.innerWidth < MOBILE_BREAKPOINT;
    }
    
    function notifyDotNet() {
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnScreenResize', checkMobile());
        }
    }
    
    window.setupMobileDetection = function(ref) {
        dotNetRef = ref;
        
        // Initial check
        notifyDotNet();
        
        // Setup media query listener
        mediaQuery = window.matchMedia(`(max-width: ${MOBILE_BREAKPOINT - 1}px)`);
        mediaQuery.addEventListener('change', notifyDotNet);
        
        // Also listen to resize for edge cases
        window.addEventListener('resize', notifyDotNet);
    };
    
    window.removeMobileDetection = function() {
        if (mediaQuery) {
            mediaQuery.removeEventListener('change', notifyDotNet);
        }
        window.removeEventListener('resize', notifyDotNet);
        dotNetRef = null;
    };
})();
