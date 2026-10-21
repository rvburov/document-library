#!/usr/bin/env python3
"""
Рисует мастер-иконки «Библиотеки PDF»: синяя плашка с градиентом, светлая
шапка с надписью «PDF», белый лист документа и лупа. Временная иконка — до своей, нарисованной
дизайнером; заменить можно, положив свои мастера в Assets/Branding и запустив
Scripts/generate-icons.sh.

Три мастера 1024×1024, по одному на систему:
  icon-windows.png — во всю площадь, скругление ~10 % стороны;
  icon-macos.png   — плашка 824×824 по центру (сетка Apple), поля прозрачные;
  icon-linux.png   — во всю площадь, скругление ~16 %.

Нужен Python 3 с Pillow. Запуск: python3 Scripts/draw-icon.py
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Assets", "Branding")
S = 4  # рисуем в 4 раза крупнее и уменьшаем — сглаживание краев


def font(size):
    for path in ("/usr/share/fonts/opentype/inter/Inter-Bold.otf",
                 "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
                 "/Library/Fonts/Arial Bold.ttf",
                 "C:/Windows/Fonts/arialbd.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def gradient(w, h):
    """Диагональный градиент: синий слева сверху → бирюзовый справа снизу."""
    top, bottom = (47, 107, 255), (18, 168, 208)
    g = Image.new("RGB", (w, h))
    px = g.load()
    for y in range(h):
        for x in range(w):
            t = (x / w * 0.55 + y / h * 0.45)
            px[x, y] = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
    return g


def plate(size, radius_ratio):
    w = size * S
    r = int(w * radius_ratio)
    mask = Image.new("L", (w, w), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w - 1, w - 1], radius=r, fill=255)

    img = gradient(w // 8, w // 8).resize((w, w), Image.BICUBIC).convert("RGBA")
    d = ImageDraw.Draw(img)

    # Светлая шапка с надписью «PDF».
    band = int(w * 0.205)
    d.rectangle([0, 0, w, band], fill=(244, 247, 254, 255))
    f = font(int(w * 0.105))
    for i, ch in enumerate("PDF"):
        cx = int(w * (0.17 + 0.33 * i))
        bbox = d.textbbox((0, 0), ch, font=f)
        d.text((cx - (bbox[2] - bbox[0]) // 2 - bbox[0], band // 2 - (bbox[3] - bbox[1]) // 2 - bbox[1]),
               ch, font=f, fill=(47, 98, 245, 255))
    for i in (1, 2):
        x = int(w * (0.335 * i))
        d.line([x, int(w * 0.04), x, band - int(w * 0.03)], fill=(200, 206, 220, 255), width=int(w * 0.012))

    # Тень под листом.
    shadow = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    px0, py0, px1, py1 = int(w * 0.25), int(w * 0.29), int(w * 0.70), int(w * 0.90)
    sd.rounded_rectangle([px0 + w * 0.015, py0 + w * 0.025, px1 + w * 0.015, py1 + w * 0.025],
                         radius=int(w * 0.03), fill=(10, 40, 110, 110))
    shadow = shadow.filter(ImageFilter.GaussianBlur(w * 0.02))
    img.alpha_composite(shadow)

    # Лист с загнутым углом.
    fold = int(w * 0.11)
    d = ImageDraw.Draw(img)
    d.polygon([(px0, py0 + int(w * 0.03)), (px0 + int(w * 0.03), py0), (px1 - fold, py0), (px1, py0 + fold),
               (px1, py1 - int(w * 0.03)), (px1 - int(w * 0.03), py1), (px0 + int(w * 0.03), py1),
               (px0, py1 - int(w * 0.03))], fill=(255, 255, 255, 255))
    d.polygon([(px1 - fold, py0), (px1 - fold, py0 + fold), (px1, py0 + fold)], fill=(210, 222, 245, 255))

    # Строки текста на листе.
    lx0 = px0 + int(w * 0.055)
    for k, (y, ln) in enumerate([(0.40, 0.26), (0.47, 0.34), (0.535, 0.30), (0.60, 0.34), (0.665, 0.20)]):
        yy = int(w * y)
        d.rounded_rectangle([lx0, yy, lx0 + int(w * ln), yy + int(w * 0.03)], radius=int(w * 0.015),
                            fill=(47, 107, 255, 255) if k == 0 else (190, 204, 230, 255))

    # Лупа поверх правого нижнего угла.
    cx, cy, r = int(w * 0.66), int(w * 0.70), int(w * 0.135)
    ring = int(w * 0.05)
    glass = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glass)
    hx, hy = cx + int(r * 0.72), cy + int(r * 0.72)
    gd.line([hx, hy, hx + int(w * 0.13), hy + int(w * 0.13)], fill=(255, 255, 255, 255), width=int(w * 0.07))
    gd.ellipse([cx - r - ring // 2, cy - r - ring // 2, cx + r + ring // 2, cy + r + ring // 2],
               fill=(255, 255, 255, 255))
    gd.ellipse([cx - r + ring // 2, cy - r + ring // 2, cx + r - ring // 2, cy + r - ring // 2],
               fill=(225, 238, 255, 255))
    gshadow = glass.copy()
    gshadow.putalpha(glass.getchannel("A").point(lambda a: a * 120 // 255))
    gshadow = Image.merge("RGBA", (Image.new("L", (w, w), 10), Image.new("L", (w, w), 40),
                                   Image.new("L", (w, w), 110), gshadow.getchannel("A")))
    gshadow = gshadow.filter(ImageFilter.GaussianBlur(w * 0.018))
    img.alpha_composite(gshadow, (int(w * 0.01), int(w * 0.02)))
    img.alpha_composite(glass)

    img.putalpha(mask)
    return img.resize((size, size), Image.LANCZOS)


def main():
    os.makedirs(OUT, exist_ok=True)
    win = plate(1024, 0.10)
    win.save(os.path.join(OUT, "icon-windows.png"))
    plate(1024, 0.16).save(os.path.join(OUT, "icon-linux.png"))
    mac = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    mac.alpha_composite(plate(824, 0.225), (100, 100))
    mac.save(os.path.join(OUT, "icon-macos.png"))
    # Windows .ico — слоями под размеры оболочки; Pillow собирает их из мастера сам.
    win.save(os.path.join(OUT, "icon.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (256, 256)])
    print("Мастера и icon.ico записаны в", OUT)


if __name__ == "__main__":
    sys.exit(main())
