"""M9 final-composition comparison and Player-capture conversion.

The external reference is intentionally not registered. Its values are only
foreground colour/luminance distributions. Pixel deltas are restricted to
captures made by the locked Unity calibration camera.
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
    if not len(foreground):
        raise RuntimeError("foreground mask is empty")
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


def red_and_magenta(rgb: np.ndarray) -> dict:
    r, g, b = (rgb[..., index].astype(np.float32) for index in range(3))
    red = (r > 48) & (r > g * 1.35) & (r > b * 1.2)
    magenta = (r > 220) & (b > 220) & (g < 64)
    return {"red_pixel_count": int(red.sum()), "magenta_pixel_count": int(magenta.sum())}


def main() -> None:
    if len(sys.argv) != 11:
        raise SystemExit(
            "usage: compare_m9.py REF M8 M9 ACES PC MOBILE PLAYER_PPM REPORT_JSON SHEET_PNG PLAYER_PNG"
        )
    ref, m8, m9, aces, pc, mobile, player, report_path, sheet_path, player_png = map(Path, sys.argv[1:])
    paths = {
        "reference_front_unregistered": ref,
        "m8_locked_camera": m8,
        "m9_neutral_smaa_locked_camera": m9,
        "m9_aces_aa_off_locked_camera": aces,
        "m9_pc_smaa_pipeline": pc,
        "m9_mobile_fxaa_locked_camera": mobile,
    }
    images: dict[str, Image.Image] = {}
    arrays: dict[str, np.ndarray] = {}
    report: dict = {"method": {
        "registration": "none",
        "reference_comparison": "foreground colour/luminance distributions only",
        "same_camera_comparison": "absolute RGB pixel difference",
        "player_png": "lossless conversion of current P6 PPM Player capture",
    }}
    for name, path in paths.items():
        images[name], arrays[name] = load(path)
        report[name] = {
            "path": str(path),
            "resolution": [images[name].width, images[name].height],
            **distribution(arrays[name]),
            **red_and_magenta(arrays[name]),
        }
    report["m8_to_m9_same_camera"] = delta(arrays["m8_locked_camera"], arrays["m9_neutral_smaa_locked_camera"])
    report["m9_neutral_to_aces_same_camera"] = delta(arrays["m9_neutral_smaa_locked_camera"], arrays["m9_aces_aa_off_locked_camera"])
    report["m9_pc_to_mobile_same_camera"] = delta(arrays["m9_pc_smaa_pipeline"], arrays["m9_mobile_fxaa_locked_camera"])

    player_image, player_array = load(player)
    player_png.parent.mkdir(parents=True, exist_ok=True)
    player_image.save(player_png)
    report["windows_player"] = {
        "path": str(player),
        "png_derivative": str(player_png),
        "resolution": [player_image.width, player_image.height],
        **distribution(player_array),
        **red_and_magenta(player_array),
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    panel_width = 280
    panels = []
    for name in paths:
        image = images[name]
        scale = panel_width / image.width
        panels.append((name.replace("_", " ").title(), image.resize((panel_width, round(image.height * scale)), Image.Resampling.LANCZOS)))
    height = max(panel.height for _, panel in panels)
    sheet = Image.new("RGB", (panel_width * len(panels) + 12 * (len(panels) + 1), height + 56), (28, 28, 28))
    draw = ImageDraw.Draw(sheet)
    for index, (label, panel) in enumerate(panels):
        x = 12 + index * (panel_width + 12)
        draw.text((x, 8), label, fill=(235, 235, 235))
        sheet.paste(panel, (x, 28))
    sheet_path.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(sheet_path)


if __name__ == "__main__":
    main()
