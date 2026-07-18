"""M8 reference and same-camera VFX/Bloom comparison.

The reference is intentionally not registered. Pixel differences are only
reported for captures produced by the locked M8 camera and render settings.
"""
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


def delta(a: np.ndarray, b: np.ndarray) -> dict:
    if a.shape != b.shape:
        raise RuntimeError(f"same-camera inputs differ in shape: {a.shape} / {b.shape}")
    diff = np.abs(a.astype(np.int16) - b.astype(np.int16))
    changed = np.any(diff > 2, axis=2)
    return {
        "rgb_mae_8bit": float(diff.mean()),
        "rgb_max_8bit": int(diff.max()),
        "changed_pixel_count": int(changed.sum()),
        "changed_pixel_coverage": float(changed.mean()),
        "changed_target_mae_8bit": float(diff[changed].mean()) if changed.any() else 0.0,
    }


def main() -> None:
    if len(sys.argv) != 7:
        raise SystemExit("usage: compare_m8.py REF_FRONT ALL_OFF BLOOM_OFF FINAL REPORT_JSON SHEET_PNG")
    ref_path, all_off_path, bloom_off_path, final_path, report_path, sheet_path = map(Path, sys.argv[1:])
    paths = {
        "reference_front": ref_path,
        "m8_all_off_same_camera": all_off_path,
        "m8_emission_bloom_off_same_camera": bloom_off_path,
        "m8_final_same_camera": final_path,
    }
    images, arrays = {}, {}
    report = {"method": {
        "registration": "none",
        "reference_comparison": "foreground colour/luminance distributions only",
        "same_camera_comparison": "absolute RGB pixel difference",
    }}
    for name, path in paths.items():
        images[name], arrays[name] = load(path)
        report[name] = {"path": str(path), "resolution": [images[name].width, images[name].height], **distribution(arrays[name])}
    report["m8_final_from_all_off_same_camera"] = delta(arrays["m8_all_off_same_camera"], arrays["m8_final_same_camera"])
    report["m8_bloom_from_emission_same_camera"] = delta(arrays["m8_emission_bloom_off_same_camera"], arrays["m8_final_same_camera"])
    final = arrays["m8_final_same_camera"]
    report["m8_final_red_pixel_count"] = int(((final[..., 0] > 48) & (final[..., 0] > final[..., 1] * 1.35) & (final[..., 0] > final[..., 2] * 1.2)).sum())
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    width = 320
    panels = []
    for name in paths:
        image = images[name]
        scale = width / image.width
        panels.append((name.replace("_", " ").title(), image.resize((width, round(image.height * scale)), Image.Resampling.LANCZOS)))
    height = max(panel.height for _, panel in panels)
    sheet = Image.new("RGB", (width * 4 + 60, height + 56), (28, 28, 28))
    draw = ImageDraw.Draw(sheet)
    for index, (label, panel) in enumerate(panels):
        x = 12 + index * (width + 12)
        draw.text((x, 8), label, fill=(235, 235, 235))
        sheet.paste(panel, (x, 28))
    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(sheet_path)


if __name__ == "__main__":
    main()
