using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    // Plays the original Spectre VR MIDI soundtrack (pre-rendered to OGG by Tools/midisynth.py,
    // stored in Assets/Resources/Music). M toggles music, N skips to the next track.
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
        public string[] levelPlaylist =
        {
            "LEVEL1", "LEVEL2", "LEVEL3", "HOUSEVR", "4FLYING1", "RAVER20", "CW", "FUNKY0",
            "SPACEWA", "JAWS22", "GUITAR33", "HENDRIX", "MARCH11", "VRGUITA",
        };

        AudioSource source;
        int trackIndex;
        bool muted;

        void Awake()
        {
            Instance = this;
            source = gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 0f;
            source.loop = true;
            source.volume = volume;
        }

        public void PlayForLevel(int level) => PlayIndex(level - 1);

        public void PlayTrack(string name, bool loop = true)
        {
            var clip = Resources.Load<AudioClip>("Music/" + name);
            if (clip == null) { Debug.LogWarning("SpectreEX: missing music " + name); return; }
            source.clip = clip;
            source.loop = loop;
            if (!muted) source.Play();
        }

        void PlayIndex(int i)
        {
            trackIndex = ((i % levelPlaylist.Length) + levelPlaylist.Length) % levelPlaylist.Length;
            PlayTrack(levelPlaylist[trackIndex]);
        }

        void Update()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.mKey.wasPressedThisFrame)
            {
                muted = !muted;
                if (muted) source.Pause(); else source.UnPause();
                if (!muted && !source.isPlaying) source.Play();
            }
            if (k.nKey.wasPressedThisFrame) PlayIndex(trackIndex + 1);
            if (k.sKey.wasPressedThisFrame && k.ctrlKey.isPressed) Sfx.Enabled = !Sfx.Enabled;
        }
    }
}
