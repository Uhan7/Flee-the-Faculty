#!/usr/bin/env bash
# Export a trained checkpoint to the .onnx plus .onnx.json pair the service's
# SpeechEngine loads from its voices directory.
#
#     ./export.sh lightning_logs/version_3/checkpoints/last.ckpt
set -euo pipefail
cd "$(dirname "$0")"

CKPT="${1:?usage: export.sh <checkpoint.ckpt> [out-dir]}"
OUT="${2:-export}"
mkdir -p "$OUT"

uv run python -m piper.train.export_onnx --checkpoint "$CKPT" --output-file "$OUT/girl-clone.onnx"
cp girl.config.json "$OUT/girl-clone.onnx.json"
echo "-> $OUT/girl-clone.onnx (+ .json). Copy both into the service's voices/ and point slots.py at it."
