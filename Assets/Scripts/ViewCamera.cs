using UnityEngine;
using UnityEngine.InputSystem;

namespace SpectreEX
{
    public enum ViewMode { Internal, External, Overhead }

    // Spectre's three views, cycled with Tab / numpad *: internal (cockpit), external (behind the
    // tank) and overhead map. + / - zoom the external and overhead views.
    public class ViewCamera : MonoBehaviour
    {
        public Transform target;
        public ViewMode mode = ViewMode.Internal;
        public Vector3 cockpitOffset = new Vector3(0f, 1.9f, 0.9f);
        public float externalDistance = 11f;
        public float externalHeight = 4.5f;
        public float overheadHeight = 90f;
        public float followSharpness = 8f;
        public string playerModelLayer = "PlayerModel";

        Camera cam;
        int playerMask;

        void Awake()
        {
            cam = GetComponent<Camera>();
            int layer = LayerMask.NameToLayer(playerModelLayer);
            playerMask = layer >= 0 ? 1 << layer : 0;
        }

        void LateUpdate()
        {
            if (target == null) return;

            var k = Keyboard.current;
            if (k != null)
            {
                if (k.tabKey.wasPressedThisFrame || k.numpadMultiplyKey.wasPressedThisFrame)
                    mode = (ViewMode)(((int)mode + 1) % 3);
                float zoom = 0f;
                if (k.equalsKey.isPressed || k.numpadPlusKey.isPressed) zoom -= 1f;
                if (k.minusKey.isPressed || k.numpadMinusKey.isPressed) zoom += 1f;
                if (mode == ViewMode.External) externalDistance = Mathf.Clamp(externalDistance + zoom * 15f * Time.deltaTime, 5f, 40f);
                if (mode == ViewMode.Overhead) overheadHeight = Mathf.Clamp(overheadHeight + zoom * 80f * Time.deltaTime, 30f, 320f);
            }

            // Hide our own hull from the inside
            if (playerMask != 0)
                cam.cullingMask = mode == ViewMode.Internal ? cam.cullingMask & ~playerMask : cam.cullingMask | playerMask;

            Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            switch (mode)
            {
                case ViewMode.Internal:
                    transform.SetPositionAndRotation(target.position + yaw * cockpitOffset, yaw);
                    break;
                case ViewMode.External:
                {
                    Vector3 desired = target.position + yaw * new Vector3(0f, externalHeight, -externalDistance);
                    transform.position = Vector3.Lerp(transform.position, desired, t);
                    Vector3 focus = target.position + yaw * Vector3.forward * 8f + Vector3.up * 1.2f;
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(focus - transform.position), t);
                    break;
                }
                case ViewMode.Overhead:
                    transform.SetPositionAndRotation(target.position + Vector3.up * overheadHeight, Quaternion.Euler(90f, target.eulerAngles.y, 0f));
                    break;
            }
        }
    }
}
