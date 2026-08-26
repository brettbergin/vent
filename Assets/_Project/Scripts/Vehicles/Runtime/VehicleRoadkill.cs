using System;
using System.Collections.Generic;
using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Damage;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;

namespace Vent.Vehicles.Runtime
{
    /// <summary>
    /// Running things over. Zombies are NavMeshAgents with static hitboxes, so the physics engine
    /// cannot shove them and the collision matrix lets cars pass straight through; instead the
    /// moving car sweeps a box over its body every physics step and applies damage in code, exactly
    /// the way a zombie's own swing does. Damage scales with speed and is fatal above the lethal
    /// speed; each body is hit at most once per pass. Kills are credited to the car (level credit,
    /// no weapon XP, like the Nuke perk).
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleRoadkill : MonoBehaviour
    {
        [Header("Sweep")]
        [SerializeField, Tooltip("Centre of the sweep box in car space.")]
        private Vector3 boxCenter = new(0f, 0.8f, 0f);
        [SerializeField, Tooltip("Half extents of the sweep box in car space.")]
        private Vector3 boxHalfExtents = new(1.0f, 0.9f, 2.4f);

        [Header("Presentation")]
        [SerializeField] private GameObject bloodPrefab;

        private static readonly Collider[] Buffer = new Collider[64];

        private VehicleController controller;
        private readonly Dictionary<IDamageable, float> lastHit = new();
        private readonly List<IDamageable> expired = new();

        /// <summary>Raised for every body the car actually damaged.</summary>
        public event Action<RoadkillInfo> Hit;

        public void Configure(GameObject blood, Vector3 center, Vector3 halfExtents)
        {
            bloodPrefab = blood;
            boxCenter = center;
            boxHalfExtents = halfExtents;
        }

        private void Awake() => controller = GetComponent<VehicleController>();

        private void FixedUpdate()
        {
            if (!controller.IsOccupied || controller.Definition == null)
            {
                return;
            }

            float speed = Mathf.Abs(controller.ForwardSpeed);
            if (speed < controller.Definition.RoadkillMinSpeed)
            {
                return;
            }

            Vector3 centre = transform.TransformPoint(boxCenter);
            int n = Physics.OverlapBoxNonAlloc(centre, boxHalfExtents, Buffer, transform.rotation, 1 << Layers.ZombieIndex, QueryTriggerInteraction.Ignore);
            if (n == 0)
            {
                return;
            }

            float now = Time.time;
            Vector3 direction = controller.Body.linearVelocity;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            float amount = controller.Definition.RoadkillDamage(speed);

            for (int i = 0; i < n; i++)
            {
                Collider col = Buffer[i];
                if (!Hitbox.TryResolve(col, out _, out IDamageable target) || target == null)
                {
                    continue;
                }

                if (lastHit.TryGetValue(target, out float when) && now - when < controller.Definition.RehitSeconds)
                {
                    continue;
                }

                lastHit[target] = now;
                Vector3 point = col.ClosestPoint(centre);
                var info = new DamageInfo(amount, DamageKind.Vehicle, controller, point, -direction, direction, headshot: false, impulse: speed);
                DamageResult result = target.ApplyDamage(info);
                if (result.Ignored)
                {
                    continue;
                }

                float loss = controller.Definition.RoadkillSpeedLoss * (result.Killed ? 1f : 0.4f);
                controller.Body.linearVelocity *= 1f - loss;
                Splash(point, direction);
                SfxPlayer.TryPlayAt(SoundId.Roadkill, point, 0.9f);
                Hit?.Invoke(new RoadkillInfo(point, speed, result.Killed));
            }

            Forget(now);
        }

        private void Splash(Vector3 point, Vector3 direction)
        {
            if (bloodPrefab == null || !GameServices.TryGet(out PoolRegistry pools))
            {
                return;
            }

            pools.Spawn(bloodPrefab, point, Quaternion.LookRotation(-direction));
        }

        /// <summary>Drop stale entries so a long drive never grows the table.</summary>
        private void Forget(float now)
        {
            expired.Clear();
            foreach (KeyValuePair<IDamageable, float> entry in lastHit)
            {
                if (now - entry.Value > 5f)
                {
                    expired.Add(entry.Key);
                }
            }

            foreach (IDamageable key in expired)
            {
                lastHit.Remove(key);
            }
        }
    }
}
