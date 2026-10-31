"""Decode the player's QuickTime movies (Apple Video, Animation, Cinepak) for the remake.

Each movie becomes <name>.frames: "SPMV", then little-endian width, height, frame count, and for each frame
its time in milliseconds and the size of its deflated RGB24 pixels, followed by those pixels, top row first.
Sound, where there is any, becomes <name>.wav.

    python qtmovie.py <movie.mov> <output folder>
"""
import os
import struct
import sys
import wave
import zlib


def atoms(d, start, end):
    o = start
    while o + 8 <= end:
        size, kind = struct.unpack_from(">I4s", d, o)
        if size < 8:
            break
        yield kind, o + 8, o + size
        o += size


def find(d, start, end, kind):
    for k, a, b in atoms(d, start, end):
        if k == kind:
            return a, b
    return None


class Track:
    def __init__(self, d, a, b):
        mdia = find(d, a, b, b"mdia")
        mdhd = find(d, *mdia, b"mdhd")
        self.scale = struct.unpack_from(">I", d, mdhd[0] + 12)[0]
        hdlr = find(d, *mdia, b"hdlr")
        self.kind = d[hdlr[0] + 8:hdlr[0] + 12]
        stbl = find(d, *find(d, *mdia, b"minf"), b"stbl")
        s = find(d, *stbl, b"stsd")
        self.desc = s[0] + 8
        self.codec = d[self.desc + 4:self.desc + 8]
        t = find(d, *stbl, b"stts")
        n = struct.unpack_from(">I", d, t[0] + 4)[0]
        self.times = []
        for i in range(n):
            count, delta = struct.unpack_from(">II", d, t[0] + 8 + i * 8)
            self.times += [delta] * count
        c = find(d, *stbl, b"stsc")
        n = struct.unpack_from(">I", d, c[0] + 4)[0]
        self.stsc = [struct.unpack_from(">III", d, c[0] + 8 + i * 12) for i in range(n)]
        z = find(d, *stbl, b"stsz")
        self.fixed, n = struct.unpack_from(">II", d, z[0] + 4)
        self.sizes = [self.fixed] * n if self.fixed else list(struct.unpack_from(f">{n}I", d, z[0] + 12))
        o = find(d, *stbl, b"stco")
        n = struct.unpack_from(">I", d, o[0] + 4)[0]
        self.chunks = list(struct.unpack_from(f">{n}I", d, o[0] + 8))

    def samples(self):
        """(offset, size) of each sample"""
        out, sample = [], 0
        for i, chunk in enumerate(self.chunks):
            per = next(c[1] for c in reversed(self.stsc) if c[0] <= i + 1)
            o = chunk
            for _ in range(per):
                if sample >= len(self.sizes):
                    break
                out.append((o, self.sizes[sample]))
                o += self.sizes[sample]
                sample += 1
        return out


def rgb555(v):
    r, g, b = (v >> 10) & 31, (v >> 5) & 31, v & 31
    return (r << 3 | r >> 2, g << 3 | g >> 2, b << 3 | b >> 2)


def rpza(data, frame, w, h):
    """Apple Video: 4x4 blocks left to right, top to bottom; skipped blocks keep the last frame"""
    pos = 4
    bx, by = 0, 0
    wide = (w + 3) // 4

    def advance():
        nonlocal bx, by
        bx += 1
        if bx == wide:
            bx, by = 0, by + 1

    def put(px, py, c):
        x, y = bx * 4 + px, by * 4 + py
        if x < w and y < h:
            frame[y * w + x] = c

    def be16():
        nonlocal pos
        v = data[pos] << 8 | data[pos + 1]
        pos += 2
        return v

    a = 0
    while pos < len(data) and by * 4 < h:
        op = data[pos]
        pos += 1
        n = (op & 0x1F) + 1
        if not op & 0x80:
            a = op << 8 | data[pos]
            pos += 1
            op = 0
            if pos < len(data) and data[pos] & 0x80:
                op, n = 0x20, 1
        kind = op & 0xE0
        if kind == 0x80:
            for _ in range(n):
                advance()
        elif kind == 0xA0:
            c = rgb555(be16())
            for _ in range(n):
                for py in range(4):
                    for px in range(4):
                        put(px, py, c)
                advance()
        elif kind in (0xC0, 0x20):
            if kind == 0xC0:
                a = be16()
            b = be16()
            mix = [b, 0, 0, a]
            for shift in (10, 5, 0):
                ta, tb = (a >> shift) & 31, (b >> shift) & 31
                mix[1] |= ((11 * ta + 21 * tb) >> 5) << shift
                mix[2] |= ((21 * ta + 11 * tb) >> 5) << shift
            colours = [rgb555(c) for c in mix]
            for _ in range(n):
                for py in range(4):
                    bits = data[pos]
                    pos += 1
                    for px in range(4):
                        put(px, py, colours[(bits >> (2 * (3 - px))) & 3])
                advance()
        else:
            for py in range(4):
                for px in range(4):
                    if px or py:
                        a = be16()
                    put(px, py, rgb555(a))
            advance()


