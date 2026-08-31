"""voicelab: hear what a candidate speaker would sound like as a Pupil.

The casting tool, and only that since ADR-0013. It clones a real voice from
about twenty seconds of a recording, which is exactly what you want when
deciding whether to book someone for a session, and exactly what you do not want
in a runtime: it stores a voice as a transformer KV cache pinned to one
checkpoint, and six renders of one sentence moved its pitch by 106Hz.

Five commands, in the order you use them.

  check    Read the recordings and say whether they are good enough. No model.
  preview  Speak both voices on real game lines so you can listen.
  shift    Lift the recordings themselves into a child register. No model.
  bake     Export the two voice states and the manifest.
  relift   Re-measure saved voice states without re-cloning them.

Run `check` before booking a recording session, `preview` to hear both voices in
the game's own register, and `bake` once you are happy.

Nothing here renders audio the game plays any more. `bake-lines` and
`web-voices` were removed with ADR-0013: what a Pupil says now comes from
`POST /v1/speech`, and authored dialogue is baked by `scripts/bake_lines.py` in
the service repository, which imports the engine that answers that route. One
engine is what stops a Character sounding like two people depending on whether
her line was written down in advance.

Only `bake` needs the gated cloning weights.
"""

import argparse
import json
import sys
import warnings
from pathlib import Path

import numpy as np

from . import dsp, lines, piper_engine, voices
from .synth import Synth

# librosa and numba are noisy about internals this tool does not control, and
# the output of these commands is a table you read by eye.
warnings.filterwarnings("ignore", category=FutureWarning)
warnings.filterwarnings("ignore", category=RuntimeWarning)

CATALOGUE_STAND_INS = {"girl": "mary", "boy": "michael"}

MISSING_STATES = """No {path}.

The voice states are not in this repository and are not meant to be. A cloned
voice is a portable model of a real person: anyone holding the file can make
that person say anything, and this repository is public. The people who were
recorded agreed to voice a game, which is not the same thing.

If you recorded the references, they are yours to rebuild:

  uv run voicelab bake --girl samples/girl.wav --boy samples/boy.wav --out out-real

If you did not, you cannot bake, and you do not need to. Run
Flee the Faculty > Voices > Export Dialogue Lines in Unity, commit the updated
lines-to-bake.json, and ask whoever holds the recordings to run bake-lines.

Nothing is broken while you wait. A line with no clip falls back to the
syllable ticks in DialogueActor, which is what every line said before any of
this existed.""".strip()


def _resolve_paths(args) -> dict[str, Path]:
    out: dict[str, Path] = {}
    for base in voices.NAMES:
        given = getattr(args, base, None)
        if given is None:
            continue
        path = Path(given)
        if not path.is_file():
            raise SystemExit(f"No such recording: {path}")
        out[base] = path
    return out


def cmd_check(args) -> int:
    recordings = _resolve_paths(args)
    if not recordings:
        raise SystemExit("Pass at least one of --girl or --boy.")

    worst = 0
    for base, path in recordings.items():
        report = dsp.report_clip(path)
        print(f"\n{base}: {path.name}")
        print(
            f"  {report['duration_s']:.1f}s file, {report['speech_s']:.1f}s speech, "
            f"{report['sample_rate']}Hz, {report['channels']}ch, "
            f"median pitch {report['median_f0_hz']:.0f}Hz"
        )
        for level, reason in dsp.verdicts(report):
            print(f"  [{level}] {reason}")
            worst = max(worst, {"pass": 0, "warn": 1, "fail": 2}[level])

    print()
    if worst == 2:
        print("Verdict: re-record. A clone reproduces the fault, it does not fix it.")
    elif worst == 1:
        print("Verdict: good enough to test the pipeline, worth re-recording to ship.")
    else:
        print("Verdict: ship these.")
    return 0


