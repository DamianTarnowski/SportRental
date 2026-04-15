// Skaner kodów kreskowych (i QR — zachowane w kodzie, ale wyłączone z UI ze względów bezpieczeństwa)
// UWAGA: Kody QR mogą być łatwo podmienione (phishing). Używamy kodów kreskowych Code 128.
// Biblioteka html5-qrcode obsługuje oba formaty — QR_CODE zostawiony w formatsToSupport
// na wypadek przyszłej potrzeby, ale UI kieruje użytkownika wyłącznie na kody kreskowe.

window.downloadFile = function(fileName, contentType, base64Data) {
    const byteCharacters = atob(base64Data);
    const byteNumbers = new Array(byteCharacters.length);

    for (let i = 0; i < byteCharacters.length; i++) {
        byteNumbers[i] = byteCharacters.charCodeAt(i);
    }

    const blob = new Blob([new Uint8Array(byteNumbers)], { type: contentType });
    const url = URL.createObjectURL(blob);

    try {
        const link = document.createElement('a');
        link.download = fileName;
        link.href = url;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    } finally {
        URL.revokeObjectURL(url);
    }
};

window.downloadFileFromStream = async function(fileName, contentType, contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: contentType });
    const url = URL.createObjectURL(blob);

    try {
        const link = document.createElement('a');
        link.download = fileName;
        link.href = url;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    } finally {
        URL.revokeObjectURL(url);
    }
};

