using UnityEngine;

namespace Vent.Vehicles.Simulation
{
    /// <summary>
    /// A spring and damper on each corner, as arithmetic. Compression is measured from full
    /// extension, so the spring is slack with the wheel hanging and stiffens as it rises; past the
    /// travel a much stiffer bump stop takes over. The force never pulls (a spring cannot hold a car
    /// to the ground), so a car leaving a kerb simply flies.
    /// </summary>
    public static class SuspensionModel
    {
        /// <summary>Upward force on the chassis from one corner, N.</summary>
        /// <param name="compression">Metres the wheel has risen from full extension.</param>
        /// <param name="compressionVelocity">Rate of that rise, m/s (positive while compressing).</param>
        /// <param name="travel">Usable travel, m; beyond it the bump stop adds its own spring.</param>
        public static float Force(float compression, float compressionVelocity, float travel, float spring, float damper, float bumpStopSpring)
        {
            if (compression <= 0f)
            {
                return 0f;
            }

            float force = spring * compression + damper * compressionVelocity;
            float overTravel = compression - travel;
            if (overTravel > 0f)
            {
                force += bumpStopSpring * overTravel;
            }

            return Mathf.Max(0f, force);
        }

        /// <summary>How far each corner sits into its travel with the car at rest: weight over the springs.</summary>
        public static float RestCompression(float mass, float spring, int wheelCount, float gravity = 9.81f)
        {
            return mass * gravity / (Mathf.Max(1, wheelCount) * Mathf.Max(1f, spring));
        }

        /// <summary>Force the anti-roll bar moves from the higher side to the lower one, N, for a difference in compression.</summary>
        public static float AntiRoll(float leftCompression, float rightCompression, float stiffness)
        {
            return (leftCompression - rightCompression) * stiffness;
        }
    }
}
