// WASM QR Scanner - runs directly in browser
let stream = null;
let scanInterval = null;
let canvas = null;
let ctx = null;

export async function startCamera(videoElement, dotNetRef) {
    console.log('WASM QR Scanner: Starting camera...');
    
    try {
        // Check if we're in a secure context
        if (!window.isSecureContext) {
            return { success: false, error: 'Kamera wymaga bezpiecznego połączenia (HTTPS)' };
        }
        
        // Check if getUserMedia is available
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            return { success: false, error: 'Przeglądarka nie obsługuje dostępu do kamery' };
        }
        
        // Request camera access
        const constraints = {
            video: {
                facingMode: { ideal: 'environment' },
                width: { ideal: 640 },
                height: { ideal: 480 }
            },
            audio: false
        };
        
        try {
            stream = await navigator.mediaDevices.getUserMedia(constraints);
        } catch (e) {
            console.log('Back camera failed, trying any camera:', e.name);
            stream = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
        }
        
        videoElement.srcObject = stream;
        
        await new Promise((resolve, reject) => {
            videoElement.onloadedmetadata = resolve;
            videoElement.onerror = reject;
            setTimeout(() => reject(new Error('Timeout')), 5000);
        });
        
        await videoElement.play();
        
        // Setup canvas for QR scanning
        canvas = document.createElement('canvas');
        ctx = canvas.getContext('2d', { willReadFrequently: true });
        
        // Start scanning loop
        startScanning(videoElement, dotNetRef);
        
        console.log('WASM QR Scanner: Camera started successfully');
        return { success: true };
        
    } catch (e) {
        console.error('WASM QR Scanner error:', e.name, e.message);
        
        let errorMsg;
        switch (e.name) {
            case 'NotAllowedError':
                errorMsg = 'Brak uprawnień do kamery. Zezwól na dostęp w ustawieniach przeglądarki.';
                break;
            case 'NotFoundError':
                errorMsg = 'Nie znaleziono kamery na tym urządzeniu.';
                break;
            case 'NotReadableError':
                errorMsg = 'Kamera jest używana przez inną aplikację.';
                break;
            case 'OverconstrainedError':
                errorMsg = 'Nie można spełnić wymagań kamery.';
                break;
            case 'SecurityError':
                errorMsg = 'Dostęp do kamery zablokowany przez ustawienia bezpieczeństwa.';
                break;
            default:
                errorMsg = e.message || 'Nieznany błąd kamery';
        }
        
        return { success: false, error: errorMsg };
    }
}

export function stopCamera() {
    console.log('WASM QR Scanner: Stopping camera...');
    
    if (scanInterval) {
        clearInterval(scanInterval);
        scanInterval = null;
    }
    
    if (stream) {
        stream.getTracks().forEach(track => track.stop());
        stream = null;
    }
    
    canvas = null;
    ctx = null;
}

function startScanning(videoElement, dotNetRef) {
    // Simple QR detection using canvas
    // For production, you might want to use a library like jsQR
    
    scanInterval = setInterval(async () => {
        if (!stream || videoElement.paused || videoElement.ended) {
            return;
        }
        
        try {
            canvas.width = videoElement.videoWidth;
            canvas.height = videoElement.videoHeight;
            ctx.drawImage(videoElement, 0, 0);
            
            // Try to use BarcodeDetector API if available
            if ('BarcodeDetector' in window) {
                const barcodeDetector = new BarcodeDetector({ formats: ['qr_code'] });
                const barcodes = await barcodeDetector.detect(canvas);
                
                if (barcodes.length > 0) {
                    const code = barcodes[0].rawValue;
                    console.log('WASM QR Scanner: Code detected:', code);
                    dotNetRef.invokeMethodAsync('OnQrCodeDetected', code);
                }
            }
        } catch (e) {
            // BarcodeDetector not supported or error, silently continue
        }
    }, 200);
}