def animation(data, frame, w, h, depth, palette):
    """QuickTime Animation: runs and skips within each line"""
    if len(data) < 8:
        return
    pos = 4
    header = struct.unpack_from(">H", data, pos)[0]
    pos += 2
    first, lines = 0, h
    if header & 8:
        first, lines = struct.unpack_from(">HxxHxx", data, pos)
        pos += 8
    size = {8: 1, 16: 2, 24: 3, 32: 4}[depth]
    group = 4 if depth == 8 else 1   # 8-bit pixels come in fours

    def pixels(raw):
        if depth == 8:
            return [palette[v] for v in raw]
        out = []
        for i in range(0, len(raw), size):
            if depth == 16:
                out.append(rgb555(raw[i] << 8 | raw[i + 1]))
            elif depth == 24:
                out.append((raw[i], raw[i + 1], raw[i + 2]))
            else:
                out.append((raw[i + 1], raw[i + 2], raw[i + 3]))
        return out

    y = first
    while lines > 0 and pos < len(data):
        lines -= 1
        x = (data[pos] - 1) * group
        pos += 1
        while pos < len(data):
            code = data[pos]
            pos += 1
            if code == 0xFF:
                break
            if code == 0:
                x += (data[pos] - 1) * group
                pos += 1
            elif code < 0x80:
                n = code * size * group
                for c in pixels(data[pos:pos + n]):
                    if x < w and y < h:
                        frame[y * w + x] = c
                    x += 1
                pos += n
            else:
                n = (256 - code)
                run = pixels(data[pos:pos + size * group])
                pos += size * group
                for _ in range(n):
                    for c in run:
                        if x < w and y < h:
                            frame[y * w + x] = c
                        x += 1
        y += 1


class Cinepak:
    def __init__(self):
        self.v1 = [[None] * 256 for _ in range(32)]
        self.v4 = [[None] * 256 for _ in range(32)]

    @staticmethod
    def clamp(v):
        return 0 if v < 0 else 255 if v > 255 else v

    def entry(self, ys, u, v):
        out = []
        for y in ys:
            r = self.clamp(y + 2 * v)
            g = self.clamp(y - (u >> 1) - v)
            b = self.clamp(y + 2 * u)
            out.append((r, g, b))
        return out

    def codebook(self, book, d, o, end, kind):
        grey = kind & 4
        selective = kind & 1
        i = 0
        while o < end and i < 256:
            flags = 0xFFFFFFFF
            count = 32
            if selective:
                flags = struct.unpack_from(">I", d, o)[0]
                o += 4
            for b in range(count):
                if i >= 256:
                    break
                if flags & (1 << (31 - b)):
                    ys = d[o:o + 4]
                    o += 4
                    u = v = 0
                    if not grey:
                        u = struct.unpack_from("b", d, o)[0]
                        v = struct.unpack_from("b", d, o + 1)[0]
                        o += 2
                    book[i] = self.entry(ys, u, v)
                i += 1

    def decode(self, d, frame, w, h):
        flags = d[0]
        strips = struct.unpack_from(">H", d, 8)[0]
        o = 10
        y0 = 0
        for s in range(strips):
            sid, ssize = struct.unpack_from(">HH", d, o)
            top, left, bottom, right = struct.unpack_from(">HHHH", d, o + 4)
            if s and not flags & 1:
                self.v1[s], self.v4[s] = list(self.v1[s - 1]), list(self.v4[s - 1])
            height = bottom - top if top == 0 or bottom > top else bottom
            y1 = y0 + height
            p = o + 12
            end = o + ssize
            while p + 4 <= end:
                cid, csize = struct.unpack_from(">HH", d, p)
                body, stop = p + 4, p + csize
                if cid & 0xFF00 == 0x2000 or cid & 0xFF00 == 0x2200:
                    book = self.v4[s] if cid < 0x2200 else self.v1[s]
                    self.codebook(book, d, body, stop, cid & 0xFF)
                elif cid in (0x3000, 0x3100, 0x3200):
                    self.vectors(d, body, stop, cid, frame, w, y0, y1, s)
                p = stop
            y0 = y1
            o += ssize

    def vectors(self, d, o, stop, cid, frame, w, y0, y1, s):
        flag, bits = 0, 0

        def next_flag():
            nonlocal flag, bits, o
            if bits == 0:
                flag = struct.unpack_from(">I", d, o)[0]
                o += 4
                bits = 32
            bits -= 1
            return (flag >> bits) & 1

        def put(x, y, c):
            if c and x < w and y * w + x < len(frame):
                frame[y * w + x] = c

        for y in range(y0, y1, 4):
            for x in range(0, w, 4):
                if o >= stop and bits == 0:
                    return
                if cid != 0x3000 and cid != 0x3200 and not next_flag():
                    continue    # intra frames code every block; inter frames may keep it
                if cid == 0x3200:
                    use4 = False
                else:
                    use4 = bool(next_flag())
                if use4:
                    a, b, c, e = d[o], d[o + 1], d[o + 2], d[o + 3]
                    o += 4
                    for i, n in enumerate((a, b, c, e)):
                        t = self.v4[s][n] or [None] * 4
                        px, py = (i & 1) * 2, (i >> 1) * 2
                        put(x + px, y + py, t[0])
                        put(x + px + 1, y + py, t[1])
                        put(x + px, y + py + 1, t[2])
                        put(x + px + 1, y + py + 1, t[3])
                else:
                    t = self.v1[s][d[o]] or [None] * 4
                    o += 1
                    for py in range(2):
                        for px in range(2):
                            c = t[py * 2 + px]
                            for ax in range(2):
                                for ay in range(2):
                                    put(x + px * 2 + ax, y + py * 2 + ay, c)


