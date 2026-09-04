"""Bake per-member recoloured copies of the Unity-chan colour textures.

usage: python Tools/recolor_unitychan.py [member ...]      (default: all)

Only four files carry the character's colour scheme: the clothing atlas, the hair atlas and the two
irises. face_00 / skin_01 / cheek_00 / eyeline_00 are deliberately left alone — they are the face and
the skin, and the brief is "same model, recoloured", so all three members keep one face.

Each file gets its own hue rotation: the uniform and the hair start at different hues and have to land
at different targets, so a single global shift cannot serve both. Saturation and value are scaled in
HSV, and `tint` warms or cools the near-neutral pixels (the uniform is mostly white, and hue rotation
does nothing to a pixel with zero saturation — tint is what turns white into cream or into snow-blue).

Alpha is carried through untouched: the irises and lashes depend on it.
"""
import os
import sys
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Assets", "unity-chan!", "Unity-chan! Model", "Art", "UnityChanShader", "Texture")
DST = os.path.join(ROOT, "Assets", "WuWa", "Characters", "UnityChan")

# The hair and iris atlases are essentially single-hue, so a global (hue, sat, val) works on them.
# The body atlas is NOT: it holds a cyan-blue uniform AND orange accents AND dark panels, so one
# global rotation that fixes the uniform drags the accents somewhere absurd (+34 turned orange into
# yellow-green, +142 turned cyan into magenta). The body is remapped per hue BAND instead: each band
# pulls a source hue range onto a target hue with its own sat/val, and anything outside every band is
# left alone. `centre`/`width`/`dst` are degrees.
MEMBERS = {
    0: {
        "key": "copper", "name": "Kindled Copper",
        "body": dict(bands=[
            dict(centre=205, width=55, dst=178, sat=0.95, val=1.00),   # uniform blue  -> sea teal
            dict(centre=28, width=32, dst=20, sat=1.05, val=1.00),     # accents       -> copper
        ], tint=((0.93, 0.99, 0.97), 0.20)),
        "hair": dict(hue=-9, sat=1.30, val=0.95, tint=((1.00, 0.90, 0.80), 0.20)),
        "iris": dict(bands=[
            dict(centre=215, width=90, dst=38, sat=1.15, val=1.05),
            dict(centre=60, width=60, dst=45, sat=1.25, val=1.05),
        ], tint=((1.0, 1.0, 1.0), 0.0)),                               # amber
    },
    1: {
        "key": "moonfire", "name": "Moonfire Gold",
        "body": dict(bands=[
            dict(centre=205, width=55, dst=237, sat=1.00, val=0.80),   # uniform blue  -> deep indigo
            dict(centre=28, width=32, dst=46, sat=1.05, val=1.05),     # accents       -> gold
        ], tint=((0.94, 0.95, 1.00), 0.30)),
        "hair": dict(hue=16, sat=0.16, val=1.38, tint=((1.00, 0.98, 0.92), 0.45)),
        "iris": dict(bands=[
            dict(centre=215, width=90, dst=218, sat=1.20, val=1.05),
            dict(centre=60, width=60, dst=196, sat=1.15, val=1.05),
        ], tint=((1.0, 1.0, 1.0), 0.0)),                               # ice blue
    },
    2: {
        "key": "winerose", "name": "Wine and Rose",
        "body": dict(bands=[
            dict(centre=205, width=55, dst=25, sat=0.42, val=1.10),    # uniform blue  -> bone cream
            dict(centre=28, width=32, dst=348, sat=1.05, val=0.88),    # accents       -> wine red
        ], tint=((1.00, 0.95, 0.86), 0.35)),
        "hair": dict(hue=-46, sat=1.40, val=0.60, tint=((0.85, 0.55, 0.62), 0.15)),
        "iris": dict(bands=[
            dict(centre=215, width=90, dst=272, sat=1.20, val=1.00),
            dict(centre=60, width=60, dst=286, sat=0.95, val=1.00),
        ], tint=((1.0, 1.0, 1.0), 0.0)),                               # violet
    },
}

FILES = [
    ("body_01.tga", "body"),
    ("hair_01.tga", "hair"),
    ("eye_iris_L_00.tga", "iris"),
    ("eye_iris_R_00.tga", "iris"),
]


def rgb_to_hsv(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = np.max(rgb, axis=-1)
    mn = np.min(rgb, axis=-1)
    d = mx - mn
    h = np.zeros_like(mx)
    nz = d > 1e-6
    rm = nz & (mx == r)
    gm = nz & (mx == g) & ~rm
    bm = nz & (mx == b) & ~rm & ~gm
    h[rm] = ((g - b)[rm] / d[rm]) % 6.0
    h[gm] = ((b - r)[gm] / d[gm]) + 2.0
    h[bm] = ((r - g)[bm] / d[bm]) + 4.0
    h = h / 6.0
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0.0)
    return h, s, mx


