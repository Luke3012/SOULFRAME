mergeInto(LibraryManager.library, {
  $webgl_audio_has_live_stream__deps: ['$webgl_audio_ctx'],
  $webgl_audio_has_live_stream: function() {
    if (!webgl_audio_ctx.stream) {
      return false;
    }

    if (webgl_audio_ctx.stream.active === false) {
      return false;
    }

    if (typeof webgl_audio_ctx.stream.getAudioTracks !== 'function') {
      return true;
    }

    var tracks = webgl_audio_ctx.stream.getAudioTracks();
    if (!tracks || !tracks.length) {
      return false;
    }

    for (var i = 0; i < tracks.length; i++) {
      var track = tracks[i];
      if (track && track.readyState === 'live' && track.enabled !== false) {
        return true;
      }
    }

    return false;
  },

  $webgl_audio_cleanup__deps: ['$webgl_audio_ctx'],
  $webgl_audio_cleanup: function() {
    try {
      if (webgl_audio_ctx.mediaRecorder) {
        try {
          if (webgl_audio_ctx.mediaRecorder.state && webgl_audio_ctx.mediaRecorder.state !== 'inactive') {
            webgl_audio_ctx.mediaRecorder.stop();
          }
        } catch (e) {}
      }
    } catch (e) {}

    webgl_audio_ctx.mediaRecorder = null;
    webgl_audio_ctx.chunks = [];
    webgl_audio_ctx.isRecording = false;

    if (webgl_audio_ctx.stream && typeof webgl_audio_ctx.stream.getTracks === 'function') {
      var tracks = webgl_audio_ctx.stream.getTracks();
      for (var i = 0; i < tracks.length; i++) {
        try { tracks[i].stop(); } catch (e) {}
      }
    }
    webgl_audio_ctx.stream = null;

    if (webgl_audio_ctx.audioContext) {
      try { webgl_audio_ctx.audioContext.close(); } catch (e) {}
    }
    webgl_audio_ctx.audioContext = null;
  },

  $webgl_audio_convert: function(audioBuffer) {
    var numChannels = audioBuffer.numberOfChannels;
    var sampleRate = audioBuffer.sampleRate;
    var format = 1;
    var bitDepth = 16;
    
    var channelData = [];
    for (var i = 0; i < numChannels; i++) {
      channelData.push(audioBuffer.getChannelData(i));
    }
    
    var length = channelData[0].length;
    var samples = new Int16Array(length * numChannels);
    
    var index = 0;
    for (var i = 0; i < length; i++) {
      for (var ch = 0; ch < numChannels; ch++) {
        var s = Math.max(-1, Math.min(1, channelData[ch][i]));
        samples[index++] = s < 0 ? s * 0x8000 : s * 0x7FFF;
      }
    }
    
    var dataLength = samples.length * 2;
    var buffer = new ArrayBuffer(44 + dataLength);
    var view = new DataView(buffer);
    
    var writeString = function(offset, string) {
      for (var i = 0; i < string.length; i++) {
        view.setUint8(offset + i, string.charCodeAt(i));
      }
    };
    
    writeString(0, 'RIFF');
    view.setUint32(4, 36 + dataLength, true);
    writeString(8, 'WAVE');
    writeString(12, 'fmt ');
    view.setUint32(16, 16, true);
    view.setUint16(20, format, true);
    view.setUint16(22, numChannels, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, sampleRate * numChannels * bitDepth / 8, true);
    view.setUint16(32, numChannels * bitDepth / 8, true);
    view.setUint16(34, bitDepth, true);
    writeString(36, 'data');
    view.setUint32(40, dataLength, true);
    
    var offset = 44;
    for (var i = 0; i < samples.length; i++) {
      view.setInt16(offset, samples[i], true);
      offset += 2;
    }
    
    return buffer;
  },

  $webgl_audio_ctx: {},

  WebGLAudio_IsSupported__deps: ['$webgl_audio_ctx'],
  WebGLAudio_IsSupported: function() {
    return typeof navigator !== 'undefined' && 
           typeof navigator.mediaDevices !== 'undefined' && 
           typeof navigator.mediaDevices.getUserMedia === 'function';
  },

  WebGLAudio_RequestPermission__deps: ['$webgl_audio_ctx', '$webgl_audio_has_live_stream', '$webgl_audio_cleanup'],
  WebGLAudio_RequestPermission: function(callbackPtr) {
    if (!webgl_audio_ctx.initialized) {
      webgl_audio_ctx.stream = null;
      webgl_audio_ctx.audioContext = null;
      webgl_audio_ctx.mediaRecorder = null;
      webgl_audio_ctx.chunks = [];
      webgl_audio_ctx.isRecording = false;
      webgl_audio_ctx.sampleRate = 16000;
      webgl_audio_ctx.initialized = true;
    }

    if (webgl_audio_has_live_stream()) {
      try {
        if (!webgl_audio_ctx.audioContext || webgl_audio_ctx.audioContext.state === 'closed') {
          webgl_audio_ctx.audioContext = new (window.AudioContext || window.webkitAudioContext)({
            sampleRate: webgl_audio_ctx.sampleRate
          });
        }

        if (webgl_audio_ctx.audioContext && webgl_audio_ctx.audioContext.state === 'suspended') {
          webgl_audio_ctx.audioContext.resume();
        }
      } catch (e) {
        console.warn('WebGL Audio: audio context refresh failed', e);
      }

      if (callbackPtr) {
        {{{ makeDynCall('vi', 'callbackPtr') }}}(1);
      }
      return;
    }

    webgl_audio_cleanup();
    
    navigator.mediaDevices.getUserMedia({ audio: true })
      .then(function(stream) {
        webgl_audio_ctx.stream = stream;
        webgl_audio_ctx.audioContext = new (window.AudioContext || window.webkitAudioContext)({
          sampleRate: webgl_audio_ctx.sampleRate
        });

        if (webgl_audio_ctx.audioContext && webgl_audio_ctx.audioContext.state === 'suspended') {
          webgl_audio_ctx.audioContext.resume();
        }
        
        if (callbackPtr) {
          {{{ makeDynCall('vi', 'callbackPtr') }}}(1);
        }
      })
      .catch(function(err) {
        console.error('WebGL Audio: Permission denied', err);
        if (callbackPtr) {
          {{{ makeDynCall('vi', 'callbackPtr') }}}(0);
        }
      });
  },

  WebGLAudio_SetSampleRate__deps: ['$webgl_audio_ctx'],
  WebGLAudio_SetSampleRate: function(sampleRate) {
    if (!webgl_audio_ctx.initialized) {
      webgl_audio_ctx.stream = null;
      webgl_audio_ctx.audioContext = null;
      webgl_audio_ctx.mediaRecorder = null;
      webgl_audio_ctx.chunks = [];
      webgl_audio_ctx.isRecording = false;
      webgl_audio_ctx.sampleRate = sampleRate || 16000;
      webgl_audio_ctx.initialized = true;
      return;
    }

    var targetRate = sampleRate || webgl_audio_ctx.sampleRate || 16000;
    if (webgl_audio_ctx.sampleRate !== targetRate) {
      webgl_audio_ctx.sampleRate = targetRate;
      if (webgl_audio_ctx.audioContext) {
        try { webgl_audio_ctx.audioContext.close(); } catch (e) {}
        webgl_audio_ctx.audioContext = new (window.AudioContext || window.webkitAudioContext)({
          sampleRate: webgl_audio_ctx.sampleRate
        });
      }
    }
  },

  WebGLAudio_StartRecording__deps: ['$webgl_audio_ctx'],
  WebGLAudio_StartRecording: function() {
    if (!webgl_audio_ctx.stream || webgl_audio_ctx.isRecording) {
      return 0;
    }

    if (webgl_audio_ctx.stream.active === false) {
      console.warn('WebGL Audio: inactive stream, recording aborted');
      return 0;
    }

    if (typeof webgl_audio_ctx.stream.getAudioTracks === 'function') {
      var tracks = webgl_audio_ctx.stream.getAudioTracks();
      var hasLiveTrack = false;
      for (var i = 0; i < tracks.length; i++) {
        var track = tracks[i];
        if (track && track.readyState === 'live' && track.enabled !== false) {
          hasLiveTrack = true;
          break;
        }
      }

      if (!hasLiveTrack) {
        console.warn('WebGL Audio: no live audio track, recording aborted');
        return 0;
      }
    }

    try {
      if (!webgl_audio_ctx.audioContext || webgl_audio_ctx.audioContext.state === 'closed') {
        webgl_audio_ctx.audioContext = new (window.AudioContext || window.webkitAudioContext)({
          sampleRate: webgl_audio_ctx.sampleRate
        });
      }

      if (webgl_audio_ctx.audioContext && webgl_audio_ctx.audioContext.state === 'suspended') {
        webgl_audio_ctx.audioContext.resume();
      }
      webgl_audio_ctx.chunks = [];
      webgl_audio_ctx.mediaRecorder = new MediaRecorder(webgl_audio_ctx.stream, {
        mimeType: 'audio/webm;codecs=opus',
        audioBitsPerSecond: 16000
      });

      webgl_audio_ctx.mediaRecorder.ondataavailable = function(e) {
        if (e.data.size > 0) {
          webgl_audio_ctx.chunks.push(e.data);
        }
      };

      webgl_audio_ctx.mediaRecorder.start();
      webgl_audio_ctx.isRecording = true;
      return 1;
    } catch (err) {
      console.error('WebGL Audio: Start recording failed', err);
      return 0;
    }
  },

  WebGLAudio_StopRecording__deps: ['$webgl_audio_ctx', '$webgl_audio_convert'],
  WebGLAudio_StopRecording: function(callbackPtr) {
    if (!webgl_audio_ctx.mediaRecorder || !webgl_audio_ctx.isRecording) {
      if (callbackPtr) {
        {{{ makeDynCall('vii', 'callbackPtr') }}}(0, 0);
      }
      return;
    }

    webgl_audio_ctx.mediaRecorder.onstop = function() {
      var blob = new Blob(webgl_audio_ctx.chunks, { type: 'audio/webm' });
      var reader = new FileReader();
      
      reader.onloadend = function() {
        var arrayBuffer = reader.result;
        
        webgl_audio_ctx.audioContext.decodeAudioData(arrayBuffer)
          .then(function(audioBuffer) {
            var wavData = webgl_audio_convert(audioBuffer);
            var buffer = _malloc(wavData.byteLength);
            HEAPU8.set(new Uint8Array(wavData), buffer);
            
            if (callbackPtr) {
              {{{ makeDynCall('vii', 'callbackPtr') }}}(buffer, wavData.byteLength);
            }
            
            _free(buffer);
          })
          .catch(function(err) {
            console.error('WebGL Audio: Decode failed', err);
            if (callbackPtr) {
              {{{ makeDynCall('vii', 'callbackPtr') }}}(0, 0);
            }
          });
      };
      
      reader.readAsArrayBuffer(blob);
      webgl_audio_ctx.chunks = [];
    };

    webgl_audio_ctx.mediaRecorder.stop();
    webgl_audio_ctx.isRecording = false;
  },

  WebGLAudio_IsRecording__deps: ['$webgl_audio_ctx'],
  WebGLAudio_IsRecording: function() {
    return (webgl_audio_ctx.isRecording) ? 1 : 0;
  },

  WebGLAudio_CaptureFixedDuration__deps: ['$webgl_audio_ctx', '$webgl_audio_convert'],
  WebGLAudio_CaptureFixedDuration: function(durationMs, callbackPtr) {
    if (!webgl_audio_ctx.stream) {
      if (callbackPtr) {
        {{{ makeDynCall('vii', 'callbackPtr') }}}(0, 0);
      }
      return;
    }

    try {
      if (webgl_audio_ctx.audioContext && webgl_audio_ctx.audioContext.state === 'suspended') {
        webgl_audio_ctx.audioContext.resume();
      }
      var chunks = [];
      var recorder = new MediaRecorder(webgl_audio_ctx.stream, {
        mimeType: 'audio/webm;codecs=opus',
        audioBitsPerSecond: 16000
      });

      recorder.ondataavailable = function(e) {
        if (e.data.size > 0) {
          chunks.push(e.data);
        }
      };

      recorder.onstop = function() {
        var blob = new Blob(chunks, { type: 'audio/webm' });
        var reader = new FileReader();
        
        reader.onloadend = function() {
          var arrayBuffer = reader.result;
          
          webgl_audio_ctx.audioContext.decodeAudioData(arrayBuffer)
            .then(function(audioBuffer) {
              var wavData = webgl_audio_convert(audioBuffer);
              var buffer = _malloc(wavData.byteLength);
              HEAPU8.set(new Uint8Array(wavData), buffer);
              
              if (callbackPtr) {
                {{{ makeDynCall('vii', 'callbackPtr') }}}(buffer, wavData.byteLength);
              }
              
              _free(buffer);
            })
            .catch(function(err) {
              console.error('WebGL Audio: Decode failed', err);
              if (callbackPtr) {
                {{{ makeDynCall('vii', 'callbackPtr') }}}(0, 0);
              }
            });
        };
        
        reader.readAsArrayBuffer(blob);
      };

      recorder.start();
      setTimeout(function() {
        if (recorder.state === 'recording') {
          recorder.stop();
        }
      }, durationMs);
      
    } catch (err) {
      console.error('WebGL Audio: Capture failed', err);
      if (callbackPtr) {
        {{{ makeDynCall('vii', 'callbackPtr') }}}(0, 0);
      }
    }
  }
});
