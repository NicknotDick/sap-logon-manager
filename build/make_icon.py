# -*- coding: utf-8 -*-
"""把渲染好的 SAP logo PNG 合成为方形图标（保持比例居中），并生成多尺寸 .ico。"""
import os
from PIL import Image

SRC = r'C:\tmp\sap_logo_1024.png'
OUT_MASTER = r'C:\tmp\sap_icon_1024.png'
OUT_256 = r'C:\tmp\sap_icon_256.png'
ICO = r'E:\WorkBuddy\SAP 登陆器\src\SapLauncher\Assets\app.ico'

src = Image.open(SRC).convert('RGBA')
size = 1024
pad_ratio = 0.02
max_w = int(size * (1 - pad_ratio * 2))
max_h = int(size * (1 - pad_ratio * 2))
scale = min(max_w / src.width, max_h / src.height)
tw, th = int(src.width * scale), int(src.height * scale)
logo = src.resize((tw, th), Image.LANCZOS)

canvas = Image.new('RGBA', (size, size), (0, 0, 0, 0))
canvas.paste(logo, ((size - tw) // 2, (size - th) // 2), logo)
canvas.save(OUT_MASTER)
canvas.resize((256, 256), Image.LANCZOS).save(OUT_256)

# 生成多尺寸 ico（覆盖前先备份已由外层完成）
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
canvas.save(ICO, format='ICO', sizes=sizes)
print('logo', tw, 'x', th)
print('ico', ICO, os.path.getsize(ICO), 'bytes')