def hsv_to_rgb(h, s, v):
    i = np.floor(h * 6.0)
    f = h * 6.0 - i
    p = v * (1.0 - s)
    q = v * (1.0 - f * s)
    t = v * (1.0 - (1.0 - f) * s)
    i = (i.astype(np.int32) % 6)
    out = np.zeros(h.shape + (3,), dtype=np.float32)
    for idx, (rr, gg, bb) in enumerate([(v, t, p), (q, v, p), (p, v, t), (p, q, v), (t, p, v), (v, p, q)]):
        m = i == idx
        out[m, 0] = rr[m]
        out[m, 1] = gg[m]
        out[m, 2] = bb[m]
    return out


def apply_bands(h, s, v, bands):
    """Pull each hue band onto its target hue, leaving hues outside every band untouched.

    Weights fall off smoothly to the band edge so there is no seam where a band stops, and the
    strongest band wins per pixel rather than the last one written.
    """
    h_out, s_out, v_out = h.copy(), s.copy(), v.copy()
    best = np.zeros_like(h)
    for b in bands:
        d = np.abs(((h * 360.0 - b["centre"] + 180.0) % 360.0) - 180.0)   # circular distance, degrees
        w = np.clip(1.0 - d / float(b["width"]), 0.0, 1.0)
        w = w * w * (3.0 - 2.0 * w)                                        # smoothstep
        w = np.where(s > 0.12, w, 0.0)                                     # near-greys keep their hue
        take = w > best
        if not np.any(take):
            continue
        # rotate the shortest way onto the target so the band keeps its internal hue variation
        delta = ((b["dst"] - b["centre"] + 180.0) % 360.0) - 180.0
        cand_h = (h + (delta / 360.0) * w) % 1.0
        cand_s = np.clip(s * (1.0 + (b["sat"] - 1.0) * w), 0.0, 1.0)
        cand_v = np.clip(v * (1.0 + (b["val"] - 1.0) * w), 0.0, 1.0)
        h_out[take], s_out[take], v_out[take] = cand_h[take], cand_s[take], cand_v[take]
        best = np.maximum(best, w)
    return h_out, s_out, v_out


def recolour(path_in, path_out, tint, hue=0.0, sat=1.0, val=1.0, bands=None):
    im = Image.open(path_in)
    has_alpha = im.mode in ("RGBA", "LA") or "transparency" in im.info
    im = im.convert("RGBA" if has_alpha else "RGB")
    a = np.asarray(im).astype(np.float32) / 255.0
    rgb = a[..., :3]

    h, s, v = rgb_to_hsv(rgb)
    if bands:
        h, s, v = apply_bands(h, s, v, bands)
    else:
        h = (h + hue / 360.0) % 1.0
        s = np.clip(s * sat, 0.0, 1.0)
        v = np.clip(v * val, 0.0, 1.0)
    out = hsv_to_rgb(h, s, v)

    # hue rotation cannot touch a near-grey pixel, and Unity-chan's uniform is mostly white —
    # tint those toward the palette so the recolour reads on the large neutral areas too.
    colour, strength = tint
    if strength > 0.0:
        neutral = np.clip(1.0 - s / 0.35, 0.0, 1.0)[..., None]
        out = out * (1.0 - neutral * strength) + out * np.array(colour, np.float32) * (neutral * strength)

    out = np.clip(out, 0.0, 1.0)
    if has_alpha:
        res = np.concatenate([out, a[..., 3:4]], axis=-1)
    else:
        res = out
    img = Image.fromarray((res * 255.0 + 0.5).astype(np.uint8), "RGBA" if has_alpha else "RGB")
    os.makedirs(os.path.dirname(path_out), exist_ok=True)
    img.save(path_out)
    return img.size, img.mode


wanted = [int(x) for x in sys.argv[1:]] or sorted(MEMBERS)
for m in wanted:
    spec = MEMBERS[m]
    for fname, role in FILES:
        src = os.path.join(SRC, fname)
        dst = os.path.join(DST, spec["key"], os.path.splitext(fname)[0] + ".png")
        size, mode = recolour(src, dst, **spec[role])
        print("m%d %-9s %-18s -> %s  %s %s" % (m, spec["key"], fname, os.path.relpath(dst, ROOT), size, mode))
print("DONE")
