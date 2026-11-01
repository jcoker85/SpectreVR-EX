using UnityEngine;

namespace SpectreEX
{
    public enum ViewMode { Internal, External, Overhead, Map }

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
        public float cockpitSharpness = 30f;   // high: feels locked on, but filters per-physics-step judder
        public float overheadSharpness = 10f;
        public float snapDistance = 12f;       // bigger jumps (hyperspace, teleports) cut instantly
        public string playerModelLayer = "PlayerModel";

        Camera cam;
        int playerMask;
        Vector3 smoothPos;
        float smoothYaw;
        bool hasSmooth;

        void Awake()
        {
            cam = GetComponent<Camera>();
            int layer = LayerMask.NameToLayer(playerModelLayer);
            playerMask = layer >= 0 ? 1 << layer : 0;
        }

        float demoClock, deadTimer;
        Health health;

        // _DRAW_GOD_VIEW: a plain map of everything in the matrix, the flags blinking
        void OnGUI()
        {
            if (mode != ViewMode.Map) return;
            var gm = GameManager.Instance;
            if (gm == null) return;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            float size = Screen.height * 0.6f, half = size * 0.5f;
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float scale = half / gm.arenaHalfSize;
            void Dot(Vector3 p, Color c)
            {
                GUI.color = c;
                GUI.DrawTexture(new Rect(centre.x + p.x * scale - 1f, centre.y - p.z * scale - 1f, 3f, 3f), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            foreach (var r in new[] { new Rect(centre.x - half, centre.y - half, size, 1f), new Rect(centre.x - half, centre.y + half, size, 1f), new Rect(centre.x - half, centre.y - half, 1f, size), new Rect(centre.x + half, centre.y - half, 1f, size) })
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            foreach (var o in gm.Obstacles) if (o != null) Dot(o.transform.position, Color.grey);
            foreach (var d in gm.Dumps) if (d != null) Dot(d.transform.position, Color.green);
            foreach (var r in gm.Robots) if (r != null && !r.IsDead) Dot(r.transform.position, Color.red);
            if (((int)(Time.time * PlayerMotor.TickRate) & 8) != 0)
                foreach (var f in gm.Flags) if (f != null) Dot(f.transform.position, Color.yellow);
            Dot(target.position, Color.cyan);
            GUI.color = Color.white;
        }

        // In demo mode the view moves on every 126 ticks (_PLAY_LEVEL), and a real game starts inside
        void NextMode() => mode = (int)mode >= 2 ? ViewMode.Internal : mode + 1;

        void CycleInDemo()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.Demo)
            {
                demoClock += Time.deltaTime;
                if (demoClock < 126f / PlayerMotor.TickRate) return;
                demoClock = 0f;
                NextMode();
            }
            else if (demoClock > 0f)
            {
                demoClock = 0f;
                mode = ViewMode.Internal;
            }
        }

        void LateUpdate()
        {
            if (target == null) return;

            CycleInDemo();

            if (GameKeys.NextView.Pressed) NextMode();
            float zoom = 0f;
            if (GameKeys.ZoomIn.Held) zoom -= 1f;
            if (GameKeys.ZoomOut.Held) zoom += 1f;
            if (mode == ViewMode.External) externalDistance = Mathf.Clamp(externalDistance + zoom * 15f * Time.deltaTime, 5f, 40f);
            if (mode == ViewMode.Overhead) overheadHeight = Mathf.Clamp(overheadHeight + zoom * 80f * Time.deltaTime, 30f, 320f);

            health = health != null ? health : target.GetComponent<Health>();
            bool dead = health != null && health.IsDead;
            deadTimer = dead ? Mathf.Min(deadTimer + 0x32 * PlayerMotor.TickRate * Time.deltaTime, 0x9C3) : 0f;

            // Hide our own hull from the inside
            if (playerMask != 0)
                cam.cullingMask = mode == ViewMode.Internal && !dead ? cam.cullingMask & ~playerMask : cam.cullingMask | playerMask;

            Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);

            // Smoothed target position and heading for the cockpit and overhead views
            float sharp = mode == ViewMode.Overhead ? overheadSharpness : cockpitSharpness;
            float st = 1f - Mathf.Exp(-sharp * Time.unscaledDeltaTime);
            if (!hasSmooth || Vector3.Distance(smoothPos, target.position) > snapDistance)
            {
                smoothPos = target.position;
                smoothYaw = target.eulerAngles.y;
                hasSmooth = true;
            }
            else
            {
                smoothPos = Vector3.Lerp(smoothPos, target.position, st);
                smoothYaw = Mathf.LerpAngle(smoothYaw, target.eulerAngles.y, st);
            }
            Quaternion smoothRot = Quaternion.Euler(0f, smoothYaw, 0f);

            // _DEAD_TIMER: the view swings round the wreck from further and further up
            if (dead)
            {
                float heading = deadTimer / 16f;
                var turn = Quaternion.Euler(0f, 180f - heading * 360f / 256f, 0f);
                float units = PlayerMotor.UnitsToMetres;
                transform.SetPositionAndRotation(target.position - turn * Vector3.forward * ((deadTimer + 15f) * 32f * units)
                    + Vector3.up * (deadTimer * 0.75f * 16f * units), turn);
                return;
            }

            switch (mode)
            {
                case ViewMode.Internal:
                {
                    // Shift looks to the side, both Shifts behind
                    bool left = GameKeys.LookLeft.Held, right = GameKeys.LookRight.Held;
                    float look = left ? (right ? 180f : -90f) : right ? 90f : 0f;
                    Quaternion eye = smoothRot * Quaternion.Euler(0f, look, 0f);
                    transform.SetPositionAndRotation(smoothPos + smoothRot * cockpitOffset, eye);
                    break;
                }
                case ViewMode.Map:
                    transform.SetPositionAndRotation(smoothPos, smoothRot);
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
                    transform.SetPositionAndRotation(smoothPos + Vector3.up * overheadHeight, Quaternion.Euler(90f, smoothYaw, 0f));
                    break;
            }
        }
    }
}
