"""M7 reference comparison. Reference distributions are unregistered; pixel
differences are used only for the same-camera M6/M7 Game View pair."""
from __future__ import annotations

import colorsys
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


def load(path: Path) -> tuple[Image.Image, np.ndarray]:
    image = Image.open(path).convert("RGB")
    return image, np.asarray(image, dtype=np.uint8)


def distribution(rgb8: np.ndarray) -> dict:
    rgb = rgb8.astype(np.float32) / 255.0
    corner = rgb[:32, :32].reshape(-1, 3).mean(axis=0)
    mask = np.linalg.norm(rgb - corner, axis=2) > 0.08
    foreground = rgb[mask]
    sample = foreground[::max(1, len(foreground) // 100_000)]
    hsv = np.asarray([colorsys.rgb_to_hsv(*pixel) for pixel in sample], dtype=np.float32)
    luminance = foreground @ np.asarray([.2126, .7152, .0722], dtype=np.float32)
    return {
        "foreground_coverage": float(mask.mean()),
        "foreground_mean_hsv": [float(hsv[:, i].mean()) for i in range(3)],
        "foreground_luminance_mean": float(luminance.mean()),
        "foreground_luminance_std": float(luminance.std()),
    }


def main() -> None:
    if len(sys.argv) != 6:
        raise SystemExit("usage: compare_m7.py REF_FRONT M6_GAMEVIEW M7_GAMEVIEW REPORT_JSON SHEET_PNG")
    ref_path, m6_path, m7_path, report_path, sheet_path = map(Path, sys.argv[1:])
    paths = {"reference_front": ref_path, "m6_same_camera": m6_path, "m7_same_camera": m7_path}
    images, arrays, report = {}, {}, {"method": {
        "registration": "none",
        "reference_comparison": "foreground colour/luminance distributions only",
        "same_camera_comparison": "absolute RGB pixel difference and background-side outline mask",
    }}
    for name, path in paths.items():
        images[name], arrays[name] = load(path)
        report[name] = {"path": str(path), "resolution": [images[name].width, images[name].height], **distribution(arrays[name])}
    if arrays["m6_same_camera"].shape != arrays["m7_same_camera"].shape:
        raise RuntimeError("M6/M7 same-camera inputs differ in resolution")
    m6 = arrays["m6_same_camera"].astype(np.int16)
    m7 = arrays["m7_same_camera"].astype(np.int16)
    diff = np.abs(m7 - m6)
    changed = np.any(diff > 3, axis=2)
    corner = m6[:32, :32].reshape(-1, 3).mean(axis=0)
    baseline_foreground = np.linalg.norm(m6 - corner, axis=2) > 12
    outside = changed & ~baseline_foreground
    dark_added = changed & (m7.mean(axis=2) + 5 < m6.mean(axis=2))
    report["m7_change_from_m6_same_camera"] = {
        "rgb_mae_8bit": float(diff.mean()),
        "rgb_max_8bit": int(diff.max()),
        "changed_pixel_count": int(changed.sum()),
        "changed_pixel_coverage": float(changed.mean()),
        "outside_baseline_ratio": float(outside.sum() / max(1, changed.sum())),
        "darkened_changed_ratio": float(dark_added.sum() / max(1, changed.sum())),
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    width = 420
    panels = []
    for name in paths:
        image = images[name]
        scale = width / image.width
        panels.append((name.replace("_", " ").title(), image.resize((width, round(image.height * scale)), Image.Resampling.LANCZOS)))
    height = max(panel.height for _, panel in panels)
    sheet = Image.new("RGB", (width * 3 + 48, height + 56), (28, 28, 28))
    draw = ImageDraw.Draw(sheet)
    for index, (label, panel) in enumerate(panels):
        x = 12 + index * (width + 12)
        draw.text((x, 8), label, fill=(235, 235, 235))
        sheet.paste(panel, (x, 28))
    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(sheet_path)


if __name__ == "__main__":
    main()