window.QrScanner = {
    scanner: null,
    dotNetRef: null,
    elementId: null,
    scanCount: 0,
    
    init: async function(elementId, dotNetReference) {
        console.log('QrScanner.init:', elementId);
        this.dotNetRef = dotNetReference;
        this.elementId = elementId;
        this.scanCount = 0;
        
        try {
            if (typeof Html5Qrcode === 'undefined') {
                console.error('Html5Qrcode not loaded!');
                return { success: false, error: 'Biblioteka skanera nie załadowana' };
            }
            
            const element = document.getElementById(elementId);
            if (!element) {
                console.error('Element not found:', elementId);
                return { success: false, error: 'Element nie znaleziony' };
            }
            
            // Formaty: priorytet na kody kreskowe, QR zachowany w razie potrzeby
            this.scanner = new Html5Qrcode(elementId, {
                verbose: false,
                formatsToSupport: [
                    Html5QrcodeSupportedFormats.CODE_128,
                    Html5QrcodeSupportedFormats.CODE_39,
                    Html5QrcodeSupportedFormats.EAN_13,
                    Html5QrcodeSupportedFormats.EAN_8,
                    Html5QrcodeSupportedFormats.QR_CODE  // zachowane, ale UI kieruje na kody kreskowe
                ]
            });
            
            console.log('QrScanner initialized successfully');
            return { success: true };
        } catch (error) {
            console.error('Init error:', error);
            return { success: false, error: error.message };
        }
    },
    
    start: async function(preferBackCamera = true) {
        console.log('QrScanner.start called');

        if (!this.scanner) {
            console.error('Scanner not initialized');
            return { success: false, error: 'Skaner nie zainicjalizowany' };
        }

        const self = this;
        const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent) ||
            (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
        console.log('iOS detected:', isIOS);

        try {
            const container = document.getElementById(this.elementId);

            // Dla kodów 1D (Code 128/39/EAN) qrbox = cała klatka daje najlepszą detekcję.
            // html5-qrcode skanuje tylko obszar qrbox; jeśli kod wychodzi poza pasek,
            // nie zostanie rozpoznany. Pełna klatka = większa szansa na trafienie.
            const config = {
                fps: 15,
                disableFlip: false,
                experimentalFeatures: {
                    // Natywne BarcodeDetector API (iOS 17+, Chrome) jest ~10× szybsze
                    // i dokładniejsze niż ZXing fallback.
                    useBarCodeDetectorIfSupported: true
                },
                videoConstraints: {
                    facingMode: "environment",
                    width: { ideal: 1920 },
                    height: { ideal: 1080 },
                },
            };

            // iOS Safari: aspectRatio w constraints powoduje OverconstrainedError
            if (!isIOS) {
                config.aspectRatio = 4 / 3;
            }

            console.log('Starting with config:', config);

            const onSuccess = function(decodedText, decodedResult) {
                self.scanCount++;
                console.log('=== CODE DETECTED ===');
                console.log('Text:', decodedText);
                console.log('Format:', decodedResult?.result?.format?.formatName);
                console.log('Scan count:', self.scanCount);

                if (self.dotNetRef) {
                    console.log('Calling Blazor callback...');
                    self.dotNetRef.invokeMethodAsync('OnQrCodeScanned', decodedText)
                        .then(() => console.log('Blazor callback success'))
                        .catch(err => console.error('Blazor callback error:', err));
                } else {
                    console.error('No dotNetRef available!');
                }
            };

            const onError = function(errorMessage) {
                // This fires constantly when no code is in frame - ignore
            };

            // iOS Safari fix: MutationObserver ustawia playsinline ZANIM video zacznie grać.
            // Bez tego iOS Safari próbuje otworzyć video fullscreen i failuje.
            let observer = null;
            if (container) {
                observer = new MutationObserver((mutations) => {
                    for (const mutation of mutations) {
                        for (const node of mutation.addedNodes) {
                            if (node.nodeName === 'VIDEO' || (node.querySelectorAll && node.querySelectorAll('video').length)) {
                                const videos = node.nodeName === 'VIDEO' ? [node] : node.querySelectorAll('video');
                                videos.forEach(v => {
                                    v.setAttribute('playsinline', 'true');
                                    v.setAttribute('muted', 'true');
                                    v.style.objectFit = 'cover';
                                });
                            }
                        }
                    }
                });
                observer.observe(container, { childList: true, subtree: true });

                // Ustaw też na istniejących video (gdyby już były)
                container.querySelectorAll('video').forEach(v => {
                    v.setAttribute('playsinline', 'true');
                    v.setAttribute('muted', 'true');
                    v.style.objectFit = 'cover';
                });
            }

            const cleanupObserver = () => {
                if (observer) { observer.disconnect(); observer = null; }
            };

            // Strategia uruchamiania kamery — od najmniej do najbardziej restrykcyjnej
            const strategies = [
                { label: 'environment', cameraId: { facingMode: "environment" } },
                { label: 'camera list', cameraId: null }, // resolved dynamically below
            ];

            let lastError = null;

            for (const strategy of strategies) {
                try {
                    let cameraId = strategy.cameraId;

                    if (cameraId === null) {
                        // Pobierz listę kamer i wybierz tylną
                        const cameras = await Html5Qrcode.getCameras();
                        console.log('Available cameras:', cameras);
                        if (!cameras || cameras.length === 0) continue;

                        const backCam = cameras.find(c =>
                            c.label && (c.label.toLowerCase().includes('back') || c.label.toLowerCase().includes('environment'))
                        ) || cameras[cameras.length - 1];
                        cameraId = backCam.id;
                    }

                    await this.scanner.start(cameraId, config, onSuccess, onError);
                    console.log('Camera started (' + strategy.label + ')');
                    cleanupObserver();
                    return { success: true };
                } catch (err) {
                    console.log(strategy.label + ' failed:', err.message);
                    lastError = err;
                }
            }

            cleanupObserver();
            throw lastError || new Error('Nie udało się uruchomić kamery');
        } catch (error) {
            console.error('Start error:', error);

            if (error.name === 'NotAllowedError') {
                return { success: false, error: 'Brak uprawnień do kamery. Sprawdź Ustawienia > Safari > Kamera.' };
            }
            if (error.name === 'NotFoundError') {
                return { success: false, error: 'Nie znaleziono kamery na tym urządzeniu.' };
            }
            if (error.name === 'OverconstrainedError') {
                return { success: false, error: 'Kamera nie obsługuje wymaganych parametrów.' };
            }
            return { success: false, error: error.message };
        }
    },
    
    stop: async function() {
        console.log('QrScanner.stop called');
        if (!this.scanner) return { success: true };
        
        try {
            const state = this.scanner.getState();
            console.log('Scanner state:', state);
            if (state === Html5QrcodeScannerState.SCANNING) {
                await this.scanner.stop();
                console.log('Scanner stopped');
            }
            return { success: true };
        } catch (error) {
            console.error('Stop error:', error);
            return { success: false };
        }
    },
    
    toggleFlash: async function() {
        if (!this.scanner) {
            return { success: false, flashOn: false };
        }
        
        try {
            const capabilities = this.scanner.getRunningTrackCapabilities();
            console.log('Camera capabilities:', capabilities);
            
            if (capabilities && capabilities.torch) {
                const settings = this.scanner.getRunningTrackSettings();
                const newTorch = !(settings.torch || false);
                await this.scanner.applyVideoConstraints({ advanced: [{ torch: newTorch }] });
                console.log('Flash toggled to:', newTorch);
                return { success: true, flashOn: newTorch };
            }
            console.log('Flash not available');
            return { success: false, flashOn: false };
        } catch (error) {
            console.error('Flash error:', error);
            return { success: false, flashOn: false };
        }
    },
    
    dispose: async function() {
        console.log('QrScanner.dispose called');
        await this.stop();
        if (this.scanner) {
            try { this.scanner.clear(); } catch (e) {}
            this.scanner = null;
        }
        this.dotNetRef = null;
    }
};

console.log('QrScanner module loaded');
