using UnityEngine;

namespace SpectreEX
{
    public enum ViewMode { Internal, External, Overhead, Map }

    // Spectre's three views, cycled with Tab / numpad *: internal (cockpit), external (a level view
    // from a fixed height behind the tank, trailing it) and overhead (from straight above, always the
    // same way up). + / - zoom the external and overhead views (_EYE_H).
    public class ViewCamera : MonoBehaviour
    {
        public Transform target;
        public ViewMode mode = ViewMode.Internal;
        public Vector3 cockpitOffset = new Vector3(0f, 0.72f, 0f);   // _PLAY_LEVEL: 0x400 above the craft, whose floor height is 0x500
        public float externalDistance = 9.6f;
        public float externalHeight = 3.2f;
        public float overheadHeight = 60f;   // _EYE_H 750, as the demo leaves it; the exe's first value is 500 (40 m)
        public float followSharpness = 4.5f;   // the external view runs four ticks behind
        public float cockpitSharpness = 30f;   // high: feels locked on, but filters per-physics-step judder
        public float overheadSharpness = 10f;
        public float snapDistance = 12f;       // bigger jumps (hyperspace, teleports) cut instantly

        Camera cam;
        Transform model;
        Vector3 smoothPos;
        float smoothYaw;
        bool hasSmooth;

        void Awake()
        {
            if (GetComponent<MapView>() == null) gameObject.AddComponent<MapView>();
            cam = GetComponent<Camera>();
            cam.usePhysicalProperties = true;
            cam.gateFit = Camera.GateFitMode.Vertical;
            cam.lensShift = new Vector2(0f, CentreDrop);
        }

        float demoClock, deadTimer;
        Health health;

        // In demo mode the view moves on every 126 ticks (_PLAY_LEVEL), and a real game starts inside
        void NextMode() => mode = (int)mode >= 2 ? ViewMode.Internal : mode + 1;

        // _XFORM3D_TO_2D projects at 128 on the 320x200 screen, 256 from overhead: 2 atan(100 / 128) and
        // 2 atan(100 / 256) top to bottom. That's held on any window, so a wider one just sees further round.
        const float ViewFov = 76f, OverheadFov = 42.68f;

        // _TY: the view's centre is 290 rows down the 530, below the score panel, not mid-window. A
        // fraction of the screen's height, kept by a lens shift so the field of view doesn't change.
        public const float CentreDrop = 290f / 530f - 0.5f;

        const float ZoomRate = 10 * 256 * PlayerMotor.UnitsToMetres * PlayerMotor.TickRate;   // _EYE_H: 10 a tick

        void CycleInDemo()
        {
            var gm = GameManager.Instance;
            if (gm != null && gm.Demo)
            {
                demoClock += Time.deltaTime;
                if (demoClock < 126f / PlayerMotor.TickRate) return;
                demoClock = 0f;
                NextMode();
                if (mode == ViewMode.External) externalDistance = 9.6f;
                if (mode == ViewMode.Overhead) overheadHeight = 60f;   // and it stays there after the demo
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
            if (mode == ViewMode.External) externalDistance = Mathf.Clamp(externalDistance + zoom * ZoomRate * Time.deltaTime, 4.8f, 32f);
            if (mode == ViewMode.Overhead) overheadHeight = Mathf.Clamp(overheadHeight + zoom * ZoomRate * Time.deltaTime, 20f, 186f);

            health = health != null ? health : target.GetComponent<Health>();
            bool dead = health != null && health.IsDead;
            cam.fieldOfView = mode == ViewMode.Overhead && !dead ? OverheadFov : ViewFov;
            deadTimer = dead ? Mathf.Min(deadTimer + 0x32 * PlayerMotor.TickRate * Time.deltaTime, 0x9C3) : 0f;

            // The original drops any object with a point behind the eye, which from the cockpit is the
            // craft itself. Switched off, not just unseen, so ray tracing can't show it in the floor.
            model = model != null ? model : target.Find("Model");
            if (model != null) model.gameObject.SetActive(mode != ViewMode.Internal || dead);

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
                    break;   // MapView draws it; the rows it leaves keep this view where it was
                case ViewMode.External:
                {
                    Vector3 desired = target.position - yaw * Vector3.forward * externalDistance;
                    desired.y = externalHeight;
                    transform.position = Vector3.Lerp(transform.position, desired, t);
                    transform.rotation = Quaternion.Slerp(transform.rotation, yaw, t);
                    break;
                }
                case ViewMode.Overhead:
                    transform.SetPositionAndRotation(new Vector3(smoothPos.x, overheadHeight, smoothPos.z), Quaternion.Euler(90f, 180f, 0f));
                    break;
            }
        }
    }
}
