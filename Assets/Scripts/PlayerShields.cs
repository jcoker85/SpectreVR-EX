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

        readonly OrbitShield[] slots = new OrbitShield[Slots];
        bool spinning;
        float spinSteps;
        Health health;
        TankWeapon weapon;

        public int Count
        {
            get { int n = 0; foreach (var s in slots) if (s != null) n++; return n; }
        }

        void Awake()
        {
            health = GetComponent<Health>();
            weapon = GetComponent<TankWeapon>();
        }

        public void Add()
        {
            for (int i = 0; i < Slots; i++)
            {
                if (slots[i] != null) continue;
                slots[i] = OrbitShield.Create(this, weapon != null ? weapon.projectileMaterial : null);
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
            if (health != null && health.IsDead) { Clear(); return; }
            if (Count < Slots) spinning = false;
            if (spinning) spinSteps = Mathf.Repeat(spinSteps + SpinStepsPerTick * PlayerMotor.TickRate * Time.deltaTime, 256f);
            for (int i = 0; i < Slots; i++) Place(i);
        }

        void Place(int i)
        {
            var s = slots[i];
            if (s == null) return;
            // Slot 0 dead ahead; each slot a quarter turn further left
            float a = (i * 64 + spinSteps) * (360f / 256f) * Mathf.Deg2Rad;
            Vector3 local = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius + Vector3.up * 0.8f;
            s.transform.position = transform.TransformPoint(local);
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
    // through its collider), a trigger that robots touch and enemy bolts are stopped by.
    public class OrbitShield : MonoBehaviour
    {
        public Team team = Team.Player;
        PlayerShields owner;

        public static OrbitShield Create(PlayerShields owner, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Orbit Shield";
            go.transform.localScale = Vector3.one * 0.9f;
            go.GetComponent<Collider>().isTrigger = true;
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var s = go.AddComponent<OrbitShield>();
            s.owner = owner;
            return s;
        }

        void Update()
        {
            if (owner == null) { Destroy(gameObject); return; }
            foreach (var c in Physics.OverlapSphere(transform.position, 0.6f, ~0, QueryTriggerInteraction.Collide))
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
