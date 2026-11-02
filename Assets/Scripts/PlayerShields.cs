using UnityEngine;

namespace SpectreEX
{
    // Orange ammo dumps: up to four shields circle the craft (SVRWIN.EXE weapon 7 shots, kept in
    // place by _MOVE_SHOTS). Each sits 0x3C table steps out - about 4.8 m - in front, left, behind
    // and right; with all four up, a fifth pickup sets them spinning (8 heading steps a tick) until
    // one is lost. A shield destroys any enemy shot that reaches it and stays; it hits the first
    // robot it touches for weapon 7's damage, and is gone after that or after touching an obstacle.
    public class PlayerShields : MonoBehaviour
    {
        public const float Radius = 0x3C * 256 * PlayerMotor.UnitsToMetres;
        const int Slots = 4;
        const float SpinStepsPerTick = 8f;
        const float Lift = 0x100 * PlayerMotor.UnitsToMetres;

        readonly OrbitShield[] slots = new OrbitShield[Slots];
        bool spinning;
        float spinSteps;
        PlayerMotor motor;

        public int Count
        {
            get { int n = 0; foreach (var s in slots) if (s != null) n++; return n; }
        }

        void Awake()
        {
            motor = GetComponent<PlayerMotor>();
        }

        public void Add()
        {
            for (int i = 0; i < Slots; i++)
            {
                if (slots[i] != null) continue;
                slots[i] = OrbitShield.Create(this);
                GameManager.Instance.GetComponent<RobotDirector>().Shots.MyShots++;   // _MY_SHOOT counts it
                Place(i);
                return;
            }
            spinning = true;   // all four up already
        }

        public void Clear()
        {
            for (int i = 0; i < Slots; i++)
                if (slots[i] != null) Destroy(slots[i].gameObject);
            spinning = false;
            spinSteps = 0f;
        }

        void OnDestroy() => Clear();

        void LateUpdate()
        {
            if (Count < Slots)
            {
                spinning = false;
                spinSteps = 0f;
            }
            if (spinning) spinSteps = Mathf.Repeat(spinSteps + SpinStepsPerTick * PlayerMotor.TickRate * GameManager.GameDelta, 256f);
            for (int i = 0; i < Slots; i++) Place(i);
        }

        void Place(int i)
        {
            var s = slots[i];
            if (s == null) return;
            // Slot 0 dead ahead; each slot a quarter turn further left
            float a = (i * 64 + spinSteps) * (360f / 256f) * Mathf.Deg2Rad;
            Vector3 local = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius + Vector3.up * Lift;
            s.transform.SetPositionAndRotation(transform.TransformPoint(local), Quaternion.Euler(0f, OriginalMath.Yaw(motor.OriginalHeading), 0f));
        }

        public static int Damage
        {
            get
            {
                var m = GameManager.Instance != null ? GameManager.Instance.CurrentMatrix : null;
                return m != null && m.Weapons.TryGetValue(7, out var w) && w.damage > 0 ? w.damage : 8;
            }
        }
    }

    // One orbiting shield: its own root object (a child of the player would collect flags and dumps
    // through its collider), a trigger that robots touch and enemy bolts are stopped by. _DO_SHOOT
    // gives weapon 7 the teleport gate's shape in orange, starting a quarter turn over and rolling 0x10 a tick.
    public class OrbitShield : MonoBehaviour
    {
        public Team team = Team.Player;
        const int ShapeType = 19, Colour = 0x40, StartRoll = 0x40, RollRate = 0x10;

        PlayerShields owner;
        Transform visual;
        BoxCollider box;
        float roll = StartRoll;

        public static OrbitShield Create(PlayerShields owner)
        {
            var gm = GameManager.Instance;
            var go = new GameObject("Orbit Shield");
            var s = go.AddComponent<OrbitShield>();
            s.owner = owner;
            s.visual = new GameObject("Visual").transform;
            s.visual.SetParent(go.transform, false);

            bool any = false;
            var bounds = new Bounds();
            foreach (var part in gm.Shapes.ForType(ShapeType))
            {
                var mesh = part.shape.Coloured(Colour);
                var panels = new GameObject("Panels");
                panels.transform.SetParent(s.visual, false);
                panels.transform.localPosition = part.offset;
                panels.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = panels.AddComponent<MeshRenderer>();
                r.sharedMaterial = gm.obstacleLook.shapes;
                var b = new Bounds(mesh.bounds.center + part.offset, mesh.bounds.size);
                if (any) bounds.Encapsulate(b);
                else bounds = b;
                any = true;
            }

            s.box = go.AddComponent<BoxCollider>();
            s.box.isTrigger = true;
            s.box.center = bounds.center;
            s.box.size = bounds.size;
            return s;
        }

        void Update()
        {
            if (owner == null) { Destroy(gameObject); return; }
            roll = Mathf.Repeat(roll + RollRate * PlayerMotor.TickRate * GameManager.GameDelta, 256f);
            visual.localRotation = Quaternion.Euler(0f, 0f, roll * OriginalMath.StepsToDegrees);
            foreach (var c in Physics.OverlapBox(transform.TransformPoint(box.center), box.size * 0.5f, transform.rotation, ~0, QueryTriggerInteraction.Collide))
            {
                // Obstacles end a shield (_MOVE_SHOTS), except the floor pads it passes over
                var obstacle = c.GetComponentInParent<Matrix.ObstacleBehaviour>();
                if (obstacle != null)
                {
                    var k = obstacle.kind;
                    if (k == Matrix.ObstacleBehaviour.Kind.Acid || k == Matrix.ObstacleBehaviour.Kind.Ice ||
                        k == Matrix.ObstacleBehaviour.Kind.Slow || k == Matrix.ObstacleBehaviour.Kind.Pusher) continue;
                    Absorb();
                    return;
                }
                if (c.isTrigger) continue;
                var robot = c.GetComponentInParent<EnemyBase>();
                if (robot == null || robot.Health == null || robot.Health.IsDead) continue;
                GameManager.Instance.GetComponent<RobotDirector>().Shots.MyHits++;
                robot.Health.TakeDamage(PlayerShields.Damage);
                Absorb();
                return;
            }
        }

        public void Absorb()
        {
            Destroy(gameObject);
        }
    }
}
