using UnityEngine;
using UnityEngine.AI;
using Vent.Core.Audio;
using Vent.Core.Damage;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Services;
using Vent.Core.Utility;
using Vent.Enemies.Data;
using Vent.Enemies.Spawning;

namespace Vent.Enemies.Runtime
{
    public enum ZombieState
    {
        /// <summary>Inactive in the pool.</summary>
        Dormant,
        /// <summary>Climbing out of a vent; no navigation, no damage.</summary>
        Emerging,
        /// <summary>Shambling around near its vent; has not noticed the player yet.</summary>
        Wandering,
        /// <summary>Pathing toward the player.</summary>
        Chasing,
        /// <summary>In range: wind up, strike, cool down.</summary>
        Attacking,
        /// <summary>Reeling from a heavy hit; stopped and harmless for a moment.</summary>
        Staggered,
        /// <summary>Corpse; waits, sinks, returns to the pool.</summary>
        Dead,
    }

    /// <summary>
    /// The zombie: a NavMesh agent driven by a small explicit state machine.
    ///
    /// Perception is deliberately simple and entirely numeric. A zombie is <em>alerted</em> — and
    /// stays alerted — once the player is within its notice radius with line of sight, within its
    /// sense radius (through walls), within hearing of a gunshot, or has hurt it. Until then it
    /// wanders near its vent. All of those radii and its strike timing come from
    /// <see cref="ZombieStats"/>, which the spawner interpolates from the difficulty profile's
    /// aggression, so an "annoyed" level-1 zombie and an "enraged" level-15 zombie are the same
    /// class with different numbers. This class never reads the level.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(PooledObject))]
    public sealed class Zombie : MonoBehaviour, IDamageable
    {
        [Header("Wiring")]
        [SerializeField] private ZombieDefinition definition;
        [SerializeField] private ZombieRuntimeSet registry;
        [SerializeField] private KillEventChannel killChannel;
        [SerializeField] private NoiseEventChannel noise;
        [SerializeField] private ZombieAnimator animator;

        private NavMeshAgent agent;
        private PooledObject pooled;
        private Collider[] colliders;
        private IPlayerTarget target;

        private ZombieStats stats;
        private float health;
        private ZombieState state = ZombieState.Dormant;
        private float stateTimer;
        private Cooldown attackCooldown;
        private Cooldown repath;
        private float nextGrowlTime;
        private Vector3 emergeFrom;
        private Vector3 emergeTo;
        private bool struckThisAttack;
        private bool alerted;
        private float nextWanderPickTime;

        public ZombieState State => state;
        public ZombieStats Stats => stats;
        public float Health => health;
        public float HealthNormalized => stats.MaxHealth > 0f ? health / stats.MaxHealth : 0f;
        public bool IsAlive => state != ZombieState.Dead && state != ZombieState.Dormant && health > 0f;
        public ZombieDefinition Definition => definition;
        /// <summary>True once the zombie knows where the player is; never resets while alive.</summary>
        public bool IsAlerted => alerted;

        public void Configure(ZombieDefinition def, ZombieRuntimeSet set, KillEventChannel kills, NoiseEventChannel noiseChannel, ZombieAnimator anim)
        {
            definition = def;
            registry = set;
            killChannel = kills;
            noise = noiseChannel;
            animator = anim;
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            pooled = GetComponent<PooledObject>();
            colliders = GetComponentsInChildren<Collider>(includeInactive: true);
            agent.enabled = false;
        }

        private void OnEnable()
        {
            registry?.Add(this);
            noise?.Subscribe(OnNoise);
        }

