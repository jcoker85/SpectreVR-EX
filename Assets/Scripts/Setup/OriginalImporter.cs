using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace SpectreEX.Setup
{
    // Builds Spectre VR-EX's game data from the player's own copy of Spectre VR. Nothing from the
    // original game ships with Spectre VR-EX or sits in its repository: the first-run setup screen (and
    // the editor's Spectre VR-EX > Import Original Game) run this into GameData.Root.
    //
    // Plain C# with no Unity types, so it runs on a worker thread. Writes, under the output folder:
    //   Pictures/  the menu and HUD pictures (PCX files the game names .DAT)
    //   Sounds/    the sound effects, WAV as they are ("&" dropped from names)
    //   Matrices/  CENTRAL.MAT, CENTRAL.BIG, CENTRAL.PCX and BASIC.MAT
    //   Textures/  the shape textures, PCX as they are
    //   Movies/    the QuickTime movies, decoded (QuickTimeMovie)
    //   Shapes/    BuiltinShapes.json, the shapes built into SVRWIN.EXE (BuiltinShapeExporter)
    //   Music/     the MIDI soundtrack rendered to Ogg Vorbis with FluidSynth and a General MIDI SoundFont
    //   import.txt written last: the importer version, the folder imported from and the music lengths
    public class OriginalImporter
    {
        // Raise when the data layout changes, so existing installs import again
        public const int Version = 1;
        public const string StampFile = "import.txt";

        // The game's pictures are PCX files named .DAT. Options is the Windows edition's SPCP20W.
        public static readonly (string name, string dat)[] Pictures =
        {
            ("MainMenu", "SPCP25"), ("Buttons", "SPCP35"), ("Matrix", "SPCP34"), ("About", "SPCP10"), ("Help", "SPCP14"),
            ("Custom", "SPCP13"), ("Options", "SPCP20W"), ("Splash", "SPCP33"), ("Vehicles", "SPCP21"), ("WeaponIcons", "SPCP36"),
        };
        public static readonly string[] MatrixFiles = { "CENTRAL.MAT", "CENTRAL.BIG", "CENTRAL.PCX", "BASIC.MAT" };

        // FluidSynth's gain for the measuring pass; each song is then rendered again to peak at 0.89
        const double MeasureGain = 0.6, TargetPeak = 0.89, Silence = 1e-3;
        const int MusicRate = 44100;

        public string Game, Output, FluidSynth, SoundFont, LayoutJson;
        public bool SkipMusic;

        // Progress for the setup screen: 0..1 and what's happening now. Set from the worker thread.
        public volatile float Progress;
        public volatile string Status = "";
        public readonly List<string> Warnings = new List<string>();
        CancellationToken cancel;

        // The folder holding SVRWIN.EXE and CENTRAL.MAT: the one given, or a folder just inside it
        // (the player may pick the folder above, or a CD's root)
        public static string FindGameFolder(string picked)
        {
            if (string.IsNullOrWhiteSpace(picked)) return null;
            picked = picked.Trim().Trim('"');
            if (!Directory.Exists(picked)) return null;
            if (IsGameFolder(picked)) return picked;
            try
            {
                foreach (var sub in Directory.GetDirectories(picked))
                {
                    if (IsGameFolder(sub)) return sub;
                    foreach (var sub2 in Directory.GetDirectories(sub))
                        if (IsGameFolder(sub2)) return sub2;
                }
            }
            catch (Exception) { }
            return null;
        }

        public static bool IsGameFolder(string folder) =>
            File.Exists(Path.Combine(folder, "SVRWIN.EXE")) && File.Exists(Path.Combine(folder, "CENTRAL.MAT"));

        // Why a folder can't be imported, or null if it can
        public string Check()
        {
            if (FindGameFolder(Game) is string found) Game = found;
            else return "That folder doesn't hold Spectre VR: look for the one with SVRWIN.EXE and CENTRAL.MAT in it.";
            foreach (var p in Pictures)
                if (!File.Exists(Path.Combine(Game, "DATA", p.dat + ".DAT"))) return $"DATA\\{p.dat}.DAT is missing. Is this a complete Spectre VR folder?";
            foreach (var m in MatrixFiles)
                if (!File.Exists(Path.Combine(Game, m))) return m + " is missing. Is this a complete Spectre VR folder?";
            foreach (var d in new[] { "SOUNDS", "TEXTURES", "MOVIES" })
                if (!Directory.Exists(Path.Combine(Game, d))) return $"The {d} folder is missing. Is this a complete Spectre VR folder?";
            if (BuiltinShapeExporter.Sha256(File.ReadAllBytes(Path.Combine(Game, "SVRWIN.EXE"))) != BuiltinShapeExporter.LayoutSha(LayoutJson))
                return "This copy's SVRWIN.EXE is a different release from the one Spectre VR-EX was made from, so its built-in shapes can't be read.";
            return null;
        }

        public void Run(CancellationToken token)
        {
            cancel = token;
            string problem = Check();
            if (problem != null) throw new InvalidDataException(problem);
            // FluidSynth runs in its own folder, so nothing may be relative
            Game = Path.GetFullPath(Game);
            Output = Path.GetFullPath(Output);
            if (!string.IsNullOrEmpty(FluidSynth)) FluidSynth = Path.GetFullPath(FluidSynth);
            if (!string.IsNullOrEmpty(SoundFont)) SoundFont = Path.GetFullPath(SoundFont);

            // Build into a fresh folder beside the output, then swap it in, so a failed or cancelled
            // import never leaves half a data folder behind
            string work = Output + ".importing";
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            try { Build(work); }
            catch
            {
                try { Directory.Delete(work, true); } catch (Exception) { }
                throw;
            }

            Status = "Finishing";
            if (Directory.Exists(Output)) Directory.Delete(Output, true);
            Directory.Move(work, Output);
            Progress = 1f;
            Status = "Done";
        }

        void Build(string work)
        {
            var steps = new List<(string what, float weight, Action<string> run)>
            {
                ("Pictures", 1, ImportPictures),
                ("Sounds", 1, ImportSounds),
                ("Matrices", 1, ImportMatrices),
                ("Textures", 1, ImportTextures),
                ("Built-in shapes", 1, ImportShapes),
                ("Movies", 6, ImportMovies),
            };
            var lengths = new Dictionary<string, int>();
            if (!SkipMusic) steps.Add(("Music", 30, w => ImportMusic(w, lengths)));
            float total = steps.Sum(s => s.weight), done = 0;
            foreach (var (what, weight, run) in steps)
            {
                cancel.ThrowIfCancellationRequested();
                Status = what;
                stepStart = done / total;
                stepSpan = weight / total;
                run(work);
                done += weight;
                Progress = done / total;
            }

            using (var f = new StreamWriter(Path.Combine(work, StampFile)))
            {
                f.WriteLine("version=" + Version);
                f.WriteLine("source=" + Game);
                f.WriteLine("music=" + (SkipMusic ? 0 : 1));
                foreach (var kv in lengths.OrderBy(k => k.Key)) f.WriteLine("length." + kv.Key + "=" + kv.Value);
            }
        }

        float stepStart, stepSpan;
        void StepProgress(int i, int n) => Progress = stepStart + stepSpan * i / Math.Max(1, n);

        static string Folder(string root, string name)
        {
            string p = Path.Combine(root, name);
            Directory.CreateDirectory(p);
            return p;
        }

        void ImportPictures(string root)
        {
            string dir = Folder(root, "Pictures");
            foreach (var (name, dat) in Pictures)
            {
                byte[] d = File.ReadAllBytes(Path.Combine(Game, "DATA", dat + ".DAT"));
                if (d.Length < 128 + 768 || d[0] != 0x0A) throw new InvalidDataException($"DATA\\{dat}.DAT isn't a PCX picture");
                File.WriteAllBytes(Path.Combine(dir, name + ".pcx"), d);
            }
        }

        void ImportSounds(string root)
        {
            string dir = Folder(root, "Sounds");
            var wavs = Directory.GetFiles(Path.Combine(Game, "SOUNDS"), "*.WAV");
            if (wavs.Length == 0) throw new InvalidDataException("The SOUNDS folder has no sounds in it");
            foreach (var wav in wavs) File.Copy(wav, Path.Combine(dir, Path.GetFileName(wav).Replace("&", "").ToUpperInvariant()), true);
        }

        void ImportMatrices(string root)
        {
            string dir = Folder(root, "Matrices");
            foreach (var m in MatrixFiles) File.Copy(Path.Combine(Game, m), Path.Combine(dir, m), true);
        }

        void ImportTextures(string root)
        {
            string dir = Folder(root, "Textures");
            foreach (var pcx in Directory.GetFiles(Path.Combine(Game, "TEXTURES"), "*.PCX"))
                File.Copy(pcx, Path.Combine(dir, Path.GetFileName(pcx).ToUpperInvariant()), true);
        }

        void ImportShapes(string root)
        {
            string json = BuiltinShapeExporter.Export(File.ReadAllBytes(Path.Combine(Game, "SVRWIN.EXE")), LayoutJson);
            File.WriteAllText(Path.Combine(Folder(root, "Shapes"), "BuiltinShapes.json"), json);
        }

        void ImportMovies(string root)
        {
            string dir = Folder(root, "Movies");
            var movies = Directory.GetFiles(Path.Combine(Game, "MOVIES"), "*.MOV");
            for (int i = 0; i < movies.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                Status = "Movies: " + Path.GetFileNameWithoutExtension(movies[i]).ToUpperInvariant();
                StepProgress(i, movies.Length);
                QuickTimeMovie.Convert(movies[i], dir);
            }
        }

        // FluidSynth renders each song twice: once to a float WAV, to find its peak and where its sound
        // ends, then to Ogg Vorbis with the gain that brings the peak to 0.89. Where the sound ends (plus
        // half a second) goes into import.txt; MusicPlayer cuts the song there so it loops on time.
        void ImportMusic(string root, Dictionary<string, int> lengths)
        {
            if (string.IsNullOrEmpty(FluidSynth) || !File.Exists(FluidSynth)) throw new FileNotFoundException("FluidSynth wasn't found, so the music can't be made.");
            if (string.IsNullOrEmpty(SoundFont) || !File.Exists(SoundFont)) throw new FileNotFoundException("The General MIDI SoundFont wasn't found, so the music can't be made.");
            string dir = Folder(root, "Music");
            string temp = Folder(root, "MusicTemp");
            var songs = Directory.GetFiles(Path.Combine(Game, "SOUNDS"), "*.MID").OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            int finished = 0;
            Exception failed = null;
            var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)), CancellationToken = cancel };
            System.Threading.Tasks.Parallel.ForEach(songs, options, midi =>
            {
                try
                {
                    string name = Path.GetFileNameWithoutExtension(midi).Replace("&", "").ToUpperInvariant();
                    string wav = Path.Combine(temp, name + ".wav");
                    Render(midi, wav, MeasureGain, "wav", "float");
                    var (peak, end) = Measure(wav);
                    File.Delete(wav);
                    double gain = peak > 0 ? MeasureGain * TargetPeak / peak : MeasureGain;
                    Render(midi, Path.Combine(dir, name + ".ogg"), gain, "oga", "float");
                    lock (lengths) lengths[name] = end;
                }
                catch (Exception e) { lock (lengths) failed = failed ?? e; }
                int n = Interlocked.Increment(ref finished);
                Status = $"Music: {n} of {songs.Length} songs";
                StepProgress(n, songs.Length);
            });
            if (failed != null) throw failed;
            Directory.Delete(temp, true);
        }

        void Render(string midi, string output, double gain, string type, string format)
        {
            var info = new ProcessStartInfo
            {
                FileName = FluidSynth,
                Arguments = $"-ni -q -g {gain.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} -r {MusicRate} -O {format} -T {type} " +
                            $"-F \"{output}\" \"{SoundFont}\" \"{midi}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(FluidSynth),
            };
            using (var p = Process.Start(info))
            {
                var err = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEnd();
                while (!p.WaitForExit(200))
                    if (cancel.IsCancellationRequested) { try { p.Kill(); } catch (Exception) { } cancel.ThrowIfCancellationRequested(); }
                if (p.ExitCode != 0 || !File.Exists(output))
                    throw new IOException($"FluidSynth couldn't render {Path.GetFileName(midi)}: {err.Result.Trim()}");
            }
        }

        // Peak, and the sample frame half a second after the last one above the silence threshold, of
        // FluidSynth's 32-bit float WAV
        static (double peak, int end) Measure(string wav)
        {
            byte[] b = File.ReadAllBytes(wav);
            int at = 12, channels = 2, rate = MusicRate, dataAt = -1, dataSize = 0;
            while (at + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, at, 4);
                int size = BitConverter.ToInt32(b, at + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(b, at + 10);
                    rate = BitConverter.ToInt32(b, at + 12);
                    if (BitConverter.ToInt16(b, at + 22) != 32) throw new InvalidDataException("FluidSynth didn't write float samples");
                }
                else if (id == "data") { dataAt = at + 8; dataSize = Math.Min(size, b.Length - dataAt); break; }
                at += 8 + size + (size & 1);
            }
            if (dataAt < 0) throw new InvalidDataException("FluidSynth wrote no sound");
            int frames = dataSize / 4 / channels, last = -1;
            double peak = 0;
            for (int i = 0; i < frames * channels; i++)
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(b, dataAt + i * 4)));
            // The silence threshold applies to the normalised song
            double scale = peak > 0 ? TargetPeak / peak : 1;
            for (int i = frames - 1; i >= 0 && last < 0; i--)
                for (int c = 0; c < channels; c++)
                    if (Math.Abs(BitConverter.ToSingle(b, dataAt + (i * channels + c) * 4)) * scale > Silence) { last = i; break; }
            int end = last < 0 ? frames : Math.Min(frames, last + rate / 2);
            return (peak, end);
        }

        // What's in a data folder's import.txt, or null if it has none
        public static Dictionary<string, string> ReadStamp(string dataFolder)
        {
            string path = Path.Combine(dataFolder, StampFile);
            if (!File.Exists(path)) return null;
            var d = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            return d;
        }
    }
}
