using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    // Spectre VR controls: arrows / WASD / numpad to drive, Space / numpad 0 / LMB to fire,
    // Backspace / ~ to hyperspace, numpad 5 / J to jump. Gamepad: left stick, RT or A fire,
    // Y hyperspace, B jump.
    [RequireComponent(typeof(TankMotor), typeof(TankWeapon), typeof(Health))]
    public class PlayerTank : MonoBehaviour
    {
        [Header("Hyperspace")]
        public float hyperspaceRechargeSeconds = 25f;
        public float jumpCost = 0.25f;
        public float jumpVelocity = 9f;

        public float HyperspaceEnergy { get; private set; } = 1f;
        public TankMotor Motor { get; private set; }
        public TankWeapon Weapon { get; private set; }
        public Health Health { get; private set; }

        Rigidbody rb;
        bool airborne;

        void Awake()
        {
            Motor = GetComponent<TankMotor>();
            Weapon = GetComponent<TankWeapon>();
            Health = GetComponent<Health>();
            rb = GetComponent<Rigidbody>();
        }

        public void ResetForLife()
        {
            HyperspaceEnergy = 1f;
            Weapon.Refill();
        }

        bool Grounded => Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, 0.5f, ~0, QueryTriggerInteraction.Ignore);

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.Demo) return;   // DemoPilot is driving
            bool canAct = !Health.IsDead && (gm == null || gm.Playing) && (gm == null || !gm.Paused);
            if (!canAct) { Motor.SetInput(0f, 0f); return; }

            HyperspaceEnergy = Mathf.Min(1f, HyperspaceEnergy + Time.deltaTime / hyperspaceRechargeSeconds);

            float throttle = 0f, turn = 0f;
            bool fire = false, hyper = false, jump = false;

            var k = Keyboard.current;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed || k.numpad8Key.isPressed || k.numpad7Key.isPressed || k.numpad9Key.isPressed) throttle += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed || k.numpad2Key.isPressed) throttle -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed || k.numpad6Key.isPressed || k.numpad9Key.isPressed) turn += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed || k.numpad4Key.isPressed || k.numpad7Key.isPressed) turn -= 1f;
                fire |= k.spaceKey.isPressed || k.numpad0Key.isPressed;
                hyper |= k.backspaceKey.wasPressedThisFrame || k.backquoteKey.wasPressedThisFrame;
                jump |= k.numpad5Key.wasPressedThisFrame || k.jKey.wasPressedThisFrame;
            }

            var m = Mouse.current;
            if (m != null) fire |= m.leftButton.isPressed;

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
            if (hyper) Hyperspace();
            if (jump) Jump();

            bool grounded = Grounded;
            if (airborne && grounded && rb.linearVelocity.y <= 0.1f)
            {
                airborne = false;
                Sfx.Play(Snd.Land);
            }
        }

        void Jump()
        {
            if (HyperspaceEnergy < jumpCost || !Grounded) return;
            HyperspaceEnergy -= jumpCost;
            rb.AddForce(Vector3.up * jumpVelocity, ForceMode.VelocityChange);
            airborne = true;
        }

        // Launching before the cell is full risks blowing up, as in the original.
        void Hyperspace()
        {
            float energy = HyperspaceEnergy;
            HyperspaceEnergy = 0f;
            Derez.Burst(transform.position + Vector3.up, Health.derezMaterial, 30, 6f);
            Sfx.Play(Snd.Warp);

            if (energy < 1f && Random.value > energy)
            {
                Health.TakeDamage(9999);
                return;
            }

            var gm = GameManager.Instance;
            if (gm != null && gm.TryFindSpawnPoint(out var pos, 40f))
            {
                var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                transform.SetPositionAndRotation(pos, rot);
                rb.position = pos;
                rb.rotation = rot;
                rb.linearVelocity = Vector3.zero;
                Derez.Burst(pos + Vector3.up, Health.derezMaterial, 30, 6f);
            }
        }
    }
}
