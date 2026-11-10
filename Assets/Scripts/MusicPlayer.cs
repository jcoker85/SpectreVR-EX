using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SpectreEX
{
    // Plays the original Spectre VR MIDI soundtrack, rendered to Ogg Vorbis through GeneralUser GS by the
    // first-run import (Setup.OriginalImporter) into GameData/Music. Each level plays its matrix song in
    // the styles ticked in Options; the menu plays 5THEME (song 24001). M toggles music.
    //
    // Songs load in the background and are cut where their sound ends (plus half a second, measured by
    // the import), so they loop on time. The menu theme and the song playing stay loaded.
    public class MusicPlayer : MonoBehaviour
    {
        static MusicPlayer instance;
        public static MusicPlayer Instance
        {
            get
            {
                if (instance == null) instance = FindFirstObjectByType<MusicPlayer>();
                return instance;
            }
            private set => instance = value;
        }

        public float volume = 0.45f;
        const string MenuTheme = "5THEME";

        AudioSource source;
        bool muted;

        void Awake()
        {
            Instance = this;
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.loop = true;
            source.volume = volume;
            source.ignoreListenerPause = true;
        }

        public void PlayMenuTheme()
        {
            if (!GameSettings.MusicOn) { source.Stop(); wanted = null; }
            else if (wanted == MenuTheme && loading.Contains(MenuTheme)) return;
            else if (source.clip == null || source.clip.name != MenuTheme || !source.isPlaying) PlayTrack(MenuTheme);
        }

        public void Stop()
        {
            source.Stop();
            wanted = null;
        }

        // A matrix level's song (null = the level has none, or no ticked style covers it)
        public void PlayTrackOrStop(string name)
        {
            if (string.IsNullOrEmpty(name)) Stop();
            else PlayTrack(name);
        }

        public void PlayTrack(string name, bool loop = true)
        {
            if (!GameSettings.MusicOn) { source.Stop(); wanted = null; return; }
            wanted = name;
            wantLoop = loop;
            if (clips.TryGetValue(name, out var clip)) { Begin(clip); return; }
            // Until the song is in, nothing plays (the old one stops, as a new song would cut it)
            source.Stop();
            if (!loading.Contains(name)) StartCoroutine(Load(name));
        }

        string wanted;
        bool wantLoop;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly HashSet<string> loading = new HashSet<string>();

        void Begin(AudioClip clip)
        {
            source.clip = clip;
            source.loop = wantLoop;
            if (!muted) source.Play();
            // Keep only the menu theme and this song
            var drop = new List<string>();
            foreach (var kv in clips)
                if (kv.Key != MenuTheme && kv.Value != clip) drop.Add(kv.Key);
            foreach (var key in drop) { Destroy(clips[key]); clips.Remove(key); }
        }

        IEnumerator Load(string name)
        {
            loading.Add(name);
            string path = Path.Combine(GameData.Music, name + ".ogg");
            if (!File.Exists(path))
            {
                Debug.LogWarning("Spectre VR-EX: missing music " + name);
                loading.Remove(name);
                yield break;
            }
            using (var req = UnityWebRequestMultimedia.GetAudioClip(new System.Uri(path).AbsoluteUri, AudioType.OGGVORBIS))
            {
                var handler = (DownloadHandlerAudioClip)req.downloadHandler;
                handler.streamAudio = false;
                handler.compressed = false;
                yield return req.SendWebRequest();
                loading.Remove(name);
                if (req.result != UnityWebRequest.Result.Success) { Debug.LogWarning($"Spectre VR-EX: couldn't load music {name}: {req.error}"); yield break; }
                var clip = Trim(handler.audioClip, GameData.MusicLength(name));
                clip.name = name;
                clips[name] = clip;
                if (wanted == name && GameSettings.MusicOn) Begin(clip);
            }
        }

        // The song up to where its sound ends
        static AudioClip Trim(AudioClip clip, int frames)
        {
            if (frames <= 0 || frames >= clip.samples) return clip;
            var data = new float[frames * clip.channels];
            clip.GetData(data, 0);
            var cut = AudioClip.Create(clip.name, frames, clip.channels, clip.frequency, false);
            cut.SetData(data, 0);
            Destroy(clip);
            return cut;
        }

        void Update()
        {
            if (GameKeys.Music.Pressed)
            {
                muted = !muted;
                if (muted) source.Pause(); else source.UnPause();
                if (!muted && !source.isPlaying) source.Play();
            }
        }
    }
}
