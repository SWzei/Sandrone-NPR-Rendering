"""Quantitative M1 reference comparison (front + side, no registration warping)."""

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
    distance = np.linalg.norm(rgb - corner, axis=2)
    mask = distance > 0.08
    foreground = rgb[mask]
    if not foreground.size:
        raise RuntimeError(f"No foreground detected in {path}")
    sample = foreground[:: max(1, len(foreground) // 100_000)]
    hsv = np.array([colorsys.rgb_to_hsv(*pixel) for pixel in sample])
    luminance = foreground @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    ys, xs = np.where(mask)
    result = {
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
    }
    return result, image


def main() -> None:
    if len(sys.argv) != 7:
        raise SystemExit("usage: compare_m1.py REF_FRONT RENDER_FRONT REF_SIDE RENDER_SIDE REPORT_JSON SHEET_PNG")
    paths = [Path(value) for value in sys.argv[1:5]]
    report_path, sheet_path = Path(sys.argv[5]), Path(sys.argv[6])
    names = ["reference_front", "render_front", "reference_side", "render_side"]
    measured: dict[str, dict] = {}
    images: dict[str, Image.Image] = {}
    for name, path in zip(names, paths):
        measured[name], images[name] = metrics(path)

    for view in ("front", "side"):
        reference = measured[f"reference_{view}"]
        render = measured[f"render_{view}"]
        measured[f"delta_{view}"] = {
            "foreground_coverage": render["foreground_coverage"] - reference["foreground_coverage"],
            "foreground_mean_hsv": [
                render["foreground_mean_hsv"][i] - reference["foreground_mean_hsv"][i] for i in range(3)
            ],
            "foreground_luminance_mean": render["foreground_luminance_mean"] - reference["foreground_luminance_mean"],
            "foreground_luminance_std": render["foreground_luminance_std"] - reference["foreground_luminance_std"],
        }

    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(measured, ensure_ascii=False, indent=2), encoding="utf-8")

    thumb_width = 480
    panels = []
    for name in names:
        image = images[name]
        scale = thumb_width / image.width
        thumb = image.resize((thumb_width, round(image.height * scale)), Image.Resampling.LANCZOS)
        panels.append((name.replace("_", " ").title(), thumb))
    row_heights = [max(panels[0][1].height, panels[1][1].height), max(panels[2][1].height, panels[3][1].height)]
    sheet = Image.new("RGB", (thumb_width * 2 + 36, sum(row_heights) + 96), (28, 28, 28))
    draw = ImageDraw.Draw(sheet)
    y = 28
    for row, offset in enumerate((0, 2)):
        for column in range(2):
            label, panel = panels[offset + column]
            x = 12 + column * (thumb_width + 12)
            draw.text((x, y - 18), label, fill=(235, 235, 235))
            sheet.paste(panel, (x, y))
        y += row_heights[row] + 34
    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(sheet_path)


if __name__ == "__main__":
    main()
