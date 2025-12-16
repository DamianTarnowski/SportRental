window.srBarcode = {
  start: async function (videoElementId) {
    const video = document.getElementById(videoElementId);
    console.log('srBarcode.start called, video element:', videoElementId, video);
    
    if (!video) {
      console.error('Video element not found:', videoElementId);
      return false;
    }
    
    // Check if mediaDevices is available
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      console.error('getUserMedia not supported');
      return false;
    }
    
    try {
      // Try back camera first, fall back to any camera
      let stream;
      try {
        stream = await navigator.mediaDevices.getUserMedia({ 
          video: { facingMode: { ideal: 'environment' } }, 
          audio: false 
        });
      } catch (e) {
        console.log('Back camera failed, trying any camera');
        stream = await navigator.mediaDevices.getUserMedia({ 
          video: true, 
          audio: false 
        });
      }
      
      video.srcObject = stream;
      video.style.display = 'block';
      await video.play();
      console.log('Camera started successfully');
      return true;
    } catch(e) {
      console.error('Camera error:', e.name, e.message);
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




