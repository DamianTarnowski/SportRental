// Leaflet map interop for Blazor WASM
window.leafletMap = {
    maps: {},
    userMarkers: {},
    circles: {},
    shopMarkerGroups: {},
    dotNetRefs: {},

    // Initialize map
    initMap: function (mapId, centerLat, centerLon, zoom, dotNetRef) {
        console.log('Initializing map:', mapId);
        
        if (this.maps[mapId]) {
            this.destroyMap(mapId);
        }

        const map = L.map(mapId).setView([centerLat, centerLon], zoom);

        L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
            attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
            maxZoom: 19
        }).addTo(map);

        this.maps[mapId] = map;
        this.shopMarkerGroups[mapId] = L.layerGroup().addTo(map);
        this.dotNetRefs[mapId] = dotNetRef;

        // Allow user to click on map to set their location
        map.on('click', (e) => {
            this.setUserLocation(mapId, e.latlng.lat, e.latlng.lng, true);
        });

        return true;
    },

    // Set user location marker (from click or geolocation)
    setUserLocation: function (mapId, lat, lon, notifyBlazor) {
        const map = this.maps[mapId];
        if (!map) return;

        // Remove existing user marker
        if (this.userMarkers[mapId]) {
            map.removeLayer(this.userMarkers[mapId]);
        }

        // User location icon - red pin
        const userIcon = L.divIcon({
            className: 'user-location-marker',
            html: `<div style="position: relative;">
                <div style="width: 30px; height: 30px; background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%); 
                            border-radius: 50% 50% 50% 0; transform: rotate(-45deg);
                            border: 3px solid white; box-shadow: 0 3px 10px rgba(0,0,0,0.4);"></div>
                <div style="position: absolute; top: 5px; left: 9px; width: 12px; height: 12px; 
                            background: white; border-radius: 50%;"></div>
            </div>`,
            iconSize: [30, 30],
            iconAnchor: [15, 30],
            popupAnchor: [0, -30]
        });

        this.userMarkers[mapId] = L.marker([lat, lon], { 
            icon: userIcon,
            draggable: true,
            zIndexOffset: 1000
        }).addTo(map);

        this.userMarkers[mapId].bindPopup('<strong>📍 Twoja lokalizacja</strong><br><small>Przeciągnij pinezkę lub kliknij na mapie</small>');

        // Handle drag
        this.userMarkers[mapId].on('dragend', (e) => {
            const pos = e.target.getLatLng();
            // Save to localStorage
            localStorage.setItem('userLocation', JSON.stringify({ lat: pos.lat, lon: pos.lng, timestamp: Date.now() }));
            if (this.dotNetRefs[mapId]) {
                this.dotNetRefs[mapId].invokeMethodAsync('OnUserLocationChanged', pos.lat, pos.lng);
            }
        });

        // Save to localStorage for use on other pages
        localStorage.setItem('userLocation', JSON.stringify({ lat, lon, timestamp: Date.now() }));

        // Notify Blazor
        if (notifyBlazor && this.dotNetRefs[mapId]) {
            this.dotNetRefs[mapId].invokeMethodAsync('OnUserLocationChanged', lat, lon);
        }
    },

    // Try to get browser geolocation
    requestGeolocation: function (mapId) {
        if (!navigator.geolocation) {
            console.log('Geolocation not supported');
            return;
        }

        navigator.geolocation.getCurrentPosition(
            (position) => {
                const lat = position.coords.latitude;
                const lon = position.coords.longitude;
                this.setUserLocation(mapId, lat, lon, true);
                
                // Center map on user
                const map = this.maps[mapId];
                if (map) {
                    map.setView([lat, lon], 10);
                }
            },
            (error) => {
                console.log('Geolocation error:', error.message);
                if (this.dotNetRefs[mapId]) {
                    this.dotNetRefs[mapId].invokeMethodAsync('OnGeolocationError', error.message);
                }
            },
            { enableHighAccuracy: true, timeout: 10000 }
        );
    },

    // Add rental shop markers
    addShopMarkers: function (mapId, locations) {
        console.log('Adding shop markers:', locations?.length || 0);
        
        const map = this.maps[mapId];
        const group = this.shopMarkerGroups[mapId];
        
        if (!map || !group) {
            console.log('Map or group not found');
            return;
        }

        // Clear existing shop markers
        group.clearLayers();

        if (!locations || locations.length === 0) {
            console.log('No locations to add');
            return;
        }

        locations.forEach((loc, index) => {
            if (loc.lat && loc.lon && loc.lat !== 0 && loc.lon !== 0) {
                // Shop icon - purple gradient
                const shopIcon = L.divIcon({
                    className: 'shop-marker',
                    html: `<div style="background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); 
                                      width: 40px; height: 40px; border-radius: 50%; 
                                      display: flex; align-items: center; justify-content: center;
                                      border: 3px solid white; box-shadow: 0 3px 12px rgba(0,0,0,0.35);
                                      font-size: 20px; cursor: pointer;">🏪</div>`,
                    iconSize: [40, 40],
                    iconAnchor: [20, 20],
                    popupAnchor: [0, -20]
                });

                const distanceText = loc.distance ? `<div style="color: #667eea; font-weight: 600; margin: 4px 0;">📏 ${loc.distance}</div>` : '';
                
                const marker = L.marker([loc.lat, loc.lon], { icon: shopIcon })
                    .bindPopup(`
                        <div style="min-width: 220px; font-family: system-ui, -apple-system, sans-serif;">
                            <div style="font-size: 15px; font-weight: 600; color: #1f2937; margin-bottom: 8px;">
                                ${loc.tenantName || 'Wypożyczalnia'}
                            </div>
                            ${distanceText}
                            ${loc.address ? `<div style="color: #6b7280; font-size: 13px; margin: 4px 0;">📍 ${loc.address}</div>` : ''}
                            ${loc.phoneNumber ? `<div style="color: #6b7280; font-size: 13px;">📞 ${loc.phoneNumber}</div>` : ''}
                            <button onclick="window.leafletMap.goToProducts('${loc.tenantId}')" 
                                    style="margin-top: 10px; padding: 8px 16px; width: 100%;
                                           background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); 
                                           color: white; border: none; border-radius: 6px; 
                                           cursor: pointer; font-weight: 500; font-size: 13px;
                                           transition: transform 0.2s;">
                                Zobacz produkty →
                            </button>
                        </div>
                    `);

                group.addLayer(marker);
            }
        });

        console.log('Added', group.getLayers().length, 'markers');

        // Fit bounds to show all markers if we have them
        if (group.getLayers().length > 0) {
            const bounds = group.getBounds();
            if (bounds.isValid()) {
                map.fitBounds(bounds, { padding: [60, 60], maxZoom: 11 });
            }
        }
    },

    // Draw radius circle around user location
    setRadiusCircle: function (mapId, lat, lon, radiusKm) {
        const map = this.maps[mapId];
        if (!map) return;

        // Remove existing circle
        if (this.circles[mapId]) {
            map.removeLayer(this.circles[mapId]);
            delete this.circles[mapId];
        }

        if (radiusKm > 0 && lat && lon) {
            this.circles[mapId] = L.circle([lat, lon], {
                radius: radiusKm * 1000,
                color: '#667eea',
                fillColor: '#667eea',
                fillOpacity: 0.08,
                weight: 2,
                dashArray: '5, 5'
            }).addTo(map);

            // Fit to circle bounds
            map.fitBounds(this.circles[mapId].getBounds(), { padding: [30, 30] });
        }
    },

    // Clear radius circle
    clearRadiusCircle: function (mapId) {
        if (this.circles[mapId]) {
            this.maps[mapId]?.removeLayer(this.circles[mapId]);
            delete this.circles[mapId];
        }
    },

    // Center map
    centerMap: function (mapId, lat, lon, zoom) {
        const map = this.maps[mapId];
        if (map) {
            map.setView([lat, lon], zoom || map.getZoom());
        }
    },

    // Navigate to products page with tenant filter
    goToProducts: function (tenantId) {
        window.location.href = `/products?tenant=${tenantId}`;
    },

    // Get user location from localStorage
    getUserLocation: function () {
        try {
            const stored = localStorage.getItem('userLocation');
            if (stored) {
                const data = JSON.parse(stored);
                // Check if location is not too old (7 days)
                if (data.timestamp && (Date.now() - data.timestamp) < 7 * 24 * 60 * 60 * 1000) {
                    return { lat: data.lat, lon: data.lon };
                }
            }
        } catch (e) {
            console.log('Error reading user location:', e);
        }
        return null;
    },

    // Clear user location from localStorage
    clearUserLocation: function () {
        localStorage.removeItem('userLocation');
    },

    // Calculate distance between two points (Haversine formula)
    calculateDistance: function (lat1, lon1, lat2, lon2) {
        const R = 6371; // Earth's radius in km
        const dLat = (lat2 - lat1) * Math.PI / 180;
        const dLon = (lon2 - lon1) * Math.PI / 180;
        const a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
                  Math.cos(lat1 * Math.PI / 180) * Math.cos(lat2 * Math.PI / 180) *
                  Math.sin(dLon / 2) * Math.sin(dLon / 2);
        const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
        return R * c;
    },

    // Destroy map
    destroyMap: function (mapId) {
        if (this.circles[mapId]) {
            this.maps[mapId]?.removeLayer(this.circles[mapId]);
            delete this.circles[mapId];
        }
        if (this.shopMarkerGroups[mapId]) {
            this.shopMarkerGroups[mapId].clearLayers();
            delete this.shopMarkerGroups[mapId];
        }
        if (this.userMarkers[mapId]) {
            this.maps[mapId]?.removeLayer(this.userMarkers[mapId]);
            delete this.userMarkers[mapId];
        }
        if (this.maps[mapId]) {
            this.maps[mapId].remove();
            delete this.maps[mapId];
        }
        delete this.dotNetRefs[mapId];
    }
};
