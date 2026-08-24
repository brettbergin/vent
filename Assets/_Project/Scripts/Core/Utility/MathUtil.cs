using UnityEngine;

namespace Vent.Core.Utility
{
    /// <summary>Small numeric helpers shared across assemblies.</summary>
    public static class MathUtil
    {
        /// <summary>Frame-rate independent exponential smoothing. <paramref name="sharpness"/> ≈ how quickly (per second) we close the gap.</summary>
        public static float Damp(float current, float target, float sharpness, float deltaTime)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));
        }

        /// <inheritdoc cref="Damp(float,float,float,float)"/>
        public static Vector2 Damp(Vector2 current, Vector2 target, float sharpness, float deltaTime)
        {
            return Vector2.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));
        }

        /// <inheritdoc cref="Damp(float,float,float,float)"/>
        public static Vector3 Damp(Vector3 current, Vector3 target, float sharpness, float deltaTime)
        {
            return Vector3.Lerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));
        }

        /// <inheritdoc cref="Damp(float,float,float,float)"/>
        public static Quaternion Damp(Quaternion current, Quaternion target, float sharpness, float deltaTime)
        {
            return Quaternion.Slerp(current, target, 1f - Mathf.Exp(-sharpness * deltaTime));
        }

        /// <summary>Random direction inside a cone of half-angle <paramref name="halfAngleRadians"/> around <paramref name="forward"/>.</summary>
        public static Vector3 RandomInCone(Vector3 forward, float halfAngleRadians)
        {
            if (halfAngleRadians <= 0f)
            {
                return forward;
            }

            Vector2 disc = Random.insideUnitCircle * Mathf.Tan(halfAngleRadians);
            Quaternion look = Quaternion.LookRotation(forward);
            return (look * new Vector3(disc.x, disc.y, 1f)).normalized;
        }

        /// <summary>Evaluate a curve at an integer level, clamping to the curve's last key so curves never need to be infinite.</summary>
        public static float EvaluateClamped(AnimationCurve curve, float x)
        {
            if (curve == null || curve.length == 0)
            {
                return 1f;
            }

            float last = curve[curve.length - 1].time;
            return curve.Evaluate(Mathf.Min(x, last));
        }
    }
}
