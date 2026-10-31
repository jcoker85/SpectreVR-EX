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
    }
}
