# voicelab

The casting tool. It clones a voice from about twenty seconds of a recording, so
you can hear what a candidate speaker would sound like as a Pupil before anyone
commits to them.

## It no longer renders anything the game plays

That changed in ADR-0013, and this README still describes the world before it in
places. What the game plays now comes from the service:

| | Then | Now |
|---|---|---|
| Lines a Pupil says in an Encounter | Pocket TTS in the browser, WebAssembly | `POST /v1/speech`, Piper on the service |
| Authored dialogue, baked | `voicelab bake-lines`, Pocket TTS | `scripts/bake_lines.py` in the service, Piper |
| Voice slots | Six folded onto two | Six, rendered from two Piper models |

The reason to move both together is that a Character has to be one person. When
baking ran here and live lines ran somewhere else, the same Pupil sounded like
two people depending on whether her line had been written down in advance. The
baker now imports the same engine that answers the route, so that cannot recur.

The six-slot offsets this README kept in its appendix were not thrown away: they
are the table in `src/flee/speech/slots.py`, and the reason they could be
restored is that Piper has a duration control the browser runtime did not. The
appendix stays below as the record of where those numbers came from.

## What it is still for

Casting. Pocket TTS clones a real voice, which is exactly what you want when
deciding whether to record someone, and exactly what you do not want in a
runtime: it stores a voice as a transformer KV cache pinned to one checkpoint,
and six renders of one sentence moved its pitch by 106Hz. Piper moved 8.8Hz.

`check`, `preview`, `shift`, `bake`, and `relift` all still do what they say. The
two that are gone are `bake-lines` and `web-voices`. Both rendered audio the game
played, and both would now put a Pocket TTS voice into a game that speaks in
Piper. The replacement for the first is `scripts/bake_lines.py` in the service
repository; the second has nothing to replace it, because there is no longer a
browser runtime to convert voices for.

## Unblock voice cloning first

Pocket TTS ships two sets of weights. The public ones have the cloning path
removed, and the cloning weights are gated behind the model's terms. Without
that, `voicelab` runs only in `--catalog` mode against stock voices.

1. Open <https://huggingface.co/kyutai/pocket-tts> and accept the terms.
2. Log in locally:

```bash
uvx hf auth login
```

Both steps need your HuggingFace account, so they are yours to do.

Only `bake` needs those weights. `preview --catalog` runs against stock voices,
which the open weights load and speak just as well, so you can tune everything
except the clone itself without a token.

## Recording the two references

Kyutai quotes about 20 seconds of reference audio per voice. `voicelab check`
warns below 20 seconds and fails below 12. Longer is better: the boy reference
at 20.4 seconds drifts about twice as much between lines as the girl's at 33.

- One session, one mic, one room, both speakers. Different rooms make the two
  voices sound like they are in different buildings.
- Mono WAV, 24000Hz or higher, 16-bit or better.
- Nothing applied. No compression, no reverb, no gate, no EQ. Pocket TTS
  reproduces the recording, and it reproduces the processing with it.
- Read in character. The model clones delivery, not only timbre, so a neutral
  read gives you the right voice being the wrong person.
- Read the game's own register. `uv run python scripts/play.py --preset` in the
  service prints real opening lines and Restatements. Read those.
- Adults reading child dialogue, then lifted here, the way animation does it. Do
  not squeeze a child's pitch out of an adult throat; that trains the strain into
  the voice.

Put the files in `samples/girl.wav` and `samples/boy.wav`. They are gitignored.

## Commands

```bash
uv sync
```

**check** reads the recordings and says whether they are good enough. No model
load, so it runs in a second.

```bash
uv run voicelab check --girl samples/girl.wav --boy samples/boy.wav
```

**shift** lifts the recordings themselves into a child register, with no model
involved. It answers the question that decides everything else: do these two
people, lifted, sound like a schoolgirl and a schoolboy? A clone can only
preserve what is already there.

```bash
uv run voicelab shift --girl samples/girl.wav --boy samples/boy.wav
```

**preview** speaks both cloned voices on real preset lines.

```bash
uv run voicelab preview --girl samples/girl.wav --boy samples/boy.wav --preset photosynthesis
```

Add `--catalog` to run against stock voices while the cloning weights are still
locked.

**bake** exports the two voice states and the manifest.

```bash
uv run voicelab bake --girl samples/girl.wav --boy samples/boy.wav --out out-real
```

You get `girl.safetensors`, `boy.safetensors`, and `voices.json`, which is where
`relift` looks by default. The safetensors are about 19MB and
13MB. The 219MB model that reads them is downloaded separately and is not
committed here.

**relift** re-measures voice states that already exist and rewrites the numbers
in `voices.json`, without re-cloning anything.

```bash
uv run voicelab relift
```

The lift is how far above its own pitch a voice has to sit to read as ten years
old. Getting it wrong does not damage a clone, it files the clone in the wrong
register: a bad measurement once put a boy voice at 470Hz, above every girl. Run
this after changing `median_f0` or `CHILD_TARGET_HZ`, and after re-recording.

## Who can bake

The voice states are not in this repository. A cloned voice is a portable model
of a real person: anyone holding `girl.safetensors` can make that person say
anything, and this repository is public. The people who were recorded agreed to
voice a game, which is not the same thing. `out-real/` and `samples/` are both
gitignored for that reason, and they are the only things `bake` cannot work
without.

So there are two roles.

**If you hold the recordings**, you can run everything here.

**If you do not**, you can still audition stock voices with `--catalog`, and you
never needed this tool to work on dialogue. Baking has not depended on the
recordings since ADR-0013: the two Piper models are public and the service's
`scripts/fetch_voices.sh` downloads them.

## The round trip with Unity

