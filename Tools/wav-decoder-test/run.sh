#!/usr/bin/env bash
# Compile the shipped WAV decoder with its tests and run them. No Unity.
#
#     ./Tools/wav-decoder-test/run.sh
#     ./Tools/wav-decoder-test/run.sh /path/to/some/wavs
#
# The optional argument is a directory of real service output. Generate one with
# `uv run python scripts/audition_voices.py --out /tmp/slots` in the service
# repository, and this checks every slot decodes to something audible.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
CLIENT="$(cd "$HERE/../.." && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

csc -nologo -warnaserror -out:"$OUT/tests.exe" \
    "$CLIENT/Assets/Scripts/Voice/WavDecoder.cs" \
    "$HERE/DecoderTests.cs"

mono "$OUT/tests.exe" "${1:-}"
