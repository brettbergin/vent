using UnityEngine;
using Vent.Core.Audio;
using Vent.Core.Damage;
using Vent.Core.Events;
using Vent.Core.Pooling;
using Vent.Core.Utility;
using Vent.Weapons.Data;
using Vent.Weapons.Progression;
using Vent.Weapons.VFX;
using Vent.Weapons.View;

namespace Vent.Weapons.Runtime
{
    public enum WeaponState
    {
        /// <summary>Not the active weapon; GameObject is inactive.</summary>
        Holstered,
        /// <summary>Being raised; cannot fire yet.</summary>
        Drawing,
        /// <summary>Can fire or reload.</summary>
        Ready,
        Reloading,
    }

    /// <summary>
    /// Runtime instance of a gun. Created from a <see cref="WeaponDefinition"/> by
    /// <see cref="WeaponInventory"/>; owns ammo, level, bloom and the view-model.
    ///
    /// Firing is hitscan: a single <see cref="Physics.Raycast"/> from the holder's aim ray,
    /// perturbed by the current spread cone. Hits resolve through <see cref="Hitbox"/> so
    /// headshot multipliers apply without the weapon knowing anything about zombies.
    ///
    /// The state machine is a plain enum + switch: with four states and two timers, classes
    /// per state would obscure more than they clarify.
    /// </summary>
    public sealed class Weapon : MonoBehaviour
    {
        private const float MaxRaycastFallback = 200f;

        public WeaponDefinition Definition { get; private set; }
        public WeaponProgression Progression { get; private set; }
        public WeaponStats Stats { get; private set; }
        public WeaponState State { get; private set; } = WeaponState.Holstered;
        public int SlotIndex { get; private set; }
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }

        /// <summary>Current cone half-angle in degrees, including movement and bloom.</summary>
        public float CurrentSpreadDegrees { get; private set; }

        /// <summary>Total shots fired this run (stats screen).</summary>
        public int ShotsFired { get; private set; }
        public int ShotsHit { get; private set; }

        private WeaponContext ctx;
        private WeaponViewModel viewModel;
        private Cooldown fireCooldown;
        private float stateTimer;
        private float bloom;
        private bool triggerHeld;
        private bool triggerPulled;
        private bool aiming;
        private bool active = true;

        // ------------------------------------------------------------------ construction

        /// <summary>Factory: builds the GameObject, the component and the view-model.</summary>
        public static Weapon Create(WeaponDefinition definition, int slotIndex, WeaponContext context)
        {
            var go = new GameObject($"Weapon_{definition.DisplayName}");
            go.transform.SetParent(context.ViewModelSocket, worldPositionStays: false);
            Weapon weapon = go.AddComponent<Weapon>();
            weapon.Initialize(definition, slotIndex, context);
            return weapon;
        }

