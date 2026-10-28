"""Dump the public symbols from the CodeView (NB02) debug info appended to a 16-bit NE executable.

Usage: python -I cv_publics.py SVRWIN.EXE out.tsv
Writes "segment<TAB>offset<TAB>type<TAB>name" lines; segment is the NE logical segment number (1-based).
"""
import struct
import sys

SUBSECTIONS = {0x101: "modules", 0x102: "publics", 0x103: "types", 0x104: "symbols",
               0x105: "srclines", 0x106: "libraries", 0x107: "imports", 0x108: "compacted",
               0x109: "srclnseg"}


def main(path, out):
    d = open(path, "rb").read()
    sig, back = struct.unpack("<4sI", d[-8:])
    if sig[:2] != b"NB":
        sys.exit("no CodeView signature at end of file")
    base = len(d) - back
    sig2, lfo_dir = struct.unpack("<4sI", d[base:base + 8])
    print("CodeView", sig.decode(), "base", hex(base), "dir", hex(lfo_dir), file=sys.stderr)

    p = base + lfo_dir
    (count,) = struct.unpack("<H", d[p:p + 2])
    p += 2
    entries = []
    for _ in range(count):
        sst, imod, lfo, cb = struct.unpack("<HHIH", d[p:p + 10])
        entries.append((sst, imod, lfo, cb))
        p += 10
    kinds = {}
    for e in entries:
        kinds[SUBSECTIONS.get(e[0], hex(e[0]))] = kinds.get(SUBSECTIONS.get(e[0], hex(e[0])), 0) + 1
    print("subsections:", kinds, file=sys.stderr)

    rows = []
    for sst, imod, lfo, cb in entries:
        if sst != 0x102:
            continue
        q, end = base + lfo, base + lfo + cb
        while q < end:
            off, seg, typ, n = struct.unpack("<HHHB", d[q:q + 7])
            name = d[q + 7:q + 7 + n].decode("latin-1")
            rows.append((seg, off, typ, name))
            q += 7 + n
    rows.sort()
    with open(out, "w") as f:
        for seg, off, typ, name in rows:
            f.write(f"{seg}\t{off:04x}\t{typ:04x}\t{name}\n")
    print(len(rows), "publics", file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
