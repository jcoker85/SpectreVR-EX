using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX
{
    // Original Spectre VR sound names, in one place so a mis-guessed mapping is a one-line fix.
    // Clips live in Assets/Resources/Sounds (WAV, copied from the original SOUNDS folder).
    public static class Snd
    {
        public const string PlayerShot = "PLYRSHT";
        public const string EnemyShot = "OPSHOT";
        public const string PlayerHit = "PLYRHT";
        public const string EnemyHit = "OPPHIT";
        public const string Explosion = "EXPLO";
        public const string PlayerDown = "TNKDWN";
        public const string PlayerUp = "TNKUP";
        public const string FlagPickup = "FLGPUL";
        public const string OneMoreFlag = "ONEMFLG";
        public const string AmmoPickup = "AMMPCK";
        public const string ShieldPickup = "SHLDPCK";
        public const string OutOfAmmo = "OUTOFAM";
        public const string LowAmmo = "SHKLOW";
        public const string Danger = "DANGER";
        public const string Warp = "WARP";
        public const string Land = "PLYRLND";
        public const string Collision = "COLLSN";
        public const string Welcome = "WELCOME";
        public const string Award = "AWARD";
        public const string ExtraLife = "LIFESPT";
        public const string TimeTick = "TMTICK";
        public const string SlicerSpin = "SPINNER";
        public const string GameOver = "URDMD";
        // Names from SVRWIN.EXE's own sound table, matched to where the code plays them
        public const string Repair = "REPPCKP";      // full repair / wings dump
        public const string Gate = "GATE";           // teleport gate
        public const string IceSlide = "ICESLD";     // driving onto ice
        public const string Goo = "GOOSQ";           // slow pads
        public const string Acid = "ACIDD";          // acid burn
        public const string HitEdge = "HTEDGE";      // arena edge
    }

    public static class Sfx
    {
        public static bool Enabled = true;

        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static AudioSource source2D;

        public static AudioClip Get(string name)
        {
            if (!cache.TryGetValue(name, out var clip))
            {
                clip = Resources.Load<AudioClip>("Sounds/" + name);
                if (clip == null) Debug.LogWarning("Spectre VR-EX: missing sound " + name);
                cache[name] = clip;
            }
            return clip;
        }

        // Non-positional: player events, voices, UI.
        public static void Play(string name, float volume = 1f)
        {
            if (!Enabled) return;
            var clip = Get(name);
            if (clip == null) return;
            if (source2D == null)
            {
                var go = new GameObject("Sfx2D");
                Object.DontDestroyOnLoad(go);
                source2D = go.AddComponent<AudioSource>();
                source2D.spatialBlend = 0f;
            }
            source2D.PlayOneShot(clip, volume);
        }

        // Positional: enemy shots, distant explosions.
        public static void PlayAt(string name, Vector3 position, float volume = 1f)
        {
            if (!Enabled) return;
            var clip = Get(name);
            if (clip == null) return;
            var go = new GameObject("Sfx3D_" + name);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 10f;
            src.maxDistance = 220f;
            src.Play();
            Object.Destroy(go, clip.length + 0.1f);
        }
    }
}
