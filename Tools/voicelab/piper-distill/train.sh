#!/usr/bin/env bash
# Fine-tune the girl student on the distillation corpus. Run after
# `chatterbox/corpus.py` has finished rendering.
#
# The amy checkpoint resumes at epoch 6679, so max_epochs counts from there:
# 7679 is 1,000 epochs of fine-tuning. Checkpoints land in
# lightning_logs/version_*/checkpoints/, the best five by validation mel loss
# plus last.ckpt, so stopping early with Ctrl-C loses nothing: export whichever
# checkpoint sounds best. To resume an interrupted run instead of starting
# over, pass its last.ckpt as the first argument.
#
#     ./train.sh
#     ./train.sh lightning_logs/version_3/checkpoints/last.ckpt
set -euo pipefail
cd "$(dirname "$0")"

CKPT="${1:-checkpoints/amy-medium.ckpt}"

PYTORCH_ENABLE_MPS_FALLBACK=1 uv run python -m piper.train fit \
    --data.voice_name girl \
    --data.csv_path ../chatterbox/distill-corpus/metadata.csv \
    --data.audio_dir ../chatterbox/distill-corpus/wav \
    --data.cache_dir cache \
    --data.config_path girl.config.json \
    --data.espeak_voice en-us \
    --data.batch_size 16 \
    --data.num_workers 4 \
    --model.sample_rate 22050 \
    --ckpt_path "$CKPT" \
    --weights_only true \
    --trainer.accelerator mps \
    --trainer.devices 1 \
    --trainer.max_epochs 7679 \
    --trainer.default_root_dir .
