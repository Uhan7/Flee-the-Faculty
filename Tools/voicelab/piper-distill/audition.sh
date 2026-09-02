#!/usr/bin/env bash
# Export a checkpoint and speak two probe lines with it, so training can be
# judged by ear while it runs. With no argument, uses the newest last.ckpt.
#
#     ./audition.sh
#     ./audition.sh lightning_logs/version_0/checkpoints/epoch=7100-val_mel=....ckpt
set -euo pipefail
cd "$(dirname "$0")"

CKPT="${1:-$(ls -t lightning_logs/version_*/checkpoints/last.ckpt 2>/dev/null | head -1)}"
if [ -z "$CKPT" ]; then
    echo "No checkpoint yet. Training writes the first one at the end of its first epoch."
    exit 1
fi

mkdir -p audition
uv run python -m piper.train.export_onnx --checkpoint "$CKPT" --output-file audition/candidate.onnx
cp girl.config.json audition/candidate.onnx.json
echo "Plants eat the dirt, right? That's what the soil is for. Oh! Tell me the part about the sun again, I want to draw it." \
    | uv run python -m piper -m audition/candidate.onnx -c audition/candidate.onnx.json -f audition/candidate.wav
echo "-> audition/candidate.wav (from $CKPT)"
open audition/candidate.wav
