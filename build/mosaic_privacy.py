# -*- coding: utf-8 -*-
# 对截图敏感区域打马赛克（像素化）
from PIL import Image
import sys, os

ROOT = r"E:\WorkBuddy\SAP 登陆器"

def mosaic(path, box, block=8):
    img = Image.open(path).convert("RGB")
    region = img.crop(box)
    w, h = region.size
    small = region.resize((max(1, w // block), max(1, h // block)), Image.NEAREST)
    region = small.resize((w, h), Image.NEAREST)
    img.paste(region, (box[0], box[1]))
    img.save(path)
    print(f"mosaic ok: {os.path.relpath(path, ROOT)} box={box}")

# screenshots/main.png: 侧边栏分组「申龙BUS」区域
mosaic(os.path.join(ROOT, "docs", "screenshots", "main.png"), (20, 195, 150, 230), block=8)
