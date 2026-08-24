using UnityEngine;
using Vent.Core.Damage;

namespace Vent.Core.Services
{
    /// <summary>
    /// The enemy-facing view of the player. Registered in <see cref="GameServices"/> by the
    /// player prefab so the Enemies assembly never references the Player assembly.
    /// </summary>
    public interface IPlayerTarget
    {
        /// <summary>Root transform (feet, on the floor).</summary>
        Transform Transform { get; }

        /// <summary>Feet position; what NavMesh agents path towards.</summary>
        Vector3 Position { get; }

        /// <summary>Chest/eye height point; what attacks and line-of-sight checks aim at.</summary>
        Vector3 AimPoint { get; }

        bool IsAlive { get; }

        IDamageable Damageable { get; }
    }
}
