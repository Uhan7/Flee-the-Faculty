"""Rewrite a voice state into the layout the browser runtime reads.

Both files hold the same thing: a six-layer transformer KV cache, `cache`
shaped (2, 1, frames, 16, 64). They disagree on the bookkeeping beside it.

The Python export writes `offset` and `pad` as scalars. The Rust port writes
`current_end`, one float per frame. Only its *length* carries meaning:
`_import_model_state` in pocket_tts reads it back as
`offset = tensor.shape[0]`, and the values are ignored, which is why every
voice Kyutai ships has that array entirely zero.

Keep the head of the cache, never the tail. Measured in a browser on one
sentence, against Kyutai's own `alba` at 5.60s:

    girl, all 414 frames, tail kept    1.76s   truncated
    girl, last 125 frames              0.16s   truncated
    girl, first 250 frames             5.04s   correct
    girl, first 125 frames             7.92s   correct
    boy,  first 250 frames             5.28s   correct

A reference that ends on a trailing pause leaves the state saying the speaker
has just stopped, and the model obliges. Trimming from the front removes that
and leaves the opening of the read, which is where the voice is clearest.
"""

import json
import struct
from pathlib import Path

import numpy as np

# 20 seconds at the model's 12.5Hz frame rate, which is what Kyutai asks for in
# a reference recording. Both voices generate full-length lines at this.
DEFAULT_FRAMES = 250
FRAME_RATE_HZ = 12.5


def read_state(path: Path) -> dict[str, np.ndarray]:
    raw = Path(path).read_bytes()
    size = struct.unpack("<Q", raw[:8])[0]
    header, body = json.loads(raw[8 : 8 + size]), raw[8 + size :]
    out: dict[str, np.ndarray] = {}
    for key, spec in header.items():
        if key == "__metadata__":
            continue
        start, end = spec["data_offsets"]
        dtype = {"F32": np.float32, "I64": np.int64}[spec["dtype"]]
        out[key] = np.frombuffer(body[start:end], dtype=dtype).reshape(spec["shape"])
    return out


def write_state(path: Path, tensors: dict[str, np.ndarray]) -> int:
    header: dict[str, dict] = {}
    body: list[bytes] = []
    offset = 0
    for key in sorted(tensors):
        array = np.ascontiguousarray(tensors[key], dtype=np.float32)
        raw = array.tobytes()
        header[key] = {
            "dtype": "F32",
            "shape": list(array.shape),
            "data_offsets": [offset, offset + len(raw)],
        }
        body.append(raw)
        offset += len(raw)

    blob = json.dumps(header, separators=(",", ":")).encode()
    blob += b" " * ((-len(blob)) % 8)  # safetensors wants an 8-byte aligned header
    Path(path).write_bytes(struct.pack("<Q", len(blob)) + blob + b"".join(body))
    return Path(path).stat().st_size


def to_browser(source: Path, destination: Path, frames: int = DEFAULT_FRAMES) -> dict:
    """Convert one state. Returns what it did, for the caller to report."""
    tensors = read_state(source)
    layers = sorted({key.split(".")[2] for key in tensors if key.startswith("transformer.layers.")})
    if not layers:
        raise SystemExit(f"{source} holds no transformer layers. Is it a voice state?")

    out: dict[str, np.ndarray] = {}
    original = 0
    for layer in layers:
        cache = tensors[f"transformer.layers.{layer}.self_attn/cache"]
        original = cache.shape[2]
        if frames and frames < original:
            cache = cache[:, :, :frames]
        out[f"transformer.layers.{layer}.self_attn/cache"] = cache.astype(np.float32)
        out[f"transformer.layers.{layer}.self_attn/current_end"] = np.zeros(
            cache.shape[2], dtype=np.float32
        )

    kept = out[f"transformer.layers.{layers[0]}.self_attn/cache"].shape[2]
    return {
        "layers": len(layers),
        "framesIn": original,
        "framesOut": kept,
        "secondsOut": kept / FRAME_RATE_HZ,
        "bytes": write_state(destination, out),
    }
