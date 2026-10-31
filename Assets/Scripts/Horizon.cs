using UnityEngine;

namespace SpectreEX
{
    // The sky's band keeps its distance from the camera, however far the camera goes.
    public class Horizon : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            transform.position = new Vector3(p.x, 0f, p.z);
        }
    }
}
