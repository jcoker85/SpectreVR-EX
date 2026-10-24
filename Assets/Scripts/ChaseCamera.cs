using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    // Smoothed third-person follow; V toggles a Battlezone-style cockpit view.
    public class ChaseCamera : MonoBehaviour
    {
        public Transform target;
        public Vector3 chaseOffset = new Vector3(0f, 4.5f, -11f);
        public Vector3 cockpitOffset = new Vector3(0f, 1.6f, 0.4f);
        public float followSharpness = 6f;
        public float lookAhead = 8f;
        public bool cockpit;

        void LateUpdate()
        {
            if (target == null) return;

            var k = Keyboard.current;
            if (k != null && k.vKey.wasPressedThisFrame) cockpit = !cockpit;

            Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            if (cockpit)
            {
                transform.SetPositionAndRotation(target.position + yaw * cockpitOffset, yaw);
                return;
            }

            Vector3 desired = target.position + yaw * chaseOffset;
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            Vector3 focus = target.position + yaw * Vector3.forward * lookAhead + Vector3.up * 1.2f;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(focus - transform.position), t);
        }
    }
}
