using UnityEngine;

namespace SpectreEX.Matrix
{
    // What a matrix obstacle is, for the original's "gate" rules (SVRWIN.EXE _HIT_A_GATE), which
    // PlayerMotor applies when the craft touches one. Solid obstacles (cubes, columns, billboards,
    // windmills) just block; this component also spins the moving parts.
    public class ObstacleBehaviour : MonoBehaviour
    {
        public enum Kind { Static, Acid, Ice, Slow, Teleport, Pusher, UpTransport, DownTransport, Windmill }

        public Kind kind;
        public int type;                   // matrix wrld type
        public int heading;                // the original's 0-255 heading (wrld degrees * 256 / 360)
        public Transform spinner;          // windmill fans, turning towers and transports
        public Vector3 spinAxis = Vector3.up;
        public float spinSpeed = 90f;      // degrees a second

        // Gates are driven over or through; the rest block
        public bool IsGate => kind != Kind.Static && kind != Kind.Windmill;

        long hitSpinUntil = -1;   // a SimClock tick

        // A Spinner shot (weapon 11) sets an obstacle turning 16 steps a tick for 0xB4 ticks
        public void SpinFromHit()
        {
            hitSpinUntil = SimClock.Ticks + 0xB4;
        }

        void Update()
        {
            if (spinner != null) spinner.Rotate(spinAxis, spinSpeed * GameManager.GameDelta, Space.Self);
            if (SimClock.Ticks < hitSpinUntil)
            {
                var model = transform.Find("Model");
                if (model != null) model.Rotate(0f, -16f * 360f / 256f * PlayerMotor.TickRate * GameManager.GameDelta, 0f, Space.Self);
            }
        }
    }
}
