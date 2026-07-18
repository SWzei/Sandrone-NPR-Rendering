"""M6 reference and accepted-M5 comparison without geometric registration.

Reference-vs-render statistics describe distributions only. Pixel MAE is used
only for the same-camera M5/M6 pair.
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
    corner = rgb[: min(32, rgb.shape[0]), : min(32, rgb.shape[1])].reshape(-1, 3).mean(axis=0)
    foreground_mask = np.linalg.norm(rgb - corner, axis=2) > 0.08
    foreground = rgb[foreground_mask]
    if not foreground.size:
        raise RuntimeError("No foreground detected")
    sample = foreground[:: max(1, len(foreground) // 100_000)]
    hsv = np.asarray([colorsys.rgb_to_hsv(*pixel) for pixel in sample], dtype=np.float32)
    lum = foreground @ np.asarray([0.2126, 0.7152, 0.0722], dtype=np.float32)
    return {
        "foreground_coverage": float(foreground_mask.mean()),
        "foreground_mean_rgb_8bit": np.rint(foreground.mean(axis=0) * 255).astype(int).tolist(),
        "foreground_mean_hsv": [float(hsv[:, i].mean()) for i in range(3)],
        "foreground_luminance_mean": float(lum.mean()),
        "foreground_luminance_std": float(lum.std()),
    }


def blue_eye_stats(rgb8: np.ndarray) -> dict:
    rgb = rgb8.astype(np.float32) / 255.0
    flat = rgb.reshape(-1, 3)
    hsv = np.asarray([colorsys.rgb_to_hsv(*pixel) for pixel in flat], dtype=np.float32).reshape(rgb.shape)
    # Saturated blue/cyan pixels are a conservative iris/eye-highlight proxy.
    mask = (hsv[:, :, 0] >= 0.50) & (hsv[:, :, 0] <= 0.72) & (hsv[:, :, 1] >= 0.35) & (hsv[:, :, 2] >= 0.20)
    selected = hsv[mask]
    return {
        "pixel_count": int(mask.sum()),
        "coverage": float(mask.mean()),
        "mean_saturation": float(selected[:, 1].mean()) if len(selected) else 0.0,
        "mean_value": float(selected[:, 2].mean()) if len(selected) else 0.0,
    }


def main() -> None:
    if len(sys.argv) != 6:
        raise SystemExit("usage: compare_m6.py REF_FRONT M5_FRONT M6_FRONT REPORT_JSON SHEET_PNG")
    ref_path, m5_path, m6_path, report_path, sheet_path = map(Path, sys.argv[1:])
    paths = {"reference_front": ref_path, "m5_front": m5_path, "m6_front": m6_path}
    images, arrays, report = {}, {}, {"method": {
        "registration": "none",
        "reference_comparison": "foreground distribution and conservative blue-pixel proxy only",
        "same_camera_comparison": "absolute 8-bit RGB pixel difference",
    }}
    for name, path in paths.items():
        images[name], arrays[name] = load(path)
        report[name] = {
            "path": str(path),
            "resolution": [images[name].width, images[name].height],
            **distribution(arrays[name]),
            "blue_eye_proxy": blue_eye_stats(arrays[name]),
        }
    if arrays["m5_front"].shape != arrays["m6_front"].shape:
        raise RuntimeError("M5/M6 same-camera inputs have different resolutions")
    diff = np.abs(arrays["m6_front"].astype(np.int16) - arrays["m5_front"].astype(np.int16))
    report["m6_change_from_m5_same_camera"] = {
        "rgb_mae_8bit": float(diff.mean()),
        "rgb_max_8bit": int(diff.max()),
        "changed_pixel_count": int(np.any(diff != 0, axis=2).sum()),
        "changed_pixel_coverage": float(np.any(diff != 0, axis=2).mean()),
        "blue_pixel_count_delta": report["m6_front"]["blue_eye_proxy"]["pixel_count"] - report["m5_front"]["blue_eye_proxy"]["pixel_count"],
        "blue_saturation_delta": report["m6_front"]["blue_eye_proxy"]["mean_saturation"] - report["m5_front"]["blue_eye_proxy"]["mean_saturation"],
        "blue_value_delta": report["m6_front"]["blue_eye_proxy"]["mean_value"] - report["m5_front"]["blue_eye_proxy"]["mean_value"],
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
