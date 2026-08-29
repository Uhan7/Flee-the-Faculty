// Speaks a line in the browser, in one of the two cloned voices.
//
// The model runs in a Web Worker so a five-second line does not stall Unity's
// frame loop. Synthesis measured 2.6 to 3.4 times real time single-threaded in
// this runtime, with first audio at about 0.19s, so the worker stays ahead of
// playback and Unity never waits for the whole clip.
//
// Lines are worked one at a time. The worker holds a single model whose state
// each generation resets, so a second generate would corrupt the first, and
// Unity asks for a whole reply at once. Finished audio goes to Unity as raw
// samples rather than as an encoded file: a WebGL AudioClip made from a file
// stays unloaded and plays silence.
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

    // Already running. Point it at whoever is asking now, and tell them the
    // model is loaded: readiness is announced once, when it finishes loading,
    // so a caller that arrives after that would otherwise wait for a message
    // that has already been and gone.
    if (window.FleeVoiceBridge && window.FleeVoiceBridge.worker) {
      window.FleeVoiceBridge.targetName = targetName;
      if (window.FleeVoiceBridge.ready) {
        window.FleeVoiceBridge.send(
          "HandleVoiceReady", String(window.FleeVoiceBridge.sampleRate));
      }
      return;
    }

    var state = {
      worker: null,
      targetName: targetName,
      ready: false,
      sampleRate: 24000,
      // One line at a time. The worker holds a single model whose state
      // `start_generation` resets, so a second generate would corrupt the
      // first. Unity asks for a Pupil's whole reply at once, so the requests
      // that queue here are the normal case rather than an edge case.
      queue: [],
      chunks: [],
      // Finished audio, by request id, waiting for Unity to copy it out.
      samples: {},
      activeRequest: "",
      send: function (methodName, value) {
        if (this.targetName) {
          SendMessage(this.targetName, methodName, value || "");
        }
      },
      // Hand the worker the next line, if it is free to take one.
      pump: function () {
        if (!this.ready || this.activeRequest || this.queue.length === 0) {
          return;
        }

        var next = this.queue.shift();
        this.activeRequest = next.id;
        this.chunks = [];
        this.send("HandleVoiceStarted", next.id);
        this.worker.postMessage({
          type: "generate",
          text: next.text,
          voiceName: next.voice,
          temperature: 0.7
        });
      },
      // Give up on the line in flight and move on. Failing it by id lets Unity
      // fall back to ticks straight away rather than waiting out its timeout.
      failActive: function (message) {
        this.chunks = [];
        if (this.activeRequest) {
          this.send("HandleVoiceFailed", this.activeRequest + "|" + message);
          this.activeRequest = "";
          this.pump();
          return;
        }

        this.send("HandleVoiceError", message);
      }
    };
    window.FleeVoiceBridge = state;

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
        state.pump();
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

        // Hand Unity the raw samples rather than an encoded file. A clip
        // fetched from a blob URL never leaves AudioDataLoadState.Unloaded in
        // WebGL, so it plays silently; AudioClip.Create with these samples
        // does not go near the browser's decoder at all.
        state.samples[state.activeRequest] = samples;
        state.send(
          "HandleVoiceSamples",
          state.activeRequest + "|" + samples.length + "|" + state.sampleRate);
        state.activeRequest = "";
        state.pump();
        return;
      }

      if (message.type === "error") {
        state.failActive(message.message || "voice worker failed");
      }
    };

    state.worker.onerror = function (error) {
      state.failActive("voice worker: " + (error.message || "unknown"));
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
    if (!state || !state.worker) {
      return;
    }

    state.queue.push({
      id: UTF8ToString(requestIdPointer),
      voice: UTF8ToString(voicePointer),
      text: UTF8ToString(textPointer)
    });
    state.pump();
  },

  // Drop a line nobody is waiting for any more, so it does not hold up the
  // lines that follow it. The line already in the worker runs to completion:
  // generation is a synchronous loop with no way in.
  VoiceSynthesis_Cancel: function (requestIdPointer) {
    var state = typeof window === "undefined" ? null : window.FleeVoiceBridge;
    if (!state) {
      return;
    }

    var requestId = UTF8ToString(requestIdPointer);
    for (var i = state.queue.length - 1; i >= 0; i--) {
      if (state.queue[i].id === requestId) {
        state.queue.splice(i, 1);
      }
    }

    delete state.samples[requestId];
  },

  // Copy one finished line into a buffer Unity owns, and forget it here.
  // Returns how many samples were written, which is zero if the line is gone.
  VoiceSynthesis_ReadSamples: function (requestIdPointer, destination, capacity) {
    var state = typeof window === "undefined" ? null : window.FleeVoiceBridge;
    if (!state) {
      return 0;
    }

    var requestId = UTF8ToString(requestIdPointer);
    var samples = state.samples[requestId];
    if (!samples) {
      return 0;
    }

    var count = Math.min(capacity, samples.length);
    HEAPF32.set(samples.subarray(0, count), destination >> 2);
    delete state.samples[requestId];
    return count;
  }
});
