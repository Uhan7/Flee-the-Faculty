"""Remove the trainer's optional val_mos checkpoint callback.

piper.train's defaults include a second ModelCheckpoint that monitors
"val_mos", a perceptual score logged only when its MOS predictor loads. The
source comments say Lightning warns and skips when the key is absent; the
Lightning actually installed raises MisconfigurationException at the end of
the first validation epoch instead, which kills the run. The val_mel
checkpoints and last.ckpt are the ones that matter, so the optional callback
is deleted from the installed file. `setup_env.sh` runs this, and it is safe
to run twice.
"""

from __future__ import annotations

import re
from pathlib import Path

target = Path(".venv/lib/python3.11/site-packages/piper/train/__main__.py")
source = target.read_text(encoding="utf-8")
patched = re.sub(
    r"\n(?:    #[^\n]*\n)*    ModelCheckpoint\(\s*monitor=\"val_mos\"[^)]*\),",
    "",
    source,
)
if patched != source:
    target.write_text(patched, encoding="utf-8")
    print("removed the val_mos ModelCheckpoint callback")
elif 'monitor="val_mos"' in patched:
    raise SystemExit("val_mos callback present but the pattern no longer matches")
else:
    print("already patched: val_mos callback")

# torch 2.9 made the dynamo exporter the default, and it refuses the
# data-dependent assert inside the VITS spline flow. The legacy TorchScript
# exporter traces straight through it, which is what this export always was.
target = Path(".venv/lib/python3.11/site-packages/piper/train/export_onnx.py")
source = target.read_text(encoding="utf-8")
patched = source.replace(
    "torch.onnx.export(\n", "torch.onnx.export(\n        dynamo=False,\n", 1
)
if "dynamo=False" not in source and patched != source:
    target.write_text(patched, encoding="utf-8")
    print("pinned export_onnx to the legacy exporter")
else:
    print("already patched: legacy exporter")
