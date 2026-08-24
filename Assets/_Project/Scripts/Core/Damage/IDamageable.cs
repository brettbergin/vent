using UnityEngine;

namespace Vent.Core.Damage
{
    /// <summary>
    /// Anything that can take damage: the player, zombies, (potentially) breakable props.
    /// Weapons only ever talk to this interface, so adding a new enemy type never touches
    /// weapon code.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>True when health is above zero and the object accepts damage.</summary>
        bool IsAlive { get; }

        /// <summary>Apply damage. Implementations decide how to handle overkill, armour, etc.</summary>
        /// <returns>The outcome (actual damage dealt, whether it killed).</returns>
        DamageResult ApplyDamage(in DamageInfo info);
    }

    /// <summary>
    /// A hit-zone forwarder. Zombies have separate head/body colliders; the collider hit by a
    /// raycast carries a <see cref="Hitbox"/> that forwards to the root <see cref="IDamageable"/>
    /// with a multiplier and a headshot flag.
    /// </summary>
    public interface IHitbox
    {
        IDamageable Target { get; }
        float DamageMultiplier { get; }
        bool IsHead { get; }
    }
}
