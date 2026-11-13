#!/usr/bin/env python3
"""Write the Avalonia app's icon files with the same look as generate_icons.py.

    python3 tool/icon/draw_glyph.py                        # only when the glyph changes
    python3 tool/icon/avalonia_icons.py <cat image path>   # settings from assets/icon/cat.json

generate_icons.py (copied unchanged from application-release-templates common/)
writes into Flutter's platform folders, which this app doesn't have. This wrapper
reuses its drawing code and writes where the Avalonia build and packaging read:

  assets/icon/app_icon_1024.png, app_icon.png   master + 256px (as generate_icons.py)
  src/DaylightCompare.App/Assets/app_icon.ico   Windows exe/window icon, no plate
  src/DaylightCompare.App/Assets/app_icon.png   256px, About dialog
  installer/macos/app_icon_1024.png             master for the .icns
  installer/macos/AppIcon.icns                  make_icns.py; package-macos.sh puts it in the .app
  installer/linux/app_icon.png                  512px, package-linux.sh

The cat artwork is a paid Icons8 asset: pass its path, do not commit it.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
import generate_icons as g  # noqa: E402
from make_icns import make_icns  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'src/DaylightCompare.App/Assets'


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit('usage: avalonia_icons.py <cat image path>')
    cfg = json.loads((g.ICON_DIR / 'cat.json').read_text(encoding='utf-8'))
    g.APP_HUE = (cfg['hue'] % 360 / 360) if 'hue' in cfg else g.hue_for(cfg['app_id'])
    g.PROP_TILE = bool(cfg.get('prop_tile', False))
    cat = Image.open(sys.argv[1]).convert('RGBA')
    glyph = g.cat_with_prop(cat, Image.open(g.ICON_DIR / 'source_glyph.png').convert('RGBA'))
    g.PROP_COMPOSED = True

    mac = g.macos_icon(glyph)
    mac.save(g.ICON_DIR / 'app_icon_1024.png')
    mac.resize((256, 256), Image.LANCZOS).save(g.ICON_DIR / 'app_icon.png')

    ASSETS.mkdir(parents=True, exist_ok=True)
    frames = [g.windows_icon(glyph, s) for s in g.WINDOWS_ICON_SIZES]
    frames[0].save(ASSETS / 'app_icon.ico', format='ICO',
                   sizes=[(s, s) for s in g.WINDOWS_ICON_SIZES], append_images=frames[1:])
    mac.resize((256, 256), Image.LANCZOS).save(ASSETS / 'app_icon.png')

    (ROOT / 'installer/macos').mkdir(parents=True, exist_ok=True)
    mac.save(ROOT / 'installer/macos/app_icon_1024.png')
    make_icns()
    (ROOT / 'installer/linux').mkdir(parents=True, exist_ok=True)
    g.desktop_icon(glyph).resize((512, 512), Image.LANCZOS).save(ROOT / 'installer/linux/app_icon.png')

    print(f'hue {round(g.APP_HUE * 360)}°  → assets/icon, {ASSETS.relative_to(ROOT)}/app_icon.ico+png, '
          'installer/macos/app_icon_1024.png+AppIcon.icns, installer/linux/app_icon.png')


if __name__ == '__main__':
    main()
