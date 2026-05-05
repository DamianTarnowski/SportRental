/**
 * SportRental Realtime Voice — Azure OpenAI Realtime + WebRTC.
 * Adapted from LegalAssistant pattern but with Azure endpoints (sessions API + WebRTC URL
 * pointing to srental2-realtime.cognitiveservices.azure.com instead of api.openai.com).
 */
window.realtimeVoice = (function () {
    let peerConnection = null;
    let localStream = null;
    let audioElement = null;
    let dataChannel = null;
    let isConnected = false;
    let dotNetRef = null;
    let currentPageHeader = '';

    async function connect(dotNet, currentPage) {
        dotNetRef = dotNet;
        currentPageHeader = currentPage || '/';

        try {
            await dotNet.invokeMethodAsync('OnVoiceStatusChanged', 'connecting');

            // 1. Pobierz ephemeral key z naszego serwera (KV master key NIGDY nie idzie do klienta).
            const sessionResp = await fetch('/api/realtime/session', {
                method: 'POST',
                credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: '{}'
            });

            if (!sessionResp.ok) {
                const txt = await sessionResp.text();
                throw new Error(`Session create failed: ${sessionResp.status} ${txt.slice(0, 200)}`);
            }

            const sessionData = await sessionResp.json();
            const ephemeralKey = sessionData.client_secret?.value;
            const webrtcUrl = sessionData.webrtc_url;

            if (!ephemeralKey) throw new Error('No ephemeral key in session response');
            if (!webrtcUrl) throw new Error('No webrtc_url in session response');

            // 2. WebRTC peer connection
            peerConnection = new RTCPeerConnection();

            // 3. Audio out — element <audio> automatycznie odtwarza strumień asystenta.
            audioElement = document.createElement('audio');
            audioElement.autoplay = true;
            peerConnection.ontrack = (e) => {
                audioElement.srcObject = e.streams[0];
            };

            // 4. Mikrofon — getUserMedia + addTrack do peer connection.
            localStream = await navigator.mediaDevices.getUserMedia({ audio: true });
            localStream.getTracks().forEach(track => peerConnection.addTrack(track, localStream));

            // 5. Data channel — Azure realtime używa kanału 'oai-events' tak samo jak OpenAI.
            dataChannel = peerConnection.createDataChannel('oai-events');
            dataChannel.onmessage = (e) => handleEvent(JSON.parse(e.data));

            // 6. SDP offer
            const offer = await peerConnection.createOffer();
            await peerConnection.setLocalDescription(offer);

            // 7. SDP exchange — wysyłka offer → otrzymujemy answer SDP od Azure realtime.
            const sdpResp = await fetch(webrtcUrl, {
                method: 'POST',
                headers: {
                    'Authorization': 'Bearer ' + ephemeralKey,
                    'Content-Type': 'application/sdp'
                },
                body: offer.sdp
            });

            if (!sdpResp.ok) {
                const txt = await sdpResp.text();
                throw new Error(`Azure SDP exchange failed: ${sdpResp.status} ${txt.slice(0, 200)}`);
            }

            const answerSdp = await sdpResp.text();
            await peerConnection.setRemoteDescription({ type: 'answer', sdp: answerSdp });

            isConnected = true;
            await dotNet.invokeMethodAsync('OnVoiceStatusChanged', 'connected');
            console.log('[Voice] Azure Realtime WebRTC connected');

        } catch (err) {
            console.error('[Voice] Connection error:', err);
            try { await dotNet.invokeMethodAsync('OnVoiceStatusChanged', 'error'); } catch { }
            try { await dotNet.invokeMethodAsync('OnVoiceError', err.message); } catch { }
            disconnect();
        }
    }

    function handleEvent(event) {
        if (!dotNetRef) return;

        switch (event.type) {
            case 'response.audio_transcript.delta':
                dotNetRef.invokeMethodAsync('OnTranscriptDelta', event.delta || '');
                break;

            case 'response.audio_transcript.done':
                dotNetRef.invokeMethodAsync('OnTranscriptDone', event.transcript || '');
                break;

            case 'conversation.item.input_audio_transcription.completed':
                dotNetRef.invokeMethodAsync('OnUserTranscript', event.transcript || '');
                break;

            case 'response.function_call_arguments.done':
                handleFunctionCall(event.name, event.arguments, event.call_id);
                break;

            case 'response.done':
                dotNetRef.invokeMethodAsync('OnVoiceStatusChanged', 'connected');
                break;

            case 'input_audio_buffer.speech_started':
                dotNetRef.invokeMethodAsync('OnVoiceStatusChanged', 'listening');
                break;

            case 'input_audio_buffer.speech_stopped':
                dotNetRef.invokeMethodAsync('OnVoiceStatusChanged', 'thinking');
                break;

            case 'response.created':
                dotNetRef.invokeMethodAsync('OnVoiceStatusChanged', 'speaking');
                break;

            case 'error':
                console.error('[Voice] Realtime error:', event.error);
                dotNetRef.invokeMethodAsync('OnVoiceError', event.error?.message || 'Unknown error');
                break;
        }
    }

    async function handleFunctionCall(name, argsJson, callId) {
        try {
            const resp = await fetch('/api/realtime/function/' + encodeURIComponent(name), {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Current-Page': currentPageHeader
                },
                body: argsJson || '{}'
            });
            const data = await resp.json();

            if (dataChannel && dataChannel.readyState === 'open') {
                // Wynik narzędzia idzie do realtime jako conversation.item z function_call_output.
                dataChannel.send(JSON.stringify({
                    type: 'conversation.item.create',
                    item: {
                        type: 'function_call_output',
                        call_id: callId,
                        output: typeof data.result === 'string' ? data.result : JSON.stringify(data.result || {})
                    }
                }));
                // Po wysłaniu wyniku każemy modelowi kontynuować — generuje odpowiedź audio na podstawie wyniku.
                dataChannel.send(JSON.stringify({ type: 'response.create' }));
            }
        } catch (err) {
            console.error('[Voice] Function call error:', err);
        }
    }

    function disconnect() {
        if (localStream) {
            localStream.getTracks().forEach(t => t.stop());
            localStream = null;
        }
        if (dataChannel) {
            try { dataChannel.close(); } catch { }
            dataChannel = null;
        }
        if (peerConnection) {
            try { peerConnection.close(); } catch { }
            peerConnection = null;
        }
        if (audioElement) {
            audioElement.srcObject = null;
            audioElement = null;
        }

        isConnected = false;
        if (dotNetRef) {
            try { dotNetRef.invokeMethodAsync('OnVoiceStatusChanged', 'disconnected'); } catch { }
            dotNetRef = null;
        }
        console.log('[Voice] Disconnected');
    }

    function setMicMuted(muted) {
        if (localStream) {
            localStream.getAudioTracks().forEach(t => { t.enabled = !muted; });
        }
    }

    return { connect, disconnect, setMicMuted, getIsConnected: () => isConnected };
})();
