"""Generate the RadioPad desktop app icon (icon.png / icon.ico / icon.icns)
from the RC design-system accent colours, replacing the legacy Open Design
(cream/orange focus-brackets) icon.

Run locally with: python scripts/gen-desktop-icon.py
Requires Pillow (already a dev dependency in this environment).
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ICON_DIR = ROOT / "desktop" / "src-tauri" / "icons"

# RC design-system accent blues (frontend/app/tokens.css, light theme values)
ACCENT = (47, 136, 216)        # --color-accent      #2f88d8
ACCENT_SOFT = (36, 96, 165)    # slightly deeper than --color-accent-deep, for gradient bottom
WHITE = (255, 255, 255)

SIZE = 1024


def superellipse_mask(size: int, radius_ratio: float = 0.225, n: float = 5.0) -> Image.Image:
    """Rounded-square (squircle-ish) alpha mask, matching Windows 11 Fluent icon geometry."""
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    radius = int(size * radius_ratio)
    draw.rounded_rectangle([0, 0, size - 1, size - 1], radius=radius, fill=255)
    return mask


def vertical_gradient(size: int, top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    grad = Image.new("RGB", (1, size))
    for y in range(size):
        t = y / (size - 1)
        r = round(top[0] + (bottom[0] - top[0]) * t)
        g = round(top[1] + (bottom[1] - top[1]) * t)
        b = round(top[2] + (bottom[2] - top[2]) * t)
        grad.putpixel((0, y), (r, g, b))
    return grad.resize((size, size))


FONT_CANDIDATES = [
    r"C:\Windows\Fonts\seguibl.ttf",   # Segoe UI Black — matches the app's --font-display weight
    r"C:\Windows\Fonts\arialbd.ttf",
]


def load_glyph_font(size: int) -> ImageFont.FreeTypeFont:
    for candidate in FONT_CANDIDATES:
        path = Path(candidate)
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def draw_r_glyph(canvas: Image.Image, size: int) -> None:
    """Draw a bold 'R' monogram, echoing the sidebar/topbar .brand-mark letter."""
    font = load_glyph_font(int(size * 0.62))
    draw = ImageDraw.Draw(canvas)
    text = "R"
    bbox = draw.textbbox((0, 0), text, font=font)
    text_w, text_h = bbox[2] - bbox[0], bbox[3] - bbox[1]
    x = (size - text_w) / 2 - bbox[0]
    y = (size - text_h) / 2 - bbox[1]
    draw.text((x, y), text, font=font, fill=WHITE)


def build_master_icon() -> Image.Image:
    bg = vertical_gradient(SIZE, ACCENT, ACCENT_SOFT).convert("RGBA")
    mask = superellipse_mask(SIZE)

    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.paste(bg, (0, 0), mask)

    draw_r_glyph(canvas, SIZE)
    return canvas


def main() -> None:
    ICON_DIR.mkdir(parents=True, exist_ok=True)
    master = build_master_icon()

    png_path = ICON_DIR / "icon.png"
    master.save(png_path)
    print(f"wrote {png_path} ({master.size[0]}x{master.size[1]})")

    ico_path = ICON_DIR / "icon.ico"
    ico_sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    master.save(ico_path, format="ICO", sizes=ico_sizes)
    print(f"wrote {ico_path}")

    icns_path = ICON_DIR / "icon.icns"
    master.save(icns_path, format="ICNS")
    print(f"wrote {icns_path}")


if __name__ == "__main__":
    main()
