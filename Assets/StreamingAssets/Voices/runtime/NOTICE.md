# Third-party runtime

`ptts_wasm.js` and `ptts_wasm_bg.wasm` are a WebAssembly build of Pocket TTS by
Laurent Mazare, from the `xn` inference framework.

- xn — Apache-2.0 — https://github.com/LaurentMazare/xn
- Pocket TTS — MIT — https://github.com/kyutai-labs/pocket-tts
- Model weights — CC BY 4.0 — https://huggingface.co/kyutai/pocket-tts

The weights are not here. They are 146MB, past GitHub's 100MB file limit, and
are fetched once at run time and cached by the browser.

The voice states beside this folder are ours: cloned from two recordings by
`Tools/voicelab`, then converted to this runtime's layout by
`voicelab web-voices`.
