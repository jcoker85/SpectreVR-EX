using UnityEngine;

namespace SpectreEX
{
    // Slicer: a spinning blade that doesn't shoot - it rushes you and cuts through your shields,
    // then reels back to line up another pass.
    [RequireComponent(typeof(Rigidbody))]
    public class SlicerAI : EnemyBase
    {
        public float speed = 16f;
        public float spinSpeed = 720f;
        public int contactDamage = 1;
        public Transform blades;

        Rigidbody rb;
        float stunnedUntil;

        protected override void Awake()
        {
            base.Awake();
            rb = GetComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        void Update()
        {
            if (blades != null && !health.IsDead) blades.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }

        void FixedUpdate()
        {
            if (!Active || !PlayerAlive || Time.time < stunnedUntil)
            {
                if (!health.IsDead) rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, new Vector3(0f, rb.linearVelocity.y, 0f), 0.05f);
                return;
            }
            Vector3 dir = Flat(Player.transform.position - transform.position).normalized;
            Vector3 target = dir * speed * Mathf.Lerp(0.8f, 1.3f, Mathf.InverseLerp(1f, 3f, difficulty));
            Vector3 flat = Vector3.MoveTowards(Flat(rb.linearVelocity), target, 30f * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector3(flat.x, rb.linearVelocity.y, flat.z);
        }

        protected override void OnCollisionEnter(Collision c)
        {
            if (health.IsDead) return;
            var victim = c.collider.GetComponentInParent<PlayerTank>();
            if (victim == null) return;
            victim.Health.TakeDamage(contactDamage);
            Sfx.PlayAt(Snd.SlicerSpin, transform.position);
            Vector3 back = Flat(transform.position - victim.transform.position).normalized;
            rb.linearVelocity = back * speed;
            stunnedUntil = Time.time + 1.2f;
        }
    }
}
