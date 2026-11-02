"""生成应用图标（多尺寸 .ico），不依赖外部素材。"""
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "src", "SapLauncher", "Assets", "app.ico")
S = 1024

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 背景圆角方块（带纵向渐变）
bg = Image.new("RGBA", (S, S), (0, 0, 0, 0))
bd = ImageDraw.Draw(bg)
r = int(S * 0.22)
for y in range(S):
    t = y / S
    c = (int(24 + 6 * (1 - t)), int(30 + 12 * (1 - t)), int(40 + 20 * (1 - t)), 255)
    bd.line([(0, y), (S, y)], fill=c)
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=r, fill=255)
img.paste(bg, (0, 0), mask)

d = ImageDraw.Draw(img)
# 外描边
d.rounded_rectangle([2, 2, S - 3, S - 3], radius=r, outline=(58, 71, 87, 255), width=int(S * 0.008))

# 门框（蓝色竖条）
door_w, door_h = int(S * 0.16), int(S * 0.52)
dx0, dy0 = int(S * 0.60), int(S * 0.24)
d.rounded_rectangle([dx0, dy0, dx0 + door_w, dy0 + door_h],
                    radius=int(S * 0.035), fill=(47, 123, 224, 255))
# 门内高光
d.rounded_rectangle([dx0 + int(S * 0.02), dy0 + int(S * 0.02),
                     dx0 + door_w - int(S * 0.02), dy0 + door_h - int(S * 0.02)],
                    radius=int(S * 0.028), fill=(61, 139, 239, 255))

# 箭头（进入）
cy = S // 2
ax0, ax1 = int(S * 0.24), int(S * 0.55)
th = int(S * 0.055)
d.rounded_rectangle([ax0, cy - th // 2, ax1, cy + th // 2], radius=th // 2, fill=(232, 238, 246, 255))
head = int(S * 0.085)
d.polygon([(ax1 - int(S * 0.01), cy - head), (ax1 - int(S * 0.01), cy + head),
           (ax1 + head, cy)], fill=(232, 238, 246, 255))

# 状态点
dot_r = int(S * 0.035)
cx, cy2 = int(S * 0.78), int(S * 0.78)
d.ellipse([cx - dot_r, cy2 - dot_r, cx + dot_r, cy2 + dot_r], fill=(75, 190, 122, 255))

sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16]
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.resize((256, 256), Image.LANCZOS).save(OUT, format="ICO", sizes=[(s, s) for s in sizes])
print("icon written:", OUT, os.path.getsize(OUT), "bytes")
