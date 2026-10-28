using UnityEngine;

namespace SpectreEX.Matrix
{
    // What the special matrix obstacles do when the player touches them. Solid obstacles (cubes,
    // columns, walls, billboards) need nothing beyond their collider.
    public class ObstacleBehaviour : MonoBehaviour
    {
        public enum Kind { Static, Acid, Teleport, Pusher, UpTransport, DownTransport, Windmill }

        public Kind kind;
        public Transform spinner;          // windmill sails, transport gems
        public float spinSpeed = 90f;
        public float pushSpeed = 22f;
        public float launchSpeed = 16f;

        static float teleportReadyAt;
        float acidNextTick;

        void Update()
        {
            if (spinner != null) spinner.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        }

        void OnTriggerStay(Collider other)
        {
            var player = other.GetComponentInParent<PlayerTank>();
            if (player == null || player.Health.IsDead) return;
            var rb = player.GetComponent<Rigidbody>();

            switch (kind)
            {
                case Kind.Acid:
                    // Burns a shield a second while you sit in it
                    if (Time.time >= acidNextTick)
                    {
                        acidNextTick = Time.time + 1f;
                        player.Health.TakeDamage(1);
                    }
                    break;

                case Kind.Teleport:
                    // Jump to another teleport pad on the grid
                    if (Time.time < teleportReadyAt) break;
                    var target = OtherPad();
                    if (target == null) break;
                    teleportReadyAt = Time.time + 2f;
                    Sfx.Play(Snd.Warp);
                    Derez.Burst(player.transform.position + Vector3.up, player.Health.derezMaterial, 20, 5f);
                    var pos = target.position + target.forward * 4f + Vector3.up * 0.1f;
                    player.transform.position = pos;
                    rb.position = pos;
                    break;

                case Kind.Pusher:
                    // Shoves you along the pad's heading
                    var v = rb.linearVelocity;
                    Vector3 push = transform.forward * pushSpeed;
                    rb.linearVelocity = new Vector3(push.x, v.y, push.z);
                    break;

                case Kind.UpTransport:
                    if (rb.linearVelocity.y < launchSpeed * 0.5f)
                        rb.linearVelocity = new Vector3(rb.linearVelocity.x, launchSpeed, rb.linearVelocity.z);
                    break;

                case Kind.DownTransport:
                    if (rb.linearVelocity.y > -launchSpeed)
                        rb.linearVelocity = new Vector3(rb.linearVelocity.x, -launchSpeed, rb.linearVelocity.z);
                    break;
            }
        }

        Transform OtherPad()
        {
            var gm = GameManager.Instance;
            if (gm == null) return null;
            Transform best = null;
            int seen = 0;
            foreach (var o in gm.Obstacles)
            {
                if (o == null || o == gameObject) continue;
                var b = o.GetComponent<ObstacleBehaviour>();
                if (b == null || b.kind != Kind.Teleport) continue;
                // Pick one of the other pads at random (reservoir sampling)
                if (Random.Range(0, ++seen) == 0) best = o.transform;
            }
            return best;
        }
    }
}
