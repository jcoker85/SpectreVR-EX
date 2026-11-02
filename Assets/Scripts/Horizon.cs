using UnityEngine;

namespace SpectreEX
{
    // The sky's band keeps its distance from the camera, however far the camera goes. _DRAW_SKY: 32
    // rows at the reference 530, black to the sky colour, and none once the eye is 0x800 under the floor.
    public class Horizon : MonoBehaviour
    {
        const float RowsPerRadian = 128f * 530f / 200f;
        const float MeshRadius = 900f, MeshHeight = 260f;
        const float GoneBelow = -0x800 * PlayerMotor.UnitsToMetres;

        Renderer band;

        void Awake()
        {
            band = GetComponent<Renderer>();
            transform.localScale = new Vector3(1f, MeshRadius * 32f / RowsPerRadian / MeshHeight, 1f);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            transform.position = new Vector3(p.x, 0f, p.z);
            band.enabled = p.y > GoneBelow;
        }
    }
}
