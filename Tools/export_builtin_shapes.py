"""Export the 3D shapes built into SVRWIN.EXE (shape ids under 1000) to JSON for the game.

SVRWIN.EXE fills its built-in object templates (_BIG_BOX, _PYRAMID, _WINDMILL, ...) at startup in
_BUILD_OBJECTS and _SET_FACES: long runs of constant stores, setting each template's point count
and pointer (+0x40/+0x42) and face count and pointer (+0x52/+0x54), then each 0x35-byte face's
vertex count and index-list pointer (+0/+2). The points themselves are initialised data in the
EXE's data segments (three 32-bit ints each: x forward, y side, z up, in world units).

This replays those stores over the EXE's segments and reads the templates back. The stores are only
pointers and counts: they were taken once from the Ghidra decompile into BuiltinShapesLayout.json,
so the import needs nothing but the player's own SVRWIN.EXE, where all the geometry is read from. The id -> template table is _OT_TO_SHAPE (DS:0x9B8): 10-byte entries of shape
id, object type, shap resource id, far pointer to the template (pointer filled by relocations).

    python export_builtin_shapes.py --make-layout <SVRWIN.EXE> <SVRWIN.EXE.c> <symbols.tsv>
    python export_builtin_shapes.py <SVRWIN.EXE> <out.json>       (a reference export, using the layout)

The game's own import is Assets/Scripts/Setup/BuiltinShapeExporter.cs, a C# port of the export here that
gives the same JSON byte for byte; this script makes the layout it reads (Assets/Resources/BuiltinShapesLayout.json).
"""
import hashlib
import json
import os
import re
import struct
import sys

LAYOUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Assets", "Resources", "BuiltinShapesLayout.json")

TEMPLATES = ["BIG_BOX", "PYRAMID", "WALL", "WINDMILL", "WINDMILL_FANS", "COLUMN", "FUEL", "PLAYER_TANK",
             "TANK", "SUPERTANK", "SHOT", "BASE", "FLAG", "ICE", "GOO", "TURRET"]


class Exe:
    def __init__(self, path):
        d = open(path, "rb").read()
        ne = struct.unpack_from("<I", d, 0x3C)[0]
        assert d[ne:ne + 2] == b"NE"
        count = struct.unpack_from("<H", d, ne + 0x1C)[0]
        table = ne + struct.unpack_from("<H", d, ne + 0x22)[0]
        align = 1 << struct.unpack_from("<H", d, ne + 0x32)[0]
        self.mem, self.relocs = {}, {}
        for i in range(count):
            off, length, flags, minalloc = struct.unpack_from("<HHHH", d, table + i * 8)
            length = length or 0x10000
            sel = 0x1000 + i * 8                      # Ghidra's selector for NE segment i + 1
            buf = bytearray(max(length, minalloc, 0x100))
            if off:
                buf[:length] = d[off * align:off * align + length]
                if flags & 0x100:                     # relocation records follow the segment data
                    r = off * align + length
                    relocs = {}
                    for k in range(struct.unpack_from("<H", d, r)[0]):
                        src, kind, at, seg, _, target = struct.unpack_from("<BBHBBH", d, r + 2 + k * 8)
                        if src == 3 and kind == 0:    # far pointer to an internal segment
                            relocs[at] = (0x1000 + (seg - 1) * 8, target)
                    self.relocs[sel] = relocs
            self.mem[sel] = buf

    def w16(self, sel, o, v): struct.pack_into("<H", self.mem[sel], o, v & 0xFFFF)
    def w32(self, sel, o, v): struct.pack_into("<I", self.mem[sel], o, v & 0xFFFFFFFF)
    def u16(self, sel, o): return struct.unpack_from("<H", self.mem[sel], o)[0]
    def s16(self, sel, o): return struct.unpack_from("<h", self.mem[sel], o)[0]
    def s32(self, sel, o): return struct.unpack_from("<i", self.mem[sel], o)[0]


def load_symbols(path):
    sym = {}
    for line in open(path):
        p = line.rstrip("\n").split("\t")
        if len(p) >= 5:
            sym[p[4]] = (0x1000 + (int(p[0], 16) - 1) * 8, int(p[1], 16))
    return sym


def replay(exe, sym, lines, name):
    """Apply a decompiled function's constant stores to memory; other statements are ignored."""
    def addr(token):
        m = re.fullmatch(r"DAT_([0-9a-f]{4})_([0-9a-f]{4})", token) or re.fullmatch(r"p?u?Ram([0-9a-f]{4})([0-9a-f]{4})", token)
        if m:
            return int(m[1], 16), int(m[2], 16)
        return sym.get(token)

    start = next(i for i, l in enumerate(lines) if l.startswith("// ==== " + name + " @"))
    for line in lines[start + 1:]:
        if line.startswith("// ===="):
            break
        m = re.fullmatch(r"\s*(\w+) = (.+);", line)
        if not m or not addr(m[1]):
            continue
        a = addr(m[1])
        rhs = re.sub(r"^\((?:undefined|char|int)\s*\*\)", "", m[2].strip())
        ref = re.fullmatch(r"&(\w+)", rhs)
        if ref:
            if addr(ref[1]):
                t = addr(ref[1])
                exe.w32(*a, (t[0] << 16) | t[1])
            continue
        # Ghidra shows some constants as "string + offset"; the value is just the address
        s = re.fullmatch(r"s_\w+_[0-9a-f]{4}_([0-9a-f]{4})(?: \+ (\w+))?", rhs)
        if s:
            exe.w16(*a, int(s[1], 16) + (int(s[2], 0) if s[2] else 0))
            continue
        try:
            v = int(rhs, 0)
        except ValueError:
            continue                                  # computed values (e.g. the pyramid's size tweak)
        (exe.w32 if v > 0xFFFF or v < -0x8000 else exe.w16)(*a, v)


