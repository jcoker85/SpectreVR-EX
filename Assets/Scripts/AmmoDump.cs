using UnityEngine;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // "Those undulating squares on the ground are called ammo dumps." In the original (SVRWIN.EXE
    // _PICK_FUEL_TYPE and the fuel pickup in _PLAY_LEVEL) each dump is one of four kinds, told
    // apart by colour, and it is used up on contact: it re-rolls its kind and moves somewhere far
    // from you (_INVISIBLE_PLACE_XY).
    //   green  - repair 10 damage points, +10 rounds
    //   white  - full repair, full ammo
    //   orange - an orbiting shield (up to four)
    //   cyan   - wings: hover for a while, plus +10 rounds and 10 points of repair
    public class AmmoDump : MonoBehaviour
    {
        public enum Kind { Green, White, Orange, Blue }

        public Transform[] tiles;
        public Kind kind;

        // _PICK_FUEL_TYPE's colours: green, white, orange and, for wings, cyan
        static readonly int[] KindRamps = { 0x20, 0xB0, 0x40, 0x60 };

        public int Ramp => KindRamps[(int)kind];

        // _MOVE_THINGS: +0x65 rises 0x10 a tick from 0x100 to 0x200 and starts again, the square growing
        // to twice its size and back. Its collision box stays the true size.
        float pulse = 0x100;
        Vector3 tileScale;

        void Awake()
        {
            if (tiles != null && tiles.Length > 0) tileScale = tiles[0].localScale;
        }

        void Update()
        {
            pulse += 0x10 * OriginalMath.TickRate * GameManager.GameDelta;
            if (pulse > 0x200) pulse = 0x100;
            float size = pulse / 0x100;
            foreach (var t in tiles) t.localScale = new Vector3(tileScale.x * size, tileScale.y, tileScale.z * size);
        }

        public void SetKind(Kind k)
        {
            kind = k;
            if (tiles == null || GameManager.Instance == null) return;
            foreach (var t in tiles)
            {
                var r = t.GetComponent<Renderer>();
                if (r != null) SpectrePalette.Paint(r, SpectrePalette.Entry(KindRamps[(int)k]), GameManager.Instance.obstacleLook.shapes);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerTank>();
            if (player == null) return;

            switch (kind)
            {
                case Kind.Green:
                    Resupply(player);
                    Sfx.Play(Snd.AmmoPickup);
                    break;
                case Kind.White:
                    player.Health.Restore(player.Health.maxShields);
                    player.Weapon.Refill();
                    Sfx.Play(Snd.Repair);
                    break;
                case Kind.Orange:
                    player.Shields.Add();
                    Sfx.Play(Snd.ShieldPickup);
                    break;
                case Kind.Blue:
                    player.Motor.AddFloating();
                    player.Weapon.OfferPulseBursts();
                    Resupply(player);
                    Sfx.Play(Snd.Repair);
                    break;
            }

            // Used up: a new kind, somewhere well away from you
            var gm = GameManager.Instance;
            if (gm != null) gm.RelocateDump(this);
        }

        static void Resupply(PlayerTank player)
        {
            player.Health.Restore(10);
            player.Weapon.AddAmmo(10);
        }
    }
}
