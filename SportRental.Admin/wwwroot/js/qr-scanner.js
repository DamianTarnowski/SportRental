// QR Scanner using html5-qrcode library
// CDN: https://unpkg.com/html5-qrcode@2.3.8/html5-qrcode.min.js

// Download file helper
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
    
    // Initialize scanner
    init: async function(elementId, dotNetReference) {
        console.log('QrScanner.init called with elementId:', elementId);
        this.dotNetRef = dotNetReference;
        this.elementId = elementId;
        
        try {
            // Check if html5QrCode is loaded
            if (typeof Html5Qrcode === 'undefined') {
                console.error('Html5Qrcode library not loaded');
                return { success: false, error: 'Biblioteka skanera QR nie została załadowana' };
            }
            
            // Check if element exists
            const element = document.getElementById(elementId);
            if (!element) {
                console.error('Element not found:', elementId);
                return { success: false, error: 'Element skanera nie został znaleziony' };
            }
            
            this.scanner = new Html5Qrcode(elementId, { 
                verbose: false,
                formatsToSupport: [ Html5QrcodeSupportedFormats.QR_CODE ]
            });
            
            console.log('QrScanner initialized successfully');
            return { success: true };
        } catch (error) {
            console.error('Failed to initialize QR scanner:', error);
            return { success: false, error: error.message };
        }
    },
    
    // Start scanning with camera
    start: async function(preferBackCamera = true) {
        console.log('QrScanner.start called');
        
        if (!this.scanner) {
            return { success: false, error: 'Skaner nie został zainicjalizowany' };
        }
        
        try {
            // Get element dimensions for responsive config
            const element = document.getElementById(this.elementId);
            const width = element ? element.offsetWidth : 300;
            const qrboxSize = Math.min(250, width - 50);
            
            const config = {
                fps: 10,
                qrbox: { width: qrboxSize, height: qrboxSize },
                aspectRatio: 1.0,
                showTorchButtonIfSupported: false,
                showZoomSliderIfSupported: false,
                defaultZoomValueIfSupported: 2
            };
            
            const cameraFacing = preferBackCamera ? "environment" : "user";
            
            console.log('Starting camera with config:', config);
            
            await this.scanner.start(
                { facingMode: cameraFacing },
                config,
                (decodedText, decodedResult) => {
                    console.log('QR Code detected:', decodedText);
                    // QR code detected - notify Blazor
                    if (this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnQrCodeScanned', decodedText);
                    }
                },
                (errorMessage) => {
                    // Scanning error (e.g., no QR code found in frame) - ignore these
                }
            );
            
            console.log('Camera started successfully');
            return { success: true };
        } catch (error) {
            console.error('Failed to start QR scanner:', error);
            
            // Try fallback - any camera
            if (error.name === 'OverconstrainedError' || error.message?.includes('facingMode')) {
                console.log('Trying fallback to any camera...');
                try {
                    const cameras = await Html5Qrcode.getCameras();
                    if (cameras && cameras.length > 0) {
                        const config = {
                            fps: 10,
                            qrbox: { width: 200, height: 200 }
                        };
                        await this.scanner.start(
                            cameras[0].id,
                            config,
                            (decodedText) => {
                                if (this.dotNetRef) {
                                    this.dotNetRef.invokeMethodAsync('OnQrCodeScanned', decodedText);
                                }
                            },
                            () => {}
                        );
                        return { success: true };
                    }
                } catch (fallbackError) {
                    console.error('Fallback failed:', fallbackError);
                }
            }
            
            // Handle specific errors
            if (error.name === 'NotAllowedError') {
                return { success: false, error: 'Brak uprawnień do kamery. Zezwól na dostęp do kamery.' };
            } else if (error.name === 'NotFoundError') {
                return { success: false, error: 'Nie znaleziono kamery na tym urządzeniu.' };
            } else if (error.name === 'NotReadableError') {
                return { success: false, error: 'Kamera jest używana przez inną aplikację.' };
            }
            
            return { success: false, error: error.message };
        }
    },
    
    // Stop scanning
    stop: async function() {
        if (!this.scanner) {
            return { success: true };
        }
        
        try {
            const state = this.scanner.getState();
            console.log('Scanner state:', state);
            if (state === Html5QrcodeScannerState.SCANNING) {
                await this.scanner.stop();
                console.log('Scanner stopped');
            }
            return { success: true };
        } catch (error) {
            console.error('Failed to stop QR scanner:', error);
            return { success: false, error: error.message };
        }
    },
    
    // Toggle flashlight (if available)
    toggleFlash: async function() {
        if (!this.scanner) {
            return { success: false, error: 'Skaner nie jest aktywny' };
        }
        
        try {
            const capabilities = this.scanner.getRunningTrackCapabilities();
            if (capabilities && capabilities.torch) {
                const settings = this.scanner.getRunningTrackSettings();
                const currentTorch = settings.torch || false;
                await this.scanner.applyVideoConstraints({ advanced: [{ torch: !currentTorch }] });
                return { success: true, flashOn: !currentTorch };
            }
            return { success: false, error: 'Latarka nie jest dostępna na tym urządzeniu' };
        } catch (error) {
            console.error('Failed to toggle flash:', error);
            return { success: false, error: error.message };
        }
    },
    
    // Switch camera (front/back)
    switchCamera: async function() {
        if (!this.scanner) {
            return { success: false, error: 'Skaner nie jest aktywny' };
        }
        
        try {
            // Get all cameras
            const cameras = await Html5Qrcode.getCameras();
            if (cameras.length < 2) {
                return { success: false, error: 'Brak drugiej kamery' };
            }
            
            // Stop current scanner
            await this.stop();
            
            // Start with different camera
            await this.start(false);
            
            return { success: true };
        } catch (error) {
            console.error('Failed to switch camera:', error);
            return { success: false, error: error.message };
        }
    },
    
    // Cleanup
    dispose: async function() {
        console.log('QrScanner.dispose called');
        await this.stop();
        if (this.scanner) {
            try {
                this.scanner.clear();
            } catch (e) {
                console.log('Clear error (ignored):', e);
            }
            this.scanner = null;
        }
        this.dotNetRef = null;
        this.elementId = null;
    },
    
    // Get available cameras
    getCameras: async function() {
        try {
            const cameras = await Html5Qrcode.getCameras();
            return cameras.map(c => ({ id: c.id, label: c.label }));
        } catch (error) {
            console.error('Failed to get cameras:', error);
            return [];
        }
    }
};
