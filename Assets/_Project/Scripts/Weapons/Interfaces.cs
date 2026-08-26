using UnityEngine;

namespace Vent.Weapons
{
    /// <summary>
    /// Receives recoil from weapons. Implemented by the player's look component; a turret or an
    /// AI shooter could implement it differently (or ignore it).
    /// </summary>
    public interface IRecoilReceiver
    {
        /// <summary>Kick in degrees: x = pitch (positive = up), y = yaw (positive = right).</summary>
        void AddRecoil(Vector2 pitchYawDegrees);
    }

    /// <summary>
    /// The thing holding the weapon. Weapons query it for the aim ray and for motion state that
    /// drives spread and view-model animation. Deliberately does not expose a transform.
    /// </summary>
    public interface IWeaponHolder
    {
        /// <summary>Ray from the eye along the view direction. Hitscan starts here.</summary>
        Ray AimRay { get; }

        /// <summary>0 = standing still, 1 = sprinting / airborne. Scales movement spread.</summary>
        float MovementFactor { get; }

        bool IsAiming { get; }
        bool IsGrounded { get; }

        /// <summary>Look input this frame (degrees-ish), used for view-model sway.</summary>
        Vector2 LookDelta { get; }

        /// <summary>World velocity, used for view-model bob and inertia.</summary>
        Vector3 Velocity { get; }

        /// <summary>Physics layers the hitscan may hit right now (a driver must not shoot their own car).</summary>
        int ShootMask { get; }
    }
}
