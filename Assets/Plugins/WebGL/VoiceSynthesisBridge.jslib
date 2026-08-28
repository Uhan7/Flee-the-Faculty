// Speaks a line in the browser, in one of the two cloned voices.
//
// The model runs in a Web Worker so a five-second line does not stall Unity's
// frame loop. Synthesis measured 2.6 to 3.4 times real time single-threaded in
// this runtime, with first audio at about 0.19s, so the worker stays ahead of
// playback and Unity never waits for the whole clip.
//
// Everything here is fail-soft. A missing model, a refused fetch, or an
// unsupported browser leaves Unity with no clip, and a line with no clip falls
// back to the syllable ticks in DialogueActor, which is what every line sounded
// like before any of this existed.
mergeInto(LibraryManager.library, {
  VoiceSynthesis_Begin: function (targetNamePointer, configPointer) {
    if (typeof window === "undefined" || typeof Worker === "undefined") {
      return;
    }

    var targetName = UTF8ToString(targetNamePointer);
    var config = JSON.parse(UTF8ToString(configPointer));

    if (window.FleeVoiceBridge && window.FleeVoiceBridge.worker) {
      window.FleeVoiceBridge.targetName = targetName;
      return;
    }

    var state = {
      worker: null,
      targetName: targetName,
      ready: false,
      sampleRate: 24000,
      pending: {},
      chunks: [],
      activeRequest: "",
      send: function (methodName, value) {
        if (this.targetName) {
          SendMessage(this.targetName, methodName, value || "");
        }
      }
    };
    window.FleeVoiceBridge = state;

    // A WAV header, so the clip can go straight into UnityWebRequestMultimedia
    // rather than through a second decode path on the C# side.
    state.toWav = function (samples, sampleRate) {
      var bytes = new ArrayBuffer(44 + samples.length * 2);
      var view = new DataView(bytes);
      var writeText = function (offset, text) {
        for (var i = 0; i < text.length; i++) {
          view.setUint8(offset + i, text.charCodeAt(i));
        }
      };
      writeText(0, "RIFF");
      view.setUint32(4, 36 + samples.length * 2, true);
      writeText(8, "WAVEfmt ");
      view.setUint32(16, 16, true);
      view.setUint16(20, 1, true);
      view.setUint16(22, 1, true);
      view.setUint32(24, sampleRate, true);
      view.setUint32(28, sampleRate * 2, true);
      view.setUint16(32, 2, true);
      view.setUint16(34, 16, true);
      writeText(36, "data");
      view.setUint32(40, samples.length * 2, true);
      for (var s = 0; s < samples.length; s++) {
        var clamped = Math.max(-1, Math.min(1, samples[s]));
        view.setInt16(44 + s * 2, clamped * 32767, true);
      }
      return new Blob([view], { type: "audio/wav" });
    };

    try {
      state.worker = new Worker(config.workerUrl, { type: "module" });
    } catch (error) {
      state.send("HandleVoiceError", "Could not start the voice worker: " + error.message);
      return;
    }

    state.worker.onmessage = function (event) {
      var message = event.data;

      if (message.type === "configured") {
        state.worker.postMessage({ type: "load", quant: config.quant || "q8" });
        return;
      }

      if (message.type === "loaded") {
        state.ready = true;
        state.sampleRate = message.sampleRate || 24000;
        state.send("HandleVoiceReady", String(state.sampleRate));
        return;
      }

      if (message.type === "chunk") {
        state.chunks.push(message.data);
        return;
      }

      if (message.type === "done") {
        var total = 0;
        for (var i = 0; i < state.chunks.length; i++) {
          total += state.chunks[i].length;
        }
        var samples = new Float32Array(total);
        var at = 0;
        for (var c = 0; c < state.chunks.length; c++) {
          samples.set(state.chunks[c], at);
          at += state.chunks[c].length;
        }
        state.chunks = [];

        var url = URL.createObjectURL(state.toWav(samples, state.sampleRate));
        state.send("HandleVoiceClip", state.activeRequest + "|" + url);
        state.activeRequest = "";
        return;
      }

      if (message.type === "error") {
        state.chunks = [];
        state.activeRequest = "";
        state.send("HandleVoiceError", message.message || "voice worker failed");
      }
    };

    state.worker.onerror = function (error) {
      state.send("HandleVoiceError", "voice worker: " + (error.message || "unknown"));
    };

    // Prefer the copy sitting next to the build: same origin, no CORS, and no
    // dependence on anyone else's hosting. VoiceModelPostBuild puts it there.
    // Fall back to the public weights when a build was made without it.
    var probe = function (url) {
      if (!url) {
        return Promise.resolve(false);
      }
      return fetch(url, { method: "HEAD" })
        .then(function (r) { return r.ok; })
        .catch(function () { return false; });
    };

    Promise.all([probe(config.modelUrl), probe(config.tokenizerUrl)]).then(function (found) {
      state.worker.postMessage({
        type: "configure",
        config: {
          voicesBase: config.voicesBase,
          voices: config.voices,
          modelUrl: found[0] ? config.modelUrl : (config.fallbackModelUrl || ""),
          tokenizerUrl: found[1] ? config.tokenizerUrl : (config.fallbackTokenizerUrl || "")
        }
      });
    });
  },

  VoiceSynthesis_IsReady: function () {
    var state = typeof window === "undefined" ? null : window.FleeVoiceBridge;
    return state && state.ready ? 1 : 0;
  },

  VoiceSynthesis_Speak: function (requestIdPointer, voicePointer, textPointer) {
    var state = typeof window === "undefined" ? null : window.FleeVoiceBridge;
    if (!state || !state.ready || !state.worker) {
      return;
    }

    state.chunks = [];
    state.activeRequest = UTF8ToString(requestIdPointer);
    state.worker.postMessage({
      type: "generate",
      text: UTF8ToString(textPointer),
      voiceName: UTF8ToString(voicePointer),
      temperature: 0.7
    });
  },

  VoiceSynthesis_ReleaseClip: function (urlPointer) {
    if (typeof URL !== "undefined") {
      URL.revokeObjectURL(UTF8ToString(urlPointer));
    }
  }
});
