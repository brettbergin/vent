using UnityEngine;

namespace Vent.Weapons.Runtime
{
    /// <summary>Engine-free gun arithmetic, kept pure so it is unit tested.</summary>
    public static class Ballistics
    {
        /// <summary>
        /// Damage multiplier at <paramref name="distance"/>: 1 up to <paramref name="falloffStart"/>,
        /// falling linearly to <paramref name="minScale"/> at <paramref name="falloffEnd"/>, and flat after.
        /// </summary>
        public static float DamageScale(float distance, float falloffStart, float falloffEnd, float minScale)
        {
            minScale = Mathf.Clamp01(minScale);
            if (distance <= falloffStart || falloffEnd <= falloffStart)
            {
                return 1f;
            }

            float t = Mathf.Clamp01((distance - falloffStart) / (falloffEnd - falloffStart));
            return Mathf.Lerp(1f, minScale, t);
        }

        /// <summary>
        /// Recoil multiplier for the Nth consecutive shot: 1 for the first, rising linearly to
        /// <paramref name="maxMultiplier"/> by <paramref name="rampShots"/> shots. The gun "climbs".
        /// </summary>
        public static float RecoilRamp(int consecutiveShots, int rampShots, float maxMultiplier)
        {
            if (rampShots <= 1 || maxMultiplier <= 1f)
            {
                return 1f;
            }

            float t = Mathf.Clamp01((consecutiveShots - 1) / (float)(rampShots - 1));
            return Mathf.Lerp(1f, maxMultiplier, t);
        }

        /// <summary>Rounds in the gun after a reload: a full magazine, plus one in the chamber if it was not empty.</summary>
        public static int RoundsAfterReload(int magazineSize, bool hadRoundChambered) => magazineSize + (hadRoundChambered ? 1 : 0);
    }
}
