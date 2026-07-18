"""Compare M4 against references and accepted M3 without geometric registration."""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw
from compare_m3 import delta, metrics


def main() -> None:
    if len(sys.argv) != 9:
        raise SystemExit("usage: compare_m4.py REF_FRONT M3_FRONT M4_FRONT REF_SIDE M3_SIDE M4_SIDE REPORT_JSON SHEET_PNG")
    paths = [Path(value) for value in sys.argv[1:7]]
    report_path, sheet_path = Path(sys.argv[7]), Path(sys.argv[8])
    names = ["reference_front", "m3_front", "m4_front", "reference_side", "m3_side", "m4_side"]
    measured, images = {}, {}
    for name, path in zip(names, paths):
        measured[name], images[name] = metrics(path)
    for view in ("front", "side"):
        reference = measured[f"reference_{view}"]
        m3_delta = delta(measured[f"m3_{view}"], reference)
        m4_delta = delta(measured[f"m4_{view}"], reference)
        measured[f"delta_m3_to_reference_{view}"] = m3_delta
        measured[f"delta_m4_to_reference_{view}"] = m4_delta
        measured[f"m4_change_from_m3_{view}"] = delta(measured[f"m4_{view}"], measured[f"m3_{view}"])
        measured[f"absolute_luminance_error_change_{view}"] = abs(m4_delta["foreground_luminance_mean"]) - abs(m3_delta["foreground_luminance_mean"])
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(measured, ensure_ascii=False, indent=2), encoding="utf-8")

    width = 420
    panels = []
    for name in names:
        image = images[name]
        scale = width / image.width
        panels.append((name.replace("_", " ").title(), image.resize((width, round(image.height * scale)), Image.Resampling.LANCZOS)))
    heights = [max(p[1].height for p in panels[:3]), max(p[1].height for p in panels[3:])]
    sheet = Image.new("RGB", (width * 3 + 48, sum(heights) + 96), (28, 28, 28))
    draw = ImageDraw.Draw(sheet); y = 28
    for row, offset in enumerate((0, 3)):
        for column in range(3):
            label, panel = panels[offset + column]; x = 12 + column * (width + 12)
            draw.text((x, y - 18), label, fill=(235, 235, 235)); sheet.paste(panel, (x, y))
        y += heights[row] + 34
    sheet_path.parent.mkdir(parents=True, exist_ok=True); sheet.save(sheet_path)


if __name__ == "__main__":
    main()
