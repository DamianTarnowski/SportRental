// Image Cropper with Croppie.js
// Provides image cropping, resizing, and preview functionality

let croppieInstance = null;
let originalFile = null;

window.imageCropper = {
    // Initialize cropper with image
    init: function (imageDataUrl, containerId) {
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('Container not found:', containerId);
            return false;
        }

        // Clean up previous instance
        if (croppieInstance) {
            croppieInstance.destroy();
        }

        // Initialize Croppie
        croppieInstance = new Croppie(container, {
            viewport: { 
                width: 300, 
                height: 300, 
                type: 'square' 
            },
            boundary: { 
                width: 400, 
                height: 400 
            },
            showZoomer: true,
            enableOrientation: true,
            enableResize: false,
            enableExif: true,
            mouseWheelZoom: 'ctrl'
        });

        // Bind image
        croppieInstance.bind({
            url: imageDataUrl,
            zoom: 0
        });

        return true;
    },

    // Get cropped image as base64
    getCroppedImage: async function (format = 'jpeg', quality = 0.85) {
        if (!croppieInstance) {
            console.error('Croppie not initialized');
            return null;
        }

        try {
            const result = await croppieInstance.result({
                type: 'base64',
                size: { width: 1920, height: 1920 }, // Max size
                format: format,
                quality: quality,
                circle: false
            });
            return result;
        } catch (error) {
            console.error('Error getting cropped image:', error);
            return null;
        }
    },

    // Rotate image
    rotate: function (degrees) {
        if (croppieInstance) {
            croppieInstance.rotate(degrees);
        }
    },

    // Destroy cropper
    destroy: function () {
        if (croppieInstance) {
            croppieInstance.destroy();
            croppieInstance = null;
        }
    },

    // Validate image file
    validateImage: function (file, maxSizeMB = 8) {
        const errors = [];

        // Check if file exists
        if (!file) {
            errors.push('Nie wybrano pliku');
            return { isValid: false, errors };
        }

        // Check file type
        const validTypes = ['image/jpeg', 'image/jpg', 'image/png', 'image/webp'];
        if (!validTypes.includes(file.type.toLowerCase())) {
            errors.push(`Nieprawidłowy format pliku. Dozwolone: JPEG, PNG, WebP`);
        }

        // Check file size
        const maxBytes = maxSizeMB * 1024 * 1024;
        if (file.size > maxBytes) {
            errors.push(`Plik jest za duży. Maksymalny rozmiar: ${maxSizeMB}MB (Twój: ${(file.size / 1024 / 1024).toFixed(2)}MB)`);
        }

        // Check minimum size
        if (file.size < 1024) {
            errors.push('Plik jest za mały (min. 1KB)');
        }

        return {
            isValid: errors.length === 0,
            errors: errors,
            fileInfo: {
                name: file.name,
                size: file.size,
                sizeFormatted: formatFileSize(file.size),
                type: file.type
            }
        };
    },

    // Read file as data URL
    readFileAsDataUrl: function (inputElement) {
        return new Promise((resolve, reject) => {
            const file = inputElement.files[0];
            if (!file) {
                reject('No file selected');
                return;
            }

            originalFile = file;

            const reader = new FileReader();
            reader.onload = (e) => {
                resolve(e.target.result);
            };
            reader.onerror = (e) => {
                reject('Failed to read file');
            };
            reader.readAsDataURL(file);
        });
    },

    // Get original file info
    getOriginalFileInfo: function () {
        if (!originalFile) return null;

        return {
            name: originalFile.name,
            size: originalFile.size,
            sizeFormatted: formatFileSize(originalFile.size),
            type: originalFile.type,
            lastModified: originalFile.lastModified
        };
    },

    // Convert base64 to blob
    base64ToBlob: function (base64Data, contentType = 'image/jpeg') {
        // Remove data URL prefix
        const base64 = base64Data.split(',')[1] || base64Data;
        
        const byteCharacters = atob(base64);
        const byteNumbers = new Array(byteCharacters.length);
        
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        
        const byteArray = new Uint8Array(byteNumbers);
        return new Blob([byteArray], { type: contentType });
    },

    // Show preview
    showPreview: function (dataUrl, previewElementId) {
        const previewElement = document.getElementById(previewElementId);
        if (previewElement) {
            previewElement.src = dataUrl;
            previewElement.style.display = 'block';
        }
    }
};

// Helper function to format file size
function formatFileSize(bytes) {
    if (bytes === 0) return '0 Bytes';
    
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
}

// Show notification
function showNotification(message, type = 'info') {
    console.log(`[${type.toUpperCase()}] ${message}`);
    // This can be extended to show MudBlazor snackbar via JSInterop
}