def cmd_shift(args) -> int:
    """Pitch the recordings themselves into the child register. No model involved.

    This answers the question that decides everything else: do these two people,
    lifted into a ten-year-old's register, sound like a schoolgirl and a
    schoolboy? It runs in seconds, needs no weights, and it fails fast if the
    answer is no. A clone can only preserve what is already there.
    """
    recordings = _resolve_paths(args)
    if not recordings:
        raise SystemExit("Pass at least one of --girl or --boy.")

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    for name, path in recordings.items():
        # Measure the whole clip, not the excerpt. A five-second window can land
        # on one unusually high or low phrase, and the lift is a property of the
        # speaker rather than of whichever sentence got picked.
        whole, sample_rate = dsp.load_excerpt(path, seconds=None)
        source_pitch = dsp.median_f0(whole, sample_rate)
        lift, warning = voices.lift_for(source_pitch)

        samples, _ = dsp.load_excerpt(path, args.seconds)
        shifted = dsp.apply_offsets(samples, sample_rate, lift, 1.0, 0.0)
        dsp.write_wav(out_dir / f"{name}.wav", shifted, sample_rate)

        print(f"\n{name}: {path.name}")
        print(
            f"  measured {source_pitch:.0f}Hz, target {voices.CHILD_TARGET_HZ:.0f}Hz, "
            f"lift {lift:+.1f} semitones, lands {source_pitch * 2 ** (lift / 12):.0f}Hz"
        )
        if warning:
            print(f"  [warn] {warning}")

    print(f"\n{len(recordings)} files in {out_dir}")
    print("These are the real voices, lifted. If they already sound like a girl")
    print("and a boy of ten here, a clone will keep that. If they do not, no")
    print("engine recovers it and the fix is a different read or speaker.")
    return 0


def _states_for(synth: Synth, args) -> dict[str, object]:
    """Clone from recordings, or fall back to catalogue stand-ins."""
    recordings = _resolve_paths(args)
    states: dict[str, object] = {}

    for base in voices.NAMES:
        if args.catalog or base not in recordings:
            name = CATALOGUE_STAND_INS[base]
            print(f"  {base}: catalogue voice '{name}' (stand-in, not your recording)")
            states[base] = synth.voice_from_catalogue(name)
        else:
            attempts = getattr(args, "attempts", 1)
            if attempts > 1:
                state, ratio, ratios, source_f0 = synth.best_clone(
                    recordings[base], lines.SLOT_PROBE, attempts
                )
                spread = " ".join(f"{r:.2f}" for r in sorted(ratios))
                print(
                    f"  {base}: cloned {recordings[base].name} {attempts}x from "
                    f"{source_f0:.0f}Hz, kept ratio {ratio:.2f} (saw {spread})"
                )
                if max(ratios) - min(ratios) > 0.2:
                    print(
                        "    [warn] wide spread across attempts. Conditioning is not "
                        "deterministic; raise --attempts or use a longer reference."
                    )
                states[base] = state
            else:
                print(f"  {base}: cloning {recordings[base].name}")
                states[base] = synth.voice_from_recording(recordings[base])

    return states


def _lifts_from_probe(synth, states: dict, probes: int = 12) -> dict[str, float]:
    """Measure each voice unshifted, then compute its lift.

    The lift belongs to the voice, so it has to come from the voice rather than
    from a constant.

    Measured over many renders of several different lines, not one render of one.
    Pocket TTS varies its pitch from line to line, and the boy varies more than
    the girl: at five renders of a single sentence the boy's lift moved 1.8
    semitones between two runs of this function, which is enough to change the
    register he ships in. Different sentences also carry different vowels, and a
    median over one sentence is a median over that sentence's vowels.

    Twelve renders costs about four seconds per voice and runs twice, offline.
    """
    pool = [lines.SLOT_PROBE] + [
        line for preset in sorted(lines.PREVIEW_LINES) for line in lines.PREVIEW_LINES[preset]
    ]

    out: dict[str, float] = {}
    print(f"Lift, computed per voice over {probes} probes:")
    for name, handle in states.items():
        measured = []
        for index in range(probes):
            audio, _elapsed = synth.say(handle, pool[index % len(pool)])
            pitch = dsp.median_f0(audio, synth.sample_rate)
            if pitch == pitch:
                measured.append(pitch)

        pitch = float(np.median(measured)) if measured else float("nan")
        spread = (max(measured) - min(measured)) if measured else float("nan")
        lift, warning = voices.lift_for(pitch)
        out[name] = lift
        print(
            f"  {name}: {pitch:.0f}Hz median, {spread:.0f}Hz spread, "
            f"target {voices.CHILD_TARGET_HZ:.0f}Hz, lift {lift:+.1f} semitones"
        )
        if warning:
            print(f"    [warn] {warning}")
        # A voice that wanders this far between lines will wander in the game
        # too, and no lift fixes that. The reference recording is the fix.
        if spread == spread and pitch == pitch and spread > pitch * 0.35:
            print(
                f"    [warn] {12 * np.log2(1 + spread / pitch):.1f} semitones of "
                f"drift between lines. A longer, steadier reference is the fix."
            )
    print()
    return out


