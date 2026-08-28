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
`voicelab/keys.py` builds the same name when the clip is baked. A rename here is
a clip the game can no longer find.

There are two voices, so a line spoken by any of the six girls in the cast is one
file. GDD 8.3 has six voice slots and the service still sends them; the client
folds V1 to V3 onto the girl and V4 to V6 onto the boy. See `VoiceId.cs`.

## Regenerating

1. **Flee the Faculty > Voices > Export Dialogue Lines**
2. `uv run voicelab bake-lines` in `Tools/voicelab`
3. **Flee the Faculty > Voices > Rebuild Voice Library**

Step 3 writes `Assets/Resources/Voice Clip Library.asset`, which is what the game
actually loads. A clip that exists here but is missing from that asset never
plays.

## What is not here

Only authored dialogue. What a Pupil says during a real Encounter is written by
the model when the Learner explains something, so it cannot be baked in advance.
ADR-0011 in the service repository covers that half.
