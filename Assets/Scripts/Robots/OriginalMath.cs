using UnityEngine;

namespace SpectreEX
{
    // The original's fixed-point helpers (SVRWIN.EXE): 0-255 headings, 256 x cos/sin tables, _ANGLE,
    // _FIX_HEADING and the distance measures the robot AI compares against.
    public static class OriginalMath
    {
        public const float TickRate = PlayerMotor.TickRate;
        public const float UnitsToMetres = PlayerMotor.UnitsToMetres;
        public const float StepsToDegrees = 360f / 256f;

        // 256 * cos / sin of heading * 2pi/256, truncated
        public static readonly int[] Cos = new int[256], Sin = new int[256];
        // _TANT: 256 * tan for the first octant pair, used by _ANGLE's search
        static readonly int[] Tan = new int[65];

        static OriginalMath()
        {
            for (int i = 0; i < 256; i++)
            {
                double a = i * System.Math.PI * 2.0 / 256.0;
                Cos[i] = (int)(256.0 * System.Math.Cos(a));
                Sin[i] = (int)(256.0 * System.Math.Sin(a));
            }
            for (int i = 0; i < 64; i++) Tan[i] = (int)(256.0 * System.Math.Tan(i * System.Math.PI * 2.0 / 256.0));
            Tan[64] = int.MaxValue;
        }

        // _ANGLE: the heading from the origin toward (dx, dy); 0 = +x, 0x40 = +y
        public static int Angle(long dx, long dy)
        {
            if (dy == 0) return dx < 0 ? 0x80 : 0;
            if (dx == 0) return dy < 0 ? 0xC0 : 0x40;
            int quadrant = dx < 1 ? (dy < 1 ? 2 : 1) : (dy < 1 ? 3 : 0);
            long ratio = System.Math.Abs((dy << 8) / dx);
            int i = 0;
            while (Tan[i] < ratio) i++;
            return quadrant switch { 0 => i, 1 => 0x80 - i, 2 => i + 0x80, _ => (0x100 - i) & 0xFF };
        }

        // _FIX_HEADING: turn toward a heading by at most 'limit' steps; returns the turn made
        public static int FixHeading(ref int heading, int want, int limit)
        {
            int d = want - heading;
            if (d > 0x80) d -= 0x100;
            if (d < -0x80) d += 0x100;
            if (d > limit) d = limit;
            else if (d < -limit) d = -limit;
            heading = (heading + d) & 0xFF;
            return d;
        }

        // Signed difference of two headings, -128..127
        public static int HeadingDelta(int a, int b)
        {
            int d = (a - b) & 0xFF;
            return d >= 0x80 ? d - 0x100 : d;
        }

        // _FIND_DIST: (dx>>8)^2 + (dy>>8)^2
        public static long Dist(long dx, long dy)
        {
            long a = dx >> 8, b = dy >> 8;
            return a * a + b * b;
        }

        // _QFIND_DIST: max(|dx|, |dy|) >> 8
        public static int QDist(long dx, long dy) => (int)(System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) >> 8);

        // Unity position <-> original floor units (x = Unity X, y = Unity Z)
        public static long ToUnits(float metres) => (long)System.Math.Round(metres / UnitsToMetres);
        public static float ToMetres(long units) => units * UnitsToMetres;

        // A unit-length Unity direction for a heading
        public static Vector3 Direction(int heading) => new Vector3(Cos[heading & 0xFF], 0f, Sin[heading & 0xFF]) / 256f;

        // Model yaw for a robot heading: the shape's nose (+z local) along the heading
        public static float Yaw(int heading) => 90f - (heading & 0xFF) * StepsToDegrees;
    }
}
