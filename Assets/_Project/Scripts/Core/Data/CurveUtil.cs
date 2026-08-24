using System;
using UnityEngine;

namespace Vent.Core.Data
{
    /// <summary>Helpers for building tuning curves from formulas so defaults live in code and stay reviewable.</summary>
    public static class CurveUtil
    {
        /// <summary>
        /// Sample <paramref name="f"/> at every integer from <paramref name="from"/> to <paramref name="to"/>
        /// and build a curve with linear tangents. Because the game only ever evaluates curves at
        /// integer levels, the shape between keys is irrelevant; keys are what matter.
        /// </summary>
        public static AnimationCurve FromFunction(Func<int, float> f, int from, int to)
        {
            var keys = new Keyframe[to - from + 1];
            for (int i = 0; i < keys.Length; i++)
            {
                int x = from + i;
                keys[i] = new Keyframe(x, f(x));
            }

            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtilityCompat.SetLinearTangents(curve, i);
            }

            return curve;
        }

        /// <summary>Evaluate at an integer level, clamping beyond the last key (curves are finite; the game is not).</summary>
        public static float EvaluateLevel(AnimationCurve curve, int level, float fallback = 1f)
        {
            if (curve == null || curve.length == 0)
            {
                return fallback;
            }

            float first = curve[0].time;
            float last = curve[curve.length - 1].time;
            return curve.Evaluate(Mathf.Clamp(level, first, last));
        }
    }

    /// <summary>
    /// Runtime-safe replacement for <c>UnityEditor.AnimationUtility.SetKeyLeftTangentMode</c>.
    /// Computes linear tangents by hand so the same code runs in builds and tests.
    /// </summary>
    internal static class AnimationUtilityCompat
    {
        public static void SetLinearTangents(AnimationCurve curve, int index)
        {
            Keyframe key = curve[index];
            if (index > 0)
            {
                Keyframe prev = curve[index - 1];
                key.inTangent = (key.value - prev.value) / Mathf.Max(1e-5f, key.time - prev.time);
            }

            if (index < curve.length - 1)
            {
                Keyframe next = curve[index + 1];
                key.outTangent = (next.value - key.value) / Mathf.Max(1e-5f, next.time - key.time);
            }

            if (index == 0 && curve.length > 1)
            {
                key.inTangent = key.outTangent;
            }

            if (index == curve.length - 1 && curve.length > 1)
            {
                key.outTangent = key.inTangent;
            }

            key.weightedMode = WeightedMode.None;
            curve.MoveKey(index, key);
        }
    }
}
