using UnityEngine;

namespace Vent.UI
{
    /// <summary>Slow orbit for the main-menu backdrop camera.</summary>
    public sealed class MenuCameraOrbit : MonoBehaviour
    {
        [SerializeField] private Vector3 pivot = Vector3.up * 1.2f;
        [SerializeField, Min(0.1f)] private float radius = 5f;
        [SerializeField] private float degreesPerSecond = 6f;
        [SerializeField] private float height = 1.6f;

        private float angle;

        public void Configure(Vector3 pivotPoint, float orbitRadius, float orbitHeight)
        {
            pivot = pivotPoint;
            radius = orbitRadius;
            height = orbitHeight;
        }

        private void Update()
        {
            angle += degreesPerSecond * Time.unscaledDeltaTime;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 pos = pivot + new Vector3(Mathf.Cos(rad) * radius, height, Mathf.Sin(rad) * radius);
            transform.position = pos;
            transform.LookAt(pivot);
        }
    }
}
