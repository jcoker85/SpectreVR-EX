using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The player's guns, as SVRWIN.EXE's _DO_PLAYER_MOVE / _KEY_WEAPON run them on the 18.2 Hz tick
    // (RobotDirector calls Tick): the cannon fires while its button is held, every MAX_FIRE_TIMER + 1
    // ticks; eight heavy weapons are unlocked level by level, cycled with one key, fired with another,
    // or picked and fired at once with keys 1-8. Every shot costs its weapon's ammo (wval).
    public class TankWeapon : MonoBehaviour
    {
        public Material projectileMaterial;
        public int maxAmmo = 40;

        public const int Weapons = 9;
        public static readonly string[] Names = { "Cannon", "Grenades", "Scatter Shots", "Seekers", "Missiles", "Spinners", "Mines", "Bombs", "Pulse Bursts" };
        static readonly int[] ShotType = { 0, 1, 5, 9, 10, 11, 2, 17, 13 };                 // _ALT_WEAPON_TYPE
        static readonly string[] ShotSound = { Snd.PlayerShot, Snd.RobotShot, Snd.TripleShot, Snd.SeekerShot, Snd.Missile, Snd.SpinnerShot, Snd.MineDrop, Snd.Bomb, Snd.PulseBurst };
        public static readonly int[] IconCell = { 6, 0, 3, 2, 1, 5, 4, 7, 8 };              // _WEAPON_X / 16 in SPCP36
        // _ALLOW_ALT_WEAPON_STRS: which weapon(s) a level's unlock number (its tip number) turns on
        static readonly int[] Unlocks = { 0, 0, 0, 0x10, 0x40, 0, 0, 0, 0, 0, 0x2, 0x8, 0x10, 0x40, 0x20, 0x4, 0xFFFF, 0x80, 0x100, 0 };
        public const float BannerSeconds = 0x36 / OriginalMath.TickRate;                     // _ALT_WEAPON_TIMER

        public int Ammo { get; private set; }
        public int AltWeapon { get; private set; }
        public int Allowed { get; private set; } = 1;
        public float BannerUntil { get; private set; }
        public bool HasAll => (Allowed & 0x1FF) == 0x1FF;

        // Input, set every frame by PlayerTank / DemoPilot; presses are kept until a tick uses them
        public bool FireHeld { get; set; }
        bool altPressed, cyclePressed;
        int keyWeapon;
        int fireTimer;

        PlayerMotor motor;
        Health health;

        void Awake()
        {
            Ammo = maxAmmo;
            motor = GetComponent<PlayerMotor>();
            health = GetComponent<Health>();
        }

        public void Refill() => Ammo = maxAmmo;
        public void AddAmmo(int rounds) => Ammo = Mathf.Clamp(Ammo + rounds, 0, maxAmmo);
        public bool NeedsAmmo => Ammo < maxAmmo;

        public void PressAlt() => altPressed = true;
        public void PressCycle() => cyclePressed = true;
        public void PressWeaponKey(int n) => keyWeapon = n;

        // _PLAY_LEVEL shows what's selected at the start of every life
        public void Announce() => BannerUntil = Time.time + BannerSeconds;

        // A new game: the cannon only
        public void ResetHeavyWeapons()
        {
            Allowed = 1;
            AltWeapon = 0;
        }

        // _ADD_ALT_WEAPON, as each level loads
        public void Unlock(int number)
        {
            if (number > 0 && number < Unlocks.Length) Allowed |= Unlocks[number] | 1;
        }

        // A wings dump hands over Pulse Bursts if nothing heavier is selected and they're unlocked
        public void OfferPulseBursts()
        {
            if (AltWeapon == 0 && (Allowed & 0x100) != 0) Select(8);
        }

        bool IsAllowed(int w) => (Allowed & (1 << w)) != 0;

        void Select(int w)
        {
            AltWeapon = w;
            BannerUntil = Time.time + BannerSeconds;
        }

        static int Cost(int shotType)
        {
            var m = GameManager.Instance != null ? GameManager.Instance.CurrentMatrix : null;
            return m != null && m.Weapons.TryGetValue(shotType, out var w) ? w.ammo : 1;
        }

        public void Tick(ShotSystem shots, int level)
        {
            // Nothing fires while spun by a Spinner, or from a wreck
            if (motor.Spinning || health.IsDead)
            {
                altPressed = cyclePressed = false;
                keyWeapon = fireTimer = 0;
                return;
            }

            // Cycle to the next unlocked heavy weapon (back to none after the last)
            if (cyclePressed)
            {
                cyclePressed = false;
                Sfx.Play(Snd.ChangeWeapon);
                int w = AltWeapon + 1;
                if (w > 8) w = 1;
                while (!IsAllowed(w)) { w++; if (w > 8) w = 0; }
                Select(w);
            }

            // Heavy weapon
            if (altPressed)
            {
                altPressed = false;
                if (AltWeapon != 0) FireHeavy(shots);
            }

            // Cannon: at once when pressed, then every MAX_FIRE_TIMER + 1 ticks while held
            if (!FireHeld) fireTimer = 0;
            else if (fireTimer-- < 1)
            {
                fireTimer = 4 - (Mathf.Max(0, level - 1) >> 4);
                int cost = Cost(0);
                if (Ammo < cost) Sfx.Play(Snd.OutOfAmmo);
                else
                {
                    Sfx.Play(Ammo < 6 ? Snd.LowAmmo : Snd.PlayerShot);
                    Ammo -= cost;
                    shots.PlayerShoot(0);
                }
            }

            // Keys 1-8: pick that weapon and fire it (_KEY_WEAPON)
            if (keyWeapon != 0)
            {
                int w = keyWeapon;
                keyWeapon = 0;
                if (IsAllowed(w))
                {
                    if (w != AltWeapon) Select(w);
                    if (Ammo < Cost(ShotType[w])) Sfx.Play(Snd.ChangeWeapon);
                    else FireHeavy(shots);
                }
            }
        }

        void FireHeavy(ShotSystem shots)
        {
            int type = ShotType[AltWeapon];
            int cost = Cost(type);
            if (Ammo < cost) return;
            Ammo -= cost;
            shots.PlayerShoot(type);
            Sfx.Play(ShotSound[AltWeapon]);
        }
    }
}
