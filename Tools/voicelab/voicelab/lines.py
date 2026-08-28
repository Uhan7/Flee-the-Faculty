"""Preview lines, taken from the service's presets.

Judge a voice on the sentences it has to deliver. These are opening lines from
`presets/*.json` in the service repository: short, wrong, and in a ten-year-old's
register, which is where a cloned adult voice fails first. A generic test
sentence passes on a voice that cannot read "the M is before the D".

Only `openingLine` is copied here. The Correction is the answer key and never
leaves the service (CLAUDE.md rule 2, GDD 18.2), and `assert_no_answer_key`
below fails the build rather than trusting that to habit.
"""

# presetId -> the lines a Pupil says first, verbatim from presets/*.json.
PREVIEW_LINES: dict[str, tuple[str, ...]] = {
    "gmdas": (
        "The M is before the D, so multiplication always goes first. That's the whole point of the letters.",
        "You add before you subtract. A comes before S, that's just the order of the rule.",
    ),
    "philippine-archipelago": (
        "It's only a theory, so it's just a guess. Nobody actually knows how the islands got here.",
        "The islands have just always been there. The ground doesn't go anywhere.",
    ),
    "photosynthesis": (
        "Plants eat the dirt, right? That's what the soil is for.",
        "Leaves are just green. That's their colour. It isn't for anything.",
    ),
    "scientific-investigation": (
        "A hypothesis is your guess. You guess what happens, then you check if you were right.",
        "The dependent variable is the thing you change. Everything depends on it, that's why it's called that.",
    ),
}

# One line per slot, for the six-way comparison. Short enough to re-listen to
# quickly, long enough to carry prosody.
SLOT_PROBE = "Plants eat the dirt, right? That's what the soil is for."

FORBIDDEN = ("correction", "answer key")


def assert_no_answer_key_in(texts, where: str) -> None:
    """The same guard, for lines that arrive from outside this module.

    `bake-lines` renders whatever the client exports. The client holds no
    Correction and cannot, but a guard that only covers the lines typed into this
    file stops covering anything the moment the input starts coming from
    somewhere else.
    """
    haystack = " ".join((text or "").lower() for text in texts)
    for term in FORBIDDEN:
        if term in haystack:
            raise SystemExit(
                f"Refusing to bake: a line in {where} mentions '{term}'. "
                f"The Correction is the answer key and must not reach the client. "
                f"See the service's CLAUDE.md rule 2."
            )


def assert_no_answer_key() -> None:
    """Refuse to run if a Correction has been pasted in here.

    `scripts/sync-to-client.sh` in the service does the same check on schema
    files. This module is the other place where service content is copied into
    the client by hand, so it gets the same guard.
    """
    haystack = " ".join(
        line.lower() for lines in PREVIEW_LINES.values() for line in lines
    )
    haystack += " " + SLOT_PROBE.lower()

    for term in FORBIDDEN:
        if term in haystack:
            raise SystemExit(
                f"Refusing to run: a preview line mentions '{term}'. "
                f"The Correction is the answer key and must not reach the client. "
                f"See the service's CLAUDE.md rule 2."
            )


def lines_for(preset: str | None) -> tuple[str, ...]:
    if preset is None:
        return tuple(line for lines in PREVIEW_LINES.values() for line in lines)
    if preset not in PREVIEW_LINES:
        raise SystemExit(
            f"Unknown preset '{preset}'. Known: {', '.join(sorted(PREVIEW_LINES))}"
        )
    return PREVIEW_LINES[preset]
