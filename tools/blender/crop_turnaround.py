"""Split a 4-view character turnaround sheet into separate, square, padded single-view images.
Usage: Blender -b --python tools/blender/crop_turnaround.py -- <sheet.png> <out_dir> name:x0:x1 [name:x0:x1 ...]
Each spec is name:x0:x1 or name:x0:x1:y0:y1 (pixels from the top-left; y range excludes titles/labels).
Background is padded with the sheet's own corner colour.
"""
import bpy, sys, os, numpy as np
args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
img = bpy.data.images.load(src)
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)   # row 0 = bottom
bg = px[h - 5, 5].copy()                                            # top-left corner colour
os.makedirs(out, exist_ok=True)
for spec in args[2:]:
    parts = spec.split(":")
    name, x0, x1 = parts[0], int(parts[1]), int(parts[2])
    y0, y1 = (int(parts[3]), int(parts[4])) if len(parts) == 5 else (0, h)
    crop = px[h - y1:h - y0, x0:x1]            # pixel rows are stored bottom-up
    ch = y1 - y0
    side = max(ch, x1 - x0) + 80
    canvas = np.tile(bg, (side, side, 1))
    oy, ox = (side - ch) // 2, (side - (x1 - x0)) // 2
    canvas[oy:oy + ch, ox:ox + (x1 - x0)] = crop
    o = bpy.data.images.new(name, side, side, alpha=True)
    o.pixels = canvas.ravel()
    o.filepath_raw = os.path.join(out, f"quacks_{name}.png"); o.file_format = 'PNG'; o.save()
    print("CROP", name, x0, x1, "->", o.filepath_raw, side)
