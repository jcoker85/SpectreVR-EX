"""Dump the symbols from the Borland Turbo Debugger (TDS 3.x) info appended to SVRWIN.EXE.

The debug block sits behind an 'NB02' wrapper (16-byte header at the start, 8-byte trailer giving
its distance from the end of the file); the Borland header (magic 0x52FB) follows it.

Usage: python -I tds_symbols.py SVRWIN.EXE out.tsv
Writes "segment<TAB>offset<TAB>type<TAB>class<TAB>name", sorted by address.
"""
import struct
import sys

HEADER = struct.Struct("<HHIHHHHHHHHHHHHIIBHHBH")   # 48 bytes


def main(path, out):
    d = open(path, "rb").read()
    sig, back = struct.unpack("<4sI", d[-8:])
    base = len(d) - back
    assert d[base:base + 4] == sig, "NB wrapper mismatch"
    p = base + 16
    h = HEADER.unpack(d[p:p + HEADER.size])
    (magic, version, names_size, names_count, types_count, members_count, symbols_count,
     globals_count, modules_count, locals_count, scopes_count, lines_count, sources_count,
     segments_count, correlations_count, image_size, hook, flags, string_seg, data_count,
     filler, ext_size) = h
    assert magic == 0x52FB, hex(magic)
    print(f"TDS v{version >> 8}.{version & 0xff:02x}: {symbols_count} symbols, {names_count} names "
          f"({names_size} bytes), {modules_count} modules, {lines_count} line blocks", file=sys.stderr)

    # Name pool: the last names_size bytes before the NB trailer, NUL-separated, 1-based
    pool_end = len(d) - 8
    pool = d[pool_end - names_size:pool_end]
    names = pool.split(b"\0")
    if len(names) < names_count:
        sys.exit(f"name pool split gave {len(names)} names, expected {names_count}")
    names = [n.decode("latin-1") for n in names]

    p += HEADER.size + ext_size
    rows = []
    for _ in range(symbols_count):
        idx, typ, off, seg, cls = struct.unpack("<HHHHB", d[p:p + 9])
        p += 9
        name = names[idx - 1] if 0 < idx <= len(names) else f"?name{idx}"
        rows.append((seg, off, typ, cls, name))
    rows.sort()
    with open(out, "w") as f:
        for seg, off, typ, cls, name in rows:
            f.write(f"{seg:04x}\t{off:04x}\t{typ:04x}\t{cls:02x}\t{name}\n")
    print(len(rows), "symbols written", file=sys.stderr)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
