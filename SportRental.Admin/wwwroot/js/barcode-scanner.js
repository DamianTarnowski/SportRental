window.srBarcode = {
  start: async function (videoElementId) {
    const video = document.getElementById(videoElementId);
    if (!video) return false;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' }, audio: false });
      video.srcObject = stream;
      await video.play();
      return true;
    } catch(e) {
      console.error('Camera error', e);
      return false;
    }
  },
  stop: function (videoElementId) {
    const video = document.getElementById(videoElementId);
    if (!video || !video.srcObject) return;
    const tracks = video.srcObject.getTracks();
    tracks.forEach(t => t.stop());
    video.srcObject = null;
  }
};




