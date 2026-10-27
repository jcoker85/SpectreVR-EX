"""Export Spectre VR's 3D shapes (CENTRAL.MAT 'shap' resources) to JSON for the Unity scene builder.

shap format (big-endian): int16 vertexCount-1, then vertices as three 16.16 fixed values (x, y up, z
forward); int16 faceCount-1, then per face int16 vertCount-1, 1-based int16 indices, int16 colour.

    python export_shapes.py <CENTRAL.MAT> <out.json>
"""
import sys
import json
import struct


def resources(path):
    d = open(path, "rb").read()
    assert d[:4] == b"SSWF"
    n = struct.unpack_from(">H", d, 4)[0]
    for i in range(n):
        t, rid, off, size = struct.unpack_from(">4sHII", d, 6 + i * 14)
        yield t.decode("latin1"), rid, d[off:off + size]


def parse_shap(b):
    o = 0

    def h():
        nonlocal o
        v = struct.unpack_from(">h", b, o)[0]
        o += 2
        return v

    def fx():
        nonlocal o
        v = struct.unpack_from(">i", b, o)[0]
        o += 4
        return v / 65536.0

    verts = [[fx(), fx(), fx()] for _ in range(h() + 1)]
    faces = []
    for _ in range(h() + 1):
        k = h() + 1
        idx = [h() - 1 for _ in range(k)]
        faces.append({"idx": idx, "col": h()})
    return verts, faces


def main():
    out = {"shapes": []}
    for t, rid, b in resources(sys.argv[1]):
        if t != "shap":
            continue
        verts, faces = parse_shap(b)
        out["shapes"].append({"id": rid, "verts": [c for v in verts for c in v], "faces": faces})
    out["shapes"].sort(key=lambda s: s["id"])
    with open(sys.argv[2], "w") as f:
        json.dump(out, f, separators=(",", ":"))
    print(len(out["shapes"]), "shapes ->", sys.argv[2])


if __name__ == "__main__":
    main()
