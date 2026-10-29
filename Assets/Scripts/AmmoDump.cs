using System.Collections.Generic;
using UnityEngine;

namespace SpectreEX
{
    // "Those undulating squares on the ground are called ammo dumps." In the original (SVRWIN.EXE
    // _PICK_FUEL_TYPE and the fuel pickup in _PLAY_LEVEL) each dump is one of four kinds, told
    // apart by colour, and it is used up on contact: it re-rolls its kind and moves somewhere far
    // from you (_INVISIBLE_PLACE_XY).
    //   green  - repair 10 damage points, +10 rounds
    //   white  - full repair, full ammo
    //   orange - an orbiting shield (up to four)
    //   blue   - wings: hover for a while, plus +10 rounds and 10 points of repair
    public class AmmoDump : MonoBehaviour
    {
        public enum Kind { Green, White, Orange, Blue }

        public Transform[] tiles;
        public float rippleHeight = 0.35f;
        public float rippleSpeed = 3f;
        public Kind kind;

        static readonly Color[] KindColors =
        {
            new Color(0.2f, 1f, 0.3f), Color.white, new Color(1f, 0.5f, 0.1f), new Color(0.25f, 0.45f, 1f),
        };
        static readonly Dictionary<(Material, Kind), Material> tinted = new Dictionary<(Material, Kind), Material>();

        Material baseMaterial;

        void Update()
        {
            if (tiles == null) return;
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                Vector3 p = t.localPosition;
                float phase = (p.x + p.z) * 0.9f;
                p.y = 0.05f + (Mathf.Sin(Time.time * rippleSpeed + phase) * 0.5f + 0.5f) * rippleHeight;
                t.localPosition = p;
            }
        }

        public void SetKind(Kind k)
        {
            kind = k;
            if (tiles == null) return;
            foreach (var t in tiles)
            {
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                if (baseMaterial == null) baseMaterial = r.sharedMaterial;
                r.sharedMaterial = Tinted(baseMaterial, k);
            }
        }

        // One tinted copy of the glow material per kind, emission kept at the original strength
        static Material Tinted(Material source, Kind k)
        {
            if (source == null) return null;
            if (tinted.TryGetValue((source, k), out var m) && m != null) return m;
            m = new Material(source) { name = source.name + "_" + k };
            Color c = KindColors[(int)k];
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_EmissiveColor"))
            {
                float strength = source.GetColor("_EmissiveColor").maxColorComponent;
                m.SetColor("_EmissiveColor", c * strength);
            }
            tinted[(source, k)] = m;
            return m;
        }

        void OnTriggerEnter(Collider other)
        {
            var player = other.GetComponentInParent<PlayerTank>();
            if (player == null || player.Health.IsDead) return;

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
