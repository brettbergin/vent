using UnityEngine;

namespace Vent.Core.Damage
{
    /// <summary>
    /// Place on each collider of a damageable entity. Raycasts hit the collider, find this
    /// component, and forward to the owning <see cref="IDamageable"/> with the zone multiplier.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class Hitbox : MonoBehaviour, IHitbox
    {
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;
        [SerializeField] private bool isHead;

        private IDamageable target;

        public IDamageable Target => target ??= GetComponentInParent<IDamageable>();
        public float DamageMultiplier => damageMultiplier;
        public bool IsHead => isHead;

        /// <summary>Configure at runtime (used by the prefab factory).</summary>
        public void Configure(float multiplier, bool head)
        {
            damageMultiplier = multiplier;
            isHead = head;
        }

        /// <summary>Apply a hit through this zone. Returns <see cref="DamageResult.None"/> if no target.</summary>
        public DamageResult Hit(in DamageInfo info)
        {
            IDamageable t = Target;
            if (t == null || !t.IsAlive)
            {
                return DamageResult.None;
            }

            return t.ApplyDamage(info.WithAmount(info.Amount * damageMultiplier, isHead));
        }

        /// <summary>
        /// Helper for raycast code: resolve a hit collider to a damage receiver. Prefers a Hitbox
        /// (so multipliers apply); falls back to an IDamageable directly on the collider's hierarchy.
        /// </summary>
        public static bool TryResolve(Collider collider, out Hitbox hitbox, out IDamageable damageable)
        {
            hitbox = null;
            damageable = null;
            if (collider == null)
            {
                return false;
            }

            if (collider.TryGetComponent(out hitbox))
            {
                damageable = hitbox.Target;
                return damageable != null;
            }

            damageable = collider.GetComponentInParent<IDamageable>();
            return damageable != null;
        }
    }
}
