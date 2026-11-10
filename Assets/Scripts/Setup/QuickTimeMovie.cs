using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SpectreEX.Setup
{
    // Decodes the player's QuickTime movies (Apple Video, Animation, Cinepak) for the remake. Plain C#
    // with no Unity types, so the importer can run on a worker thread.
    //
    // Each movie becomes <name>.frames: "SPMV", then little-endian width, height, frame count and length
    // in milliseconds, then for each frame its time in milliseconds and the size of its zlib-packed RGB24
    // pixels, followed by those pixels, top row first (read by Matrix.MovieFile). Sound, where there is
    // any, becomes <name>.wav.
    public static class QuickTimeMovie
    {
        struct Rgb { public byte r, g, b; public bool set; public Rgb(int r, int g, int b) { this.r = (byte)r; this.g = (byte)g; this.b = (byte)b; set = true; } }

        static int U16(byte[] d, int o) => d[o] << 8 | d[o + 1];
        static short S16(byte[] d, int o) => (short)U16(d, o);
        static uint U32(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
        static string Kind(byte[] d, int o) => System.Text.Encoding.ASCII.GetString(d, o, 4);

        static IEnumerable<(string kind, int start, int end)> Atoms(byte[] d, int start, int end)
        {
            int o = start;
            while (o + 8 <= end)
            {
                int size = (int)U32(d, o);
                if (size < 8) yield break;
                yield return (Kind(d, o + 4), o + 8, o + size);
                o += size;
            }
        }

        static (int start, int end) Find(byte[] d, int start, int end, string kind)
        {
            foreach (var a in Atoms(d, start, end))
                if (a.kind == kind) return (a.start, a.end);
            throw new InvalidDataException("QuickTime atom missing: " + kind);
        }

        class Track
        {
            public int scale, desc;
            public string kind, codec;
            public readonly List<int> times = new List<int>();
            public readonly List<(int first, int per)> stsc = new List<(int, int)>();
            public int fixedSize;
            public readonly List<int> sizes = new List<int>();
            public readonly List<int> chunks = new List<int>();

            public Track(byte[] d, int a, int b)
            {
                var mdia = Find(d, a, b, "mdia");
                var mdhd = Find(d, mdia.start, mdia.end, "mdhd");
                scale = (int)U32(d, mdhd.start + 12);
                var hdlr = Find(d, mdia.start, mdia.end, "hdlr");
                kind = Kind(d, hdlr.start + 8);
                var minf = Find(d, mdia.start, mdia.end, "minf");
                var stbl = Find(d, minf.start, minf.end, "stbl");
                desc = Find(d, stbl.start, stbl.end, "stsd").start + 8;
                codec = Kind(d, desc + 4);
                var t = Find(d, stbl.start, stbl.end, "stts");
                int n = (int)U32(d, t.start + 4);
                for (int i = 0; i < n; i++)
                {
                    int count = (int)U32(d, t.start + 8 + i * 8), delta = (int)U32(d, t.start + 12 + i * 8);
                    for (int k = 0; k < count; k++) times.Add(delta);
                }
                var c = Find(d, stbl.start, stbl.end, "stsc");
                n = (int)U32(d, c.start + 4);
                for (int i = 0; i < n; i++) stsc.Add(((int)U32(d, c.start + 8 + i * 12), (int)U32(d, c.start + 12 + i * 12)));
                var z = Find(d, stbl.start, stbl.end, "stsz");
                fixedSize = (int)U32(d, z.start + 4);
                n = (int)U32(d, z.start + 8);
                for (int i = 0; i < n; i++) sizes.Add(fixedSize != 0 ? fixedSize : (int)U32(d, z.start + 12 + i * 4));
                var o = Find(d, stbl.start, stbl.end, "stco");
                n = (int)U32(d, o.start + 4);
                for (int i = 0; i < n; i++) chunks.Add((int)U32(d, o.start + 8 + i * 4));
            }

            public int PerChunk(int chunk)
            {
                for (int k = stsc.Count - 1; k >= 0; k--)
                    if (stsc[k].first <= chunk + 1) return stsc[k].per;
                return 0;
            }

            // (offset, size) of each sample
            public List<(int offset, int size)> Samples()
            {
                var list = new List<(int, int)>();
                int sample = 0;
                for (int i = 0; i < chunks.Count; i++)
                {
                    int per = PerChunk(i), o = chunks[i];
                    for (int k = 0; k < per && sample < sizes.Count; k++)
                    {
                        list.Add((o, sizes[sample]));
                        o += sizes[sample];
                        sample++;
                    }
                }
                return list;
            }
        }

        static Rgb Rgb555(int v)
        {
            int r = (v >> 10) & 31, g = (v >> 5) & 31, b = v & 31;
            return new Rgb(r << 3 | r >> 2, g << 3 | g >> 2, b << 3 | b >> 2);
        }

        // Apple Video: 4x4 blocks left to right, top to bottom; skipped blocks keep the last frame
        static void Rpza(byte[] data, Rgb[] frame, int w, int h)
        {
            int pos = 4, bx = 0, by = 0, wide = (w + 3) / 4;
            void Advance() { if (++bx == wide) { bx = 0; by++; } }
            void Put(int px, int py, Rgb c)
            {
                int x = bx * 4 + px, y = by * 4 + py;
                if (x < w && y < h) frame[y * w + x] = c;
            }
            int Be16() { int v = data[pos] << 8 | data[pos + 1]; pos += 2; return v; }

            int a = 0;
            while (pos < data.Length && by * 4 < h)
            {
                int op = data[pos++], n = (op & 0x1F) + 1;
                if ((op & 0x80) == 0)
                {
                    a = op << 8 | data[pos++];
                    op = 0;
                    if (pos < data.Length && (data[pos] & 0x80) != 0) { op = 0x20; n = 1; }
                }
                int kind = op & 0xE0;
                if (kind == 0x80)
                {
                    for (int i = 0; i < n; i++) Advance();
                }
                else if (kind == 0xA0)
                {
                    var c = Rgb555(Be16());
                    for (int i = 0; i < n; i++)
                    {
                        for (int py = 0; py < 4; py++) for (int px = 0; px < 4; px++) Put(px, py, c);
                        Advance();
                    }
                }
                else if (kind == 0xC0 || kind == 0x20)
                {
                    if (kind == 0xC0) a = Be16();
                    int b = Be16();
                    var mix = new[] { b, 0, 0, a };
                    foreach (int shift in new[] { 10, 5, 0 })
                    {
                        int ta = (a >> shift) & 31, tb = (b >> shift) & 31;
                        mix[1] |= ((11 * ta + 21 * tb) >> 5) << shift;
                        mix[2] |= ((21 * ta + 11 * tb) >> 5) << shift;
                    }
                    var colours = Array.ConvertAll(mix, Rgb555);
                    for (int i = 0; i < n; i++)
                    {
                        for (int py = 0; py < 4; py++)
                        {
                            int bits = data[pos++];
                            for (int px = 0; px < 4; px++) Put(px, py, colours[(bits >> (2 * (3 - px))) & 3]);
                        }
                        Advance();
                    }
                }
                else
                {
                    for (int py = 0; py < 4; py++)
                    for (int px = 0; px < 4; px++)
                    {
                        if (px != 0 || py != 0) a = Be16();
                        Put(px, py, Rgb555(a));
                    }
                    Advance();
                }
            }
        }

        // QuickTime Animation: runs and skips within each line
        static void Animation(byte[] data, Rgb[] frame, int w, int h, int depth, Rgb[] palette)
        {
            if (data.Length < 8) return;
            int pos = 4;
            int header = U16(data, pos);
            pos += 2;
            int first = 0, lines = h;
            if ((header & 8) != 0)
            {
                first = U16(data, pos);
                lines = U16(data, pos + 4);
                pos += 8;
            }
            int size = depth == 8 ? 1 : depth == 16 ? 2 : depth == 24 ? 3 : depth == 32 ? 4 : throw new InvalidDataException("Animation depth " + depth);
            int group = depth == 8 ? 4 : 1;   // 8-bit pixels come in fours

            Rgb Pixel(int i)
            {
                switch (depth)
                {
                    case 8: return palette[data[i]];
                    case 16: return Rgb555(data[i] << 8 | data[i + 1]);
                    case 24: return new Rgb(data[i], data[i + 1], data[i + 2]);
                    default: return new Rgb(data[i + 1], data[i + 2], data[i + 3]);
                }
            }

            int y = first;
            while (lines > 0 && pos < data.Length)
            {
                lines--;
                int x = (data[pos++] - 1) * group;
                while (pos < data.Length)
                {
                    int code = data[pos++];
                    if (code == 0xFF) break;
                    if (code == 0)
                    {
                        x += (data[pos++] - 1) * group;
                    }
                    else if (code < 0x80)
                    {
                        int n = code * size * group;
                        for (int i = 0; i + size <= n && pos + i + size <= data.Length; i += size)
                        {
                            if (x < w && y < h) frame[y * w + x] = Pixel(pos + i);
                            x++;
                        }
                        pos += n;
                    }
                    else
                    {
                        int n = 256 - code, at = pos;
                        pos += size * group;
                        for (int k = 0; k < n; k++)
                        for (int i = 0; i < size * group; i += size)
                        {
                            if (x < w && y < h) frame[y * w + x] = Pixel(at + i);
                            x++;
                        }
                    }
                }
                y++;
            }
        }

        class Cinepak
        {
            readonly Rgb[][][] v1 = new Rgb[32][][], v4 = new Rgb[32][][];

            public Cinepak()
            {
                for (int i = 0; i < 32; i++) { v1[i] = new Rgb[256][]; v4[i] = new Rgb[256][]; }
            }

            static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

            static Rgb[] Entry(byte[] d, int o, int u, int v)
            {
                var out4 = new Rgb[4];
                for (int i = 0; i < 4; i++)
                {
                    int y = d[o + i];
                    out4[i] = new Rgb(Clamp(y + 2 * v), Clamp(y - (u >> 1) - v), Clamp(y + 2 * u));
                }
                return out4;
            }

            static void Codebook(Rgb[][] book, byte[] d, int o, int end, int kind)
            {
                bool grey = (kind & 4) != 0, selective = (kind & 1) != 0;
                int i = 0;
                while (o < end && i < 256)
                {
                    uint flags = 0xFFFFFFFF;
                    if (selective) { flags = U32(d, o); o += 4; }
                    for (int b = 0; b < 32 && i < 256; b++, i++)
                    {
                        if ((flags & (1u << (31 - b))) == 0) continue;
                        int ys = o;
                        o += 4;
                        int u = 0, v = 0;
                        if (!grey)
                        {
                            u = (sbyte)d[o];
                            v = (sbyte)d[o + 1];
                            o += 2;
                        }
                        book[i] = Entry(d, ys, u, v);
                    }
                }
            }

            public void Decode(byte[] d, Rgb[] frame, int w, int h)
            {
                int flags = d[0], strips = U16(d, 8), o = 10, y0 = 0;
                for (int s = 0; s < strips; s++)
                {
                    int ssize = U16(d, o + 2);
                    int top = U16(d, o + 4), bottom = U16(d, o + 8);
                    if (s != 0 && (flags & 1) == 0)
                    {
                        v1[s] = (Rgb[][])v1[s - 1].Clone();
                        v4[s] = (Rgb[][])v4[s - 1].Clone();
                    }
                    int height = top == 0 || bottom > top ? bottom - top : bottom;
                    int y1 = y0 + height, p = o + 12, end = o + ssize;
                    while (p + 4 <= end)
                    {
                        int cid = U16(d, p), csize = U16(d, p + 2);
                        int body = p + 4, stop = p + csize;
                        if ((cid & 0xFF00) == 0x2000 || (cid & 0xFF00) == 0x2200)
                            Codebook(cid < 0x2200 ? v4[s] : v1[s], d, body, stop, cid & 0xFF);
                        else if (cid == 0x3000 || cid == 0x3100 || cid == 0x3200)
                            Vectors(d, body, stop, cid, frame, w, y0, y1, s);
                        if (csize == 0) break;
                        p = stop;
                    }
                    y0 = y1;
                    o += ssize;
                }
            }

            void Vectors(byte[] d, int o, int stop, int cid, Rgb[] frame, int w, int y0, int y1, int s)
            {
                uint flag = 0;
                int bits = 0;
                int NextFlag()
                {
                    if (bits == 0) { flag = U32(d, o); o += 4; bits = 32; }
                    bits--;
                    return (int)((flag >> bits) & 1);
                }
                void Put(int x, int y, Rgb c)
                {
                    if (c.set && x < w && y * w + x < frame.Length) frame[y * w + x] = c;
                }
                var none = new Rgb[4];

                for (int y = y0; y < y1; y += 4)
                for (int x = 0; x < w; x += 4)
                {
                    if (o >= stop && bits == 0) return;
                    if (cid != 0x3000 && cid != 0x3200 && NextFlag() == 0) continue;   // inter frames may keep a block
                    bool use4 = cid != 0x3200 && NextFlag() != 0;
                    if (use4)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            var t = v4[s][d[o + i]] ?? none;
                            int px = (i & 1) * 2, py = (i >> 1) * 2;
                            Put(x + px, y + py, t[0]);
                            Put(x + px + 1, y + py, t[1]);
                            Put(x + px, y + py + 1, t[2]);
                            Put(x + px + 1, y + py + 1, t[3]);
                        }
                        o += 4;
                    }
                    else
                    {
                        var t = v1[s][d[o++]] ?? none;
                        for (int py = 0; py < 2; py++)
                        for (int px = 0; px < 2; px++)
                        {
                            var c = t[py * 2 + px];
                            for (int ax = 0; ax < 2; ax++)
                            for (int ay = 0; ay < 2; ay++)
                                Put(x + px * 2 + ax, y + py * 2 + ay, c);
                        }
                    }
                }
            }
        }

        // A 256-colour description's own colours, else the Mac default ramp
        static Rgb[] PaletteFor(byte[] d, Track track)
        {
            int a = track.desc;
            int size = (int)U32(d, a);
            var colours = new Rgb[256];
            for (int i = 0; i < 256; i++) colours[i] = new Rgb(i, i, i);
            if (S16(d, a + 84) == 0 && size > 86 + 8)
            {
                int start = (int)U32(d, a + 86), count = U16(d, a + 92);
                for (int i = 0; i <= count; i++)
                    if (start + i < 256)
                        colours[start + i] = new Rgb(d[a + 94 + i * 8 + 2], d[a + 94 + i * 8 + 4], d[a + 94 + i * 8 + 6]);
            }
            return colours;
        }

        // Writes <NAME>.frames (and <NAME>.wav if the movie has sound) into outFolder
        public static void Convert(string path, string outFolder)
        {
            byte[] d = File.ReadAllBytes(path);
            var moov = Find(d, 0, d.Length, "moov");
            var tracks = new List<Track>();
            foreach (var a in Atoms(d, moov.start, moov.end))
                if (a.kind == "trak") tracks.Add(new Track(d, a.start, a.end));
            var video = tracks.Find(t => t.kind == "vide") ?? throw new InvalidDataException("No video track in " + path);
            int w = U16(d, video.desc + 32), h = U16(d, video.desc + 34), depth = U16(d, video.desc + 82);
            string name = Path.GetFileNameWithoutExtension(path).ToUpperInvariant();

            var frame = new Rgb[w * h];
            for (int i = 0; i < frame.Length; i++) frame[i] = new Rgb(0, 0, 0);
            var cinepak = new Cinepak();
            var palette = PaletteFor(d, video);
            var packs = new List<byte[]>();
            var when = new List<int>();
            double ms = 0;
            var raw = new byte[w * h * 3];
            var samples = video.Samples();
            for (int i = 0; i < samples.Count; i++)
            {
                var data = new byte[samples[i].size];
                Buffer.BlockCopy(d, samples[i].offset, data, 0, data.Length);
                switch (video.codec)
                {
                    case "rpza": Rpza(data, frame, w, h); break;
                    case "rle ": Animation(data, frame, w, h, depth, palette); break;
                    case "cvid": cinepak.Decode(data, frame, w, h); break;
                    default: throw new InvalidDataException($"{name}: no decoder for {video.codec}");
                }
                for (int p = 0; p < frame.Length; p++)
                {
                    raw[p * 3] = frame[p].r;
                    raw[p * 3 + 1] = frame[p].g;
                    raw[p * 3 + 2] = frame[p].b;
                }
                packs.Add(Zlib(raw));
                when.Add((int)ms);
                ms += video.times[Math.Min(i, video.times.Count - 1)] * 1000.0 / video.scale;
            }

            Directory.CreateDirectory(outFolder);
            using (var f = new BinaryWriter(File.Create(Path.Combine(outFolder, name + ".frames"))))
            {
                f.Write(new[] { (byte)'S', (byte)'P', (byte)'M', (byte)'V' });
                f.Write(w); f.Write(h); f.Write(packs.Count); f.Write((int)ms);
                for (int i = 0; i < packs.Count; i++) { f.Write(when[i]); f.Write(packs[i].Length); }
                foreach (var p in packs) f.Write(p);
            }
            foreach (var t in tracks)
                if (t.kind == "soun") Sound(d, t, Path.Combine(outFolder, name + ".wav"));
        }

        // RFC 1950 stream: header, deflate data, Adler-32 (MovieFile skips the header and reads the deflate data)
        static byte[] Zlib(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78);
                ms.WriteByte(0x9C);
                using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                uint s1 = 1, s2 = 0;
                foreach (byte b in raw) { s1 = (s1 + b) % 65521; s2 = (s2 + s1) % 65521; }
                uint adler = s2 << 16 | s1;
                ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                return ms.ToArray();
            }
        }

        static void Sound(byte[] d, Track t, string path)
        {
            int a = t.desc;
            int channels = U16(d, a + 24), bits = U16(d, a + 26);
            int rate = (int)(U32(d, a + 32) >> 16);
            var raw = new MemoryStream();
            for (int i = 0; i < t.chunks.Count; i++)
            {
                int n = t.PerChunk(i) * (bits / 8) * channels;
                n = Math.Min(n, d.Length - t.chunks[i]);
                raw.Write(d, t.chunks[i], n);
            }
            byte[] pcm = raw.ToArray();
            if (bits == 16)   // QuickTime's 16-bit sound is big-endian
                for (int i = 0; i + 1 < pcm.Length; i += 2) { byte x = pcm[i]; pcm[i] = pcm[i + 1]; pcm[i + 1] = x; }
            WavWriter.Write(path, pcm, channels, rate, bits);
        }
    }

    public static class WavWriter
    {
        public static void Write(string path, byte[] pcm, int channels, int rate, int bits)
        {
            using (var f = new BinaryWriter(File.Create(path)))
            {
                int block = channels * bits / 8;
                f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                f.Write(36 + pcm.Length);
                f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                f.Write(16);
                f.Write((short)1);
                f.Write((short)channels);
                f.Write(rate);
                f.Write(rate * block);
                f.Write((short)block);
                f.Write((short)bits);
                f.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                f.Write(pcm.Length);
                f.Write(pcm);
            }
        }
    }
}
