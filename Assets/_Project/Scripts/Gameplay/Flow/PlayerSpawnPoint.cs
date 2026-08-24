using UnityEngine;

namespace Vent.Gameplay.Flow
{
    /// <summary>Marks where the player starts in the Building scene. Forward = initial view direction.</summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        public Vector3 Position => transform.position;
        public float Yaw => transform.eulerAngles.y;

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 1.5f);
        }
    }
}
