using UnityEngine;
using UnityEngine.Rendering;
using SpectreEX.Matrix;

namespace SpectreEX
{
    // The first level's two shortcut pads (_BUILD_WORLD, _CHECK_JUMP_COLLIDE): the built-in fuel shape,
    // spinning 8 steps a tick, labelled "Level 5" and "Level 10". Driving onto one sets the score to
    // 5000 / 10000, unlocks the heavy weapons of the levels before it and ends the level at once.
    public class JumpPad : MonoBehaviour
    {
        public int toLevel;        // 1-based level that follows
        public int score;

        Transform spinner;

        public static JumpPad Create(int toLevel, int score, Vector3 position, Material glow, float edgeThickness)
        {
            var go = new GameObject($"JumpPad_Level{toLevel}");
            go.transform.position = position;
            var pad = go.AddComponent<JumpPad>();
            pad.toLevel = toLevel;
            pad.score = score;

            pad.spinner = new GameObject("Model").transform;
            pad.spinner.SetParent(go.transform, false);
            var shape = BuiltinShapes.Named("FUEL");
            if (shape != null)
            {
                var face = new GameObject("Panels");
                face.transform.SetParent(pad.spinner, false);
                face.transform.localPosition = Vector3.up * 0.05f;
                face.AddComponent<MeshFilter>().sharedMesh = shape.Body;
                var r = face.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                SpectrePalette.Paint(r, SpectrePalette.Entry(0x30), GameManager.Instance.obstacleLook.shapes);   // _FIXUP_COLORS
            }

            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = Vector3.up * 1.6f;
            label.text = $"LEVEL!{toLevel}";   // '!' is the segment font's full-width blank
            label.anchor = TextAnchor.MiddleCenter;
            label.characterSize = 0.15f;
            label.fontSize = 48;
            label.font = Resources.Load<Font>("DSEG14Classic-Bold");
            label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.color = glow != null && glow.HasProperty("_EmissiveColor") ? Color.white : Color.cyan;

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            float side = 0xC00 * PlayerMotor.UnitsToMetres;   // _FLAT_CUBE_COLLIDE: at any height
            col.center = Vector3.up * 50f;
            col.size = new Vector3(side, 100f, side);
            return pad;
        }

        void Update()
        {
            if (spinner != null) spinner.Rotate(0f, -8f * OriginalMath.StepsToDegrees * OriginalMath.TickRate * Time.deltaTime, 0f);
            var cam = Camera.main;
            var label = transform.Find("Label");
            if (cam != null && label != null) label.rotation = Quaternion.LookRotation(label.position - cam.transform.position);
        }

        void OnTriggerEnter(Collider other)
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.Playing || other.GetComponentInParent<PlayerTank>() == null) return;
            gm.TakeJumpPad(this);
        }
    }
}
