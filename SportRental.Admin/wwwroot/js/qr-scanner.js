// Skaner kodów kreskowych (i QR — zachowane w kodzie, ale wyłączone z UI ze względów bezpieczeństwa)
// UWAGA: Kody QR mogą być łatwo podmienione (phishing). Używamy kodów kreskowych Code 128.
// Biblioteka html5-qrcode obsługuje oba formaty — QR_CODE zostawiony w formatsToSupport
// na wypadek przyszłej potrzeby, ale UI kieruje użytkownika wyłącznie na kody kreskowe.

window.downloadFile = function(fileName, contentType, base64Data) {
    const link = document.createElement('a');
    link.download = fileName;
    link.href = `data:${contentType};base64,${base64Data}`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
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
                return { success: false, error: 'Biblioteka QR nie załadowana' };
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
        
        try {
            // Dynamic qrbox — prostokątny (szerszy) dla kodów kreskowych
            const container = document.getElementById(this.elementId);
            const containerWidth = container ? container.clientWidth : 280;
            const containerHeight = container ? container.clientHeight : 200;
            const qrboxWidth = Math.max(150, Math.min(containerWidth - 40, 280));  // szerszy dla barcodes
            const qrboxHeight = Math.max(80, Math.min(containerHeight - 60, 120)); // niższy — kod kreskowy jest płaski
            
            const config = {
                fps: 10,
                qrbox: { width: qrboxWidth, height: qrboxHeight },
                // iOS Safari wymaga aspect ratio bliskiego 4:3 dla stabilnej pracy kamery
                aspectRatio: 4 / 3,
                formatsToSupport: [
                    Html5QrcodeSupportedFormats.CODE_128,
                    Html5QrcodeSupportedFormats.CODE_39,
                    Html5QrcodeSupportedFormats.EAN_13,
                    Html5QrcodeSupportedFormats.EAN_8,
                    Html5QrcodeSupportedFormats.QR_CODE
                ]
            };
            
            console.log('Starting with config:', config);
            
            const onSuccess = function(decodedText, decodedResult) {
                self.scanCount++;
                console.log('=== QR CODE DETECTED ===');
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
                // This fires constantly when no QR in frame - ignore
            };
            
            // iOS Safari fix: wymuszenie playsinline na elemencie video po starcie
            const fixIOSVideo = () => {
                const videos = container ? container.querySelectorAll('video') : [];
                videos.forEach(v => {
                    v.setAttribute('playsinline', 'true');
                    v.setAttribute('webkit-playsinline', 'true');
                    v.setAttribute('muted', 'true');
                    v.style.objectFit = 'cover';
                });
            };
            
            // Try back camera first (environment = tylna kamera)
            try {
                await this.scanner.start(
                    { facingMode: "environment" },
                    config,
                    onSuccess,
                    onError
                );
                fixIOSVideo();
                console.log('Camera started (environment)');
                return { success: true };
            } catch (envError) {
                console.log('Environment camera failed:', envError.message);
                
                // iOS Safari fallback: spróbuj exact environment, potem listę kamer
                try {
                    await this.scanner.start(
                        { facingMode: { exact: "environment" } },
                        config,
                        onSuccess,
                        onError
                    );
                    fixIOSVideo();
                    console.log('Camera started (exact environment)');
                    return { success: true };
                } catch (exactError) {
                    console.log('Exact environment failed:', exactError.message);
                }
                
                // Last resort: try any camera from list
                try {
                    const cameras = await Html5Qrcode.getCameras();
                    console.log('Available cameras:', cameras);
                    
                    if (cameras && cameras.length > 0) {
                        // Prefer back camera on iOS (usually last in list or contains 'back'/'environment')
                        const backCam = cameras.find(c => 
                            c.label && (c.label.toLowerCase().includes('back') || c.label.toLowerCase().includes('environment'))
                        ) || cameras[cameras.length - 1];
                        
                        await this.scanner.start(
                            backCam.id,
                            config,
                            onSuccess,
                            onError
                        );
                        fixIOSVideo();
                        console.log('Camera started:', backCam.label || backCam.id);
                        return { success: true };
                    }
                } catch (camError) {
                    console.error('Camera list failed:', camError);
                }
                
                throw envError;
            }
        } catch (error) {
            console.error('Start error:', error);
            
            if (error.name === 'NotAllowedError') {
                return { success: false, error: 'Brak uprawnień do kamery' };
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