def cmd_preview(args) -> int:
    lines.assert_no_answer_key()

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    if args.engine == "piper":
        print("Loading Piper...")
        synth = piper_engine.PiperEngine.load()
        models_dir = Path(args.voices_dir)
        states = {}
        print("Voices:")
        for name in voices.NAMES:
            explicit = getattr(args, f"{name}_model", None)
            path = piper_engine.resolve_model(name, explicit, models_dir)
            kind = "yours" if explicit else "stand-in, not your recording"
            print(f"  {name}: {path.name} ({kind})")
            states[name] = synth.open_voice(path)
        print(f"  loaded in {synth.load_seconds:.2f}s at {synth.sample_rate}Hz\n")
    else:
        print("Loading Pocket TTS...")
        synth = Synth.load()
        print(f"  loaded in {synth.load_seconds:.1f}s at {synth.sample_rate}Hz\n")
        print("Voices:")
        states = _states_for(synth, args)
        print()

    lifts = _lifts_from_probe(synth, states)

    text_lines = [lines.SLOT_PROBE] if args.preset is None else list(lines.lines_for(args.preset))

    print(
        f"{'voice':<6} {'lift':>6} {'pause':>7} {'audio':>7} {'synth':>7} {'x rt':>6}"
    )
    print("-" * 48)

    total_synth = 0.0
    for spec in voices.VOICES:
        if spec.name not in states:
            continue
        for index, text in enumerate(text_lines):
            raw, elapsed = synth.say_paced(states[spec.name], text, spec.pause_ms)
            shifted = dsp.apply_offsets(raw, synth.sample_rate, lifts[spec.name], 1.0, 0.0)
            seconds = len(shifted) / synth.sample_rate
            suffix = "" if len(text_lines) == 1 else f"_{index + 1}"
            dsp.write_wav(out_dir / f"{spec.name}{suffix}.wav", shifted, synth.sample_rate)

            total_synth += elapsed
            print(
                f"{spec.name:<6} {lifts[spec.name]:>+6.1f} {spec.pause_ms:>6d}ms "
                f"{seconds:>6.2f}s {elapsed:>6.2f}s {seconds / elapsed:>5.1f}x"
            )

    print(f"\n{len(states) * len(text_lines)} files in {out_dir}")
    print(f"Total synthesis time {total_synth:.1f}s")

    print("\nBoth have to read as ten years old, and as a girl and a boy. If one")
    print("sounds like an adult, its lift is wrong: check the measured pitch above")
    print("against the 255Hz target. If one sounds strained, it is the wrong read.")
    return 0


def cmd_bake(args) -> int:
    recordings = _resolve_paths(args)
    missing = [name for name in voices.NAMES if name not in recordings]
    if missing and not args.catalog:
        raise SystemExit(f"bake needs both recordings. Missing: {', '.join(missing)}")

    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    print("Loading Pocket TTS...")
    synth = Synth.load()
    print(f"  loaded in {synth.load_seconds:.1f}s\n")

    print("Voices:")
    states = _states_for(synth, args)
    print()

    lifts = _lifts_from_probe(synth, states)

    manifest = {
        "sampleRate": synth.sample_rate,
        "childTargetHz": voices.CHILD_TARGET_HZ,
        "voices": {},
    }

    for spec in voices.VOICES:
        if spec.name not in states:
            continue
        path = out_dir / f"{spec.name}.safetensors"
        size = synth.export(states[spec.name], path)
        source = (
            "catalogue"
            if (args.catalog or spec.name not in recordings)
            else recordings[spec.name].name
        )
        manifest["voices"][spec.name] = {
            "file": path.name,
            "bytes": size,
            "source": source,
            "liftSemitones": lifts[spec.name],
            "pauseMs": spec.pause_ms,
            "description": spec.description,
        }
        print(f"  {spec.name}: {path.name}  {size / 1e6:.1f}MB  from {source}")

    manifest_path = out_dir / "voices.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"\n  manifest: {manifest_path.name}")
    print("\nNext: uv run voicelab bake-lines")
    return 0


