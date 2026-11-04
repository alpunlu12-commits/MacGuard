"""MacGuard Windows simgesini çizer (Assets/AppIcon.ico).

macOS sürümündeki Scripts/make_icon.swift ile aynı tasarım: koyu yuvarlak kare
zemin, ortada yeşil kalkan ve kilit. SF Symbols Windows'ta olmadığı için kalkan
elle çiziliyor.

Kullanım:  python Windows/Scripts/make_icon.py   (Pillow gerekir)
"""
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 1024
GREEN = (51, 212, 153, 255)
DARK = (11, 15, 20, 255)


def draw(size: int = SIZE) -> Image.Image:
    s = size / SIZE
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # Dikey gradyanlı yuvarlak kare zemin
    top, bottom = (23, 33, 46), (8, 13, 18)
    grad = Image.new("RGBA", (size, size))
    gd = ImageDraw.Draw(grad)
    for y in range(size):
        t = y / max(1, size - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        gd.line([(0, y), (size, y)], fill=c)
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)

    # Kalkan: üstte düz kenar, altta sivri uç
    cx = size / 2
    w, top_y, mid_y, tip_y = 520 * s, 210 * s, 560 * s, 840 * s
    shield = [
        (cx, top_y - 30 * s),
        (cx + w / 2, top_y + 40 * s),
        (cx + w / 2, mid_y),
        (cx, tip_y),
        (cx - w / 2, mid_y),
        (cx - w / 2, top_y + 40 * s),
    ]
    d.polygon(shield, fill=GREEN)

    # Kilit gövdesi ve halkası (koyu)
    bw, bh = 220 * s, 170 * s
    by = 470 * s
    d.rounded_rectangle([cx - bw / 2, by, cx + bw / 2, by + bh], radius=int(28 * s), fill=DARK)
    ring_w, ring_t = 150 * s, 34 * s
    d.arc([cx - ring_w / 2, by - 130 * s, cx + ring_w / 2, by + 40 * s], start=180, end=360,
          fill=DARK, width=int(ring_t))
    d.line([(cx - ring_w / 2 + ring_t / 2, by - 45 * s), (cx - ring_w / 2 + ring_t / 2, by + 5 * s)],
           fill=DARK, width=int(ring_t))
    d.line([(cx + ring_w / 2 - ring_t / 2, by - 45 * s), (cx + ring_w / 2 - ring_t / 2, by + 5 * s)],
           fill=DARK, width=int(ring_t))
    # Anahtar deliği
    d.ellipse([cx - 22 * s, by + 50 * s, cx + 22 * s, by + 94 * s], fill=GREEN)
    d.rectangle([cx - 9 * s, by + 80 * s, cx + 9 * s, by + 130 * s], fill=GREEN)
    return img


def main() -> None:
    out = Path(__file__).resolve().parent.parent / "Assets" / "AppIcon.ico"
    out.parent.mkdir(parents=True, exist_ok=True)
    big = draw()
    sizes = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)]
    big.save(out, format="ICO", sizes=sizes)
    print(f"simge yazıldı: {out}")


if __name__ == "__main__":
    main()
