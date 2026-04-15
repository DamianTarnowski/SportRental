// File upload helper - bypasses SignalR for large files
window.fileUpload = {
    uploadProductImage: async function (inputElementId, productId, antiForgeryToken) {
        const input = document.getElementById(inputElementId);
        if (!input || !input.files || input.files.length === 0) {
            return { success: false, error: 'Nie wybrano pliku' };
        }

        const file = input.files[0];
        const maxSize = 10 * 1024 * 1024; // 10MB
        if (file.size > maxSize) {
            return { success: false, error: 'Plik jest zbyt duży. Maksymalnie 10MB.' };
        }

        const allowedTypes = ['image/jpeg', 'image/png', 'image/webp'];
        if (!allowedTypes.includes(file.type)) {
            return { success: false, error: 'Nieobsługiwany format. Dozwolone: JPG, PNG, WEBP.' };
        }

        const formData = new FormData();
        formData.append('file', file);

        try {
            const response = await fetch(`/api/products/${productId}/image`, {
                method: 'POST',
                body: formData,
                credentials: 'include'
            });

            if (response.ok) {
                const result = await response.json();
                return { success: true, imageUrl: result.imageUrl, basePath: result.basePath };
            } else {
                const errorText = await response.text();
                return { success: false, error: errorText || 'Błąd uploadu' };
            }
        } catch (e) {
            return { success: false, error: 'Błąd połączenia: ' + e.message };
        }
    },

    getFileName: function (inputElementId) {
        const input = document.getElementById(inputElementId);
        if (input && input.files && input.files.length > 0) {
            return input.files[0].name;
        }
        return null;
    },

    getFilePreview: async function (inputElementId) {
        const input = document.getElementById(inputElementId);
        if (!input || !input.files || input.files.length === 0) {
            return null;
        }

        return new Promise((resolve) => {
            const reader = new FileReader();
            reader.onload = (e) => resolve(e.target.result);
            reader.onerror = () => resolve(null);
            reader.readAsDataURL(input.files[0]);
        });
    },

    clearInput: function (inputElementId) {
        const input = document.getElementById(inputElementId);
        if (input) {
            input.value = '';
        }
    }
};
