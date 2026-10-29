using System.Collections.Generic;
using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // Runs the robots and their shots on the original's 18.2 Hz tick, in SVRWIN.EXE's order: every
    // robot in spawn order (_MOVE_TANKS), then every shot (_MOVE_SHOTS). Lives on the GameManager.
    public class RobotDirector : MonoBehaviour
    {
        const int MaxTicksPerFrame = 8;

        public readonly List<Robot> Robots = new List<Robot>();
        public ShotSystem Shots { get; private set; }

        GameManager gm;
        float accumulator;

        void Awake()
        {
            gm = GetComponent<GameManager>();
            Shots = new ShotSystem(this);
        }

        public void Clear()
        {
            Robots.Clear();
            Shots.Clear();
        }

        void Update()
        {
            if (gm == null || !gm.Playing) return;
            accumulator += Time.deltaTime * OriginalMath.TickRate;
            int ticks = 0;
            while (accumulator >= 1f && ticks < MaxTicksPerFrame)
            {
                accumulator -= 1f;
                ticks++;
                Tick();
            }
            if (ticks == MaxTicksPerFrame) accumulator = 0f;
            foreach (var r in Robots) if (r != null) r.Interpolate(accumulator);
            Shots.Interpolate(accumulator);
        }

        void Tick()
        {
            if (PlayerAlive && !gm.Paused) gm.player.Weapon.Tick(Shots, gm.Level);
            bool needChaser = true;
            foreach (var r in Robots) if (r != null && r.mode == 3) needChaser = false;
            foreach (var r in Robots)
            {
                if (r == null) continue;
                r.BeginTick();
                r.Tick(this, ref needChaser);
                r.EndTick();
            }
            Shots.Tick();
        }

        // ---------- what the robots know about the player ----------

        public PlayerMotor Motor => gm.player != null ? gm.player.Motor : null;
        public bool PlayerAlive => gm.player != null && !gm.player.Health.IsDead;
        public Vector3 PlayerPos => Motor != null ? Motor.SimPosition : Vector3.zero;
        public long PlayerZ => Motor != null ? Motor.ZUnits : 0;
        public bool PlayerOnGround => Motor == null || Motor.OnGround;
        public bool PlayerAirborne => Motor != null && Motor.Airborne;
        public bool PlayerUnder => Motor != null && Motor.Under;
        public bool PlayerFloating => Motor != null && Motor.Floating > 0;

        // _GUESS_AT: where the player will be, d2 / 0x2000 ticks on
        public Vector3 Lead(long d2) => PlayerPos + (Motor != null ? Motor.TickDelta : Vector3.zero) * (d2 / 0x2000);

        // _BLOCK_AT: a point this far along the player's (model) heading. The original keeps the
        // player's model heading at the move heading + 0x80, so this lands behind the craft.
        public Vector3 PointAhead(int units) => PlayerPos - (Motor != null ? Motor.transform.forward : Vector3.forward) * (units * OriginalMath.UnitsToMetres);

        public bool FlagFor(int i, out Vector3 position)
        {
            position = default;
            if (i < 0 || i >= gm.Flags.Count || gm.Flags[i] == null || !gm.Flags[i].isActiveAndEnabled) return false;
            position = gm.Flags[i].transform.position;
            return true;
        }
    }
}
