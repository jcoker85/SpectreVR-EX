using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    // Spectre VR controls, from GameKeys for the keyboard and mouse. Gamepad: left stick, RT or A cannon, X heavy weapon, LB / RB
    // change, Y hyperspace, B jump. PlayerMotor does the moving and TankWeapon the firing, the original's way.
    [RequireComponent(typeof(PlayerMotor), typeof(TankWeapon), typeof(Health))]
    public class PlayerTank : MonoBehaviour
    {
        public float HyperspaceEnergy => Motor.Hyper01;
        public PlayerMotor Motor { get; private set; }
        public TankWeapon Weapon { get; private set; }
        public Health Health { get; private set; }
        public PlayerShields Shields { get; private set; }

        void Awake()
        {
            Motor = GetComponent<PlayerMotor>();
            Weapon = GetComponent<TankWeapon>();
            Health = GetComponent<Health>();
            Shields = GetComponent<PlayerShields>();
            if (Shields == null) Shields = gameObject.AddComponent<PlayerShields>();
            if (GetComponent<PlayerModel>() == null) gameObject.AddComponent<PlayerModel>();
        }

        public void ResetForLife()
        {
            Motor.ResetHyper();
            Weapon.Refill();
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.Demo) return;   // DemoPilot is driving
            bool canAct = !Health.IsDead && (gm == null || gm.Playing) && (gm == null || !gm.Paused);
            if (!canAct) { Motor.SetInput(0f, 0f); Weapon.FireHeld = false; return; }

            float throttle = 0f, turn = 0f;
            bool fire = false, hyper = false, jump = false;

            if (GameKeys.Forward.Held || GameKeys.ForwardLeft.Held || GameKeys.ForwardRight.Held) throttle += 1f;
            if (GameKeys.Back.Held || GameKeys.BackLeft.Held || GameKeys.BackRight.Held) throttle -= 1f;
            if (GameKeys.TurnRight.Held || GameKeys.ForwardRight.Held || GameKeys.BackRight.Held) turn += 1f;
            if (GameKeys.TurnLeft.Held || GameKeys.ForwardLeft.Held || GameKeys.BackLeft.Held) turn -= 1f;
            fire |= GameKeys.Fire.Held;
            hyper |= GameKeys.Hyperspace.Pressed;
            jump |= GameKeys.Jump.Pressed;
            if (GameKeys.HeavyWeapon.Pressed) Weapon.PressAlt();
            if (GameKeys.NextWeapon.Pressed) Weapon.PressCycle();
            for (int i = 1; i <= GameKeys.Weapons.Length; i++)
                if (GameKeys.Weapons[i - 1].Pressed) Weapon.PressWeaponKey(i);

            var g = GameSettings.Joystick ? Gamepad.current : null;
            if (g != null)
            {
                Vector2 stick = g.leftStick.ReadValue();
                throttle += stick.y;
                turn += stick.x;
                fire |= g.rightTrigger.isPressed || g.buttonSouth.isPressed;
                hyper |= g.buttonNorth.wasPressedThisFrame;
                jump |= g.buttonEast.wasPressedThisFrame;
                if (g.buttonWest.wasPressedThisFrame) Weapon.PressAlt();
                if (g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame) Weapon.PressCycle();
            }

            Motor.SetInput(throttle, turn);
            Weapon.FireHeld = fire;
            if (hyper) Motor.RequestHyperspace();
            if (jump) Motor.RequestJump();
        }
    }
}
