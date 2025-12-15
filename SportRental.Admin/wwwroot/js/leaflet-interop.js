// Leaflet map interop for Blazor
window.leafletInterop = {
    maps: {},
    markers: {},

    initMap: function (mapId, lat, lon, zoom, allowDrag, dotNetRef) {
        // Check if map already exists
        if (this.maps[mapId]) {
            this.destroyMap(mapId);
        }

        // Initialize map
        const map = L.map(mapId).setView([lat, lon], zoom);

        // Add OpenStreetMap tiles
        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
            maxZoom: 19
        }).addTo(map);

        // Add marker
        const marker = L.marker([lat, lon], {
            draggable: allowDrag
        }).addTo(map);

        // Store references
        this.maps[mapId] = map;
        this.markers[mapId] = marker;

        // Handle marker drag
        if (allowDrag && dotNetRef) {
            const self = this;
            marker.on('dragend', function (e) {
                const position = marker.getLatLng();
                self.reverseGeocode(position.lat, position.lng, dotNetRef);
            });

            // Also allow clicking on map to move marker
            map.on('click', function (e) {
                marker.setLatLng(e.latlng);
                self.reverseGeocode(e.latlng.lat, e.latlng.lng, dotNetRef);
            });
        }

        // Add search control (geocoding)
        this.addSearchControl(map, marker, dotNetRef);

        return true;
    },

    addSearchControl: function (map, marker, dotNetRef) {
        // Create search input
        const searchContainer = L.DomUtil.create('div', 'leaflet-search-container');
        searchContainer.innerHTML = `
            <div style="background: white; padding: 8px; border-radius: 8px; box-shadow: 0 2px 6px rgba(0,0,0,0.3); display: flex; gap: 8px;">
                <input type="text" id="map-search-input" placeholder="Wpisz adres..." 
                       style="padding: 8px 12px; border: 1px solid #ddd; border-radius: 4px; width: 250px; font-size: 14px;" />
                <button id="map-search-btn" 
                        style="padding: 8px 16px; background: #1976d2; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 14px;">
                    Szukaj
                </button>
            </div>
        `;

        const SearchControl = L.Control.extend({
            options: { position: 'topright' },
            onAdd: function () {
                return searchContainer;
            }
        });

        map.addControl(new SearchControl());

        // Prevent map interactions when clicking on search
        L.DomEvent.disableClickPropagation(searchContainer);

        // Search functionality using Nominatim (OpenStreetMap geocoding)
        const searchInput = searchContainer.querySelector('#map-search-input');
        const searchBtn = searchContainer.querySelector('#map-search-btn');

        const performSearch = async function () {
            const query = searchInput.value.trim();
            if (!query) return;

            try {
                searchBtn.textContent = '...';
                const response = await fetch(
                    `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(query)}&countrycodes=pl&limit=1`,
                    { headers: { 'Accept-Language': 'pl' } }
                );
                const results = await response.json();

                if (results && results.length > 0) {
                    const result = results[0];
                    const lat = parseFloat(result.lat);
                    const lon = parseFloat(result.lon);

                    map.setView([lat, lon], 15);
                    marker.setLatLng([lat, lon]);

                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnMarkerDragged', lat, lon);
                    }
                } else {
                    alert('Nie znaleziono adresu. Spróbuj wpisać inaczej.');
                }
            } catch (error) {
                console.error('Geocoding error:', error);
                alert('Błąd wyszukiwania. Spróbuj ponownie.');
            } finally {
                searchBtn.textContent = 'Szukaj';
            }
        };

        searchBtn.addEventListener('click', performSearch);
        searchInput.addEventListener('keypress', function (e) {
            if (e.key === 'Enter') {
                e.preventDefault();
                performSearch();
            }
        });
    },

    setMarkerPosition: function (mapId, lat, lon) {
        const marker = this.markers[mapId];
        const map = this.maps[mapId];
        if (marker && map) {
            marker.setLatLng([lat, lon]);
            map.setView([lat, lon], map.getZoom());
        }
    },

    centerMap: function (mapId, lat, lon, zoom) {
        const map = this.maps[mapId];
        if (map) {
            map.setView([lat, lon], zoom);
        }
    },

    destroyMap: function (mapId) {
        if (this.maps[mapId]) {
            this.maps[mapId].remove();
            delete this.maps[mapId];
            delete this.markers[mapId];
        }
    },

    // Reverse geocoding - get city and voivodeship from coordinates
    reverseGeocode: async function (lat, lon, dotNetRef) {
        try {
            const response = await fetch(
                `https://nominatim.openstreetmap.org/reverse?format=json&lat=${lat}&lon=${lon}&zoom=18&addressdetails=1`,
                { headers: { 'Accept-Language': 'pl' } }
            );
            const result = await response.json();

            let city = null;
            let voivodeship = null;
            let address = null;

            if (result && result.address) {
                const addr = result.address;
                
                // City - try different fields
                city = addr.city || addr.town || addr.village || addr.municipality || addr.county || null;
                
                // Voivodeship (state in OSM)
                voivodeship = addr.state || null;
                // Remove "województwo " prefix if present
                if (voivodeship && voivodeship.toLowerCase().startsWith('województwo ')) {
                    voivodeship = voivodeship.substring(12);
                }
                
                // Full address
                address = result.display_name || null;
            }

            // Call Blazor with all the data
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnLocationWithDetails', lat, lon, city, voivodeship, address);
            }
        } catch (error) {
            console.error('Reverse geocoding error:', error);
            // Fallback - just send coordinates without details
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnLocationWithDetails', lat, lon, null, null, null);
            }
        }
    }
};
