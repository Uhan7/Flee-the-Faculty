"""The name a baked clip is filed under. The other half is `VoiceKey.cs`.

Two programs in two languages have to agree on one string, so the rule is
deliberately small: collapse whitespace, hash the UTF-8 bytes with FNV-1a, print
sixteen hex characters. A cryptographic digest would work equally well and would
have to behave identically under IL2CPP, in the Unity editor, and in CPython;
this does not need the argument.

Keying on the text rather than on a conversation id and a line number means a
reordered conversation keeps its audio and an edited line loses it, which is the
right answer in both cases.
"""

FNV_OFFSET_BASIS = 0xCBF29CE484222325
FNV_PRIME = 0x100000001B3
MASK64 = 0xFFFFFFFFFFFFFFFF

# The client folds the service's six slots onto these two before it asks for a
# clip, so a key never carries a V1 through V6. See `VoiceCatalog.TryParseWire`.
VOICES = ("girl", "boy")


def normalise(text: str) -> str:
    """Every run of whitespace becomes one space, and the ends are trimmed.

    Unity wraps long strings when it serialises a TextArea, so the same sentence
    can arrive with a newline in it on one machine and a space on another.
    """
    return " ".join((text or "").split())


def fingerprint(normalised_text: str) -> str:
    """FNV-1a, 64 bits, lower-case hex."""
    hashed = FNV_OFFSET_BASIS
    for byte in normalised_text.encode("utf-8"):
        hashed ^= byte
        hashed = (hashed * FNV_PRIME) & MASK64
    return f"{hashed:016x}"


def key_for(voice: str, text: str) -> str:
    """The key for one spoken line, or an empty string when there is nothing.

    Matches `VoiceKey.For` exactly, including the empty-string answers, so a
    disagreement shows up as a missing clip rather than as a wrong one.
    """
    name = (voice or "").strip().lower()
    if name not in VOICES:
        return ""

    normalised = normalise(text)
    if not normalised:
        return ""

    return f"{name}_{fingerprint(normalised)}"


# Both implementations are checked against these. `VoiceLibraryBuilder` runs the
# same four before it rebuilds, which is the moment a silent disagreement would
# start costing time: every clip would bake correctly and every lookup would miss.
GOLDEN: tuple[tuple[str, str, str], ...] = (
    ("girl", "Hello there.", "girl_892362dd056bbf55"),
    ("girl", "Plants eat the dirt, right?", "girl_365fe4b4b1fe52f8"),
    ("boy", "  spaced   out\n\nline  ", "boy_5473212866159303"),
    ("girl", "Café naïve résumé — unicode.", "girl_c522c547d1bd00df"),
)


def check_golden() -> None:
    """Raise if this module has drifted from `VoiceKey.cs`."""
    for voice, text, expected in GOLDEN:
        actual = key_for(voice, text)
        if actual != expected:
            raise SystemExit(
                f"keys.py no longer agrees with VoiceKey.cs: {voice} {text!r} "
                f"gives {actual}, expected {expected}"
            )


if __name__ == "__main__":
    check_golden()
    print(f"{len(GOLDEN)} key vectors match VoiceKey.cs")