Authored dialogue lives in Unity and the voices live in the service, so the work
list passes between them as a file that lands here. Both sides compute the same
key for a line, from the voice and a fingerprint of the text: `VoiceKey.cs` on
one side, `scripts/bake_lines.py` on the other, checked against the same four
vectors.

1. In Unity: **Flee the Faculty > Voices > Export Dialogue Lines**. Writes
   `lines-to-bake.json` here.
2. In the service repository:

   ```bash
   uv run python scripts/bake_lines.py \
       --lines ../flee-the-faculty-game-client/Tools/voicelab/lines-to-bake.json \
       --out ../flee-the-faculty-game-client/Assets/Audio/Voices
   ```

3. In Unity: **Flee the Faculty > Voices > Rebuild Voice Library**. Files them
   into `Assets/Resources/Voice Clip Library.asset`, which the game loads.

Repeat any time a line changes. Editing a line changes its fingerprint, so the
old clip stops matching and the new one gets baked; reordering a conversation
changes nothing.

A line with no clip falls back to the syllable ticks in `DialogueActor`, so
nothing is broken in between. Only the Console says otherwise.

Only authored lines go through this. What a Pupil says in a real Encounter is
written by the model at run time and arrives from `POST /v1/speech` as it is
said. See ADR-0013.

## Layout

```
voicelab/voices.py   The two voices: lift into a child register, and pacing
voicelab/dsp.py      Pitch shift, level, and the clip report
voicelab/lines.py    Preview lines, copied from the service's presets
voicelab/synth.py    The one place this tool calls Pocket TTS
voicelab/piper_engine.py  Piper, for comparing a clone against what ships
voicelab/__main__.py The five commands
samples/             Your two recordings. Gitignored.
out-real/            The baked voice states and voices.json. Gitignored.
lines-to-bake.json   Written by Unity. The work list the service's baker reads.
```

`lines.py` holds opening lines only, and `assert_no_answer_key` fails the run if
a Correction is ever pasted in. It is the same check `scripts/sync-to-client.sh`
applies on the service side, for the same reason: the Correction is the answer
key and the client is a public bundle. See the service's CLAUDE.md rule 2 and
GDD 18.2.

## Measure with pyin, not yin

`dsp.median_f0` uses `librosa.pyin`. `yin` returns a number for every frame
including silence and breath and offers no voicing flag to drop them, and it
slips an octave on low voices. Both faults pushed the same way on the male
reference: it read the boy clone near 160Hz where the voice actually speaks near
245Hz, which asked for a lift past the eight-semitone clamp and put the boy above
the girl.

The lift is a median over twelve renders of several different lines, not one
render of one. Pocket TTS varies its pitch line to line, and at five renders of a
single sentence the boy's lift moved 1.8 semitones between two runs. Twelve
brings the girl to zero movement between runs and the boy to about half a
semitone. His remaining spread, roughly 45Hz against the girl's 20Hz, is the
short reference and the fix is a longer one.

## Lines the model writes

Baking covers authored dialogue. Everything a Pupil says in a real Encounter is
written by the model at turn time, so it cannot be baked by anyone.

Those used to be synthesised in the browser, by the same two voices in a
WebAssembly build of Pocket TTS, which `voicelab web-voices` wrote into
`Assets/StreamingAssets/Voices`. ADR-0013 moved them to the service. That
directory, the 146MB download it needed, and the `web-voices` command that filled
it are all gone.

The measurements below are kept because they are what the decision was made
against.

Measured in that runtime on a 107-character line, single-threaded with only
`simd128`:

| | Audio | Time | Speed | First audio |
|---|---|---|---|---|
| Kyutai's own voice | 5.60s | 1.64s | 3.41x | 0.15s |
| Our girl | 5.04s | 1.50s | 3.37x | 0.15s |
| Our boy | 5.28s | 1.57s | 3.36x | 0.15s |

The 146MB model is not in this repository and cannot be: GitHub rejects files
past 100MB. It is fetched once and cached by the browser, and the game is
playable while it downloads because unspoken lines fall back to the syllable
ticks.

## Appendix: the six-slot offsets

Kept because the measurements cost something to get. Six is where this went:
these numbers are now the table in `src/flee/speech/slots.py`, applied to Piper
rather than to a clone.

Each voice was lifted into the child register, then each of its three slots took
a further offset.

| Slot | Voice | Semitones | Rate | Gain | GDD 8.3 |
|---|---|---|---|---|---|
| V1 | girl | +3.0 | 1.15 | 0dB | Female, high, fast, bright |
| V2 | girl | +3.0 | 0.90 | 0dB | Female, high, slow, dreamy |
| V3 | girl | +0.0 | 1.00 | 0dB | Female, mid, flat, deadpan |
| V4 | boy | +2.0 | 1.15 | 0dB | Male, mid, warm, eager |
| V5 | boy | −3.0 | 0.85 | 0dB | Male, low, slow, weary |
| V6 | boy | −1.0 | 0.95 | +4dB | Male, mid, loud, blunt |

Two things that table had to learn. V4 and V6 are both "mid", so they separate on
rate and level rather than pitch; a first pass gave them 0.1 semitones and 5%
rate, and two Pupils drawn onto those slots would have sounded like one boy.

And pitch and rate have to move independently, because V2 is high and slow.
Plain resampling moves both together and cannot produce that slot at all, so this
tool needs a phase vocoder rather than a resampler.

The service does not, and that is the one line of this appendix that turned out
to be wrong about runtimes in general rather than about this tool. Piper has
`length_scale`, which stretches a line at synthesis time through the model's own
duration predictor, so a slot is rendered by asking for the wrong duration on
purpose and then playing the result at a different rate. The two cancel into
exactly this table, with no vocoder and no resampling. See `speech/slots.py`.
