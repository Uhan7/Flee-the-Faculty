mergeInto(LibraryManager.library, {
  SpeechRecognition_IsSupported: function () {
    if (typeof window === "undefined") {
      return 0;
    }

    return window.SpeechRecognition || window.webkitSpeechRecognition ? 1 : 0;
  },

  SpeechRecognition_StartListening: function (targetNamePointer) {
    if (typeof window === "undefined") {
      return;
    }

    var targetName = UTF8ToString(targetNamePointer);
    var state = window.FleeSpeechBridge;

    if (!state) {
      state = {
        recognition: null,
        targetName: "",
        finalTranscript: "",
        isActive: false,
        manualStop: false,
        sendMessage: function (methodName, value) {
          if (!this.targetName) {
            return;
          }

          SendMessage(this.targetName, methodName, value || "");
        }
      };

      window.FleeSpeechBridge = state;
    }

    var RecognitionType = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!RecognitionType) {
      state.targetName = targetName;
      state.sendMessage("HandleSpeechError", "This browser does not support speech recognition.");
      return;
    }

    if (!state.recognition) {
      state.recognition = new RecognitionType();
      state.recognition.continuous = true;
      state.recognition.interimResults = true;
      state.recognition.lang = "en-US";
      state.recognition.maxAlternatives = 1;

      state.recognition.onstart = function () {
        state.isActive = true;
        state.sendMessage("HandleSpeechStatus", "started");
      };

      state.recognition.onresult = function (event) {
        var combinedTranscript = state.finalTranscript;
        var interimTranscript = "";

        for (var i = event.resultIndex; i < event.results.length; i++) {
          var transcriptChunk = event.results[i][0].transcript;
          if (event.results[i].isFinal) {
            combinedTranscript += transcriptChunk + " ";
          } else {
            interimTranscript += transcriptChunk;
          }
        }

        state.finalTranscript = combinedTranscript;
        state.sendMessage("HandleTranscriptUpdated", (combinedTranscript + interimTranscript).trim());
      };

      state.recognition.onerror = function (event) {
        state.isActive = false;
        state.manualStop = false;

        var errorMessage = event && event.error ? event.error : "unknown error";
        state.sendMessage("HandleSpeechError", errorMessage);
      };

      state.recognition.onend = function () {
        state.isActive = false;

        var status = state.manualStop ? "stopped" : "ended";
        state.manualStop = false;
        state.sendMessage("HandleSpeechStatus", status);
      };
    }

    state.targetName = targetName;
    state.finalTranscript = "";
    state.manualStop = false;

    if (state.isActive) {
      state.sendMessage("HandleSpeechStatus", "already-listening");
      return;
    }

    state.sendMessage("HandleTranscriptUpdated", "");

    try {
      state.recognition.start();
    } catch (error) {
      var safeMessage = error && error.message ? error.message : "Unable to start speech recognition.";
      state.sendMessage("HandleSpeechError", safeMessage);
    }
  },

  SpeechRecognition_StopListening: function () {
    if (typeof window === "undefined") {
      return;
    }

    var state = window.FleeSpeechBridge;
    if (!state || !state.recognition) {
      return;
    }

    if (!state.isActive) {
      state.sendMessage("HandleSpeechStatus", "stopped");
      return;
    }

    state.manualStop = true;

    try {
      state.recognition.stop();
    } catch (error) {
      var safeMessage = error && error.message ? error.message : "Unable to stop speech recognition.";
      state.sendMessage("HandleSpeechError", safeMessage);
    }
  }
});
