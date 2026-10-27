using UnityEngine;

namespace SpectreEX
{
    // Shared tank movement: forward/back throttle plus yaw, Battlezone style.
    [RequireComponent(typeof(Rigidbody))]
    public class TankMotor : MonoBehaviour
    {
        public float maxSpeed = 18f;
        public float acceleration = 40f;
        public float turnSpeed = 95f;
        public float speedMultiplier = 1f;

        Rigidbody rb;
        float throttle, turn;

        public float CurrentSpeed => new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z).magnitude;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public void SetInput(float throttle, float turn)
        {
            this.throttle = Mathf.Clamp(throttle, -1f, 1f);
            this.turn = Mathf.Clamp(turn, -1f, 1f);
        }

        void FixedUpdate()
        {
            if (rb.isKinematic) return;

            float dt = Time.fixedDeltaTime;
            Vector3 vel = rb.linearVelocity;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            Vector3 target = transform.forward * (throttle * maxSpeed * speedMultiplier);
            flat = Vector3.MoveTowards(flat, target, acceleration * dt);
            rb.linearVelocity = new Vector3(flat.x, vel.y, flat.z);

            // Collisions hand the body spin around Y that nothing else removes, so the craft would keep
            // turning on its own after a bump. Steering is entirely ours: drop any physics spin each step.
            rb.angularVelocity = Vector3.zero;

            // Reverse steering like a real tank when backing up
            float steer = throttle < -0.1f ? -turn : turn;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, steer * turnSpeed * dt, 0f));
        }
    }
}
