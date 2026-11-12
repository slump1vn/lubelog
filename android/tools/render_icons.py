"""Tạo biểu tượng app Android từ tools/branding/*.svg.

Chạy: pip install cairosvg pillow && python android/tools/render_icons.py
"""
import io
import re
from pathlib import Path

import cairosvg
from PIL import Image

root = Path(__file__).resolve().parents[2]
branding = root / "tools" / "branding"
res = root / "android" / "app" / "src" / "main" / "res"

densities = {"mdpi": 1, "hdpi": 1.5, "xhdpi": 2, "xxhdpi": 3, "xxxhdpi": 4}


def render(svg: str, size: int) -> Image.Image:
    png = cairosvg.svg2png(bytestring=svg.encode(), output_width=size, output_height=size)
    return Image.open(io.BytesIO(png)).convert("RGBA")


maskable = (branding / "icon-maskable.svg").read_text(encoding="utf-8")
# Lớp trước của adaptive icon: bỏ nền (nền là drawable gradient) và thu nhỏ nội dung
# để vừa vùng an toàn 72dp/108dp, giữ tỉ lệ giống icon maskable của web.
foreground = re.sub(r'<rect width="512" height="512" fill="url\(#bg\)"/>', "", maskable)
foreground = foreground.replace("scale(0.72)", f"scale({0.72 * 72 / 108:.4f})")
legacy = (branding / "icon.svg").read_text(encoding="utf-8")

for name, factor in densities.items():
    folder = res / f"mipmap-{name}"
    folder.mkdir(parents=True, exist_ok=True)
    render(foreground, round(108 * factor)).save(folder / "ic_launcher_foreground.png", optimize=True)
    render(legacy, round(48 * factor)).save(folder / "ic_launcher.png", optimize=True)

splash_dir = res / "drawable-nodpi"
splash_dir.mkdir(parents=True, exist_ok=True)
render(legacy, 432).save(splash_dir / "splash.png", optimize=True)
print("ok")
