using System.Collections.Generic;
using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // Runs the robots and their shots on the original's 18.2 Hz tick, in SVRWIN.EXE's order: every
    // robot in spawn order (_MOVE_TANKS), then every shot (_MOVE_SHOTS). Lives on the GameManager.
    public class RobotDirector : MonoBehaviour
    {
        public readonly List<Robot> Robots = new List<Robot>();
        public ShotSystem Shots { get; private set; }
        ViewCamera view;

        GameManager gm;

        void Awake()
        {
            gm = GetComponent<GameManager>();
            Shots = new ShotSystem(this);
            if (Camera.main != null) view = Camera.main.GetComponent<ViewCamera>();
        }

        public void Clear()
        {
            Robots.Clear();
            Shots.Clear();
        }

        // How far through the tick this frame is drawn
        public void Present(float t)
        {
            if (gm == null || !gm.Playing) return;
            foreach (var r in Robots) if (r != null) r.Interpolate(t);
            Shots.Interpolate(t);
        }

        // One original tick, from SimClock
        public void SimTick()
        {
            if (gm == null || !gm.Playing) return;
            if (gm.Paused)
            {
                foreach (var r in Robots) if (r != null) { r.BeginTick(); r.EndTick(); }
                Shots.Hold();
                return;
            }
            bool needChaser = true;
            foreach (var r in Robots) if (r != null && r.mode == 3) needChaser = false;
            foreach (var r in Robots)
            {
                if (r == null) continue;
                r.BeginTick();
                r.Tick(this, ref needChaser);
                r.EndTick();
            }
            gm.player.Shields.Tick();
            Shots.Tick();
        }

        public void FireWeapons()
        {
            if (gm != null && gm.Playing && !gm.Paused) gm.player.Weapon.Tick(Shots, gm.Level);
        }

        // ---------- what the robots know about the player ----------

        public PlayerMotor Motor => gm.player != null ? gm.player.Motor : null;
        public bool PlayerAlive => gm.player != null && !gm.player.Health.IsDead;
        public Vector3 PlayerPos => Motor != null ? Motor.SimPosition : Vector3.zero;
        public long PlayerZ => Motor != null ? Motor.ZUnits - PlayerMotor.GroundUnits : 0;   // the object's height, 0 on the floor
        public bool PlayerOnGround => Motor == null || Motor.LowForRobots;
        public bool PlayerAirborne => Motor != null && Motor.Airborne;
        public bool PlayerUnder => Motor != null && Motor.Under;
        public bool PlayerUnderInCockpit => PlayerUnder && (view == null || view.mode == ViewMode.Internal);
        public bool PlayerFloating => Motor != null && Motor.Floating > 0;

        // _GUESS_AT: where the player's _SPEED and heading would put it, d2 / 0x2000 ticks on
        public Vector3 Lead(long d2) => Motor == null ? PlayerPos
            : PlayerPos + Motor.transform.forward * ((d2 / 0x2000) * 256 * Motor.CommandSpeed * OriginalMath.UnitsToMetres);

        // _BLOCK_AT: a point this far along the player's object heading, which is the way it's going
        public Vector3 PointAhead(int units) => PlayerPos + (Motor != null ? Motor.transform.forward : Vector3.forward) * (units * OriginalMath.UnitsToMetres);

        public bool FlagFor(int i, out Vector3 position)
        {
            position = default;
            if (i < 0 || i >= gm.Flags.Count || gm.Flags[i] == null || !gm.Flags[i].isActiveAndEnabled) return false;
            position = gm.Flags[i].transform.position;
            return true;
        }
    }
}