def cmd_relift(args) -> int:
    """Recompute the register mapping for voice states that already exist.

    The lift answers one question: how far above its own pitch does this voice
    have to sit to read as ten years old. That is arithmetic over a measurement,
    and getting it wrong does not damage the clone, it files the clone in the
    wrong register. Re-running `bake` would fix the arithmetic and re-roll the
    clones with it, and the clones are the part a casting decision went into.

    So this loads the saved states, measures them again, and rewrites nothing but
    the numbers. Run it after changing `median_f0` or `CHILD_TARGET_HZ`.

    It also migrates a manifest written before the six slots were folded onto two
    voices, so an old `out-real` does not have to be rebuilt from the recordings.
    """
    states_dir = Path(args.states)
    manifest_path = states_dir / "voices.json"
    if not manifest_path.is_file():
        raise SystemExit(f"No {manifest_path}. Run `voicelab bake` first.")

    manifest = json.loads(manifest_path.read_text())
    stored = manifest.get("voices") or manifest.get("bases") or {}
    if not stored:
        raise SystemExit(f"{manifest_path} names no voices.")

    print("Loading Pocket TTS...")
    synth = Synth.load()
    print(f"  loaded in {synth.load_seconds:.1f}s\n")

    states = {}
    for name, entry in stored.items():
        path = states_dir / entry["file"]
        if not path.is_file():
            raise SystemExit(f"Missing voice state {path}.")
        states[name] = synth.voice_from_state(path)

    was = manifest.get("liftSemitones") or {
        name: entry.get("liftSemitones", float("nan")) for name, entry in stored.items()
    }
    lifts = _lifts_from_probe(synth, states)

    print("\nLift, before and after:")
    for name in sorted(lifts):
        print(f"  {name}: {was.get(name, float('nan')):+.2f} -> {lifts[name]:+.2f} semitones")

    rebuilt = {
        "sampleRate": manifest.get("sampleRate", synth.sample_rate),
        "childTargetHz": voices.CHILD_TARGET_HZ,
        "voices": {},
    }
    for spec in voices.VOICES:
        if spec.name not in stored:
            continue
        entry = stored[spec.name]
        rebuilt["voices"][spec.name] = {
            "file": entry["file"],
            "bytes": entry.get("bytes", 0),
            "source": entry.get("source", "unknown"),
            "liftSemitones": lifts[spec.name],
            "pauseMs": spec.pause_ms,
            "description": spec.description,
        }

    manifest_path.write_text(json.dumps(rebuilt, indent=2) + "\n")
    print(f"\n  rewritten: {manifest_path}")
    print("\nAny clips baked from the old numbers are now stale:")
    print("  uv run voicelab bake-lines --force")
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="voicelab", description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    def add_common(p, needs_out: str | None):
        p.add_argument("--girl", help="Reference recording for the girl voice.")
        p.add_argument("--boy", help="Reference recording for the boy voice.")
        if needs_out:
            p.add_argument("--out", default=needs_out, help="Output directory.")
            p.add_argument(
                "--catalog",
                action="store_true",
                help="Use stock voices instead of the recordings. Works without the gated weights.",
            )
            p.add_argument(
                "--attempts",
                type=int,
                default=5,
                help="Clone this many times and keep the closest to the source pitch.",
            )

    p_check = sub.add_parser("check", help="Report on the recordings. No model load.")
    add_common(p_check, None)
    p_check.set_defaults(func=cmd_check)

    p_preview = sub.add_parser("preview", help="Generate both voices on real game lines.")
    add_common(p_preview, "preview")
    p_preview.add_argument(
        "--preset",
        help=f"Use a preset's opening lines. One of: {', '.join(sorted(lines.PREVIEW_LINES))}",
    )
    p_preview.add_argument(
        "--engine",
        choices=("pocket", "piper"),
        default="pocket",
        help="pocket clones from your recordings; piper is what the game ships with.",
    )
    p_preview.add_argument("--girl-model", help="Piper .onnx for the girl voice.")
    p_preview.add_argument("--boy-model", help="Piper .onnx for the boy voice.")
    p_preview.add_argument(
        "--voices-dir",
        default="voices",
        help="Where the Piper stand-in voices were downloaded.",
    )
    p_preview.set_defaults(func=cmd_preview)

    p_shift = sub.add_parser(
        "shift", help="Lift the recordings themselves into a child register. No model."
    )
    add_common(p_shift, "shift")
    p_shift.add_argument(
        "--seconds", type=float, default=5.0, help="Length of excerpt to shift."
    )
    p_shift.set_defaults(func=cmd_shift)

    p_bake = sub.add_parser("bake", help="Export the voice states and the manifest.")
    add_common(p_bake, "out")
    p_bake.set_defaults(func=cmd_bake)

    p_relift = sub.add_parser(
        "relift",
        help="Re-measure existing voice states and rewrite voices.json. No re-cloning.",
    )
    p_relift.add_argument(
        "--states", default="out-real", help="Directory holding voices.json and the safetensors."
    )
    p_relift.set_defaults(func=cmd_relift)


    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
