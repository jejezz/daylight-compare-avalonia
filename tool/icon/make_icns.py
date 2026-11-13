#!/usr/bin/env python3
"""Build the macOS app icon (.icns) from the 1024px master.

    python3 tool/icon/make_icns.py      # installer/macos/app_icon_1024.png -> installer/macos/AppIcon.icns

avalonia_icons.py calls this after it writes the master, so you only run it by hand
when the master changed some other way. scripts/package-macos.sh copies the result
into <App>.app/Contents/Resources/AppIcon.icns (Info.plist CFBundleIconFile = AppIcon).

Every slot of Apple's iconset (16-512 @1x/@2x) is resized from the master with
LANCZOS. On a Mac `iconutil` packs them (Apple's own writer); elsewhere Pillow's
ICNS writer does.
"""
from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
MASTER = ROOT / 'installer/macos/app_icon_1024.png'
OUT = ROOT / 'installer/macos/AppIcon.icns'
SIZES = (16, 32, 128, 256, 512)


def make_icns(master: Path = MASTER, out: Path = OUT) -> None:
    img = Image.open(master).convert('RGBA')
    if img.size != (1024, 1024):
        raise SystemExit(f'{master}: expected 1024x1024, got {img.size}')
    out.parent.mkdir(parents=True, exist_ok=True)
    if shutil.which('iconutil'):
        with tempfile.TemporaryDirectory() as tmp:
            iconset = Path(tmp) / 'AppIcon.iconset'
            iconset.mkdir()
            for s in SIZES:
                img.resize((s, s), Image.LANCZOS).save(iconset / f'icon_{s}x{s}.png')
                img.resize((s * 2, s * 2), Image.LANCZOS).save(iconset / f'icon_{s}x{s}@2x.png')
            subprocess.run(['iconutil', '-c', 'icns', str(iconset), '-o', str(out)], check=True)
    else:
        extra = [img.resize((s, s), Image.LANCZOS) for s in (16, 32, 64, 128, 256, 512)]
        img.save(out, format='ICNS', append_images=extra)


def main() -> None:
    if len(sys.argv) > 1:
        raise SystemExit('usage: make_icns.py   (reads installer/macos/app_icon_1024.png)')
    make_icns()
    print(f'{OUT.relative_to(ROOT)}')


if __name__ == '__main__':
    main()
