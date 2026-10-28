using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    // Spectre VR controls: arrows / WASD / numpad to drive, Space / numpad 0 / LMB to fire,
    // Backspace / ~ to hyperspace, numpad 5 / J to jump. Gamepad: left stick, RT or A fire,
    // Y hyperspace, B jump. PlayerMotor does the moving, the original's way.
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
            if (!canAct) { Motor.SetInput(0f, 0f); return; }

            float throttle = 0f, turn = 0f;
            bool fire = false, hyper = false, jump = false;

            var k = Keyboard.current;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed || k.numpad8Key.isPressed || k.numpad7Key.isPressed || k.numpad9Key.isPressed) throttle += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed || k.numpad2Key.isPressed || k.numpad1Key.isPressed || k.numpad3Key.isPressed) throttle -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed || k.numpad6Key.isPressed || k.numpad9Key.isPressed || k.numpad3Key.isPressed) turn += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed || k.numpad4Key.isPressed || k.numpad7Key.isPressed || k.numpad1Key.isPressed) turn -= 1f;
                fire |= k.spaceKey.isPressed || k.numpad0Key.isPressed;
                hyper |= k.backspaceKey.wasPressedThisFrame || k.backquoteKey.wasPressedThisFrame;
                jump |= k.numpad5Key.wasPressedThisFrame || k.jKey.wasPressedThisFrame;
            }

            var m = Mouse.current;
            if (m != null)
            {
                fire |= m.leftButton.isPressed;
                jump |= m.rightButton.wasPressedThisFrame;   // the original's second mouse button jumps
            }

            var g = GameSettings.Joystick ? Gamepad.current : null;
            if (g != null)
            {
                Vector2 stick = g.leftStick.ReadValue();
                throttle += stick.y;
                turn += stick.x;
                fire |= g.rightTrigger.isPressed || g.buttonSouth.isPressed;
                hyper |= g.buttonNorth.wasPressedThisFrame;
                jump |= g.buttonEast.wasPressedThisFrame;
            }

            Motor.SetInput(throttle, turn);
            if (fire) Weapon.TryFire();
            if (hyper) Motor.RequestHyperspace();
            if (jump) Motor.RequestJump();
        }
    }
}
