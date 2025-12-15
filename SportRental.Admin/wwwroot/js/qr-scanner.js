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
    
    // Initialize scanner
    init: async function(elementId, dotNetReference) {
        this.dotNetRef = dotNetReference;
        
        try {
            // Check if html5QrCode is loaded
            if (typeof Html5Qrcode === 'undefined') {
                console.error('Html5Qrcode library not loaded');
                return { success: false, error: 'Biblioteka skanera QR nie została załadowana' };
            }
            
            this.scanner = new Html5Qrcode(elementId);
            return { success: true };
        } catch (error) {
            console.error('Failed to initialize QR scanner:', error);
            return { success: false, error: error.message };
        }
    },
    
    // Start scanning with camera
    start: async function(preferBackCamera = true) {
        if (!this.scanner) {
            return { success: false, error: 'Skaner nie został zainicjalizowany' };
        }
        
        try {
            const config = {
                fps: 10,
                qrbox: { width: 250, height: 250 },
                aspectRatio: 1.0
            };
            
            const cameraFacing = preferBackCamera ? "environment" : "user";
            
            await this.scanner.start(
                { facingMode: cameraFacing },
                config,
                (decodedText, decodedResult) => {
                    // QR code detected - notify Blazor
                    if (this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnQrCodeScanned', decodedText);
                    }
                },
                (errorMessage) => {
                    // Scanning error (e.g., no QR code found in frame) - ignore these
                }
            );
            
            return { success: true };
        } catch (error) {
            console.error('Failed to start QR scanner:', error);
            
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
            if (state === Html5QrcodeScannerState.SCANNING) {
                await this.scanner.stop();
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
            // This is a simplified version - in production you'd track current camera
            await this.start(false); // Toggle to user-facing
            
            return { success: true };
        } catch (error) {
            console.error('Failed to switch camera:', error);
            return { success: false, error: error.message };
        }
    },
    
    // Cleanup
    dispose: async function() {
        await this.stop();
        if (this.scanner) {
            this.scanner.clear();
            this.scanner = null;
        }
        this.dotNetRef = null;
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
