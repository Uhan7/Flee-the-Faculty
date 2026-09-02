#!/usr/bin/env bash
# Build this project's environment, including the two things `uv sync` cannot
# do on its own: the piper-tts wheel ships the monotonic_align Cython extension
# as an unbuilt setup.py with no source, so the .pyx is fetched from the
# piper1-gpl repository and compiled into the venv; and the rhasspy checkpoint
# needs its 2023 pickle sanitised before torch 2.6 will load it (see
# prepare_checkpoint.py). Re-run this after deleting .venv.
set -euo pipefail
cd "$(dirname "$0")"

uv sync
uv pip install cython onnx onnxscript

MA=.venv/lib/python3.11/site-packages/piper/train/vits/monotonic_align
if [ ! -f "$MA"/monotonic_align/core.*.so ]; then
    curl -sL "https://raw.githubusercontent.com/OHF-Voice/piper1-gpl/main/src/piper/train/vits/monotonic_align/core.pyx" \
        -o "$MA/core.pyx"
    mkdir -p "$MA/monotonic_align"
    touch "$MA/monotonic_align/__init__.py"
    (cd "$MA" && ../../../../../../../bin/python setup.py build_ext --inplace >/dev/null 2>&1 || true)
    cp "$MA"/build/lib.*/piper/train/vits/monotonic_align/core.*.so "$MA/monotonic_align/"
fi
uv run python -c "from piper.train.vits.monotonic_align import maximum_path; print('monotonic_align ok')"

uv run python patch_trainer.py

CKPT=checkpoints/amy-medium.ckpt
if [ ! -f "$CKPT" ]; then
    mkdir -p checkpoints
    curl -L "https://huggingface.co/datasets/rhasspy/piper-checkpoints/resolve/main/en/en_US/amy/medium/epoch%3D6679-step%3D1554200.ckpt" \
        -o "$CKPT"
    uv run python prepare_checkpoint.py "$CKPT"
fi
echo "ready"