def make_layout(exe_path, c_path, sym_path):
    """Record the stores and the addresses the export needs; the geometry stays in the EXE."""
    exe = Exe(exe_path)
    sym = load_symbols(sym_path)
    lines = open(c_path, encoding="utf-8").read().split("\n")
    writes = []
    w16, w32 = exe.w16, exe.w32
    exe.w16 = lambda sel, o, v: (writes.append([sel, o, 2, v]), w16(sel, o, v))
    exe.w32 = lambda sel, o, v: (writes.append([sel, o, 4, v]), w32(sel, o, v))
    replay(exe, sym, lines, "_BUILD_OBJECTS")
    replay(exe, sym, lines, "_SET_FACES")
    names = ["_" + n for n in TEMPLATES] + ["_OT_TO_SHAPE", "_OBJ_COLORS", "_SKY_COLORS", "_TEXTURE_FILE_NAMES"]
    with open(LAYOUT, "w") as f:
        json.dump({"sha256": hashlib.sha256(open(exe_path, "rb").read()).hexdigest(),
                   "symbols": {n: sym[n] for n in names}, "writes": writes}, f, separators=(",", ":"))
    print(len(writes), "stores ->", LAYOUT)


def colour_table(exe, at):
    """30 levels of 16-bit RGB (three words each), as 8-bit values."""
    return [exe.u16(at[0], at[1] + i * 2) >> 8 for i in range(90)]


def texture_files(exe, at):
    """The file behind each texture face colour, 6000-6015: far pointers, filled by relocations."""
    names = []
    for i in range(16):
        target = exe.relocs.get(at[0], {}).get(at[1] + i * 4)
        name = bytearray()
        while target and len(name) < 32:
            c = exe.u16(target[0], target[1] + len(name)) & 0xFF
            if c == 0:
                break
            name.append(c)
        names.append(name.decode("latin1").split("\\")[-1].upper())
    return names


def main():
    if sys.argv[1] == "--make-layout":
        make_layout(*sys.argv[2:5])
        return
    exe_path, out_path = sys.argv[1:3]
    layout = json.load(open(LAYOUT))
    if hashlib.sha256(open(exe_path, "rb").read()).hexdigest() != layout["sha256"]:
        sys.exit("This SVRWIN.EXE isn't the release the built-in shape layout was made from.")
    exe = Exe(exe_path)
    sym = {n: tuple(v) for n, v in layout["symbols"].items()}
    for sel, o, size, v in layout["writes"]:
        (exe.w32 if size == 4 else exe.w16)(sel, o, v)

    shapes, at = [], {}
    for name in TEMPLATES:
        sel, o = sym["_" + name]
        at[(sel, o)] = name
        npts, po, ps = exe.u16(sel, o + 0x40), exe.u16(sel, o + 0x42), exe.u16(sel, o + 0x44)
        nfaces, fo, fs = exe.u16(sel, o + 0x52), exe.u16(sel, o + 0x54), exe.u16(sel, o + 0x56)
        verts = [exe.s32(ps, po + i * 12 + k * 4) for i in range(npts) for k in range(3)]
        faces = []
        for f in range(nfaces):
            b = fo + f * 0x35
            n, io, iseg = exe.u16(fs, b), exe.u16(fs, b + 2), exe.u16(fs, b + 4)
            faces.append({"idx": [exe.u16(iseg, io + k * 2) for k in range(n)], "col": exe.u16(fs, b + 0x2B)})
        shapes.append({"name": name, "verts": verts, "faces": faces})

    ds = sym["_OT_TO_SHAPE"][0]
    table, o = [], sym["_OT_TO_SHAPE"][1]
    while exe.s16(ds, o + 2) >= 0:
        target = exe.relocs.get(ds, {}).get(o + 6)
        table.append({"id": exe.s16(ds, o), "type": exe.s16(ds, o + 2), "shap": exe.s16(ds, o + 4),
                      "shape": at.get(target, "") if target else ""})
        o += 10

    with open(out_path, "w") as f:
        json.dump({"table": table, "shapes": shapes, "typeColors": [exe.u16(ds, 0x8FC + i * 2) & 0xFF for i in range(64)],
                   "objColors": colour_table(exe, sym["_OBJ_COLORS"]), "skyColors": colour_table(exe, sym["_SKY_COLORS"]),
                   "textures": texture_files(exe, sym["_TEXTURE_FILE_NAMES"])},
                  f, separators=(",", ":"))
    print(len(shapes), "built-in shapes,", len(table), "table entries ->", out_path)


if __name__ == "__main__":
    main()
