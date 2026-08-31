# Baked voice clips

One WAV per authored line per voice. Generated, not hand-made: nothing in this
folder should be renamed, edited, or added to by hand.

## The file name is the lookup key

```
girl_365fe4b4b1fe52f8.wav
^^^^ ^^^^^^^^^^^^^^^^
|    fingerprint of the line's text
the voice: girl or boy
```

`VoiceKey.cs` builds that name at run time from the line about to be spoken, and
`scripts/bake_lines.py` in the service repository builds the same name when the
clip is baked. A rename here is a clip the game can no longer find.

## Two voices here, six in a Classroom

There are two base voices, so a line spoken by any of the three girls in a
Classroom is one file. That is a fact about baking rather than about how a Pupil
sounds: GDD 8.3 has six voice slots and the service speaks all six, rendering
them from these two models with a pitch, rate, and level offset each. See
`speech/slots.py` in the service and `VoiceId.cs` here.

A Pupil never plays a baked clip anyway. Everything she says in an Encounter is
written by the model at turn time, so it arrives as audio from `POST /v1/speech`
in whichever of the six slots `cast.py` pinned to her Character. This folder is
authored dialogue only, which is spoken in the base voice with no slot offset.

## Regenerating

1. In Unity: **Flee the Faculty > Voices > Export Dialogue Lines**
2. In the service repository:

   ```bash
   uv run python scripts/bake_lines.py \
       --lines ../flee-the-faculty-game-client/Tools/voicelab/lines-to-bake.json \
       --out ../flee-the-faculty-game-client/Assets/Audio/Voices
   ```

3. Back in Unity: **Flee the Faculty > Voices > Rebuild Voice Library**

Step 3 writes `Assets/Resources/Voice Clip Library.asset`, which is what the game
actually loads. A clip that exists here but is missing from that asset never
plays.

Step 2 runs in the service repository on purpose. It imports the engine that
answers `POST /v1/speech`, so a baked line and a live line come out of the same
code. Before ADR-0013 baking ran on Pocket TTS in `Tools/voicelab` and live lines
came from a different model, which made one Character two people depending on
whether her line had been written down in advance.
