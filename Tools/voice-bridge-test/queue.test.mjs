// Exercises the real VoiceSynthesisBridge.jslib against a stub worker.
//
// Run with `node Tools/voice-bridge-test/queue.test.mjs`. No dependencies, and
// nothing here needs Unity: it loads the shipped .jslib and calls it.
//
// The thing under test is ordering. A Pupil's reply is two lines delivered
// together, so two requests are always in flight, and the bridge has to keep
// them apart: each must come back under its own request id, in order, carrying
// its own audio. Getting that wrong is silent, which is why it is tested here
// rather than by listening.
import { readFile } from 'node:fs/promises';

const source = await readFile(
  new URL('../../Assets/Plugins/WebGL/VoiceSynthesisBridge.jslib', import.meta.url),
  'utf8');

// ---- Emscripten and browser stand-ins ----
const library = {};
const sent = [];
const generated = [];

globalThis.LibraryManager = { library };
globalThis.mergeInto = (target, members) => Object.assign(target, members);
globalThis.UTF8ToString = (value) => value;
globalThis.SendMessage = (target, method, value) => sent.push([method, value]);
globalThis.window = globalThis;
globalThis.HEAPF32 = new Float32Array(1 << 16);
globalThis.fetch = async () => ({ ok: true });

let worker = null;
globalThis.Worker = class {
  constructor() { worker = this; this.onmessage = null; this.onerror = null; }
  postMessage(message) {
    if (message.type === 'configure') {
      queueMicrotask(() => this.onmessage({ data: { type: 'configured' } }));
    } else if (message.type === 'load') {
      queueMicrotask(() => this.onmessage({ data: { type: 'loaded', sampleRate: 24000 } }));
    } else if (message.type === 'generate') {
      generated.push(message.text);
      // The real worker runs one generation to completion before it looks at
      // the next message, so the stub answers on its own tick too.
      queueMicrotask(() => {
        if (message.text.includes('breaks')) {
          this.onmessage({ data: { type: 'error', message: 'out of memory' } });
          return;
        }

        this.onmessage({ data: { type: 'chunk', data: new Float32Array([generated.length / 10]) } });
        this.onmessage({ data: { type: 'done' } });
      });
    }
  }
};

eval(source);

const settle = () => new Promise((resolve) => setTimeout(resolve, 10));

library.VoiceSynthesis_Begin('Dialogue Voice Player', JSON.stringify({
  workerUrl: 'w.js', voicesBase: 'v', voices: ['girl', 'boy'],
  modelUrl: 'm.gguf', tokenizerUrl: 't.model', quant: 'q8',
}));
await settle();

// Two lines at once, which is what a Pupil's reply is: a restatement and a
// follow-up, prefetched together the moment the conversation opens.
library.VoiceSynthesis_Speak('1', 'girl', 'Plants make their own food.');
library.VoiceSynthesis_Speak('2', 'girl', 'So what does the soil give them?');
await settle();

// A third line arrives later and must not disturb the two before it.
library.VoiceSynthesis_Speak('3', 'boy', 'My turn now.');
// And a fourth that nobody wants any more.
library.VoiceSynthesis_Speak('4', 'boy', 'Never heard.');
library.VoiceSynthesis_Cancel('4');
await settle();

const clips = sent.filter(([method]) => method === 'HandleVoiceSamples').map(([, v]) => v);
const starts = sent.filter(([method]) => method === 'HandleVoiceStarted').map(([, v]) => v);
const errors = sent.filter(([method]) => method === 'HandleVoiceError');

const expectations = [
  ['ready fires once', sent.filter(([m]) => m === 'HandleVoiceReady').length === 1],
  ['three lines generated, the cancelled one dropped',
    JSON.stringify(generated) === JSON.stringify([
      'Plants make their own food.', 'So what does the soil give them?', 'My turn now.'])],
  ['every request is started exactly once, in order',
    JSON.stringify(starts) === JSON.stringify(['1', '2', '3'])],
  ['each clip comes back under its own id, in order',
    JSON.stringify(clips.map((c) => c.split('|')[0])) === JSON.stringify(['1', '2', '3'])],
  ['each clip reports one sample at 24000Hz',
    clips.every((c) => c.split('|')[1] === '1' && c.split('|')[2] === '24000')],
  ['reading a clip copies its own audio out, once',
    (() => {
      const read = (id) => {
        const n = library.VoiceSynthesis_ReadSamples(id, 0, 8);
        return n === 0 ? null : globalThis.HEAPF32[0];
      };
      const first = [read('1'), read('2'), read('3')];
      // Distinct audio per line, and nothing left behind to be read twice.
      return new Set(first).size === 3 && read('1') === null;
    })()],
  ['no global error', errors.length === 0],
];

let failed = 0;
for (const [name, ok] of expectations) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) failed++;
}

// A failing line must fail by id and let the queue carry on.
sent.length = 0;
generated.length = 0;
library.VoiceSynthesis_Speak('5', 'girl', 'This one breaks.');
library.VoiceSynthesis_Speak('6', 'girl', 'This one should still be heard.');
await settle();

const failures = sent.filter(([m]) => m === 'HandleVoiceFailed').map(([, v]) => v.split('|')[0]);
const recovered = sent.filter(([m]) => m === 'HandleVoiceSamples').map(([, v]) => v.split('|')[0]);
const after = [
  ['a failed line is named by id', JSON.stringify(failures) === JSON.stringify(['5'])],
  ['the line behind it still plays', JSON.stringify(recovered) === JSON.stringify(['6'])],
];
for (const [name, ok] of after) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}`);
  if (!ok) failed++;
}

console.log(failed === 0 ? '\nALL PASS' : `\n${failed} FAILED`);
process.exit(failed === 0 ? 0 : 1);
