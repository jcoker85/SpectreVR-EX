using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    // WASD/arrows to drive, Space or left mouse to fire. Gamepad: left stick + right trigger / A.
    [RequireComponent(typeof(TankMotor), typeof(TankWeapon), typeof(TankHealth))]
    public class PlayerTankInput : MonoBehaviour
    {
        TankMotor motor;
        TankWeapon weapon;
        TankHealth health;

        void Awake()
        {
            motor = GetComponent<TankMotor>();
            weapon = GetComponent<TankWeapon>();
            health = GetComponent<TankHealth>();
        }

        void Update()
        {
            if (health.IsDead) { motor.SetInput(0f, 0f); return; }

            float throttle = 0f, turn = 0f;
            bool fire = false;

            var k = Keyboard.current;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) throttle += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) throttle -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) turn += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) turn -= 1f;
                fire |= k.spaceKey.isPressed;
            }

            var m = Mouse.current;
            if (m != null) fire |= m.leftButton.isPressed;

            var g = Gamepad.current;
            if (g != null)
            {
                Vector2 stick = g.leftStick.ReadValue();
                throttle += stick.y;
                turn += stick.x;
                fire |= g.rightTrigger.isPressed || g.buttonSouth.isPressed;
            }

            motor.SetInput(throttle, turn);
            if (fire) weapon.TryFire();
        }
    }
}
