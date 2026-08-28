"""The two voices, and how each is tuned into a ten-year-old's register.

GDD 8.3 describes six slots and the service still sends them: `cast.py` pins one
of V1 to V6 to each Character, and `rules.py` refuses to draw a Classroom where
two Pupils share one. None of that changes. The client folds the six onto the two
voices that were actually recorded, and this module is the two.

What that costs is worth naming. `rules.py` separates voices because Pupils speak
aloud one after another, so three girls in one Classroom now sound identical.
The name box, the sprite, and the Personality are what tell them apart.

The README records the six-slot offsets this replaced, along with the pair that
had to be re-tuned after `preview` measured them as one voice. Restoring six is
that table plus the `VoiceId` enum in the client, and nothing between them.
"""

from dataclasses import dataclass

# Both recordings are adults reading child dialogue, the way animation does it.
# The lift moves a recorded adult into a ten-year-old's register.
#
# Measured, not guessed. Two public talks by ten-year-olds gave median pitches of
# 269Hz and 240Hz, so call the target register 255Hz.
#
# The lift belongs to one recorded voice, not to the game, so it is computed per
# voice rather than stored:
#
#   lift = 12 * log2(255 / measured_median_f0_of_the_voice)
#
# `voicelab relift` measures the saved voice states and writes the answer into
# voices.json. Run it after re-recording, or after changing `median_f0`.
CHILD_TARGET_HZ = 255.0

# Past this, a phase vocoder with no formant correction sounds thin rather than
# young. A speaker who needs more than this is the wrong speaker, which is a
# casting answer and not a tuning one.
MAX_LIFT_SEMITONES = 8.0


def lift_for(source_hz: float) -> tuple[float, str | None]:
    """The formula above, applied. Returns the lift and a warning if clamped.

    A stored constant cannot serve both voices. The two references measure 282Hz
    and 233Hz as clones, which want -1.8 and +1.6 semitones. One number applied
    to both leaves one of them in the wrong register.
    """
    import math

    if not source_hz or source_hz <= 0 or math.isnan(source_hz):
        return 0.0, "no usable pitch measured, lift left at zero"

    wanted = 12.0 * math.log2(CHILD_TARGET_HZ / source_hz)
    if wanted > MAX_LIFT_SEMITONES:
        return MAX_LIFT_SEMITONES, (
            f"wants {wanted:+.1f} semitones to reach {CHILD_TARGET_HZ:.0f}Hz, "
            f"clamped at {MAX_LIFT_SEMITONES:+.1f}: too deep to pass as ten "
            f"without formant correction"
        )
    if wanted < -MAX_LIFT_SEMITONES:
        return -MAX_LIFT_SEMITONES, f"wants {wanted:+.1f} semitones, clamped"
    return wanted, None


@dataclass(frozen=True)
class VoiceSpec:
    """One recorded voice: what it is called and how it is paced."""

    name: str
    pause_ms: int
    description: str


# How long a gap to insert between sentences, per voice.
#
# Pause density is a property of the person who was recorded, not of the engine,
# and the clone copies it: the girl reference sits at 15.0% silence and the boy
# at 39.5%, so the same line read by both makes her sound like she is rushing.
# Adding a gap to her take is the fix that does not touch either clone.
#
# The boy is deliberately zero rather than a small number. Pacing works by
# splitting the line into sentences and synthesising each one, which throws away
# the trailing pause the model generates on its own; at 60ms his silence fell
# from 39.5% to 21.6%, the opposite of what was wanted. Zero routes him through
# the unsplit path instead.
VOICES: tuple[VoiceSpec, ...] = (
    VoiceSpec("girl", 1500, "Female, ten years old. Speaks for V1 to V3."),
    VoiceSpec("boy", 0, "Male, ten years old. Speaks for V4 to V6."),
)

BY_NAME = {spec.name: spec for spec in VOICES}
NAMES = tuple(spec.name for spec in VOICES)
