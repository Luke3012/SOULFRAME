mergeInto(LibraryManager.library, {
  TtsStream_Start: function (urlPtr, textPtr, avatarIdPtr, languagePtr, targetPtr, maxChunkChars) {
    var url = UTF8ToString(urlPtr);
    var text = UTF8ToString(textPtr);
    var avatarId = UTF8ToString(avatarIdPtr);
    var language = UTF8ToString(languagePtr);
    var target = UTF8ToString(targetPtr);

    if (!window.__ttsStream) {
      window.__ttsStream = {};
    }
    var state = window.__ttsStream;

    if (state.abortController) {
      try { state.abortController.abort(); } catch (e) {}
    }

    state.queue = [];
    state.leftover = null;
    state.headerBuffer = null;
    state.headerBytes = 0;
    state.headerReady = false;
    state.sampleRate = 24000;
    state.channels = 1;
    state.bytes = 0;
    state.streamEnded = false;
    state.endNotified = false;
    state.target = target || "";

    var AudioContextClass = window.AudioContext || window.webkitAudioContext;
    if (!AudioContextClass) {
      if (state.target) {
        SendMessage(state.target, "OnTtsStreamError", "AudioContext not supported");
      }
      return;
    }

    if (!state.ctx || state.ctx.state === "closed") {
      state.ctx = new AudioContextClass({ sampleRate: state.sampleRate });
    }

    if (state.ctx.state === "suspended") {
      state.ctx.resume();
    }

    if (state.node) {
      try { state.node.disconnect(); } catch (e) {}
      state.node = null;
    }

    state.node = state.ctx.createScriptProcessor(2048, 0, 1);
    state.node.onaudioprocess = function (e) {
      var out = e.outputBuffer.getChannelData(0);
      var offset = 0;

      while (offset < out.length && state.queue.length > 0) {
        var head = state.queue[0];
        var available = head.length - head.offset;
        var needed = out.length - offset;
        var take = Math.min(available, needed);
        out.set(head.data.subarray(head.offset, head.offset + take), offset);
        head.offset += take;
        offset += take;
        if (head.offset >= head.length) {
          state.queue.shift();
        }
      }

      if (offset < out.length) {
        out.fill(0, offset);
      }

      if (state.streamEnded && state.queue.length === 0 && !state.endNotified) {
        state.endNotified = true;
        try { state.node.disconnect(); } catch (e) {}
        if (state.target) {
          var duration = 0;
          if (state.sampleRate > 0 && state.channels > 0) {
            duration = state.bytes / (2 * state.channels * state.sampleRate);
          }
          var stats = "bytes=" + state.bytes + ",duration=" + duration.toFixed(2) + "s";
          SendMessage(state.target, "OnTtsStreamCompleted", stats);
        }
      }
    };
    state.node.connect(state.ctx.destination);

    state.abortController = new AbortController();
    var form = new FormData();
    form.append("text", text || "");
    form.append("avatar_id", avatarId || "default");
    form.append("language", language || "it");
    form.append("split_sentences", "true");
    if (maxChunkChars) {
      form.append("max_chunk_chars", String(maxChunkChars));
    }

    fetch(url, {
      method: "POST",
      body: form,
      signal: state.abortController.signal
    }).then(function (response) {
      if (!response.ok) {
        return response.text().then(function (bodyText) {
          var detail = bodyText ? String(bodyText).slice(0, 220) : "";
          throw new Error("HTTP " + response.status + (detail ? " - " + detail : ""));
        });
      }
      if (!response.body) {
        throw new Error("HTTP " + response.status + " - Empty response body");
      }
      var reader = response.body.getReader();

      function pump() {
        return reader.read().then(function (result) {
          if (result.done) {
            state.streamEnded = true;
            return;
          }

          var chunk = result.value;
          if (!state.headerReady) {
            if (!state.headerBuffer) {
              state.headerBuffer = new Uint8Array(44);
            }
            var needed = 44 - state.headerBytes;
            var take = Math.min(needed, chunk.length);
            state.headerBuffer.set(chunk.subarray(0, take), state.headerBytes);
            state.headerBytes += take;

            if (state.headerBytes >= 44) {
              var header = state.headerBuffer;
              state.channels = header[22] | (header[23] << 8);
              state.sampleRate = header[24] | (header[25] << 8) | (header[26] << 16) | (header[27] << 24);
              state.headerReady = true;
              if (state.ctx && state.ctx.sampleRate !== state.sampleRate) {
                try {
                  state.ctx.close();
                  state.ctx = new AudioContextClass({ sampleRate: state.sampleRate });
                  state.node.connect(state.ctx.destination);
                } catch (e) {}
              }
            }

            if (take < chunk.length) {
              chunk = chunk.subarray(take);
            } else {
              return pump();
            }
          }

          if (chunk.length > 0) {
            var bytes = chunk;
            if (state.leftover) {
              var combined = new Uint8Array(state.leftover.length + bytes.length);
              combined.set(state.leftover, 0);
              combined.set(bytes, state.leftover.length);
              bytes = combined;
              state.leftover = null;
            }

            var sampleCount = Math.floor(bytes.length / 2);
            var aligned = sampleCount * 2;
            if (aligned < bytes.length) {
              state.leftover = bytes.subarray(aligned);
              bytes = bytes.subarray(0, aligned);
            }

            if (sampleCount > 0) {
              state.bytes += aligned;
              var floats = new Float32Array(sampleCount / state.channels);
              var idx = 0;
              var outIdx = 0;
              if (state.channels === 1) {
                for (idx = 0; idx < sampleCount; idx++) {
                  var lo = bytes[idx * 2];
                  var hi = bytes[idx * 2 + 1];
                  var s = (hi << 8) | lo;
                  if (s & 0x8000) s = s - 0x10000;
                  floats[idx] = s / 32768.0;
                }
              } else {
                for (idx = 0; idx < sampleCount; idx += state.channels) {
                  var sum = 0;
                  for (var ch = 0; ch < state.channels; ch++) {
                    var b = (idx + ch) * 2;
                    var lo2 = bytes[b];
                    var hi2 = bytes[b + 1];
                    var s2 = (hi2 << 8) | lo2;
                    if (s2 & 0x8000) s2 = s2 - 0x10000;
                    sum += s2;
                  }
                  floats[outIdx++] = (sum / state.channels) / 32768.0;
                }
                if (outIdx < floats.length) {
                  floats = floats.subarray(0, outIdx);
                }
              }

              state.queue.push({ data: floats, offset: 0, length: floats.length });
            }
          }

          return pump();
        });
      }

      return pump();
    }).catch(function (err) {
      if (state.target && !state.endNotified) {
        state.endNotified = true;
        SendMessage(state.target, "OnTtsStreamError", err && err.message ? err.message : "Stream error");
      }
    });
  },

  TtsStream_Stop: function () {
    var state = window.__ttsStream;
    if (!state) {
      return;
    }
    if (state.abortController) {
      try { state.abortController.abort(); } catch (e) {}
    }
    if (state.node) {
      try { state.node.disconnect(); } catch (e) {}
      state.node = null;
    }
    state.queue = [];
    state.leftover = null;
    state.headerBuffer = null;
    state.headerBytes = 0;
    state.headerReady = false;
    state.streamEnded = true;
  }
});
