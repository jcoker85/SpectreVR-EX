using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    // Spectre VR controls: arrows / WASD / numpad to drive, Space / numpad 0 / LMB to fire the cannon,
    // Alt to fire the heavy weapon, Ctrl to change it, 1-8 to pick one and fire it, Backspace / ~ to
    // hyperspace, numpad 5 / J to jump. Gamepad: left stick, RT or A cannon, X heavy weapon, LB / RB
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

            var k = Keyboard.current;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed || k.numpad8Key.isPressed || k.numpad7Key.isPressed || k.numpad9Key.isPressed || k.homeKey.isPressed || k.pageUpKey.isPressed) throttle += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed || k.numpad2Key.isPressed || k.numpad1Key.isPressed || k.numpad3Key.isPressed || k.endKey.isPressed || k.pageDownKey.isPressed) throttle -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed || k.numpad6Key.isPressed || k.numpad9Key.isPressed || k.numpad3Key.isPressed || k.pageUpKey.isPressed || k.pageDownKey.isPressed) turn += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed || k.numpad4Key.isPressed || k.numpad7Key.isPressed || k.numpad1Key.isPressed || k.homeKey.isPressed || k.endKey.isPressed) turn -= 1f;
                fire |= k.spaceKey.isPressed || k.numpad0Key.isPressed || k.insertKey.isPressed;
                hyper |= k.backspaceKey.wasPressedThisFrame || k.backquoteKey.wasPressedThisFrame;
                jump |= k.numpad5Key.wasPressedThisFrame || k.jKey.wasPressedThisFrame;
                if (k.leftAltKey.wasPressedThisFrame || k.rightAltKey.wasPressedThisFrame) Weapon.PressAlt();
                if (k.leftCtrlKey.wasPressedThisFrame || k.rightCtrlKey.wasPressedThisFrame) Weapon.PressCycle();
                for (int i = 1; i <= 8; i++)
                    if (k[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + i - 1)].wasPressedThisFrame) Weapon.PressWeaponKey(i);
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
