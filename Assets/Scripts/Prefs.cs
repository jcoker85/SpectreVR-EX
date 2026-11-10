using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace SpectreEX
{
    // The settings, controls, high scores and saved games, kept in UserData\Settings.txt beside the game
    // (GameData.UserData) rather than in the registry as PlayerPrefs would, so the game is portable.
    // The same calls as PlayerPrefs. One "key=value" line each, with \, newline and CR escaped.
    public static class Prefs
    {
        const string FileName = "Settings.txt";
        static Dictionary<string, string> values;
        static bool dirty;

        static string FilePath => Path.Combine(GameData.UserData, FileName);

        static Dictionary<string, string> Values
        {
            get
            {
                if (values != null) return values;
                values = new Dictionary<string, string>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                        {
                            int eq = line.IndexOf('=');
                            if (eq > 0) values[Unescape(line.Substring(0, eq))] = Unescape(line.Substring(eq + 1));
                        }
                }
                catch (System.Exception e) { Debug.LogWarning($"Spectre VR-EX: couldn't read {FilePath}: {e.Message}"); }
                return values;
            }
        }

        // Whatever was set and not yet saved is saved on quitting, as PlayerPrefs does
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            values = null;
            dirty = false;
            Application.quitting -= Save;
            Application.quitting += Save;
        }

        public static bool HasKey(string key) => Values.ContainsKey(key);

        public static int GetInt(string key, int fallback = 0) =>
            Values.TryGetValue(key, out var v) && int.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : fallback;

        public static string GetString(string key, string fallback = "") =>
            Values.TryGetValue(key, out var v) ? v : fallback;

        public static void SetInt(string key, int value) =>
            Set(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public static void SetString(string key, string value) => Set(key, value ?? "");

        public static void DeleteKey(string key)
        {
            if (Values.Remove(key)) dirty = true;
        }

        static void Set(string key, string value)
        {
            if (Values.TryGetValue(key, out var old) && old == value) return;
            Values[key] = value;
            dirty = true;
        }

        // Written to a temporary file first, so a crash mid-write can't lose the settings
        public static void Save()
        {
            if (!dirty || values == null) return;
            try
            {
                Directory.CreateDirectory(GameData.UserData);
                var sb = new StringBuilder();
                var keys = new List<string>(values.Keys);
                keys.Sort(System.StringComparer.Ordinal);
                foreach (var k in keys) sb.Append(Escape(k)).Append('=').Append(Escape(values[k])).Append('\n');
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
                dirty = false;
            }
            catch (System.Exception e) { Debug.LogWarning($"Spectre VR-EX: couldn't save {FilePath}: {e.Message}"); }
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("=", "\\e");

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                sb.Append(c == 'n' ? '\n' : c == 'r' ? '\r' : c == 'e' ? '=' : c);
            }
            return sb.ToString();
        }
    }
}
