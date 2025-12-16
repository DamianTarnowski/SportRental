// QR Scanner using html5-qrcode library

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
    
    init: async function(elementId, dotNetReference) {
        console.log('QrScanner.init:', elementId);
        this.dotNetRef = dotNetReference;
        this.elementId = elementId;
        
        try {
            if (typeof Html5Qrcode === 'undefined') {
                return { success: false, error: 'Biblioteka QR nie załadowana' };
            }
            
            const element = document.getElementById(elementId);
            if (!element) {
                return { success: false, error: 'Element nie znaleziony' };
            }
            
            this.scanner = new Html5Qrcode(elementId, { 
                verbose: false,
                formatsToSupport: [ Html5QrcodeSupportedFormats.QR_CODE ]
            });
            
            console.log('QrScanner initialized');
            return { success: true };
        } catch (error) {
            console.error('Init error:', error);
            return { success: false, error: error.message };
        }
    },
    
    start: async function(preferBackCamera = true) {
        console.log('QrScanner.start');
        
        if (!this.scanner) {
            return { success: false, error: 'Skaner nie zainicjalizowany' };
        }
        
        try {
            // Mniejsza ramka skanowania dla lepszego wykrywania
            const config = {
                fps: 15,
                qrbox: { width: 180, height: 180 },
                aspectRatio: 1.0,
                disableFlip: false,
                experimentalFeatures: {
                    useBarCodeDetectorIfSupported: true
                }
            };
            
            const cameraConfig = preferBackCamera 
                ? { facingMode: "environment" } 
                : { facingMode: "user" };
            
            await this.scanner.start(
                cameraConfig,
                config,
                (decodedText, decodedResult) => {
                    console.log('QR detected:', decodedText);
                    if (this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnQrCodeScanned', decodedText);
                    }
                },
                (errorMessage) => {
                    // Ignore - no QR in frame
                }
            );
            
            console.log('Camera started');
            return { success: true };
        } catch (error) {
            console.error('Start error:', error);
            
            // Fallback - try any camera
            try {
                const cameras = await Html5Qrcode.getCameras();
                if (cameras && cameras.length > 0) {
                    await this.scanner.start(
                        cameras[0].id,
                        { fps: 15, qrbox: { width: 180, height: 180 } },
                        (decodedText) => {
                            if (this.dotNetRef) {
                                this.dotNetRef.invokeMethodAsync('OnQrCodeScanned', decodedText);
                            }
                        },
                        () => {}
                    );
                    return { success: true };
                }
            } catch (e) {
                console.error('Fallback failed:', e);
            }
            
            if (error.name === 'NotAllowedError') {
                return { success: false, error: 'Brak uprawnień do kamery' };
            }
            return { success: false, error: error.message };
        }
    },
    
    stop: async function() {
        if (!this.scanner) return { success: true };
        
        try {
            const state = this.scanner.getState();
            if (state === Html5QrcodeScannerState.SCANNING) {
                await this.scanner.stop();
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
            if (capabilities && capabilities.torch) {
                const settings = this.scanner.getRunningTrackSettings();
                const newTorch = !(settings.torch || false);
                await this.scanner.applyVideoConstraints({ advanced: [{ torch: newTorch }] });
                return { success: true, flashOn: newTorch };
            }
            return { success: false, flashOn: false };
        } catch (error) {
            console.error('Flash error:', error);
            return { success: false, flashOn: false };
        }
    },
    
    dispose: async function() {
        await this.stop();
        if (this.scanner) {
            try { this.scanner.clear(); } catch (e) {}
            this.scanner = null;
        }
        this.dotNetRef = null;
    }
};