        private void OnDisable()
        {
            noise?.Unsubscribe(OnNoise);
            registry?.Remove(this);
            state = ZombieState.Dormant;
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>Bring a pooled zombie to life at a vent with the given numbers.</summary>
        public void Spawn(in ZombieStats spawnStats, AirVent vent)
        {
            stats = spawnStats;
            health = stats.MaxHealth;
            target = GameServices.TryGet(out IPlayerTarget t) ? t : null;

            emergeFrom = vent.GratePosition;
            emergeTo = vent.FloorPosition;
            transform.SetPositionAndRotation(emergeFrom, Quaternion.LookRotation(FlattenDirection(vent.Facing)));

            SetCollidersEnabled(true);
            agent.enabled = false;
            agent.speed = stats.Speed;
            agent.stoppingDistance = definition.AttackRange * 0.6f;
            agent.avoidancePriority = Random.Range(30, 70); // varied priorities stop crowds locking into lines

            attackCooldown.Reset();
            repath.Reset();
            struckThisAttack = false;
            alerted = false;
            nextGrowlTime = Time.time + Random.Range(0.5f, 2f);

            animator?.ResetPose();
            EnterState(ZombieState.Emerging);
        }

        /// <summary>Level changed mid-life: adopt new damage/speed but keep the current health fraction.</summary>
        public void Rescale(in ZombieStats newStats)
        {
            float fraction = HealthNormalized;
            stats = newStats;
            health = stats.MaxHealth * fraction;
            agent.speed = state == ZombieState.Wandering ? stats.WanderSpeed : stats.Speed; // legal on a disabled agent
        }

        /// <summary>Make the zombie aware of the player from now on (noise, damage, being seen).</summary>
        public void Alert()
        {
            if (alerted)
            {
                return;
            }

            alerted = true;
            if (state == ZombieState.Wandering)
            {
                EnterState(ZombieState.Chasing);
            }
        }

        /// <summary>Instantly remove (run reset). No kill event, no experience.</summary>
        public void Despawn()
        {
            if (state == ZombieState.Dormant)
            {
                return;
            }

            state = ZombieState.Dormant;
            agent.enabled = false;
            pooled.Release();
        }

        // ------------------------------------------------------------------ state machine

        private void EnterState(ZombieState next)
        {
            state = next;
            stateTimer = 0f;

            switch (next)
            {
                case ZombieState.Emerging:
                    animator?.SetLocomotion(0f);
                    break;

                case ZombieState.Wandering:
                    EnsureAgentOnNavMesh();
                    agent.speed = stats.WanderSpeed;
                    nextWanderPickTime = 0f; // pick a point immediately
                    break;

                case ZombieState.Chasing:
                    EnsureAgentOnNavMesh();
                    agent.speed = stats.Speed;
                    repath.Reset();
                    break;

                case ZombieState.Attacking:
                    if (agent.isOnNavMesh)
                    {
                        agent.isStopped = true;
                    }

                    struckThisAttack = false;
                    animator?.PlayAttack(stats.AttackWindup);
                    SfxPlayer.TryPlayAt(SoundId.ZombieAttack, transform.position, 0.8f);
                    break;

                case ZombieState.Staggered:
                    if (agent.isOnNavMesh)
                    {
                        agent.isStopped = true;
                    }

                    struckThisAttack = true; // an interrupted attack never lands
                    animator?.PlayStagger(definition.StaggerSeconds);
                    break;

                case ZombieState.Dead:
                    agent.enabled = false;
                    SetCollidersEnabled(false);
                    break;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            stateTimer += dt;

            switch (state)
            {
                case ZombieState.Emerging:
                    TickEmerging();
                    break;
                case ZombieState.Wandering:
                    TickWandering();
                    break;
                case ZombieState.Chasing:
                    TickChasing();
                    break;
                case ZombieState.Attacking:
                    TickAttacking();
                    break;
                case ZombieState.Staggered:
                    if (stateTimer >= definition.StaggerSeconds)
                    {
                        EnterState(ZombieState.Chasing);
                    }

                    break;
                case ZombieState.Dead:
                    if (stateTimer >= definition.CorpseSeconds)
                    {
                        state = ZombieState.Dormant;
                        pooled.Release();
                    }

                    break;
            }

            if (IsAlive && Time.time >= nextGrowlTime)
            {
                nextGrowlTime = Time.time + Random.Range(definition.GrowlIntervalRange.x, definition.GrowlIntervalRange.y);
                SfxPlayer.TryPlayAt(SoundId.ZombieGrowl, transform.position, 0.6f);
            }
        }

        private void TickEmerging()
        {
            float t = Mathf.Clamp01(stateTimer / definition.EmergeSeconds);
            // Push out of the duct first, then fall: horizontal eases out, vertical eases in.
            float horizontal = 1f - (1f - t) * (1f - t);
            float vertical = t * t;
            Vector3 flat = Vector3.Lerp(emergeFrom, new Vector3(emergeTo.x, emergeFrom.y, emergeTo.z), horizontal);
            flat.y = Mathf.Lerp(emergeFrom.y, emergeTo.y, vertical);
            transform.position = flat;

            if (t >= 1f)
            {
                EnterState(alerted || CanPerceivePlayer() ? ZombieState.Chasing : ZombieState.Wandering);
            }
        }

        private void TickWandering()
        {
            if (CanPerceivePlayer())
            {
                Alert();
                return;
            }

            if (Time.time >= nextWanderPickTime && agent.isOnNavMesh)
            {
                nextWanderPickTime = Time.time + Random.Range(definition.WanderRepickRange.x, definition.WanderRepickRange.y);
                Vector2 offset = Random.insideUnitCircle * definition.WanderRadius;
                Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, definition.WanderRadius, NavMesh.AllAreas))
                {
                    agent.isStopped = false;
                    agent.SetDestination(hit.position);
                }
            }

            animator?.SetLocomotion(agent.velocity.magnitude / Mathf.Max(0.1f, stats.Speed));
        }

        /// <summary>Sight (needs line of sight) or "sense" (through walls), per the current stats.</summary>
        private bool CanPerceivePlayer()
        {
            if (target == null || !target.IsAlive)
            {
                return false;
            }

            Vector3 eye = transform.position + Vector3.up * 1.5f;
            Vector3 toTarget = target.AimPoint - eye;
            float distance = toTarget.magnitude;
            if (distance <= stats.SenseRadius)
            {
                return true;
            }

            if (distance > stats.NoticeRadius)
            {
                return false;
            }

            return !Physics.Raycast(eye, toTarget / distance, distance, Layers.OcclusionMask, QueryTriggerInteraction.Ignore);
        }

