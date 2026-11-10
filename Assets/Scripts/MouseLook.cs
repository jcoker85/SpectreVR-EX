using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    public enum MouseMode { None, Capped, Uncapped, Free, Spectator }

    // The remake's mouse controls (Controls page, MOUSE), with the gamepad's right stick doing the same.
    // The original has none of these; they sit on top of its controls, which all keep working.
    //   None:      the mouse only has its buttons.
    //   Capped:    across turns the craft, never faster than the turn keys at full rate.
    //   Uncapped:  across turns the craft as fast as the mouse goes.
    //   Free:      as Uncapped, and up / down tilts the view and the aim, the craft staying level. The
    //              straight-firing weapons go up with it, but never below level: aimed down, they fly
    //              as the original's do.
    //   Spectator: the mouse only looks, from the cockpit or round the craft from behind; the craft
    //              drives and fires as ever.
    // The crosshair is the cockpit's only, as in the original, and always sits where the shots go.
    // Free and Spectator need a view to look from, so from overhead (and the map) they turn the craft
    // as Uncapped does.
    public class MouseLook : MonoBehaviour
    {
        public const string ModeKey = "ctl.mouse", SpeedKey = "ctl.mouse.speed", StickKey = "ctl.stick.speed", InvertKey = "ctl.mouse.invert";
        public static readonly string[] ModeNames = { "None", "Capped", "Uncapped", "Free", "Spectator" };
        public const int MinSpeed = 1, MaxSpeed = 20;

        public static MouseMode Mode
        {
            get => (MouseMode)Mathf.Clamp(PlayerPrefs.GetInt(ModeKey, 0), 0, ModeNames.Length - 1);
            set => Save(ModeKey, (int)value);
        }

        public static int Speed
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SpeedKey, 5), MinSpeed, MaxSpeed);
            set => Save(SpeedKey, Mathf.Clamp(value, MinSpeed, MaxSpeed));
        }

        public static int StickSpeed
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(StickKey, 8), MinSpeed, MaxSpeed);
            set => Save(StickKey, Mathf.Clamp(value, MinSpeed, MaxSpeed));
        }

        public static bool Invert
        {
            get => PlayerPrefs.GetInt(InvertKey, 0) != 0;
            set => Save(InvertKey, value ? 1 : 0);
        }

        static void Save(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }

        // The mode working now, for this view
        public static MouseMode InForce { get; private set; }

        // Free: the aim's tilt, degrees up. Spectator: where the view is turned, degrees right and up.
        // Settable so scripted checks can aim without a mouse.
        public static float AimPitch { get; set; }
        public static float LookYaw { get; set; }
        public static float LookPitch { get; set; }

        // Free: the shots' tilt. Up as far as the aim goes, never below level, which is the original's path.
        public static float ShotPitch => Aiming ? Mathf.Max(0f, AimPitch) : 0f;

        public static bool Aiming => InForce == MouseMode.Free;
        public static bool Spectating => InForce == MouseMode.Spectator;

        const float DegreesPerCount = 0.02f;   // a mouse count's turn at speed 1
        const float StickDegrees = 20f;        // the right stick's degrees a second at speed 1
        const float MaxAim = 75f, MaxLook = 80f;

        PlayerTank tank;
        ViewCamera view;
        MouseMode lastInForce;
        bool locked;

        void Awake() => tank = GetComponent<PlayerTank>();

        void Update()
        {
            if (view == null) view = FindFirstObjectByType<ViewCamera>();
            var gm = GameManager.Instance;
            var viewMode = view != null ? view.mode : ViewMode.Internal;
            bool overhead = viewMode == ViewMode.Overhead || viewMode == ViewMode.Map;
            var setting = Mode;
            InForce = overhead && setting >= MouseMode.Free ? MouseMode.Uncapped : setting;
            if (InForce != lastInForce)
            {
                AimPitch = LookYaw = LookPitch = 0f;
                lastInForce = InForce;
            }

            bool dead = tank == null || tank.Health.IsDead;
            if (dead) AimPitch = LookYaw = LookPitch = 0f;   // each life starts looking ahead
            bool active = InForce != MouseMode.None && gm != null && gm.InGame && gm.Playing && !gm.Paused && !gm.Demo && !dead;
            Lock(active);
            if (!active)
            {
                if (tank != null) tank.Motor.ClearMouseTurn();
                return;
            }

            Vector2 d = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse != null) d += mouse.delta.ReadValue() * (Speed * DegreesPerCount);
            var pad = GameSettings.Joystick ? Gamepad.current : null;
            if (pad != null) d += pad.rightStick.ReadValue() * (StickSpeed * StickDegrees * Time.unscaledDeltaTime);
            if (Invert) d.y = -d.y;

            switch (InForce)
            {
                case MouseMode.Capped:
                    tank.Motor.AddMouseTurn(d.x, false);
                    break;
                case MouseMode.Uncapped:
                    tank.Motor.AddMouseTurn(d.x, true);
                    break;
                case MouseMode.Free:
                    tank.Motor.AddMouseTurn(d.x, true);
                    AimPitch = Mathf.Clamp(AimPitch + d.y, -MaxAim, MaxAim);
                    break;
                case MouseMode.Spectator:
                    LookYaw = Mathf.Repeat(LookYaw + d.x + 180f, 360f) - 180f;
                    LookPitch = Mathf.Clamp(LookPitch + d.y, -MaxLook, MaxLook);
                    break;
            }
        }

        // The pointer is held and hidden while the mouse steers, and free again for the menus
        void Lock(bool on)
        {
            if (on == locked) return;
            locked = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        void OnDisable() => Lock(false);
    }
}
