from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
import math

out = Path(__file__).resolve().parent / "evidence"
out.mkdir(parents=True, exist_ok=True)

BG, CARD, INK, MUTED, ACCENT, PAID = "#0F202E", "#1B323B", "#E8F6F5", "#7F969B", "#91D0D4", "#F0A8CC"
GREAT, MISS, TOP = "#2F7F6E", "#B08A86", "#0D171F"
W, H = 540, 960


def font(size):
    for p in (
        r"C:\Windows\Fonts\msyh.ttc",
        r"C:\Windows\Fonts\msyhbd.ttc",
        r"C:\Windows\Fonts\arial.ttf",
    ):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            continue
    return ImageFont.load_default()


def chamfer(draw, cx, cy, w, h, fill):
    cut = min(w, h) * 0.22
    x0, y0, x1, y1 = cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2
    pts = [
        (x0 + cut, y0),
        (x1 - cut, y0),
        (x1, y0 + cut),
        (x1, y1 - cut),
        (x1 - cut, y1),
        (x0 + cut, y1),
        (x0, y1 - cut),
        (x0, y0 + cut),
    ]
    draw.polygon(pts, fill=fill)


def field_base():
    im = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(im, "RGBA")
    d.rectangle((145, 70, 255, 730), fill=CARD)
    d.rectangle((285, 70, 395, 730), fill=CARD)
    d.rectangle((40, 696, 500, 702), fill=INK)
    return im, d


im, d = field_base()
d.rectangle((0, 0, W, 44), fill=TOP)
d.text((16, 10), "测试场", font=font(18), fill=INK)
d.text((W - 16, 12), "v1.0", font=font(14), fill=MUTED, anchor="ra")
chamfer(d, 200, 490, 84, 18, ACCENT)
chamfer(d, 340, 430, 84, 18, ACCENT)
d.rectangle((0, 750, W, H), fill="#0F1C24")
d.rounded_rectangle((16, 770, 176, 808), 6, fill=ACCENT)
d.rounded_rectangle((188, 770, 348, 808), 6, fill=GREAT)
d.rounded_rectangle((360, 770, 520, 808), 6, fill=MISS)
d.text((96, 778), "Perfect", font=font(14), fill=BG, anchor="mt")
d.text((268, 778), "Great", font=font(14), fill=INK, anchor="mt")
d.text((440, 778), "Miss", font=font(14), fill=INK, anchor="mt")
im.save(out / "workbench.png")

im, d = field_base()
cx, cy = 200, 699
d.ellipse((cx - 45, cy - 45, cx + 45, cy + 45), outline=ACCENT, width=6)
d.ellipse((cx - 70, cy - 70, cx + 70, cy + 70), outline=ACCENT, width=3)
for ang in (0, 90, 180, 270):
    rad = math.radians(ang)
    x = cx + math.cos(rad) * 80
    y = cy + math.sin(rad) * 80
    d.polygon([(cx, cy), (x - 6, y - 6), (x + 6, y + 6)], fill=ACCENT)
d.text((W / 2, 36), "PERFECT", font=font(22), fill=ACCENT, anchor="mt")
im.save(out / "hit.png")

im, d = field_base()
for i in range(40):
    a = int(230 * (1 - i / 40))
    y0 = 360 + i * 8
    d.rectangle((310, y0, 370, y0 + 9), fill=(240, 168, 204, a))
chamfer(d, 340, 699, 84, 18, PAID)
d.text((W / 2, 36), "HOLD", font=font(22), fill=PAID, anchor="mt")
im.save(out / "hold.png")

im, d = field_base()
d.rectangle((18, 18, 522, 24), fill="#22343C")
d.rectangle((18, 18, 230, 24), fill=ACCENT)
d.text((18, 40), "0012600", font=font(20), fill=INK)
d.text((522, 42), "00:18 / 00:42", font=font(14), fill=MUTED, anchor="ra")
d.text((W / 2, 380), "24", font=font(56), fill=PAID, anchor="mt")
d.text((W / 2, 450), "COMBO", font=font(14), fill=MUTED, anchor="mt")
im.save(out / "hud.png")

im = Image.new("RGB", (W, H), "#0A141C")
d = ImageDraw.Draw(im)
d.text((W / 2, 36), "选曲", font=font(26), fill=INK, anchor="mt")
rows = [
    ("酸橙色信笺  ·  Easy", "鼓  ·  4K  ·  Lv.3", ACCENT),
    ("酸橙色信笺  ·  双指", "鼓  ·  4K  ·  Lv.8", PAID),
    ("无声测试谱  ·  全类型", "调试  ·  5K  ·  虚拟钟", ACCENT),
]
y = 90
for title, meta, bar in rows:
    d.rounded_rectangle((24, y, 516, y + 88), 10, fill=CARD)
    d.rectangle((24, y, 30, y + 88), fill=bar)
    d.text((44, y + 18), title, font=font(16), fill=INK)
    d.text((44, y + 50), meta, font=font(13), fill=MUTED)
    y += 104
im.save(out / "song-select.png")

im = Image.new("RGB", (W, H), "#0A141C")
d = ImageDraw.Draw(im)
d.text((W / 2, 48), "结算", font=font(16), fill=MUTED, anchor="mt")
d.text((W / 2, 220), "S", font=font(96), fill=PAID, anchor="mt")
d.text((W / 2, 380), "SCORE  0012600", font=font(22), fill=INK, anchor="mt")
d.text((W / 2, 430), "P  12    G  3    M  1", font=font(16), fill=ACCENT, anchor="mt")
d.text((W / 2, 470), "MAX COMBO  12", font=font(14), fill=MUTED, anchor="mt")
im.save(out / "result.png")
print("wrote", sorted(p.name for p in out.glob("*.png")))
