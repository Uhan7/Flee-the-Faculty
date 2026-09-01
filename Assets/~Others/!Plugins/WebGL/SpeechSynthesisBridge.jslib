mergeInto(LibraryManager.library, {
  SpeechSynthesis_IsSupported: function () {
    return typeof window !== 'undefined'
      && 'speechSynthesis' in window
      && typeof SpeechSynthesisUtterance !== 'undefined'
      ? 1
      : 0;
  },

  SpeechSynthesis_Speak: function (
    targetNamePointer,
    requestIdPointer,
    textPointer,
    voiceId,
    rate,
    pitch,
    volume) {
    if (!window.speechSynthesis || typeof SpeechSynthesisUtterance === 'undefined') {
      return 0;
    }

    var targetName = UTF8ToString(targetNamePointer);
    var requestId = UTF8ToString(requestIdPointer);
    var text = UTF8ToString(textPointer);
    var utterance = new SpeechSynthesisUtterance(text);
    var voices = window.speechSynthesis.getVoices().filter(function (voice) {
      return !voice.lang || voice.lang.toLowerCase().indexOf('en') === 0;
    });
    var girlPattern = /female|samantha|victoria|karen|moira|ava|susan|tessa|fiona|serena|aria/i;
    var boyPattern = /male|daniel|alex|fred|tom|oliver|aaron|arthur|rishi/i;
    var preferredPattern = voiceId === 1 ? girlPattern : boyPattern;
    var preferredVoices = voices.filter(function (voice) {
      return preferredPattern.test(voice.name);
    });
    var selectedVoices = preferredVoices.length > 0 ? preferredVoices : voices;

    if (selectedVoices.length > 0) {
      utterance.voice = selectedVoices[requestId.length % selectedVoices.length];
    }

    utterance.rate = rate;
    utterance.pitch = pitch;
    utterance.volume = volume;
    utterance.onend = function () {
      SendMessage(targetName, 'OnBrowserSpeechFinished', requestId);
    };
    utterance.onerror = function () {
      SendMessage(targetName, 'OnBrowserSpeechFinished', requestId);
    };

    window.speechSynthesis.cancel();
    window.speechSynthesis.speak(utterance);
    return 1;
  },

  SpeechSynthesis_Stop: function () {
    if (window.speechSynthesis) {
      window.speechSynthesis.cancel();
    }
  }
});
