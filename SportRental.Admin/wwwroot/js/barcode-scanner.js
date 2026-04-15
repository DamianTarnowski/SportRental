window.srBarcode = {
  start: async function (videoElementId) {
    console.log('srBarcode.start called for:', videoElementId);
    
    const video = document.getElementById(videoElementId);
    if (!video) {
      console.error('Video element not found:', videoElementId);
      alert('Element video nie znaleziony: ' + videoElementId);
      return false;
    }
    
    // Check HTTPS (required for camera on mobile)
    if (location.protocol !== 'https:' && location.hostname !== 'localhost') {
      console.error('Camera requires HTTPS');
      alert('Kamera wymaga połączenia HTTPS');
      return false;
    }
    
    // Check if mediaDevices is available
    if (!navigator.mediaDevices) {
      console.error('navigator.mediaDevices not available');
      alert('Twoja przeglądarka nie obsługuje dostępu do kamery');
      return false;
    }
    
    if (!navigator.mediaDevices.getUserMedia) {
      console.error('getUserMedia not supported');
      alert('getUserMedia nie jest obsługiwane');
      return false;
    }
    
    try {
      console.log('Requesting camera access...');
      
      // Try back camera first
      let stream;
      try {
        stream = await navigator.mediaDevices.getUserMedia({ 
          video: { 
            facingMode: { ideal: 'environment' },
            width: { ideal: 640 },
            height: { ideal: 480 }
          }, 
          audio: false 
        });
        console.log('Got back camera stream');
      } catch (backError) {
        console.log('Back camera failed:', backError.name, '- trying front camera');
        try {
          stream = await navigator.mediaDevices.getUserMedia({ 
            video: true, 
            audio: false 
          });
          console.log('Got front camera stream');
        } catch (frontError) {
          console.error('All cameras failed:', frontError.name, frontError.message);
          throw frontError;
        }
      }
      
      video.srcObject = stream;
      video.style.display = 'block';
      
      // Wait for video to be ready
      await new Promise((resolve, reject) => {
        video.onloadedmetadata = () => {
          console.log('Video metadata loaded');
          resolve();
        };
        video.onerror = (e) => {
          console.error('Video error:', e);
          reject(e);
        };
        setTimeout(() => reject(new Error('Video load timeout')), 5000);
      });
      
      await video.play();
      console.log('Camera started successfully, video playing');
      return true;
      
    } catch(e) {
      console.error('Camera error:', e.name, e.message);
      
      let errorMsg = 'Błąd kamery: ';
      if (e.name === 'NotAllowedError') {
        errorMsg += 'Brak uprawnień. Zezwól na dostęp do kamery w ustawieniach przeglądarki.';
      } else if (e.name === 'NotFoundError') {
        errorMsg += 'Nie znaleziono kamery.';
      } else if (e.name === 'NotReadableError') {
        errorMsg += 'Kamera jest używana przez inną aplikację.';
      } else if (e.name === 'OverconstrainedError') {
        errorMsg += 'Nie można spełnić wymagań kamery.';
      } else {
        errorMsg += e.message || e.name;
      }
      
      alert(errorMsg);
      return false;
    }
  },
  
  stop: function (videoElementId) {
    const video = document.getElementById(videoElementId);
    if (!video) return;
    
    if (video.srcObject) {
      const tracks = video.srcObject.getTracks();
      tracks.forEach(t => t.stop());
      video.srcObject = null;
    }
    video.style.display = 'none';
    console.log('Camera stopped');
  }
};




