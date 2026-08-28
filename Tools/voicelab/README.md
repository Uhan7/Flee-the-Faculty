# voicelab

Turns two recordings into the two voices the client plays.

## Two voices against six slots

GDD 8.3 specifies six voice slots, and the service still uses all six. `cast.py`
pins one of V1 to V6 to each of the ten Characters, and `rules.py` refuses to
draw a Classroom where two Pupils share one, because Pupils speak aloud one
after another. None of that changes.

The client folds those six onto the two voices that were actually recorded: V1
to V3 speak with the girl, V4 to V6 with the boy. That is the same split
`cast.py` already holds as `FEMALE_VOICES` and `MALE_VOICES`.

The cost is real. Three girls in one Classroom now sound identical, which is the
thing `rules.py` was separating voices to avoid. The name box, the sprite, and
the Personality are what tell them apart instead. The appendix at the end holds
the six-slot offsets this replaced, so restoring them is a table and an enum.

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

Only `bake` needs those weights. `bake-lines` replays voice states that `bake`
already wrote, and the open weights load and speak those just as well, so
nothing downstream of the one-time bake needs a token.

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
`relift` and `bake-lines` look by default. The safetensors are about 19MB and
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

**bake-lines** renders the client's authored dialogue, one clip per line.

```bash
uv run voicelab bake-lines
```

Lines that already have a clip are skipped, so a re-run after adding one line
costs one line. Pass `--force` after a `relift`, because every existing clip was
baked from the old numbers.

## The round trip with Unity

Authored dialogue lives in Unity and the voices live here, so the work list
passes between them as a file. Both sides compute the same key for a line, from
the voice and a fingerprint of the text: `VoiceKey.cs` on one side,
`voicelab/keys.py` on the other, checked against the same four vectors.

1. In Unity: **Flee the Faculty > Voices > Export Dialogue Lines**. Writes
   `lines-to-bake.json` here.
2. Here: `uv run voicelab bake-lines`. Writes WAVs into `Assets/Audio/Voices/`,
   named after their key, for example `girl_365fe4b4b1fe52f8.wav`.
3. In Unity: **Flee the Faculty > Voices > Rebuild Voice Library**. Files them
   into `Assets/Resources/Voice Clip Library.asset`, which the game loads.

Repeat any time a line changes. Editing a line changes its fingerprint, so the
old clip stops matching and the new one gets baked; reordering a conversation
changes nothing.

Only authored lines go through this. What a Pupil says in a real Encounter is
written by the model at run time, which is the half ADR-0011 gives to the
service.

## Layout

```
voicelab/voices.py   The two voices: lift into a child register, and pacing
voicelab/dsp.py      Pitch shift, level, and the clip report
voicelab/lines.py    Preview lines, copied from the service's presets
voicelab/keys.py     The clip key. Twin of the client's VoiceKey.cs
voicelab/synth.py    The one place this tool calls Pocket TTS
voicelab/__main__.py The five commands
samples/             Your two recordings. Gitignored.
out-real/            The baked voice states and voices.json. Gitignored.
lines-to-bake.json   Written by Unity. The work list for bake-lines.
```

`lines.py` holds opening lines only, and `assert_no_answer_key` fails the run if
a Correction is ever pasted in. `assert_no_answer_key_in` applies the same guard
to whatever Unity exports. It is the same check `scripts/sync-to-client.sh`
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

## Not built yet

Authored lines play from baked clips today. Lines the model writes during an
Encounter still have no audio: that needs the service to synthesise and return
it, which is ADR-0011 and is not implemented. `DialogueVoicePlayer` in the client
already resolves a clip inside a coroutine so that a download fits there.

## Appendix: the six-slot offsets

Kept because the measurements cost something to get, and because six is where
this goes if the identical-sounding classmates ever become a problem.

Each voice was lifted into the child register, then each of its three slots took
a further offset. Restoring them means putting this table back in `voices.py`,
widening the `VoiceId` enum in the client, and re-baking.

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
Plain resampling moves both together and cannot produce that slot at all, so both
the tool and any runtime need a phase vocoder rather than a resampler.
