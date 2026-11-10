using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SpectreEX.Setup
{
    // Reads the 3D shapes built into SVRWIN.EXE (shape ids under 1000) into the JSON that
    // Matrix.BuiltinShapes loads. Plain C# with no Unity types, so the importer can run on a worker thread.
    //
    // SVRWIN.EXE fills its built-in object templates (_BIG_BOX, _PYRAMID, _WINDMILL, ...) at startup in
    // _BUILD_OBJECTS and _SET_FACES: long runs of constant stores, setting each template's point count
    // and pointer (+0x40/+0x42) and face count and pointer (+0x52/+0x54), then each 0x35-byte face's
    // vertex count and index-list pointer (+0/+2). The points themselves are initialised data in the
    // EXE's data segments (three 32-bit ints each: x forward, y side, z up, in world units).
    //
    // This replays those stores over the EXE's segments and reads the templates back. The stores are
    // only pointers and counts: they were taken once from the Ghidra decompile into
    // Assets/Resources/BuiltinShapesLayout.json (Tools/export_builtin_shapes.py --make-layout), so the
    // import needs nothing but the player's own SVRWIN.EXE, where all the geometry is read from. The
    // id -> template table is _OT_TO_SHAPE (DS:0x9B8): 10-byte entries of shape id, object type, shap
    // resource id, far pointer to the template (pointer filled by relocations).
    public static class BuiltinShapeExporter
    {
        static readonly string[] Templates = { "BIG_BOX", "PYRAMID", "WALL", "WINDMILL", "WINDMILL_FANS", "COLUMN", "FUEL", "PLAYER_TANK",
                                               "TANK", "SUPERTANK", "SHOT", "BASE", "FLAG", "ICE", "GOO", "TURRET" };

        class Exe
        {
            public readonly Dictionary<int, byte[]> mem = new Dictionary<int, byte[]>();
            public readonly Dictionary<int, Dictionary<int, (int sel, int off)>> relocs = new Dictionary<int, Dictionary<int, (int, int)>>();

            public Exe(byte[] d)
            {
                int ne = BitConverter.ToInt32(d, 0x3C);
                if (d[ne] != 'N' || d[ne + 1] != 'E') throw new InvalidDataException("SVRWIN.EXE isn't a Windows 3.1 (NE) program");
                int count = BitConverter.ToUInt16(d, ne + 0x1C);
                int table = ne + BitConverter.ToUInt16(d, ne + 0x22);
                int align = 1 << BitConverter.ToUInt16(d, ne + 0x32);
                for (int i = 0; i < count; i++)
                {
                    int off = BitConverter.ToUInt16(d, table + i * 8), length = BitConverter.ToUInt16(d, table + i * 8 + 2);
                    int flags = BitConverter.ToUInt16(d, table + i * 8 + 4), minalloc = BitConverter.ToUInt16(d, table + i * 8 + 6);
                    if (length == 0) length = 0x10000;
                    int sel = 0x1000 + i * 8;                       // Ghidra's selector for NE segment i + 1
                    var buf = new byte[Math.Max(Math.Max(length, minalloc), 0x100)];
                    if (off != 0)
                    {
                        Buffer.BlockCopy(d, off * align, buf, 0, Math.Min(length, d.Length - off * align));
                        if ((flags & 0x100) != 0)                   // relocation records follow the segment data
                        {
                            int r = off * align + length;
                            var list = new Dictionary<int, (int, int)>();
                            int n = BitConverter.ToUInt16(d, r);
                            for (int k = 0; k < n; k++)
                            {
                                int e = r + 2 + k * 8;
                                int src = d[e], kind = d[e + 1], at = BitConverter.ToUInt16(d, e + 2), seg = d[e + 4], target = BitConverter.ToUInt16(d, e + 6);
                                if (src == 3 && kind == 0) list[at] = (0x1000 + (seg - 1) * 8, target);   // far pointer to an internal segment
                            }
                            relocs[sel] = list;
                        }
                    }
                    mem[sel] = buf;
                }
            }

            public void W16(int sel, int o, long v) { var m = mem[sel]; m[o] = (byte)v; m[o + 1] = (byte)(v >> 8); }
            public void W32(int sel, int o, long v) { W16(sel, o, v & 0xFFFF); W16(sel, o + 2, (v >> 16) & 0xFFFF); }
            public int U16(int sel, int o) => BitConverter.ToUInt16(mem[sel], o);
            public int S16(int sel, int o) => BitConverter.ToInt16(mem[sel], o);
            public int S32(int sel, int o) => BitConverter.ToInt32(mem[sel], o);

            public (int sel, int off)? Reloc(int sel, int at) =>
                relocs.TryGetValue(sel, out var list) && list.TryGetValue(at, out var t) ? t : ((int, int)?)null;
        }

        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(data)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // The SHA-256 the layout was made from
        public static string LayoutSha(string layoutJson) => Regex.Match(layoutJson, "\"sha256\"\\s*:\\s*\"([0-9a-f]{64})\"").Groups[1].Value;

        // Returns the JSON for Matrix.BuiltinShapes. Throws if exe isn't the release the layout was made from.
        public static string Export(byte[] exeBytes, string layoutJson)
        {
            if (Sha256(exeBytes) != LayoutSha(layoutJson))
                throw new InvalidDataException("This SVRWIN.EXE isn't the release Spectre VR-EX's built-in shape layout was made from.");
            var exe = new Exe(exeBytes);

            var sym = new Dictionary<string, (int sel, int off)>();
            foreach (Match m in Regex.Matches(layoutJson, "\"(_\\w+)\"\\s*:\\s*\\[\\s*(\\d+)\\s*,\\s*(\\d+)\\s*\\]"))
                sym[m.Groups[1].Value] = (int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            int writesAt = layoutJson.IndexOf("\"writes\"", StringComparison.Ordinal);
            if (writesAt < 0) throw new InvalidDataException("Built-in shape layout has no stores");
            foreach (Match m in Regex.Matches(layoutJson.Substring(writesAt), "\\[\\s*(-?\\d+)\\s*,\\s*(-?\\d+)\\s*,\\s*(-?\\d+)\\s*,\\s*(-?\\d+)\\s*\\]"))
            {
                int sel = int.Parse(m.Groups[1].Value), o = int.Parse(m.Groups[2].Value), size = int.Parse(m.Groups[3].Value);
                long v = long.Parse(m.Groups[4].Value);
                if (size == 4) exe.W32(sel, o, v); else exe.W16(sel, o, v);
            }

            var sb = new StringBuilder();
            var at = new Dictionary<(int, int), string>();
            var shapes = new List<string>();
            foreach (var name in Templates)
            {
                var (sel, o) = sym["_" + name];
                at[(sel, o)] = name;
                int npts = exe.U16(sel, o + 0x40), po = exe.U16(sel, o + 0x42), ps = exe.U16(sel, o + 0x44);
                int nfaces = exe.U16(sel, o + 0x52), fo = exe.U16(sel, o + 0x54), fs = exe.U16(sel, o + 0x56);
                var s = new StringBuilder();
                s.Append("{\"name\":\"").Append(name).Append("\",\"verts\":[");
                for (int i = 0; i < npts; i++)
                for (int k = 0; k < 3; k++)
                {
                    if (i + k > 0) s.Append(',');
                    s.Append(exe.S32(ps, po + i * 12 + k * 4).ToString(CultureInfo.InvariantCulture));
                }
                s.Append("],\"faces\":[");
                for (int f = 0; f < nfaces; f++)
                {
                    int b = fo + f * 0x35;
                    int n = exe.U16(fs, b), io = exe.U16(fs, b + 2), iseg = exe.U16(fs, b + 4);
                    if (f > 0) s.Append(',');
                    s.Append("{\"idx\":[");
                    for (int k = 0; k < n; k++) { if (k > 0) s.Append(','); s.Append(exe.U16(iseg, io + k * 2)); }
                    s.Append("],\"col\":").Append(exe.U16(fs, b + 0x2B)).Append('}');
                }
                s.Append("]}");
                shapes.Add(s.ToString());
            }

            var (ds, to) = sym["_OT_TO_SHAPE"];
            var table = new List<string>();
            while (exe.S16(ds, to + 2) >= 0)
            {
                var target = exe.Reloc(ds, to + 6);
                string shape = target.HasValue && at.TryGetValue(target.Value, out var nm) ? nm : "";
                table.Add($"{{\"id\":{exe.S16(ds, to)},\"type\":{exe.S16(ds, to + 2)},\"shap\":{exe.S16(ds, to + 4)},\"shape\":\"{shape}\"}}");
                to += 10;
            }

            var typeColours = new List<string>();
            for (int i = 0; i < 64; i++) typeColours.Add((exe.U16(ds, 0x8FC + i * 2) & 0xFF).ToString());

            sb.Append("{\"table\":[").Append(string.Join(",", table)).Append("],\"shapes\":[").Append(string.Join(",", shapes))
              .Append("],\"typeColors\":[").Append(string.Join(",", typeColours))
              .Append("],\"objColors\":[").Append(ColourTable(exe, sym["_OBJ_COLORS"]))
              .Append("],\"skyColors\":[").Append(ColourTable(exe, sym["_SKY_COLORS"]))
              .Append("],\"textures\":[").Append(TextureFiles(exe, sym["_TEXTURE_FILE_NAMES"])).Append("]}");
            return sb.ToString();
        }

        // 30 levels of 16-bit RGB (three words each), as 8-bit values
        static string ColourTable(Exe exe, (int sel, int off) at)
        {
            var list = new List<string>();
            for (int i = 0; i < 90; i++) list.Add((exe.U16(at.sel, at.off + i * 2) >> 8).ToString());
            return string.Join(",", list);
        }

        // The file behind each texture face colour, 6000-6015: far pointers, filled by relocations
        static string TextureFiles(Exe exe, (int sel, int off) at)
        {
            var names = new List<string>();
            for (int i = 0; i < 16; i++)
            {
                var target = exe.Reloc(at.sel, at.off + i * 4);
                var name = new StringBuilder();
                while (target.HasValue && name.Length < 32)
                {
                    int c = exe.U16(target.Value.sel, target.Value.off + name.Length) & 0xFF;
                    if (c == 0) break;
                    name.Append((char)c);
                }
                string s = name.ToString();
                s = s.Substring(s.LastIndexOf('\\') + 1).ToUpperInvariant();
                names.Add("\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
            }
            return string.Join(",", names);
        }
    }
}
