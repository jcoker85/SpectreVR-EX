using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX
{
    // Original Spectre VR sound names, in one place so a mis-guessed mapping is a one-line fix.
    // Clips are the original SOUNDS folder's WAVs, copied into GameData/Sounds by the first-run setup.
    public static class Snd
    {
        public const string PlayerShot = "PLYRSHT";
        public const string EnemyShot = "OPSHOT";
        public const string PlayerHit = "PLYRHT";
        public const string EnemyHit = "OPPHIT";
        public const string Explosion = "EXPLO";
        public const string TransportDown = "TNKDWN"; // 0x29: going down a transport
        public const string TransportUp = "TNKUP";     // 0xC: coming back up
        public const string FlagPickup = "FLGPUL";
        public const string OneMoreFlag = "ONEMFLG";
        public const string AmmoPickup = "AMMPCK";
        public const string ShieldPickup = "SHLDPCK";
        public const string OutOfAmmo = "OUTOFAM";
        public const string LowAmmo = "LOONSHT";       // 0xA: a cannon shot with under 6 rounds left
        public const string Warp = "WARP";
        public const string Land = "PLYRLND";
        public const string Collision = "COLLSN";
        public const string Logo = "SPCLOG";            // 0x3A: the game starting up
        public const string LevelStart = "ECYBSHT";     // 0xF: under the level banner
        public const string AccessDenied = "ACCDEN";   // 0x39: no joystick to use
        public const string Welcome = "WELCOME";        // 0x3B: sound turned on
        public const string Leave = "LTRDUDE";          // 0x10: leaving a game
        public const string SlicerBite = "SHKDAM";      // 0x19: touching a Slicer
        public const string Award = "AWARD";
        public const string NewLife = "NEWMAN";         // 0x2A: an extra tank
        public const string TimeTick = "TMTICK";
        public const string SlicerSpin = "SPINNER";
        // Names from SVRWIN.EXE's own sound table, matched to where the code plays them
        public const string Repair = "REPPCKP";      // full repair / wings dump
        public const string Gate = "GATE";           // teleport gate
        public const string IceSlide = "ICESLD";     // driving onto ice
        public const string Goo = "GOOSQ";           // slow pads
        public const string Acid = "ACIDD";          // acid burn
        public const string HitEdge = "HTEDGE";      // arena edge
        // From the EXE's numbered sound table (_SOUNDTABLE, see docs/original-mechanics.md)
        public const string HeavyDamage = "HVDAMG";  // 9: a hit with 20 points or less left
        public const string RobotShot = "GRSHOT";    // 0x18: robot fire other than the plain shot
        public const string SmartMove = "ACIDMV";    // 0x13 / 0x14: Smart Wall / Smart Acid set off
        public const string FlyerDive = "FLYRDV";    // 0x27
        public const string SlicerFar = "SHKLWR";    // 0x1F
        public const string SlicerNear = "SHKLOW";   // 0x20
        public const string SlicerJump = "SHARK";    // 0x21
        public const string PlayerDestroyed = "CYBOVRL"; // 0xD: the craft destroyed
        public const string AllRobots = "ALLBOTS";   // 0x2C: the last robot destroyed
        public const string ChangeWeapon = "CHGWEPN"; // 0x22: heavy weapon changed, or too little ammo
        public const string TripleShot = "TRPLSHT";  // heavy weapons' own sounds (_SHOT_SOUNDS)
        public const string SeekerShot = "SEKRSHT";
        public const string Missile = "MISSL2";
        public const string SpinnerShot = "SPNSHT";
        public const string MineDrop = "MINDRP";
        public const string Bomb = "MINBM";
        public const string PulseBurst = "DBLSTCY";
    }

    public static class Sfx
    {
        static bool enabled = true;

        public static bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                if (!value) Stop();
            }
        }

        // Sound 0: what's playing stops
        public static void Stop()
        {
            if (source2D != null) source2D.Stop();
            if (sourceRestart != null) sourceRestart.Stop();
        }

        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static AudioSource source2D;

        public static AudioClip Get(string name)
        {
            if (!cache.TryGetValue(name, out var clip))
            {
                clip = GameData.Sound(name);
                if (clip == null) Debug.LogWarning("Spectre VR-EX: missing sound " + name);
                cache[name] = clip;
            }
            return clip;
        }

        // Non-positional: player events, voices, UI.
        static AudioSource sourceRestart;

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

        // One sound at a time, cutting off the last: the original's channel, for a sound replayed every tick
        public static void Restart(string name)
        {
            if (!Enabled) return;
            var clip = Get(name);
            if (clip == null) return;
            if (sourceRestart == null)
            {
                var go = new GameObject("SfxRestart");
                Object.DontDestroyOnLoad(go);
                sourceRestart = go.AddComponent<AudioSource>();
                sourceRestart.spatialBlend = 0f;
            }
            sourceRestart.clip = clip;
            sourceRestart.Play();
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
