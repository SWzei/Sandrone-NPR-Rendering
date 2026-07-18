"""Quantitative M3 comparison against references and the accepted M2 baseline.

No registration or image warping is applied. Colour/luminance distribution is
reported separately from pose, crop and framing so it is not misrepresented as
a pixel-wise likeness score.
"""

from __future__ import annotations

import colorsys
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


def metrics(path: Path) -> tuple[dict, Image.Image]:
    image = Image.open(path).convert("RGB")
    rgb = np.asarray(image, dtype=np.float32) / 255.0
    corner = rgb[: min(32, rgb.shape[0]), : min(32, rgb.shape[1])].reshape(-1, 3).mean(axis=0)
    mask = np.linalg.norm(rgb - corner, axis=2) > 0.08
    foreground = rgb[mask]
    if not foreground.size:
        raise RuntimeError(f"No foreground detected in {path}")
    sample = foreground[:: max(1, len(foreground) // 100_000)]
    hsv = np.array([colorsys.rgb_to_hsv(*pixel) for pixel in sample])
    luminance = foreground @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    ys, xs = np.where(mask)
    return {
        "path": str(path),
        "resolution": [image.width, image.height],
        "background_rgb_8bit": np.rint(corner * 255).astype(int).tolist(),
        "foreground_coverage": float(mask.mean()),
        "foreground_bbox_normalized": [
            float(xs.min() / image.width),
            float(ys.min() / image.height),
            float((xs.max() + 1) / image.width),
            float((ys.max() + 1) / image.height),
        ],
        "foreground_mean_rgb_8bit": np.rint(foreground.mean(axis=0) * 255).astype(int).tolist(),
        "foreground_mean_hsv": [float(hsv[:, i].mean()) for i in range(3)],
        "foreground_luminance_mean": float(luminance.mean()),
        "foreground_luminance_std": float(luminance.std()),
    }, image


def delta(candidate: dict, reference: dict) -> dict:
    return {
        "foreground_coverage": candidate["foreground_coverage"] - reference["foreground_coverage"],
        "foreground_mean_hsv": [
            candidate["foreground_mean_hsv"][i] - reference["foreground_mean_hsv"][i] for i in range(3)
        ],
        "foreground_luminance_mean": candidate["foreground_luminance_mean"] - reference["foreground_luminance_mean"],
        "foreground_luminance_std": candidate["foreground_luminance_std"] - reference["foreground_luminance_std"],
    }


def main() -> None:
    if len(sys.argv) != 9:
        raise SystemExit(
            "usage: compare_m3.py REF_FRONT M2_FRONT M3_FRONT REF_SIDE M2_SIDE M3_SIDE REPORT_JSON SHEET_PNG"
        )
    paths = [Path(value) for value in sys.argv[1:7]]
    report_path, sheet_path = Path(sys.argv[7]), Path(sys.argv[8])
    names = [
        "reference_front", "m2_front", "m3_front",
        "reference_side", "m2_side", "m3_side",
    ]
    measured: dict[str, dict] = {}
    images: dict[str, Image.Image] = {}
    for name, path in zip(names, paths):
        measured[name], images[name] = metrics(path)

    for view in ("front", "side"):
        reference = measured[f"reference_{view}"]
        m2_delta = delta(measured[f"m2_{view}"], reference)
        m3_delta = delta(measured[f"m3_{view}"], reference)
        measured[f"delta_m2_to_reference_{view}"] = m2_delta
        measured[f"delta_m3_to_reference_{view}"] = m3_delta
        measured[f"m3_change_from_m2_{view}"] = delta(measured[f"m3_{view}"], measured[f"m2_{view}"])
        measured[f"absolute_luminance_error_change_{view}"] = (
            abs(m3_delta["foreground_luminance_mean"]) - abs(m2_delta["foreground_luminance_mean"])
        )

    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(measured, ensure_ascii=False, indent=2), encoding="utf-8")

    thumb_width = 420
    panels = []
    for name in names:
        image = images[name]
        scale = thumb_width / image.width
        thumb = image.resize((thumb_width, round(image.height * scale)), Image.Resampling.LANCZOS)
        panels.append((name.replace("_", " ").title(), thumb))
    row_heights = [max(panel[1].height for panel in panels[:3]), max(panel[1].height for panel in panels[3:])]
    sheet = Image.new("RGB", (thumb_width * 3 + 48, sum(row_heights) + 96), (28, 28, 28))
    draw = ImageDraw.Draw(sheet)
    y = 28
    for row, offset in enumerate((0, 3)):
        for column in range(3):
            label, panel = panels[offset + column]
            x = 12 + column * (thumb_width + 12)
            draw.text((x, y - 18), label, fill=(235, 235, 235))
            sheet.paste(panel, (x, y))
        y += row_heights[row] + 34
    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(sheet_path)


if __name__ == "__main__":
    main()
