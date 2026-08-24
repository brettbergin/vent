using UnityEngine;

namespace Vent.Core.Utility
{
    /// <summary>
    /// A tiny value-type timer. Weapons use it for fire rate, zombies for attack cadence,
    /// the spawner for spawn intervals. Time source is injected so tests can drive it.
    /// </summary>
    public struct Cooldown
    {
        private float readyAt;

        /// <summary>True when the cooldown has elapsed (or never started).</summary>
        public readonly bool IsReady(float now) => now >= readyAt;

        /// <summary>Seconds remaining, clamped at zero.</summary>
        public readonly float Remaining(float now) => Mathf.Max(0f, readyAt - now);

        /// <summary>Start (or restart) the cooldown for <paramref name="duration"/> seconds.</summary>
        public void Start(float now, float duration) => readyAt = now + Mathf.Max(0f, duration);

        /// <summary>Force ready.</summary>
        public void Reset() => readyAt = 0f;

        /// <summary>Try to consume: if ready, restarts and returns true; else false.</summary>
        public bool TryConsume(float now, float duration)
        {
            if (!IsReady(now))
            {
                return false;
            }

            Start(now, duration);
            return true;
        }
    }
}
