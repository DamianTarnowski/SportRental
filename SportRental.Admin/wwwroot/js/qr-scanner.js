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
            
            // Create scanner without format restriction for better detection
            this.scanner = new Html5Qrcode(elementId, { verbose: true });
            
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
            // Simpler config - let library handle sizing
            const config = {
                fps: 10,
                qrbox: 200,
                aspectRatio: 1.0
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
            
            // Try back camera first
            try {
                await this.scanner.start(
                    { facingMode: "environment" },
                    config,
                    onSuccess,
                    onError
                );
                console.log('Camera started (environment)');
                return { success: true };
            } catch (envError) {
                console.log('Environment camera failed:', envError.message);
                
                // Try any camera
                try {
                    const cameras = await Html5Qrcode.getCameras();
                    console.log('Available cameras:', cameras);
                    
                    if (cameras && cameras.length > 0) {
                        await this.scanner.start(
                            cameras[0].id,
                            config,
                            onSuccess,
                            onError
                        );
                        console.log('Camera started (first available)');
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
