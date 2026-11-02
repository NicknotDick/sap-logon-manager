# -*- coding: utf-8 -*-
"""生成多尺寸图标预览图，便于核对小尺寸清晰度。"""
from PIL import Image

master = Image.open(r'C:\tmp\sap_icon_1024.png').convert('RGBA')
sizes = [256, 128, 64, 48, 32, 16]
gap, pad = 16, 20
W = pad * 2 + sum(sizes) + gap * (len(sizes) - 1)
H = pad * 2 + max(sizes)
bg = (60, 64, 72, 255)  # 深灰，接近深色任务栏
sheet = Image.new('RGBA', (W, H), bg)
x = pad
for s in sizes:
    ic = master.resize((s, s), Image.LANCZOS)
    y = (H - s) // 2
    sheet.alpha_composite(ic, (x, y))
    x += s + gap
sheet.convert('RGB').save(r'C:\tmp\icon_sizes.png')
print('done', W, 'x', H)