        private void Initialize(WeaponDefinition definition, int slotIndex, WeaponContext context)
        {
            Definition = definition;
            SlotIndex = slotIndex;
            ctx = context;

            Progression = new WeaponProgression(definition.LevelCurve != null
                ? definition.LevelCurve
                : FlatLevelTable.Instance);
            Progression.LevelUp += OnLevelUp;

            RecomputeStats();
            Magazine = Stats.MagazineSize;
            Reserve = definition.StartingReserve;

            if (definition.ViewModelPrefab != null)
            {
                GameObject vm = Instantiate(definition.ViewModelPrefab, transform);
                vm.name = "ViewModel";
                viewModel = vm.GetComponent<WeaponViewModel>();
                if (viewModel == null)
                {
                    viewModel = vm.AddComponent<WeaponViewModel>();
                }

                if (ctx.ViewModelLayer >= 0)
                {
                    Layers.SetRecursively(vm, ctx.ViewModelLayer);
                }
            }

            State = WeaponState.Holstered;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Progression != null)
            {
                Progression.LevelUp -= OnLevelUp;
            }
        }

        // ------------------------------------------------------------------ commands

        public void Draw()
        {
            gameObject.SetActive(true);
            State = WeaponState.Drawing;
            stateTimer = Definition.DrawSeconds;
            triggerPulled = false;
            viewModel?.PlayDraw(Definition.DrawSeconds);
            ctx.Sfx?.Play2D(SoundId.WeaponDraw, 0.5f);
            PublishHud();
        }

        public void Holster()
        {
            State = WeaponState.Holstered;
            triggerHeld = false;
            triggerPulled = false;
            gameObject.SetActive(false);
        }

        public void PullTrigger()
        {
            triggerHeld = true;
            triggerPulled = true;
        }

        public void ReleaseTrigger() => triggerHeld = false;

        public void SetAiming(bool value)
        {
            aiming = value;
            viewModel?.SetAiming(value);
        }

        /// <summary>Suppress firing (menus, death) without holstering.</summary>
        public void SetActiveControl(bool value)
        {
            active = value;
            if (!value)
            {
                triggerHeld = false;
                triggerPulled = false;
            }
        }

        public bool TryReload()
        {
            if (State != WeaponState.Ready || Reserve <= 0 || Magazine >= Stats.MagazineSize)
            {
                return false;
            }

            State = WeaponState.Reloading;
            stateTimer = Stats.ReloadSeconds;
            viewModel?.PlayReload(Stats.ReloadSeconds);
            ctx.Sfx?.Play2D(SoundId.ReloadStart, 0.7f);
            PublishHud();
            return true;
        }

        /// <summary>Top up magazine and reserve to their maximums (level transitions).</summary>
        public void RefillAmmo()
        {
            Magazine = Stats.MagazineSize;
            Reserve = Definition.MaxReserve;
            if (State == WeaponState.Reloading)
            {
                State = WeaponState.Ready;
            }

            PublishHud();
        }

        public void GrantExperience(int amount)
        {
            Progression.AddExperience(amount);
            PublishHud();
        }

        public void ResetForNewRun()
        {
            Progression.Reset();
            RecomputeStats();
            Magazine = Stats.MagazineSize;
            Reserve = Definition.StartingReserve;
            bloom = 0f;
            ShotsFired = 0;
            ShotsHit = 0;
            fireCooldown.Reset();
            if (State != WeaponState.Holstered)
            {
                State = WeaponState.Ready;
            }

            PublishHud();
        }

        // ------------------------------------------------------------------ update loop

        private void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;

            bloom = MathUtil.Damp(bloom, 0f, Definition.SpreadRecovery, dt);
            float movement = ctx.Holder?.MovementFactor ?? 0f;
            float spread = (Definition.BaseSpread + Definition.MovementSpread * movement + bloom) * Stats.SpreadScale;
            CurrentSpreadDegrees = aiming ? spread * Definition.AimSpreadScale : spread;

            if (viewModel != null && ctx.Holder != null)
            {
                viewModel.SetMotion(movement, ctx.Holder.IsGrounded, ctx.Holder.LookDelta);
            }

            switch (State)
            {
                case WeaponState.Drawing:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        State = WeaponState.Ready;
                        PublishHud();
                    }

                    break;

                case WeaponState.Reloading:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        CompleteReload();
                    }

                    break;

                case WeaponState.Ready:
                    TickReady(now);
                    break;
            }

            triggerPulled = false;
        }

        private void TickReady(float now)
        {
            if (!active)
            {
                return;
            }

            bool wantsFire = Definition.FireMode == FireMode.Automatic ? triggerHeld : triggerPulled;
            if (!wantsFire)
            {
                return;
            }

            if (Magazine <= 0)
            {
                // Empty: auto-reload if we can, otherwise a dry click (once per pull).
                if (!TryReload() && triggerPulled)
                {
                    ctx.Sfx?.Play2D(SoundId.DryFire, 0.6f);
                }

                return;
            }

            if (fireCooldown.TryConsume(now, Stats.SecondsBetweenShots))
            {
                Fire();
            }
        }

        private void CompleteReload()
        {
            int needed = Stats.MagazineSize - Magazine;
            int taken = Mathf.Min(needed, Reserve);
            Magazine += taken;
            Reserve -= taken;
            State = WeaponState.Ready;
            ctx.Sfx?.Play2D(SoundId.ReloadEnd, 0.7f);
            PublishHud();
        }

        // ------------------------------------------------------------------ firing

        private void Fire()
        {
            Magazine--;
            ShotsFired++;
            bloom = Mathf.Min(bloom + Definition.SpreadPerShot, Definition.MaxBloom);

            // Recoil: the view kicks up and slightly sideways; aiming steadies it.
            float recoilScale = aiming ? Definition.AimRecoilScale : 1f;
            var kick = new Vector2(
                Random.Range(Definition.VerticalKickRange.x, Definition.VerticalKickRange.y),
                Random.Range(Definition.HorizontalKickRange.x, Definition.HorizontalKickRange.y)) * recoilScale;
            ctx.Recoil?.AddRecoil(kick);
            viewModel?.Kick();

            // Hitscan.
            Ray aim = ctx.Holder?.AimRay ?? new Ray(transform.position, transform.forward);
            Vector3 direction = MathUtil.RandomInCone(aim.direction, CurrentSpreadDegrees * Mathf.Deg2Rad);
            float range = Definition.Range > 0f ? Definition.Range : MaxRaycastFallback;
            Vector3 endPoint = aim.origin + direction * range;

            bool hitSomething = false;
            if (Physics.Raycast(aim.origin, direction, out RaycastHit hit, range, Layers.ShootableMask, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;
                hitSomething = ApplyHit(hit, direction);
            }

            SpawnMuzzleFlash();
            SpawnTracer(endPoint);
            ctx.Sfx?.Play2D(Definition.FireSound, Definition.FireVolume);

            if (hitSomething)
            {
                ShotsHit++;
            }

            PublishHud();
        }

        /// <summary>Resolve a raycast hit: damage a hitbox if present, otherwise just spawn an impact.</summary>
        private bool ApplyHit(in RaycastHit hit, Vector3 direction)
        {
            if (Hitbox.TryResolve(hit.collider, out Hitbox hitbox, out IDamageable damageable) && damageable.IsAlive)
            {
                var info = new DamageInfo(Stats.Damage, DamageKind.Bullet, this, hit.point, hit.normal, direction);
                DamageResult result = hitbox != null
                    ? hitbox.Hit(info.WithAmount(Stats.Damage, false))
                    : damageable.ApplyDamage(info);

                if (!result.Ignored)
                {
                    bool headshot = hitbox != null && hitbox.IsHead;
                    ctx.HitChannel?.Raise(headshot);
                    SpawnImpact(Definition.BloodImpactPrefab, hit.point, hit.normal);
                    ctx.Sfx?.Play2D(headshot ? SoundId.HeadshotMarker : SoundId.HitMarker, 0.5f);
                    return true;
                }
            }

            SpawnImpact(Definition.ImpactPrefab, hit.point, hit.normal);
            SfxPlayer.TryPlayAt(SoundId.ImpactConcrete, hit.point, 0.4f);
            return false;
        }

        private void SpawnMuzzleFlash()
        {
            if (Definition.MuzzleFlashPrefab == null || ctx.Pools == null)
            {
                return;
            }

            Transform muzzle = viewModel != null ? viewModel.Muzzle : transform;
            var flash = ctx.Pools.Spawn<MuzzleFlash>(Definition.MuzzleFlashPrefab, muzzle.position, muzzle.rotation);
            flash?.Play(muzzle);
        }

        private void SpawnTracer(Vector3 endPoint)
        {
            if (Definition.TracerPrefab == null || ctx.Pools == null)
            {
                return;
            }

            Transform muzzle = viewModel != null ? viewModel.Muzzle : transform;
            var tracer = ctx.Pools.Spawn<Tracer>(Definition.TracerPrefab, muzzle.position, Quaternion.identity);
            tracer?.Show(muzzle.position, endPoint);
        }

        private void SpawnImpact(GameObject prefab, Vector3 point, Vector3 normal)
        {
            if (prefab == null || ctx.Pools == null)
            {
                return;
            }

            ctx.Pools.Spawn(prefab, point + normal * 0.01f, Quaternion.LookRotation(normal));
        }

        // ------------------------------------------------------------------ progression

        private void OnLevelUp(int newLevel)
        {
            int previousMagazine = Stats.MagazineSize;
            RecomputeStats();

            // A level-up tops the magazine up: immediate, tangible reward.
            Magazine = Stats.MagazineSize;
            Reserve = Mathf.Min(Definition.MaxReserve, Reserve + (Stats.MagazineSize - previousMagazine));

            ctx.LevelUpChannel?.Raise(new WeaponLevelUpInfo(Definition.DisplayName, newLevel));
            ctx.Sfx?.Play2D(SoundId.WeaponLevelUp, 0.7f);
        }

        private void RecomputeStats()
        {
            WeaponLevelModifiers mods = Definition.LevelCurve != null
                ? Definition.LevelCurve.Evaluate(Progression.Level)
                : WeaponLevelModifiers.Identity;
            Stats = new WeaponStats(Definition, mods);
        }

        /// <summary>Push a HUD snapshot; cheap enough to call on every state change.</summary>
        public void PublishHud()
        {
            ctx.HudChannel?.Raise(new WeaponHudInfo(
                Definition.DisplayName,
                SlotIndex,
                Magazine,
                Reserve,
                Progression.Level,
                Progression.Progress01,
                State == WeaponState.Reloading,
                CurrentSpreadDegrees));
        }

        /// <summary>Fallback table for definitions without a curve: level 1 forever.</summary>
        private sealed class FlatLevelTable : IWeaponLevelTable
        {
            public static readonly FlatLevelTable Instance = new();
            public int MaxLevel => 1;
            public int ExperienceToNext(int level) => int.MaxValue;
        }
    }
}
