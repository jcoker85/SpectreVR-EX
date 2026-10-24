using UnityEngine;

namespace SpectreEX
{
    public class Flag : MonoBehaviour
    {
        public float spinSpeed = 70f;
        public float bobHeight = 0.35f;
        public Transform visual;

        Vector3 visualBase;

        void Start()
        {
            if (visual != null) visualBase = visual.localPosition;
        }

        void Update()
        {
            if (visual == null) return;
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * 2f) * bobHeight);
        }

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerTankInput>() == null) return;
            var mat = visual != null ? visual.GetComponentInChildren<Renderer>().sharedMaterial : null;
            Derez.Burst(transform.position + Vector3.up * 2f, mat, 20, 6f);
            gameObject.SetActive(false);
            if (GameManager.Instance != null) GameManager.Instance.OnFlagCollected(this);
        }

        public void Respawn() => gameObject.SetActive(true);
    }
}
