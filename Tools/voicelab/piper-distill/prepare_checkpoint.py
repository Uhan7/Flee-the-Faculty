"""Make the downloaded Piper checkpoint loadable under torch's safe default.

The rhasspy checkpoints were saved before torch 2.6 and pickle a
pathlib.PosixPath inside their hyperparameters, which `weights_only=True`
rejects. This loads the file permissively once, turns every Path into a
string, and re-saves it. Run it on a checkpoint that came from
huggingface.co/datasets/rhasspy/piper-checkpoints and nowhere else: the
permissive load executes pickled code, which is the thing being sanitised away.

    uv run python prepare_checkpoint.py checkpoints/amy-medium.ckpt
"""

from __future__ import annotations

import sys
from pathlib import Path, PosixPath

import torch


def destringed(value):
    if isinstance(value, (Path, PosixPath)):
        return str(value)
    if isinstance(value, dict):
        return {key: destringed(item) for key, item in value.items()}
    if isinstance(value, (list, tuple)):
        return type(value)(destringed(item) for item in value)
    return value


def main() -> int:
    source = Path(sys.argv[1])
    checkpoint = torch.load(source, map_location="cpu", weights_only=False)
    # The 2023 trainer's hyperparameters name options the rewritten trainer no
    # longer has (`sample_bytes`), and Lightning refuses a fit whose checkpoint
    # hparams it cannot parse. Every hyperparameter is given on the command
    # line anyway, so the stored copy carries no information worth keeping.
    checkpoint.pop("hyper_parameters", None)
    torch.save(destringed(checkpoint), source)
    print(f"re-saved {source} with paths as strings and no stored hparams")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
