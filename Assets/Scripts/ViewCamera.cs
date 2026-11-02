using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX
{
    public enum ViewMode { Internal, External, Overhead, Map }

    // Spectre's three views, cycled with Tab / numpad *: internal (cockpit), external (a level view
    // from a fixed height behind the tank, trailing it) and overhead (from straight above, always the
    // same way up). + / - zoom the external and overhead views (_EYE_H). Each is where the original puts
    // it on every tick, drawn between ticks rather than jumping from one to the next.
    public class ViewCamera : MonoBehaviour
    {
        public Transform target;
        public ViewMode mode = ViewMode.Internal;
        public Vector3 cockpitOffset = new Vector3(0f, 0.72f, 0f);   // _PLAY_LEVEL: 0x400 above the craft, whose floor height is 0x500
        public float externalDistance = 9.6f;   // _EYE_H[1] 120, x 256
        public float externalHeight = 3.2f;     // 0x2800
        public float overheadHeight = 60f;      // _EYE_H 750, as the demo leaves it; the exe's first value is 500 (40 m)

        const float ExternalNear = 60 * 256 * PlayerMotor.UnitsToMetres, ExternalFar = 400 * 256 * PlayerMotor.UnitsToMetres;
        const float OverheadNear = 100 * 256 * PlayerMotor.UnitsToMetres, OverheadFar = 930 * 256 * PlayerMotor.UnitsToMetres;
        const int ViewDelay = 4;                 // _PUT_VIEW is filled four deep before the first _GET_VIEW

        Camera cam;
        Transform model;

        // From the cockpit the player's own craft isn't there to see, its de-res included
        public static bool CraftHidden { get; private set; }

        void Awake()
        {
            if (GetComponent<MapView>() == null) gameObject.AddComponent<MapView>();
            cam = GetComponent<Camera>();
            cam.usePhysicalProperties = true;
            cam.gateFit = Camera.GateFitMode.Vertical;
            cam.lensShift = new Vector2(0f, CentreDrop);
        }

        long demoSince = -1;
        Health health;
        PlayerMotor motor;
        bool wasDead;

        // In demo mode the view moves on every 127 ticks (_PLAY_LEVEL), and a real game starts inside
        void NextMode()
        {
            mode = (int)mode >= 2 ? ViewMode.Internal : mode + 1;
            if (mode == ViewMode.External) FillView();
        }

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
                if (demoSince < 0) demoSince = SimClock.Ticks;
                if (SimClock.Ticks - demoSince <= 0x7E) return;
                demoSince = SimClock.Ticks;
                if (mode == ViewMode.Internal) externalDistance = 120 * 256 * PlayerMotor.UnitsToMetres;
                if (mode == ViewMode.External) overheadHeight = 750 * 256 * PlayerMotor.UnitsToMetres;   // and it stays there after the demo
                NextMode();
            }
            else if (demoSince >= 0)
            {
                demoSince = -1;
                mode = ViewMode.Internal;
            }
        }

        // _PUT_VIEW / _GET_VIEW: every tick the external view queues an eye behind the craft along its
        // heading, at a fixed height, and looks from the one queued four ticks before
        readonly List<(Vector3 eye, float yaw)> viewQueue = new List<(Vector3, float)>();
        long lastQueued = -1;

        (Vector3, float) ViewFrom(Vector3 craft, float yaw)
        {
            Vector3 eye = craft - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * externalDistance;
            eye.y = externalHeight;
            return (eye, yaw);
        }

        void FillView()
        {
            viewQueue.Clear();
            var entry = ViewFrom(target.position, target.eulerAngles.y);
            for (int i = 0; i <= ViewDelay; i++) viewQueue.Add(entry);
            lastQueued = SimClock.Ticks;
        }

        void OnTick(Vector3 craft, float yaw)
        {
            if (mode != ViewMode.External || (health != null && health.IsDead)) return;
            if (viewQueue.Count == 0) FillView();
            viewQueue.Add(ViewFrom(craft, yaw));
            while (viewQueue.Count > ViewDelay + 1) viewQueue.RemoveAt(0);
            lastQueued = SimClock.Ticks;
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (motor == null)
            {
                motor = target.GetComponent<PlayerMotor>();
                if (motor != null) motor.TickEnded += OnTick;
            }

            CycleInDemo();

            // _CENTER_X / _CENTER_Y: a bounce shakes the whole picture, floor dots and sky included
            if (motor != null) cam.lensShift = new Vector2(motor.CenterX / 320f, CentreDrop + motor.CenterY / 200f);

            if (GameKeys.NextView.Pressed) NextMode();
            float zoom = 0f;
            if (GameKeys.ZoomIn.Held) zoom -= 1f;
            if (GameKeys.ZoomOut.Held) zoom += 1f;
            if (mode == ViewMode.External) externalDistance = Mathf.Clamp(externalDistance + zoom * ZoomRate * GameManager.GameDelta, ExternalNear, ExternalFar);
            if (mode == ViewMode.Overhead) overheadHeight = Mathf.Clamp(overheadHeight + zoom * ZoomRate * GameManager.GameDelta, OverheadNear, OverheadFar);

            health = health != null ? health : target.GetComponent<Health>();
            bool dead = health != null && health.IsDead;
            if (wasDead && !dead && mode == ViewMode.External) FillView();   // _PLAY_LEVEL refills it for each life
            wasDead = dead;
            cam.fieldOfView = mode == ViewMode.Overhead && !dead ? OverheadFov : ViewFov;

            // The original drops any object with a point behind the eye, which from the cockpit is the
            // craft itself. Switched off, not just unseen, so ray tracing can't show it in the floor.
            model = model != null ? model : target.Find("Model");
            CraftHidden = mode == ViewMode.Internal && !dead;
            if (model != null) model.gameObject.SetActive(!CraftHidden);

            // The craft's own transform is already drawn between its steps
            Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);

            // _DEAD_TIMER: 0x32 a tick from the tick the craft went, the view swinging round the wreck
            // from further and further up
            if (dead)
            {
                var gm = GameManager.Instance;
                float ticks = gm != null ? SimClock.Ticks - gm.DeathTick + SimClock.TickFraction + 1f : 1f;
                float deadTimer = Mathf.Min(ticks * 0x32, 0x9C3);
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
                    transform.SetPositionAndRotation(target.position + yaw * cockpitOffset, yaw * Quaternion.Euler(0f, look, 0f));
                    break;
                }
                case ViewMode.Map:
                    break;   // MapView draws it; the rows it leaves keep this view where it was
                case ViewMode.External:
                {
                    if (viewQueue.Count < ViewDelay + 1) FillView();
                    // Between the eye this tick uses and the next tick's, unless the queue has stopped (the banner)
                    var from = viewQueue[0];
                    var to = viewQueue[1];
                    float f = lastQueued == SimClock.Ticks ? SimClock.TickFraction : 1f;
                    transform.SetPositionAndRotation(Vector3.Lerp(from.eye, to.eye, f), Quaternion.Euler(0f, Mathf.LerpAngle(from.yaw, to.yaw, f), 0f));
                    break;
                }
                case ViewMode.Overhead:
                    transform.SetPositionAndRotation(new Vector3(target.position.x, overheadHeight, target.position.z), Quaternion.Euler(90f, 180f, 0f));
                    break;
            }
        }

        void OnDestroy()
        {
            if (motor != null) motor.TickEnded -= OnTick;
        }
    }
}
