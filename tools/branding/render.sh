#!/usr/bin/env bash
# Tạo lại bộ ảnh thương hiệu trong wwwroot/defaults từ các tệp SVG ở thư mục này.
# Cần rsvg-convert (librsvg) và Python Pillow (để tạo favicon.ico).
set -euo pipefail
cd "$(dirname "$0")"
out=../../wwwroot/defaults
for s in 72 128 144 192 512; do
  rsvg-convert -w $s -h $s icon.svg -o $out/app_icon_$s.png
  rsvg-convert -w $s -h $s icon-maskable.svg -o $out/app_maskable_icon_$s.png
done
rsvg-convert -w 408 -h 96 logo.svg -o $out/app_logo.png
rsvg-convert -w 96 -h 96 icon.svg -o $out/app_logo_small.png
rsvg-convert -w 1125 -h 2436 launch.svg -o $out/app_launch.png
rsvg-convert -w 256 -h 256 icon.svg -o /tmp/app_icon_256.png 2>/dev/null || rsvg-convert -w 256 -h 256 icon.svg -o "${TEMP:-.}/app_icon_256.png"
python -c "import sys;from PIL import Image;Image.open(sys.argv[1]).save(sys.argv[2],sizes=[(16,16),(32,32),(48,48),(64,64)])" "$( [ -f /tmp/app_icon_256.png ] && echo /tmp/app_icon_256.png || echo ${TEMP:-.}/app_icon_256.png )" ../../wwwroot/favicon.ico
