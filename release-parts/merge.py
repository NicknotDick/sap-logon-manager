import os, glob
d = os.path.dirname(os.path.abspath(__file__))
parts = sorted(glob.glob(os.path.join(d, 'part_*')))
out = os.path.join(os.path.dirname(d), 'dist', 'SAP登录管理器.exe')
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, 'wb') as o:
    for p in parts:
        o.write(open(p, 'rb').read())
print('已合并 %d 个分卷 -> %s' % (len(parts), out))
