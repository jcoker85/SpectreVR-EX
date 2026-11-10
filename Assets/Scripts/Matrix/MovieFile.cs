using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpectreEX.Matrix
{
    // One of the player's own QuickTime movies, decoded by Tools/qtmovie.py into StreamingAssets/Movies.
    public class MovieFile
    {
        public static string Folder => Path.Combine(Application.streamingAssetsPath, "Movies");

        readonly byte[] data;
        readonly int[] times, offsets, sizes;
        readonly byte[] pixels, flipped;
        readonly int width, height;
        int shown = -1;

        public readonly Texture2D Texture;
        public readonly float Seconds;
        public int Width => width;
        public int Height => height;

        MovieFile(byte[] file)
        {
            data = file;
            width = System.BitConverter.ToInt32(file, 4);
            height = System.BitConverter.ToInt32(file, 8);
            int count = System.BitConverter.ToInt32(file, 12);
            Seconds = System.BitConverter.ToInt32(file, 16) / 1000f;
            times = new int[count];
            offsets = new int[count];
            sizes = new int[count];
            int at = 20 + count * 8;
            for (int i = 0; i < count; i++)
            {
                times[i] = System.BitConverter.ToInt32(file, 20 + i * 8);
                sizes[i] = System.BitConverter.ToInt32(file, 24 + i * 8);
                offsets[i] = at;
                at += sizes[i];
            }
            pixels = new byte[width * height * 3];
            flipped = new byte[pixels.Length];
            Texture = new Texture2D(width, height, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            ShowFrame(0);
        }

        public static MovieFile Load(string name)
        {
            string path = Path.Combine(Folder, name + ".frames");
            try { return File.Exists(path) ? new MovieFile(File.ReadAllBytes(path)) : null; }
            catch (System.Exception e) { Debug.LogWarning($"Spectre VR-EX: couldn't read movie {path}: {e.Message}"); return null; }
        }

        public void Show(float seconds)
        {
            int ms = (int)(seconds * 1000f), frame = System.Array.BinarySearch(times, ms);
            if (frame < 0) frame = Mathf.Max(0, ~frame - 1);
            ShowFrame(frame);
        }

        void ShowFrame(int frame)
        {
            if (frame == shown) return;
            shown = frame;
            using (var stream = new DeflateStream(new MemoryStream(data, offsets[frame] + 2, sizes[frame] - 2), CompressionMode.Decompress))
            {
                int got = 0;
                while (got < pixels.Length)
                {
                    int n = stream.Read(pixels, got, pixels.Length - got);
                    if (n <= 0) break;
                    got += n;
                }
            }
            int row = width * 3;
            for (int y = 0; y < height; y++) System.Buffer.BlockCopy(pixels, y * row, flipped, (height - 1 - y) * row, row);
            Texture.LoadRawTextureData(flipped);
            Texture.Apply(false);
        }
    }

    // The faces that show a movie: colours 1028-1031 name WALL1-WALL4 (_TEXTURE_MOVIE_NAMES). Like the
    // original, a level has one movie, the first it comes to, and it loops on every such face in real time.
    public static class MovieTextures
    {
        const int First = 1028, Last = 1031;

        static readonly Dictionary<int, MovieFile> movies = new Dictionary<int, MovieFile>();
        static Material material;
        static MovieFile current;
        static float clock;

        static MovieFile Movie(int id)
        {
            if (!movies.TryGetValue(id, out var m)) movies[id] = m = MovieFile.Load("WALL" + (id - First + 1));
            return m;
        }

        public static bool Has(int faceColour) => faceColour >= First && faceColour <= Last && Movie(faceColour) != null;

        public static void NewLevel()
        {
            current = null;
            clock = 0f;
        }

        public static Material Material(int id, Material shapes)
        {
            if (material == null) material = new Material(shapes);
            if (current == null)
            {
                current = Movie(id);
                material.SetTexture("_BaseColorMap", current.Texture);
                material.SetTexture("_EmissiveColorMap", current.Texture);
            }
            return material;
        }

        public static void Advance(float seconds)
        {
            if (current == null || current.Seconds <= 0f) return;
            clock = (clock + seconds) % current.Seconds;
            current.Show(clock);
        }
    }

    // _SHOW_MOVIE: the startup movie, once, over a black screen; any input ends it
    public class StartupMovie : MonoBehaviour
    {
        const string Name = "STARTUP";

        public static IEnumerator Play(MonoBehaviour host)
        {
            var movie = MovieFile.Load(Name);
            if (movie == null) yield break;

            var go = new GameObject("Startup Movie");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var back = new GameObject("Black").AddComponent<RawImage>();
            back.transform.SetParent(go.transform, false);
            back.color = Color.black;
            Stretch(back.rectTransform);
            var picture = new GameObject("Picture").AddComponent<RawImage>();
            picture.transform.SetParent(go.transform, false);
            picture.texture = movie.Texture;
            movie.Texture.filterMode = FilterMode.Point;
            var frame = picture.rectTransform;
            frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.zero;

            AudioSource audio = null;
            var clip = Sfx.Enabled ? LoadWave(Path.Combine(MovieFile.Folder, Name + ".wav")) : null;
            if (clip != null)
            {
                audio = go.AddComponent<AudioSource>();
                audio.clip = clip;
                audio.spatialBlend = 0f;
                audio.Play();
            }

            float clock = 0f;
            Fit(frame, movie);
            yield return null;
            while (clock < movie.Seconds && !Pressed())
            {
                clock += Time.unscaledDeltaTime;
                Fit(frame, movie);
                movie.Show(clock);
                yield return null;
            }
            if (audio != null) audio.Stop();
            Object.Destroy(go);
            Object.Destroy(movie.Texture);
        }

        // Square pixels at the largest whole-number scale that fits, on whole screen pixels, so every
        // movie pixel is the same size and none is smeared. The overlay canvas counts in screen pixels.
        static void Fit(RectTransform frame, MovieFile movie)
        {
            int scale = Mathf.Max(1, Mathf.Min(Screen.width / movie.Width, Screen.height / movie.Height));
            int w = movie.Width * scale, h = movie.Height * scale;
            frame.sizeDelta = new Vector2(w, h);
            frame.anchoredPosition = new Vector2((Screen.width - w) / 2, (Screen.height - h) / 2);
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static bool Pressed()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            return (k != null && k.anyKey.wasPressedThisFrame) || (m != null && m.leftButton.wasPressedThisFrame);
        }

        // The movie's sound: plain 8-bit unsigned PCM
        static AudioClip LoadWave(string path)
        {
            if (!File.Exists(path)) return null;
            var b = File.ReadAllBytes(path);
            int channels = System.BitConverter.ToInt16(b, 22), rate = System.BitConverter.ToInt32(b, 24);
            int at = 12;
            while (at + 8 <= b.Length && !(b[at] == 'd' && b[at + 1] == 'a' && b[at + 2] == 't' && b[at + 3] == 'a'))
                at += 8 + System.BitConverter.ToInt32(b, at + 4);
            if (at + 8 > b.Length) return null;
            int size = Mathf.Min(System.BitConverter.ToInt32(b, at + 4), b.Length - at - 8);
            var samples = new float[size];
            for (int i = 0; i < size; i++) samples[i] = (b[at + 8 + i] - 128) / 128f;
            var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), size / channels, channels, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