def palette_for(d, track):
    """A 256-colour description's own colours, else the Mac default ramp"""
    a = track.desc
    size = struct.unpack_from(">I", d, a)[0]
    colours = [(i, i, i) for i in range(256)]
    if struct.unpack_from(">h", d, a + 84)[0] == 0 and size > 86 + 8:
        start, count = struct.unpack_from(">IxxH", d, a + 86)
        for i in range(count + 1):
            r, g, b = d[a + 94 + i * 8 + 2], d[a + 94 + i * 8 + 4], d[a + 94 + i * 8 + 6]
            if start + i < 256:
                colours[start + i] = (r, g, b)
    return colours


def convert(path, out):
    d = open(path, "rb").read()
    moov = find(d, 0, len(d), b"moov")
    tracks = [Track(d, a, b) for k, a, b in atoms(d, *moov) if k == b"trak"]
    video = next(t for t in tracks if t.kind == b"vide")
    w, h = struct.unpack_from(">HH", d, video.desc + 32)
    depth = struct.unpack_from(">H", d, video.desc + 82)[0]
    name = os.path.splitext(os.path.basename(path))[0].upper()
    print(name, video.codec.decode(), f"{w}x{h}", depth, "bit,", len(video.sizes), "frames")
    frame = [(0, 0, 0)] * (w * h)
    cinepak = Cinepak()
    palette = palette_for(d, video)
    pack, when, ms = [], [], 0.0
    for i, (o, size) in enumerate(video.samples()):
        data = d[o:o + size]
        if video.codec == b"rpza":
            rpza(data, frame, w, h)
        elif video.codec == b"rle ":
            animation(data, frame, w, h, depth, palette)
        elif video.codec == b"cvid":
            cinepak.decode(data, frame, w, h)
        else:
            raise SystemExit(f"{name}: no decoder for {video.codec}")
        pack.append(zlib.compress(bytes(v for p in frame for v in p), 6))
        when.append(int(ms))
        ms += video.times[min(i, len(video.times) - 1)] * 1000.0 / video.scale
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, name + ".frames"), "wb") as f:
        f.write(b"SPMV" + struct.pack("<IIII", w, h, len(pack), int(ms)))
        for t, p in zip(when, pack):
            f.write(struct.pack("<II", t, len(p)))
        for p in pack:
            f.write(p)
    for t in tracks:
        if t.kind == b"soun":
            sound(d, t, os.path.join(out, name + ".wav"))


def sound(d, t, path):
    a = t.desc
    channels, bits = struct.unpack_from(">HH", d, a + 24)
    rate = struct.unpack_from(">I", d, a + 32)[0] >> 16
    raw = bytearray()
    for chunk, i in zip(t.chunks, range(len(t.chunks))):
        pass
    per = 0
    total = len(t.sizes) * (t.fixed or 1)
    for i, chunk in enumerate(t.chunks):
        per = next(c[1] for c in reversed(t.stsc) if c[0] <= i + 1)
        raw += d[chunk:chunk + per * (bits // 8) * channels]
    print("  sound", t.codec.decode(), rate, "Hz", bits, "bit", channels, "ch", len(raw), "bytes", total)
    with wave.open(path, "wb") as w:
        w.setnchannels(channels)
        w.setsampwidth(bits // 8)
        w.setframerate(rate)
        w.writeframes(bytes(raw) if bits != 16 else bytes(raw[i ^ 1] for i in range(len(raw))))


if __name__ == "__main__":
    convert(sys.argv[1], sys.argv[2])