        private void OnNoise(NoiseInfo info)
        {
            if (alerted || !IsAlive)
            {
                return;
            }

            float hearing = stats.HearingRadius * Mathf.Max(0f, info.Loudness);
            if ((info.Position - transform.position).sqrMagnitude <= hearing * hearing)
            {
                Alert();
            }
        }

        private void EnsureAgentOnNavMesh()
        {
            if (!agent.enabled)
            {
                agent.enabled = true;
                agent.Warp(emergeTo);
            }

            if (agent.isOnNavMesh)
            {
                agent.isStopped = false;
            }
        }

        private void TickChasing()
        {
            if (target == null || !target.IsAlive)
            {
                if (agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                }

                animator?.SetLocomotion(0f);
                return;
            }

            if (repath.TryConsume(Time.time, stats.RepathInterval) && agent.isOnNavMesh)
            {
                agent.SetDestination(target.Position);
            }

            animator?.SetLocomotion(agent.velocity.magnitude / Mathf.Max(0.1f, stats.Speed));

            if (InAttackRange() && attackCooldown.IsReady(Time.time))
            {
                EnterState(ZombieState.Attacking);
            }
        }

        private void TickAttacking()
        {
            if (target != null)
            {
                FaceTowards(target.Position, 720f * Time.deltaTime);
            }

            if (!struckThisAttack && stateTimer >= stats.AttackWindup)
            {
                struckThisAttack = true;
                TryStrike();
                attackCooldown.Start(Time.time, stats.AttackCooldown);
            }

            // Recovery: stay in the attack pose briefly, then reassess.
            if (stateTimer >= stats.AttackWindup + 0.25f)
            {
                EnterState(ZombieState.Chasing);
            }
        }

        private bool InAttackRange()
        {
            if (target == null)
            {
                return false;
            }

            Vector3 toTarget = target.Position - transform.position;
            toTarget.y = 0f;
            return toTarget.sqrMagnitude <= definition.AttackRange * definition.AttackRange;
        }

        private void TryStrike()
        {
            if (target == null || !target.IsAlive || !InAttackRange())
            {
                return;
            }

            Vector3 toTarget = FlattenDirection(target.Position - transform.position);
            if (Vector3.Angle(transform.forward, toTarget) > definition.AttackArc)
            {
                return;
            }

            var info = new DamageInfo(stats.Damage, DamageKind.Melee, this, target.AimPoint, -toTarget, toTarget);
            target.Damageable.ApplyDamage(info);
        }

        // ------------------------------------------------------------------ damage

        public DamageResult ApplyDamage(in DamageInfo info)
        {
            if (!IsAlive || info.Amount <= 0f)
            {
                return DamageResult.None;
            }

            float dealt = Mathf.Min(health, info.Amount);
            health -= dealt;
            Alert(); // getting shot ends any doubt about where the player is

            if (health <= 0f)
            {
                Die(info);
                return new DamageResult(dealt, true);
            }

            // Heavy hits and headshots stagger: the zombie stops and reels, so good shots buy time.
            bool heavy = stats.MaxHealth > 0f && dealt / stats.MaxHealth >= definition.StaggerThreshold;
            if ((heavy || info.Headshot) && state != ZombieState.Emerging && state != ZombieState.Staggered)
            {
                EnterState(ZombieState.Staggered);
            }
            else
            {
                animator?.Flinch(info.Direction, info.Headshot ? 1f : 0.5f);
            }

            SfxPlayer.TryPlayAt(SoundId.ZombieHurt, info.Point, 0.5f);
            return new DamageResult(dealt, false);
        }

        private void Die(in DamageInfo killingBlow)
        {
            EnterState(ZombieState.Dead);
            if (killingBlow.Kind == DamageKind.Vehicle)
            {
                animator?.PlayRoadkill(definition.CorpseSeconds, killingBlow.Direction, killingBlow.Impulse);
            }
            else
            {
                animator?.PlayDeath(definition.CorpseSeconds, killingBlow.Direction);
            }

            SfxPlayer.TryPlayAt(SoundId.ZombieDeath, transform.position, 0.9f);
            killChannel?.Raise(new KillInfo(transform.position + Vector3.up, killingBlow.Source, killingBlow.Headshot, stats.Experience));
        }

        // ------------------------------------------------------------------ helpers

        private void FaceTowards(Vector3 worldPoint, float maxDegrees)
        {
            Vector3 dir = FlattenDirection(worldPoint - transform.position);
            if (dir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), maxDegrees);
        }

        private void SetCollidersEnabled(bool enabled)
        {
            foreach (Collider c in colliders)
            {
                c.enabled = enabled;
            }
        }

        private static Vector3 FlattenDirection(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
        }
    }
}
